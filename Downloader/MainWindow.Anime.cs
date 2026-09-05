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
        private AnimeSeriesInfo? _currentAnimeSeries;
        private AnimeDubInfo? _selectedAnimeDub;

        public async Task<bool> CheckAndHandleAnimeUrlAsync(string url)
        {
            if (!YummyAnimeService.IsYummyAnimeUrl(url))
            {
                return false;
            }

            await LoadAnimeSeriesAsync(url);
            return true;
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

                await Dispatcher.InvokeAsync(() =>
                {
                    // Update Anime Drawer UI
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
                            if (count > 0)
                            {
                                meta = !string.IsNullOrWhiteSpace(meta) ? $"{meta} • {count} серий" : $"{count} серий";
                            }
                            AnimeDrawerMetaText.Text = meta;
                        }
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
                                AnimeDownloadSelectedButton.Content = "⬇ Скачать выбранные";
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

                    // Open Anime Drawer
                    OpenAnimeDrawer();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load anime: {ex.Message}");
                ShowToast($"Ошибка загрузки аниме: {ex.Message}");
            }
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
                AnimeSelectedCountText.Text = $"Выбрано: {selectedCount} из {totalCount}";
            }

            if (AnimeDownloadSelectedButton != null)
            {
                AnimeDownloadSelectedButton.Content = selectedCount == totalCount
                    ? $"⬇ Скачать все ({totalCount} серий)"
                    : $"⬇ Скачать выбранные ({selectedCount})";
                AnimeDownloadSelectedButton.IsEnabled = selectedCount > 0;
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

            int enqueuedCount = 0;

            foreach (var ep in selectedEpisodes)
            {
                string cleanSeriesTitle = SanitizeName(_currentAnimeSeries.Title);
                string cleanDub = SanitizeName(_selectedAnimeDub.Name);
                string itemTitle = _selectedAnimeDub.Episodes.Count == 1 && ep.EpisodeNumber == 1
                    ? $"{cleanSeriesTitle} [{cleanDub}]"
                    : $"{cleanSeriesTitle} - E{ep.EpisodeNumber:D2} [{cleanDub}]";

                // Build download target URL - prefer working streams: Aksor (1080p) -> CVH (1080p) -> Sibnet (1080p) -> Alloha (1080p decrypted stream) -> Kodik (720p decrypted stream) -> others
                var playerToUse = ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Aksor", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("CVH", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Sibnet", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Alloha", StringComparison.OrdinalIgnoreCase))
                               ?? ep.Players.FirstOrDefault(p => p.PlayerName.Contains("Kodik", StringComparison.OrdinalIgnoreCase))
                               ?? ep.SelectedPlayer
                               ?? ep.Players.FirstOrDefault();

                string rawUrl = playerToUse?.IframeUrl ?? string.Empty;
                string resolvedUrl = string.Empty;

                if (!string.IsNullOrWhiteSpace(rawUrl))
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

                var qItem = new DownloadQueueItem
                {
                    Title = itemTitle,
                    Url = resolvedUrl,
                    FormatCode = "bestvideo+bestaudio/best",
                    DestinationFolder = downloadFolder
                };

                _queueManager.Enqueue(qItem);
                enqueuedCount++;
            }

            ShowToast($"Добавлено в очередь: {enqueuedCount} серий! 🎬");
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
