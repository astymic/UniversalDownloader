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
                        string meta = $"{series.Year} • {series.TotalEpisodesCount} серий";
                        if (!string.IsNullOrWhiteSpace(series.Rating))
                        {
                            meta += $" • ⭐ {series.Rating}";
                        }
                        AnimeDrawerMetaText.Text = meta;
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
                        AnimeDubsComboBox.ItemsSource = series.Dubs;
                        if (series.Dubs.Count > 0)
                        {
                            AnimeDubsComboBox.SelectedIndex = 0;
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
                AnimeDownloadSelectedButton.IsEnabled = selectedCount > 0;
                AnimeDownloadSelectedButton.Content = selectedCount == totalCount
                    ? $"⬇ Скачать все ({totalCount} серий)"
                    : $"⬇ Скачать выбранные ({selectedCount})";
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

        private void AnimeDownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnimeSeries == null || _selectedAnimeDub == null) return;

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
                string quality = ep.BestQualityText;
                string itemTitle = $"{cleanSeriesTitle} - E{ep.EpisodeNumber:D2} [{cleanDub}] [{quality}]";

                // Build download target URL
                string targetUrl = ep.SelectedPlayer?.IframeUrl ?? string.Empty;
                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    // Fallback to series URL with episode marker
                    targetUrl = $"https://ru.yummyani.me/catalog/item/{_currentAnimeSeries.Slug}?episode={ep.EpisodeNumber}&dub={Uri.EscapeDataString(_selectedAnimeDub.Name)}";
                }

                var qItem = new DownloadQueueItem
                {
                    Title = itemTitle,
                    Url = targetUrl,
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
