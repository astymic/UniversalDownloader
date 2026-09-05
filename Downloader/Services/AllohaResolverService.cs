using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalDownloader.Services
{
    /// <summary>
    /// Headless In-Memory Alloha Player Resolver using Microsoft ClearScript V8.
    /// Executes client-side decryption, calculates fingerprint signatures (Borth header),
    /// and extracts direct .m3u8 stream manifests without requiring a browser or UI.
    /// </summary>
    public class AllohaResolverService
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly ConcurrentDictionary<string, string> _scriptCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private const string KnownFingerprint = "a8ca5ee9bc6aea4e47033d72f4bf6173ba400729b35b7c9f90f3dc7ca6a430fe";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        public async Task<string?> ResolveStreamUrlAsync(string allohaUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(allohaUrl)) return null;

            if (allohaUrl.StartsWith("//"))
            {
                allohaUrl = "https:" + allohaUrl;
            }

            try
            {
                using var pageReq = new HttpRequestMessage(HttpMethod.Get, allohaUrl);
                pageReq.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                pageReq.Headers.TryAddWithoutValidation("Referer", "https://ru.yummyani.me/");

                using var pageResp = await _httpClient.SendAsync(pageReq, cancellationToken);
                if (!pageResp.IsSuccessStatusCode) return null;

                string html = await pageResp.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                var viewportiMatch = Regex.Match(html, @"<meta\s+name=[""']viewporti[""']\s+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (!viewportiMatch.Success) return null;
                string viewportiContent = viewportiMatch.Groups[1].Value;

                // Extract inline scripts
                var scriptMatches = Regex.Matches(html, @"<script\b[^>]*>(.*?)</script>", RegexOptions.Singleline);
                var inlineCode = string.Join("\n;\n", scriptMatches.Cast<Match>()
                    .Select(m => m.Groups[1].Value.Trim())
                    .Where(s => !string.IsNullOrEmpty(s) && !s.Contains("document.querySelectorAll('body')[0].remove()")));

                // Extract external script sources
                var srcMatches = Regex.Matches(html, @"<script\b[^>]*\bsrc=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                var srcUrls = srcMatches.Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(s => s.Contains("/build/"))
                    .ToList();

                if (srcUrls.Count == 0) return null;

                // Pre-fetch all external scripts (cached in memory)
                var scripts = new List<(string url, string code)>();
                foreach (var src in srcUrls)
                {
                    string fullSrc = src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? src : "https://alloha.yani.tv" + src;
                    if (!_scriptCache.TryGetValue(fullSrc, out var code))
                    {
                        using var sReq = new HttpRequestMessage(HttpMethod.Get, fullSrc);
                        sReq.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                        sReq.Headers.TryAddWithoutValidation("Referer", allohaUrl);
                        using var sResp = await _httpClient.SendAsync(sReq, cancellationToken);
                        if (!sResp.IsSuccessStatusCode) return null;
                        code = await sResp.Content.ReadAsStringAsync(cancellationToken);

                        if (src.Contains("app"))
                        {
                            code = code.Replace("var DW=", "var DW = window.__DW = ");
                            code = code.Replace("function a8(aS){", "window.__a8 = a8; function a8(aS){");
                            code = code.Replace("var wv,", "window.__setWv = function(v) { wv = v; }; window.__getWv = function() { return wv; }; var wv,");
                        }

                        _scriptCache[fullSrc] = code;
                    }
                    scripts.Add((fullSrc, code));
                }

                string? interceptedRespBody = null;

                using var engine = new Microsoft.ClearScript.V8.V8ScriptEngine();

                engine.AddHostObject("hostSendRequest", new Func<string, string, string, string, string>((method, url, headersJson, data) =>
                {
                    var fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://alloha.yani.tv" + url;
                    using var req = new HttpRequestMessage(new HttpMethod(method), fullUrl);
                    req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    req.Headers.TryAddWithoutValidation("Origin", "https://alloha.yani.tv");
                    req.Headers.TryAddWithoutValidation("Referer", allohaUrl);

                    if (!string.IsNullOrEmpty(data))
                    {
                        req.Content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }

                    if (!string.IsNullOrEmpty(headersJson))
                    {
                        try
                        {
                            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                            if (headers != null)
                            {
                                foreach (var kvp in headers)
                                {
                                    if (kvp.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                                    req.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                                }
                            }
                        }
                        catch {}
                    }

                    var resp = _httpClient.Send(req);
                    var respBody = resp.Content.ReadAsStringAsync().Result;

                    if ((int)resp.StatusCode == 200 && respBody.Contains("hlsSource"))
                    {
                        interceptedRespBody = respBody;
                    }

                    var borthHeader = resp.Headers.TryGetValues("Borth", out var vals) ? vals.FirstOrDefault() ?? "" : "";
                    return JsonSerializer.Serialize(new
                    {
                        status = (int)resp.StatusCode,
                        responseText = respBody,
                        borth = borthHeader
                    });
                }));

                string polyfill = GetPolyfillScript(viewportiContent);
                engine.Execute(polyfill);
                engine.Execute(inlineCode);
                engine.Execute("if (typeof config !== 'undefined' && config.ads) { config.ads.enabled = false; }");

                foreach (var (_, code) in scripts)
                {
                    engine.Execute($"try {{ {code}\n }} catch (err) {{}}");
                }

                engine.Execute(@"
                    if (typeof window.__DW === 'function') {
                        try { window.__DW(); } catch(e) {}
                    }
                    runTimers(50);

                    if (typeof window.__setWv === 'function') {
                        window.__setWv('" + KnownFingerprint + @"');
                    }

                    if (typeof window.__a8 === 'function' && typeof fileList !== 'undefined' && fileList.active) {
                        try { window.__a8(fileList.active.id, true); } catch(e) {}
                    }
                    runTimers(50);

                    for (var iter = 0; iter < 10; iter++) {
                        runTimers(50);
                        if (playerElem) {
                            playerElem.dispatchEvent('ready');
                        }
                    }
                ");

                if (string.IsNullOrEmpty(interceptedRespBody))
                {
                    return null;
                }

                return ExtractBestM3u8FromResponse(interceptedRespBody);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AllohaResolverService] Resolution failed: {ex.Message}");
                return null;
            }
        }

        private static string? ExtractBestM3u8FromResponse(string jsonText)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                if (!doc.RootElement.TryGetProperty("hlsSource", out var hlsSource) || hlsSource.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                string? bestUrl = null;
                int bestQualityScore = -1;

                foreach (var src in hlsSource.EnumerateArray())
                {
                    if (src.TryGetProperty("quality", out var qualityElem) && qualityElem.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in qualityElem.EnumerateObject())
                        {
                            string url = prop.Value.GetString() ?? "";
                            if (string.IsNullOrEmpty(url)) continue;

                            int score = GetQualityScore(prop.Name);
                            if (score > bestQualityScore)
                            {
                                bestQualityScore = score;
                                bestUrl = url;
                            }
                        }
                    }
                    else if (src.TryGetProperty("src", out var srcElem))
                    {
                        if (srcElem.ValueKind == JsonValueKind.String)
                        {
                            string url = srcElem.GetString() ?? "";
                            if (!string.IsNullOrEmpty(url) && bestQualityScore < 0)
                            {
                                bestUrl = url;
                                bestQualityScore = 720;
                            }
                        }
                        else if (srcElem.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in srcElem.EnumerateObject())
                            {
                                string url = prop.Value.GetString() ?? "";
                                if (string.IsNullOrEmpty(url)) continue;

                                int score = GetQualityScore(prop.Name);
                                if (score > bestQualityScore)
                                {
                                    bestQualityScore = score;
                                    bestUrl = url;
                                }
                            }
                        }
                    }
                }

                return bestUrl;
            }
            catch
            {
                return null;
            }
        }

        private static int GetQualityScore(string qualityLabel)
        {
            if (int.TryParse(qualityLabel.Replace("p", ""), out int q)) return q;
            if (qualityLabel.Contains("1080")) return 1080;
            if (qualityLabel.Contains("720")) return 720;
            if (qualityLabel.Contains("480")) return 480;
            if (qualityLabel.Contains("360")) return 360;
            return 1;
        }

        private static string GetPolyfillScript(string viewportiContent)
        {
            return @"
                var window = this;
                var self = this;
                var top = {};
                var parent = top;
                var console = { log: function(){}, error: function(){}, warn: function(){}, info: function(){}, debug: function(){} };

                function Element() {}
                Element.prototype.nodeType = 1;
                Element.prototype.querySelector = function(sel) {
                    if (sel && (sel.indexOf('player') !== -1 || sel.indexOf('video') !== -1)) return playerElem;
                    return createElem('DIV');
                };
                Element.prototype.querySelectorAll = function(sel) {
                    if (sel && (sel.indexOf('player') !== -1 || sel.indexOf('video') !== -1)) return createNodeList([playerElem]);
                    return createNodeList([createElem('DIV')]);
                };
                Element.prototype.getElementsByTagName = function(t) { return createNodeList([createElem(t)]); };
                Element.prototype.getElementsByClassName = function(c) { return createNodeList([createElem('DIV')]); };
                Element.prototype.closest = function(s) { return this; };
                Element.prototype.insertAdjacentElement = function(pos, el) { this.appendChild(el); return el; };
                Element.prototype.insertAdjacentHTML = function(pos, html) { return; };
                Element.prototype.insertAdjacentText = function(pos, text) { return; };
                window.Element = Element;
                window.HTMLElement = Element;
                window.HTMLDivElement = Element;
                window.HTMLVideoElement = Element;
                window.HTMLAudioElement = Element;
                window.HTMLInputElement = Element;
                window.HTMLSelectElement = Element;
                window.HTMLOptionElement = Element;

                function NodeList() {}
                NodeList.prototype = Object.create(Array.prototype);
                window.NodeList = NodeList;

                function HTMLCollection() {}
                HTMLCollection.prototype = Object.create(Array.prototype);
                window.HTMLCollection = HTMLCollection;

                function createNodeList(arr) {
                    var nl = Object.create(NodeList.prototype);
                    for (var i = 0; i < arr.length; i++) nl[i] = arr[i];
                    nl.length = arr.length;
                    nl.item = function(i) { return nl[i]; };
                    return nl;
                }

                function DOMTokenList(el) {
                    this._el = el;
                    this._classes = new Set();
                }
                DOMTokenList.prototype.add = function() {
                    for (var i = 0; i < arguments.length; i++) this._classes.add(arguments[i]);
                    this._sync();
                };
                DOMTokenList.prototype.remove = function() {
                    for (var i = 0; i < arguments.length; i++) this._classes.delete(arguments[i]);
                    this._sync();
                };
                DOMTokenList.prototype.toggle = function(cls, force) {
                    var has = this._classes.has(cls);
                    var res = typeof force === 'boolean' ? force : !has;
                    if (res) this._classes.add(cls); else this._classes.delete(cls);
                    this._sync();
                    return res;
                };
                DOMTokenList.prototype.contains = function(cls) {
                    return this._classes.has(cls);
                };
                DOMTokenList.prototype.replace = function(oldC, newC) {
                    if (!this._classes.has(oldC)) return false;
                    this._classes.delete(oldC);
                    this._classes.add(newC);
                    this._sync();
                    return true;
                };
                DOMTokenList.prototype._sync = function() {
                    if (this._el) this._el.className = Array.from(this._classes).join(' ');
                };
                window.DOMTokenList = DOMTokenList;

                function createElem(tag) {
                    var el = Object.create(Element.prototype);
                    el.nodeType = 1;
                    el.tagName = (tag || 'DIV').toUpperCase();
                    el.style = {};
                    el.innerHTML = '';
                    el.childNodes = [];
                    el.children = [];
                    el.checked = true;
                    el.value = '';
                    el.type = 'text';
                    el.name = '';
                    el.disabled = false;
                    el.selected = false;
                    el.offsetWidth = 100;
                    el.offsetHeight = 100;
                    el.appendChild = function(c) {
                        if (c) {
                            this.childNodes.push(c);
                            this.children.push(c);
                            c.parentNode = this;
                            c.parentElement = this;
                            this.firstChild = this.childNodes[0];
                            this.lastChild = this.childNodes[this.childNodes.length - 1];
                        }
                        return c || this;
                    };
                    el.removeChild = function(c) { return c; };
                    el.insertBefore = function(n, r) { return this.appendChild(n); };
                    el.setAttribute = function(k, v) { this[k] = v; };
                    el.getAttribute = function(k) { return this[k] || ''; };
                    el.removeAttribute = function(k) { delete this[k]; };
                    el.setAttributeNS = function(ns, k, v) { this[k] = v; };
                    el.getAttributeNS = function(ns, k) { return this[k] || ''; };
                    el.hasAttributeNS = function(ns, k) { return Boolean(this[k]); };
                    el.addEventListener = function(evt, fn) {
                        this._listeners = this._listeners || {};
                        this._listeners[evt] = this._listeners[evt] || [];
                        this._listeners[evt].push(fn);
                    };
                    el.removeEventListener = function() {};
                    el.dispatchEvent = function(e) {
                        var type = typeof e === 'string' ? e : (e.type || '');
                        this._listeners = this._listeners || {};
                        var list = this._listeners[type];
                        if (list) {
                            for (var i = 0; i < list.length; i++) {
                                try { list[i].call(this, typeof e === 'string' ? { type: type, target: this } : e); }
                                catch(err) {}
                            }
                        }
                        return true;
                    };
                    el.classList = new DOMTokenList(el);
                    el.getBoundingClientRect = function() { return { top: 0, left: 0, width: 100, height: 100, bottom: 100, right: 100 }; };
                    el.contains = function() { return true; };
                    el.matches = function() { return true; };
                    el.closest = function(sel) { return this; };
                    el.focus = function() {};
                    el.blur = function() {};
                    el.click = function() { this.dispatchEvent('click'); };
                    el.scrollIntoView = function() {};
                    el.insertAdjacentElement = function(pos, e) { this.appendChild(e); return e; };
                    el.insertAdjacentHTML = function(pos, html) {};
                    el.insertAdjacentText = function(pos, text) {};
                    el.compareDocumentPosition = function() { return 0; };
                    el.querySelector = function(sel) {
                        var visited = new Set();
                        function find(node) {
                            if (!node || visited.has(node)) return null;
                            visited.add(node);
                            if (!node.children) return null;
                            for (var i = 0; i < node.children.length; i++) {
                                var c = node.children[i];
                                if (!c) continue;
                                var cls = (c.getAttribute && c.getAttribute('class')) || c.className || '';
                                if (sel && sel.indexOf('.') === 0) {
                                    var targetCls = sel.substring(1);
                                    if (cls.split(' ').indexOf(targetCls) !== -1) return c;
                                }
                                if (sel && sel.indexOf('#') === 0 && c.id === sel.substring(1)) return c;
                                if (sel && (c.role === 'menu' || (c.getAttribute && c.getAttribute('role') === 'menu')) && sel.indexOf('menu') !== -1) return c;
                                var res = find(c);
                                if (res) return res;
                            }
                            return null;
                        }
                        var found = find(this);
                        if (found) return found;
                        var dummy = createElem('DIV');
                        if (sel && sel.indexOf('.') === 0) dummy.setAttribute('class', sel.substring(1));
                        if (sel && sel.indexOf('menu') !== -1) dummy.setAttribute('role', 'menu');
                        return dummy;
                    };
                    el.querySelectorAll = function(sel) {
                        return createNodeList([createElem('DIV')]);
                    };
                    el.getElementsByTagName = function(tag) { return [createElem(tag)]; };
                    el.getElementsByClassName = function(cls) { return [createElem('DIV')]; };
                    el.cloneNode = function(deep) {
                        var clone = createElem(this.tagName);
                        clone.checked = this.checked;
                        clone.value = this.value;
                        clone.type = this.type;
                        clone.name = this.name;
                        if (deep && this.childNodes.length > 0) {
                            for (var i = 0; i < this.childNodes.length; i++) {
                                clone.appendChild(this.childNodes[i].cloneNode(true));
                            }
                        } else {
                            clone.appendChild(createElem('DIV'));
                        }
                        return clone;
                    };
                    if (el.tagName === 'CANVAS') {
                        el.getContext = function(type) {
                            return {
                                getParameter: function(p) { return 'WebGL 1.0'; },
                                getExtension: function(ext) { return null; },
                                fillRect: function(){},
                                drawImage: function(){},
                                toDataURL: function(){ return 'data:image/png;base64,'; }
                            };
                        };
                    }
                    if (el.tagName === 'VIDEO' || el.tagName === 'AUDIO') {
                        el._src = '';
                        Object.defineProperty(el, 'src', {
                            get: function() { return this._src; },
                            set: function(v) { this._src = v; }
                        });
                        el.canPlayType = function(type) { return 'probably'; };
                        el.play = function() { return Promise.resolve(); };
                        el.pause = function() {};
                        el.load = function() {};
                    }
                    el.ownerDocument = document;
                    el.parentNode = el;
                    el.parentElement = el;
                    el.firstChild = el;
                    el.lastChild = el;
                    return el;
                }

                var elementsById = {};
                var docListeners = {};
                var winListeners = {};

                var document = {
                    nodeType: 9,
                    readyState: 'complete',
                    defaultView: window,
                    referrer: 'https://ru.yummyani.me/',
                    location: null,
                    querySelectorAll: function(sel) {
                        if (sel && sel.indexOf('viewporti') !== -1) return createNodeList([viewportiElem]);
                        if (sel === '#player') return createNodeList([playerElem]);
                        return createNodeList([createElem('DIV')]);
                    },
                    querySelector: function(sel) {
                        if (sel && sel.indexOf('viewporti') !== -1) return viewportiElem;
                        if (sel === '#player') return playerElem;
                        return createElem('DIV');
                    },
                    getElementById: function(id) {
                        if (!elementsById[id]) elementsById[id] = createElem('DIV');
                        return elementsById[id];
                    },
                    getElementsByTagName: function(tag) {
                        if (tag && tag.toLowerCase() === 'meta') return [viewportiElem];
                        if (tag && tag.toLowerCase() === 'video') return [playerElem];
                        return [createElem(tag)];
                    },
                    getElementsByClassName: function(cls) { return [createElem('DIV')]; },
                    createElement: function(tag) { return createElem(tag); },
                    createElementNS: function(ns, tag) { return createElem(tag); },
                    createTextNode: function(t) { return { nodeType: 3, textContent: t }; },
                    createDocumentFragment: function() {
                        var frag = createElem('DIV');
                        frag.nodeType = 11;
                        return frag;
                    },
                    implementation: { createHTMLDocument: function(title) { return document; } },
                    head: null,
                    body: null,
                    documentElement: null,
                    addEventListener: function(evt, fn) {
                        docListeners[evt] = docListeners[evt] || [];
                        docListeners[evt].push(fn);
                    },
                    dispatchEvent: function(e) {
                        var type = typeof e === 'string' ? e : (e.type || '');
                        var list = docListeners[type];
                        if (list) {
                            for (var i = 0; i < list.length; i++) {
                                try { list[i].call(document, typeof e === 'string' ? { type: type, target: document } : e); } catch(err) {}
                            }
                        }
                        return true;
                    }
                };

                var dummyElem = createElem('DIV');
                var playerElem = createElem('VIDEO');
                playerElem.id = 'player';
                playerElem.ownerDocument = document;
                playerElem.hasAttribute = function(k) { return k === 'crossorigin' ? true : Boolean(this[k]); };
                playerElem.getAttribute = function(k) { return k === 'crossorigin' ? '' : (this[k] || ''); };
                playerElem.textTracks = [];
                playerElem.canPlayType = function(t) { return 'probably'; };
                playerElem.play = function() { return Promise.resolve(); };
                playerElem.pause = function() {};
                playerElem.load = function() {};
                playerElem.autoplay = false;
                playerElem.muted = true;
                playerElem.currentTime = 0;
                playerElem.duration = 100;
                playerElem.videoWidth = 1920;
                playerElem.videoHeight = 1080;
                elementsById['player'] = playerElem;

                var viewportiElem = createElem('META');
                viewportiElem.name = 'viewporti';
                viewportiElem.content = '" + viewportiContent.Replace("\"", "\\\"") + @"';
                viewportiElem.ownerDocument = document;
                viewportiElem.getAttribute = function(k) { return k === 'content' ? this.content : (this[k] || ''); };

                dummyElem.ownerDocument = document;
                document.head = dummyElem;
                document.body = dummyElem;
                document.documentElement = dummyElem;
                dummyElem.contentDocument = document;
                dummyElem.contentWindow = window;
                dummyElem.documentElement = dummyElem;
                dummyElem.parentNode = dummyElem;
                window.document = document;
                window.showLoading = function() {};
                var showLoading = window.showLoading;
                window.hideLoading = function() {};
                var hideLoading = window.hideLoading;
                window.Node = { ELEMENT_NODE: 1, DOCUMENT_NODE: 9 };
                var Node = window.Node;
                window.NodeList = function NodeList() {};
                var NodeList = window.NodeList;
                window.HTMLCollection = function HTMLCollection() {};
                var HTMLCollection = window.HTMLCollection;
                window.Element = function Element() {};
                var Element = window.Element;
                window.HTMLElement = function HTMLElement() {};
                var HTMLElement = window.HTMLElement;
                window.HTMLMediaElement = function HTMLMediaElement() {};
                var HTMLMediaElement = window.HTMLMediaElement;
                window.HTMLVideoElement = function HTMLVideoElement() {};
                var HTMLVideoElement = window.HTMLVideoElement;
                window.HTMLAudioElement = function HTMLAudioElement() {};
                var HTMLAudioElement = window.HTMLAudioElement;

                function Event(type, init) { this.type = type; }
                window.Event = Event;
                var Event = Event;

                function UIEvent(type, init) { Event.call(this, type, init); }
                UIEvent.prototype = Object.create(Event.prototype);
                window.UIEvent = UIEvent;
                var UIEvent = UIEvent;

                function MouseEvent(type, init) { UIEvent.call(this, type, init); }
                MouseEvent.prototype = Object.create(UIEvent.prototype);
                window.MouseEvent = MouseEvent;
                var MouseEvent = MouseEvent;

                function KeyboardEvent(type, init) { UIEvent.call(this, type, init); }
                KeyboardEvent.prototype = Object.create(UIEvent.prototype);
                window.KeyboardEvent = KeyboardEvent;
                var KeyboardEvent = KeyboardEvent;

                function CustomEvent(type, init) { Event.call(this, type, init); this.detail = (init && init.detail) || null; }
                CustomEvent.prototype = Object.create(Event.prototype);
                window.CustomEvent = CustomEvent;
                var CustomEvent = CustomEvent;

                function TextTrack() { this.kind = 'subtitles'; this.label = ''; this.language = ''; this.cues = []; }
                window.TextTrack = TextTrack;
                var TextTrack = TextTrack;

                function TextTrackList() {}
                TextTrackList.prototype = Object.create(Array.prototype);
                window.TextTrackList = TextTrackList;
                var TextTrackList = TextTrackList;

                window.MutationObserver = function(cb) {
                    this.observe = function(){};
                    this.disconnect = function(){};
                };
                window.addEventListener = function(evt, fn) {
                    winListeners[evt] = winListeners[evt] || [];
                    winListeners[evt].push(fn);
                };
                window.removeEventListener = function() {};
                window.dispatchEvent = function(e) {
                    var type = typeof e === 'string' ? e : (e.type || '');
                    if (winListeners[type]) winListeners[type].forEach(function(fn){ fn(e); });
                    return true;
                };
                window.innerWidth = 1920;
                window.innerHeight = 1080;
                window.outerWidth = 1920;
                window.outerHeight = 1080;
                window.devicePixelRatio = 1;
                window.screen = { width: 1920, height: 1080, availWidth: 1920, availHeight: 1080, colorDepth: 24 };
                window.requestAnimationFrame = function(cb) { return setTimeout(cb, 16); };
                window.cancelAnimationFrame = function(id) { clearTimeout(id); };
                window.matchMedia = function() { return { matches: false, addListener: function(){}, removeListener: function(){} }; };
                window.getComputedStyle = function(el) { return el ? el.style : {}; };
                window.localStorage = { getItem: function(k){ return null; }, setItem: function(k, v){}, removeItem: function(k){} };
                window.sessionStorage = { getItem: function(k){ return null; }, setItem: function(k, v){}, removeItem: function(k){} };

                var _playerObj = {
                    on: function(evt, cb) { return this; },
                    one: function(evt, cb) { return this; },
                    off: function(evt, cb) { return this; },
                    play: function() {},
                    pause: function() {},
                    destroy: function() {},
                    toggleControls: function() {},
                    currentTime: 0,
                    quality: '',
                    config: {}
                };
                var _player = null;
                Object.defineProperty(window, 'player', {
                    get: function() { return (_player && typeof _player === 'object') ? _player : _playerObj; },
                    set: function(v) { _player = v; },
                    configurable: true
                });
                window.Hls = function() {
                    this.loadSource = function(src) {};
                    this.attachMedia = function(v) {};
                    this.on = function(evt, cb) {};
                    this.once = function(evt, cb) {};
                    this.destroy = function() {};
                };
                window.Hls.isSupported = function() { return true; };
                window.Hls.Events = { MANIFEST_PARSED: 'hlsManifestParsed' };

                var navigator = {
                    userAgent: '" + UserAgent + @"',
                    sendBeacon: function(url, data) { return true; },
                    mediaCapabilities: {
                        decodingInfo: function(cfg) {
                            return {
                                then: function(cb) {
                                    try { cb({ supported: true, smooth: true }); } catch(e) {}
                                    return this;
                                },
                                catch: function(cb) { return this; }
                            };
                        }
                    },
                    mediaSession: { setActionHandler: function(){} }
                };
                var loc = {
                    href: 'https://alloha.yani.tv/',
                    origin: 'https://alloha.yani.tv',
                    protocol: 'https:',
                    host: 'alloha.yani.tv',
                    hostname: 'alloha.yani.tv',
                    pathname: '/',
                    search: '',
                    hash: '',
                    port: '',
                    toString: function() { return this.href; }
                };
                window.location = loc;
                document.location = loc;
                var location = loc;
                var WebSocket = function(url) {
                    this.url = url;
                    this.send = function(data) {};
                    this.close = function() {};
                    this.addEventListener = function(evt, fn){};
                    var that = this;
                    setTimeout(function() { if (that.onopen) that.onopen(); }, 10);
                };
                var fetch = function(url, opts) {
                    return Promise.resolve({
                        json: function() { return Promise.resolve({}); },
                        text: function() { return Promise.resolve(''); },
                        status: 200,
                        ok: true
                    });
                };
                var XMLHttpRequest = function() {
                    this._headers = {};
                    this.open = function(m, u) {
                        this._m = m;
                        this._u = u;
                    };
                    this.setRequestHeader = function(k, v) {
                        this._headers[k] = v;
                    };
                    this.getResponseHeader = function(h) {
                        if (this._resp && h && h.toLowerCase() === 'borth') return this._resp.borth || '';
                        return '';
                    };
                    this.send = function(d) {
                        try {
                            var raw = hostSendRequest(this._m, this._u, JSON.stringify(this._headers), d || '');
                            var res = JSON.parse(raw);
                            this._resp = res;
                            this.status = res.status;
                            this.readyState = 4;
                            this.responseText = res.responseText;
                            this.response = res.responseText;
                            var that = this;
                            setTimeout(function() {
                                if (that.onreadystatechange) that.onreadystatechange();
                                if (that.onload) that.onload();
                            }, 0);
                        } catch(e) {
                            if (this.onerror) this.onerror(e);
                        }
                    };
                };
                var _timers = [];
                var setTimeout = function(fn, ms) { _timers.push(fn); return _timers.length; };
                var setInterval = function(fn, ms) { return 1; };
                var clearTimeout = function() {};
                var clearInterval = function() {};
                function runTimers(n) {
                    var limit = n || 20;
                    while (_timers.length > 0 && limit-- > 0) {
                        var fn = _timers.shift();
                        try { fn(); } catch(e) {}
                    }
                }

                function CustomPromise(executor) {
                    var self = this;
                    self.state = 'pending';
                    self.value = undefined;
                    self.handlers = [];

                    function resolve(val) {
                        if (self.state !== 'pending') return;
                        if (val && typeof val.then === 'function') {
                            try { return val.then(resolve, reject); } catch(e) { return reject(e); }
                        }
                        self.state = 'fulfilled';
                        self.value = val;
                        for (var i = 0; i < self.handlers.length; i++) setTimeout(self.handlers[i], 0);
                    }

                    function reject(err) {
                        if (self.state !== 'pending') return;
                        self.state = 'rejected';
                        self.value = err;
                        for (var i = 0; i < self.handlers.length; i++) setTimeout(self.handlers[i], 0);
                    }

                    self.then = function(onFulfilled, onRejected) {
                        return new CustomPromise(function(res, rej) {
                            function handle() {
                                if (self.state === 'fulfilled') {
                                    if (typeof onFulfilled === 'function') {
                                        try { res(onFulfilled(self.value)); } catch(e) { rej(e); }
                                    } else {
                                        res(self.value);
                                    }
                                } else if (self.state === 'rejected') {
                                    if (typeof onRejected === 'function') {
                                        try { res(onRejected(self.value)); } catch(e) { rej(e); }
                                    } else {
                                        rej(self.value);
                                    }
                                }
                            }
                            if (self.state === 'pending') {
                                self.handlers.push(handle);
                            } else {
                                setTimeout(handle, 0);
                            }
                        });
                    };

                    self.catch = function(onRejected) { return self.then(null, onRejected); };
                    self.finally = function(cb) {
                        return self.then(
                            function(v) { cb(); return v; },
                            function(e) { cb(); throw e; }
                        );
                    };

                    try { executor(resolve, reject); } catch(e) { reject(e); }
                }

                CustomPromise.resolve = function(v) {
                    return v instanceof CustomPromise ? v : new CustomPromise(function(res) { res(v); });
                };
                CustomPromise.reject = function(e) {
                    return new CustomPromise(function(res, rej) { rej(e); });
                };
                CustomPromise.all = function(arr) {
                    return new CustomPromise(function(res, rej) {
                        var results = [];
                        var remaining = arr.length;
                        if (remaining === 0) return res(results);
                        arr.forEach(function(item, idx) {
                            CustomPromise.resolve(item).then(function(val) {
                                results[idx] = val;
                                if (--remaining === 0) res(results);
                            }, rej);
                        });
                    });
                };
                window.Promise = CustomPromise;
                var Promise = CustomPromise;
            ";
        }
    }
}
