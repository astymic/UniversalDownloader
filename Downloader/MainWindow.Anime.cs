using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using UniversalDownloader.Models;
using UniversalDownloader.Services;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private readonly YummyAnimeService _yummyAnimeService = new();
        private readonly KinogoService _kinogoService = new();
        private AnimeSeriesInfo? _currentAnimeSeries;
        private AnimeDubInfo? _selectedAnimeDub;

        public async Task<bool> CheckAndHandleAnimeUrlAsync(string url)
        {
            if (KinogoService.IsKinogoUrl(url))
            {
                await LoadKinogoSeriesAsync(url);
                return true;
            }

            if (!YummyAnimeService.IsYummyAnimeUrl(url))
            {
                return false;
            }

            await LoadAnimeSeriesAsync(url);
            return true;
        }

        public async Task LoadKinogoSeriesAsync(string url)
        {
            ShowToast("Загрузка фильма/сериала и списка озвучек... 🍿");

            try
            {
                var series = await _kinogoService.FetchSeriesAsync(url);
                if (series == null)
                {
                    ShowToast("Не удалось загрузить данные фильма/сериала. Проверьте ссылку.");
                    return;
                }

                _currentAnimeSeries = series;
                await DisplaySeriesInDrawerAsync(series);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load Kinogo series: {ex.Message}");
                ShowToast($"Ошибка загрузки: {ex.Message}");
            }
        }

        public async Task LoadAnimeSeriesAsync(string url)
        {
            ShowToast("Загрузка данных аниме и списка озвучек... 🍿");

            try
            {
                var series = await _yummyAnimeService.FetchAnimeSeriesAsync(url);
                if (series == null)
                {
                    ShowToast("Не удалось загрузить данные об аниме. Проверьте ссылку.");
                    return;
                }

                _currentAnimeSeries = series;
                await DisplaySeriesInDrawerAsync(series);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load anime: {ex.Message}");
                ShowToast($"Ошибка загрузки аниме: {ex.Message}");
            }
        }

        private async Task DisplaySeriesInDrawerAsync(AnimeSeriesInfo series)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                // Update Drawer UI category & titles
                if (AnimeDrawerCategoryText != null)
                {
                    if (series.SourceService == "Kinogo")
                    {
                        AnimeDrawerCategoryText.Text = series.IsMovie ? "🎬 ФИЛЬМ (КИНОГО)" : "🎬 СЕРИАЛ (КИНОГО)";
                    }
                    else
                    {
                        AnimeDrawerCategoryText.Text = "🎬 АНИМЕ СЕРИИ";
                    }
                }

                if (AnimeDrawerTitleText != null)
                    AnimeDrawerTitleText.Text = !string.IsNullOrWhiteSpace(series.Title) ? series.Title : series.Slug;

                if (AnimeDrawerOriginalTitleText != null)
                {
                    AnimeDrawerOriginalTitleText.Text = series.OriginalTitle;
                    AnimeDrawerOriginalTitleText.Visibility = !string.IsNullOrWhiteSpace(series.OriginalTitle) ? Visibility.Visible : Visibility.Collapsed;
                }

                if (AnimeDrawerMetaText != null)
                {
                    string yearStr = !string.IsNullOrWhiteSpace(series.Year) && series.Year != "0" ? series.Year : "";
                    if (series.Dubs.Count == 0)
                    {
                        AnimeDrawerMetaText.Text = !string.IsNullOrWhiteSpace(yearStr) ? $"{yearStr} • Анонс" : "Анонс";
                    }
                    else
                    {
                        int count = series.TotalEpisodesCount > 0 ? series.TotalEpisodesCount : series.Dubs.Max(d => d.Episodes.Count);
                        string meta = yearStr;
                        if (series.IsMovie)
                        {
                            meta = !string.IsNullOrWhiteSpace(meta) ? $"{meta} • Фильм" : "Фильм";
                        }
                        else if (count > 0)
                        {
                            meta = !string.IsNullOrWhiteSpace(meta) ? $"{meta} • {count} серий" : $"{count} серий";
                        }
                        AnimeDrawerMetaText.Text = meta;
                    }
                }

                if (AnimeDrawerFallbackText != null)
                {
                    AnimeDrawerFallbackText.Text = series.SourceService == "Kinogo"
                        ? "Автовыбор: 1080p (Cinemar/VideoCDN/Alloha)"
                        : "Автовыбор: 1080p (CVH/Alloha) ➔ 720p (Kodik)";
                }

                if (AnimeDrawerPosterImage != null && !string.IsNullOrWhiteSpace(series.PosterUrl))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(series.PosterUrl);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        AnimeDrawerPosterImage.Source = bitmap;
                    }
                    catch { }
                }

                // Populate Dubs
                if (AnimeDubsComboBox != null)
                {
                    if (series.Dubs.Count > 0)
                    {
                        AnimeDubsComboBox.ItemsSource = series.Dubs;
                        AnimeDubsComboBox.SelectedIndex = 0;
                        if (AnimeDownloadSelectedButton != null)
                        {
                            AnimeDownloadSelectedButton.IsEnabled = true;
                            AnimeDownloadSelectedButton.Content = series.IsMovie ? "⬇ Скачать фильм" : "⬇ Скачать выбранные";
                        }
                    }
                    else
                    {
                        _selectedAnimeDub = null;
                        AnimeDubsComboBox.ItemsSource = null;
                        if (AnimeEpisodesItemsControl != null)
                        {
                            AnimeEpisodesItemsControl.ItemsSource = null;
                        }
                        if (AnimeSelectedCountText != null)
                        {
                            AnimeSelectedCountText.Text = "Серии еще не вышли (Анонс)";
                        }
                        if (AnimeDownloadSelectedButton != null)
                        {
                            AnimeDownloadSelectedButton.IsEnabled = false;
                            AnimeDownloadSelectedButton.Content = "❌ Серии не вышли";
                        }
                    }
                }

                // Open Drawer
                OpenAnimeDrawer();
            });
        }

        private void AnimeDubsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnimeDubsComboBox?.SelectedItem is AnimeDubInfo dub)
            {
                _selectedAnimeDub = dub;
                if (AnimeEpisodesItemsControl != null)
                {
                    AnimeEpisodesItemsControl.ItemsSource = dub.Episodes;
                }
                UpdateAnimeSelectionCount();
            }
        }

        private void AnimeEpisodeCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAnimeSelectionCount();
        }

        private void UpdateAnimeSelectionCount()
        {
            if (_selectedAnimeDub == null) return;

            int selectedCount = _selectedAnimeDub.Episodes.Count(ep => ep.IsSelected);
            int totalCount = _selectedAnimeDub.Episodes.Count;

            if (AnimeSelectedCountText != null)
            {
                if (_currentAnimeSeries?.IsMovie == true)
                {
                    AnimeSelectedCountText.Text = "Фильм готов к скачиванию";
                }
                else
                {
                    AnimeSelectedCountText.Text = $"Выбрано: {selectedCount} из {totalCount}";
                }
            }

            if (AnimeDownloadSelectedButton != null)
            {
                if (_currentAnimeSeries?.IsMovie == true)
                {
                    AnimeDownloadSelectedButton.Content = "⬇ Скачать фильм";
                    AnimeDownloadSelectedButton.IsEnabled = true;
                }
                else
                {
                    AnimeDownloadSelectedButton.Content = selectedCount == totalCount
                        ? $"⬇ Скачать все ({totalCount} серий)"
                        : $"⬇ Скачать выбранные ({selectedCount})";
                    AnimeDownloadSelectedButton.IsEnabled = selectedCount > 0;
                }
            }
        }

        private void AnimeSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAnimeDub == null) return;
            foreach (var ep in _selectedAnimeDub.Episodes)
            {
                ep.IsSelected = true;
            }
            UpdateAnimeSelectionCount();
        }

        private void AnimeDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAnimeDub == null) return;
            foreach (var ep in _selectedAnimeDub.Episodes)
            {
                ep.IsSelected = false;
            }
            UpdateAnimeSelectionCount();
        }

        private async void AnimeDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAnimeDub == null || _currentAnimeSeries == null) return;

            var selectedEpisodes = _selectedAnimeDub.Episodes.Where(ep => ep.IsSelected).ToList();
            if (selectedEpisodes.Count == 0)
            {
                ShowToast("Выберите хотя бы одну серию для скачивания.");
                return;
            }

            string downloadFolder = SelectedDirectory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            bool downloadSubs = AnimeDownloadSubsCheckBox?.IsChecked == true;

            int enqueuedCount = 0;

            foreach (var ep in selectedEpisodes)
            {
                string cleanSeriesTitle = SanitizeName(_currentAnimeSeries.Title);
                string cleanDub = SanitizeName(_selectedAnimeDub.Name);

                string itemTitle;
                if (_currentAnimeSeries.IsMovie || (_selectedAnimeDub.Episodes.Count == 1 && ep.EpisodeNumber == 1))
                {
                    itemTitle = $"{cleanSeriesTitle} [{cleanDub}]";
                }
                else if (ep.SeasonNumber > 1 || !string.IsNullOrWhiteSpace(ep.SeasonTitle))
                {
                    itemTitle = $"{cleanSeriesTitle} - S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} [{cleanDub}]";
                }
                else
                {
                    itemTitle = $"{cleanSeriesTitle} - E{ep.EpisodeNumber:D2} [{cleanDub}]";
                }

                AnimePlayerInfo? playerToUse;
                if (_currentAnimeSeries.SourceService == "Kinogo")
                {
                    // For Kinogo: Cinemar (1080p MP4/HLS) -> VideoCDN (1080p HLS) -> Alloha (1080p)
                    playerToUse = ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Cinemar", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("VideoCDN", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                               ?? ep.SelectedPlayer
                               ?? ep.Players.FirstOrDefault();
                }
                else
                {
                    // For YummyAnime: Aksor -> CVH -> Sibnet -> Alloha -> Kodik
                    playerToUse = ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Aksor", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("CVH", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Sibnet", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Kodik", StringComparison.OrdinalIgnoreCase))
                               ?? ep.SelectedPlayer
                               ?? ep.Players.FirstOrDefault();
                }

                string rawUrl = playerToUse?.IframeUrl ?? string.Empty;
                string resolvedUrl = string.Empty;

                if (_currentAnimeSeries.SourceService == "Kinogo" && playerToUse != null)
                {
                    try
                    {
                        resolvedUrl = await _kinogoService.ResolveEpisodeDownloadUrlAsync(playerToUse) ?? "";
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to resolve Kinogo stream URL: {ex.Message}");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(rawUrl))
                {
                    try
                    {
                        resolvedUrl = await _yummyAnimeService.ResolveEpisodeDownloadUrlAsync(rawUrl);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to resolve episode URL: {ex.Message}");
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedUrl))
                {
                    resolvedUrl = !string.IsNullOrWhiteSpace(rawUrl) 
                        ? rawUrl 
                        : $"https://ru.yummyani.me/catalog/item/{_currentAnimeSeries.Slug}?episode={ep.EpisodeNumber}&dub={Uri.EscapeDataString(_selectedAnimeDub.Name)}";
                }

                string formatCode = playerToUse?.FormatCode ?? "bestvideo+bestaudio/best";
                if (formatCode == "bestvideo+bestaudio/best" && !string.IsNullOrWhiteSpace(_selectedAnimeDub.Name))
                {
                    string dubName = _selectedAnimeDub.Name;
                    if (dubName.Contains("Eng", StringComparison.OrdinalIgnoreCase) ||
                        dubName.Contains("Original", StringComparison.OrdinalIgnoreCase) ||
                        dubName.Contains("English", StringComparison.OrdinalIgnoreCase) ||
                        dubName.Contains("Англ", StringComparison.OrdinalIgnoreCase))
                    {
                        formatCode = "bestvideo+bestaudio[language^=en]/bestvideo+bestaudio[format_id*=eng]/bestvideo+bestaudio/best";
                    }
                    else if (dubName.Contains("Рус", StringComparison.OrdinalIgnoreCase) ||
                             dubName.Contains("Дубл", StringComparison.OrdinalIgnoreCase) ||
                             dubName.Contains("Rus", StringComparison.OrdinalIgnoreCase))
                    {
                        formatCode = "bestvideo+bestaudio[language^=ru]/bestvideo+bestaudio[format_id*=rus]/bestvideo+bestaudio/best";
                    }
                    else if (dubName.Contains("Укр", StringComparison.OrdinalIgnoreCase) ||
                             dubName.Contains("Ukr", StringComparison.OrdinalIgnoreCase))
                    {
                        formatCode = "bestvideo+bestaudio[language^=uk]/bestvideo+bestaudio[format_id*=ukr]/bestvideo+bestaudio/best";
                    }
                    else if (dubName.Contains("Япон", StringComparison.OrdinalIgnoreCase) ||
                             dubName.Contains("Jap", StringComparison.OrdinalIgnoreCase))
                    {
                        formatCode = "bestvideo+bestaudio[language^=ja]/bestvideo+bestaudio[format_id*=jap]/bestvideo+bestaudio/best";
                    }
                }

                var qItem = new DownloadQueueItem
                {
                    Title = itemTitle,
                    Url = resolvedUrl,
                    FormatCode = formatCode,
                    DestinationFolder = downloadFolder,
                    DownloadSubtitles = downloadSubs,
                    SubtitleTracks = playerToUse?.Subtitles != null && playerToUse.Subtitles.Count > 0
                        ? new List<SubtitleTrackInfo>(playerToUse.Subtitles)
                        : new List<SubtitleTrackInfo>(ep.Subtitles)
                };

                _queueManager.Enqueue(qItem);
                enqueuedCount++;
            }

            string label = _currentAnimeSeries.IsMovie ? "Фильм добавлен в очередь! 🎬" : $"Добавлено в очередь: {enqueuedCount} серий! 🎬";
            ShowToast(label);
            CloseAnimeDrawer();
        }

        private void OpenAnimeDrawer()
        {
            if (AnimeDrawerGrid != null)
            {
                AnimeDrawerGrid.Visibility = Visibility.Visible;
            }
        }


        private void CloseAnimeDrawer()
        {
            if (AnimeDrawerGrid != null)
            {
                AnimeDrawerGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void AnimeDrawerClose_Click(object sender, RoutedEventArgs e)
        {
            CloseAnimeDrawer();
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Anime";
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        }
    }
}
