using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using UniversalDownloader.Models;

namespace UniversalDownloader.Controls
{
    public class BatchImportDialog : Window
    {
        private TextBox _urlsTextBox = null!;
        private TextBlock _detectedCountTextBlock = null!;
        private ComboBox _formatComboBox = null!;
        private TextBox _folderTextBox = null!;
        private Button _queueButton = null!;

        public List<DownloadQueueItem> ResultItems { get; private set; } = new();
        public bool Confirmed { get; private set; } = false;

        public static (bool Confirmed, List<DownloadQueueItem> Items) Show(string defaultFolder, Window? owner = null)
        {
            var dialog = new BatchImportDialog(defaultFolder)
            {
                Owner = owner ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
            return (dialog.Confirmed, dialog.ResultItems);
        }

        public BatchImportDialog(string defaultFolder)
        {
            Title = "Batch URL Import";
            Width = 620;
            Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Inter, Segoe UI, Arial");

            ResizeMode = ResizeMode.NoResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 44,
                CornerRadius = new CornerRadius(16),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            Content = BuildUi(defaultFolder);
        }

        private UIElement BuildUi(string defaultFolder)
        {
            var rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 14, 20)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 30,
                    ShadowDepth = 8,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); // Title bar
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) }); // Buttons

            // --- Title Bar ---
            var titleBar = new Grid { Background = Brushes.Transparent };
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = "📋",
                FontSize = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Batch Multi-URL Import",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeButton = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 12, 0)
            };
            closeButton.Click += (s, e) => Close();

            titleBar.Children.Add(titleStack);
            titleBar.Children.Add(closeButton);
            Grid.SetRow(titleBar, 0);
            mainGrid.Children.Add(titleBar);

            // --- Content ---
            var contentStack = new StackPanel { Margin = new Thickness(24, 0, 24, 0) };

            contentStack.Children.Add(new TextBlock
            {
                Text = "Paste one URL per line (YouTube, Spotify, TikTok, Instagram, Twitter/X, direct files, etc.):",
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // URLs Text Box
            var textBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Height = 180,
                Padding = new Thickness(10)
            };

            _urlsTextBox = new TextBox
            {
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12.5,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CaretBrush = Brushes.White
            };
            _urlsTextBox.TextChanged += (s, e) => UpdateDetectedUrls();
            textBorder.Child = _urlsTextBox;
            contentStack.Children.Add(textBorder);

            // Detected Count Badge
            _detectedCountTextBlock = new TextBlock
            {
                Text = "0 valid URLs detected",
                Foreground = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 16)
            };
            contentStack.Children.Add(_detectedCountTextBlock);

            // Options Grid
            var optionsGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            optionsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            optionsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

            // Format Row
            var formatLabel = new TextBlock
            {
                Text = "Download Format:",
                Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231)),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _formatComboBox = new ComboBox
            {
                Height = 32,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            _formatComboBox.Items.Add("Auto / Best Video + Audio");
            _formatComboBox.Items.Add("Audio (MP3 320kbps)");
            _formatComboBox.Items.Add("Audio (Best Quality / M4A)");
            _formatComboBox.Items.Add("Video (1080p Full HD)");
            _formatComboBox.Items.Add("Video (720p HD)");
            _formatComboBox.SelectedIndex = 0;

            Grid.SetRow(formatLabel, 0); Grid.SetColumn(formatLabel, 0);
            Grid.SetRow(_formatComboBox, 0); Grid.SetColumn(_formatComboBox, 1);
            optionsGrid.Children.Add(formatLabel);
            optionsGrid.Children.Add(_formatComboBox);

            // Folder Row
            var folderLabel = new TextBlock
            {
                Text = "Save To Folder:",
                Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231)),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };

            var folderGrid = new Grid();
            folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            _folderTextBox = new TextBox
            {
                Text = defaultFolder,
                Height = 32,
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                VerticalAlignment = VerticalAlignment.Center
            };

            var browseBtn = new Button
            {
                Content = "Browse",
                Height = 32,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0)
            };
            browseBtn.Click += (s, e) =>
            {
                var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog
                {
                    Description = "Select Destination Folder",
                    UseDescriptionForTitle = true,
                    SelectedPath = _folderTextBox.Text
                };
                if (dialog.ShowDialog(this) == true)
                {
                    _folderTextBox.Text = dialog.SelectedPath;
                }
            };

            Grid.SetColumn(_folderTextBox, 0);
            Grid.SetColumn(browseBtn, 1);
            folderGrid.Children.Add(_folderTextBox);
            folderGrid.Children.Add(browseBtn);

            Grid.SetRow(folderLabel, 1); Grid.SetColumn(folderLabel, 0);
            Grid.SetRow(folderGrid, 1); Grid.SetColumn(folderGrid, 1);
            optionsGrid.Children.Add(folderLabel);
            optionsGrid.Children.Add(folderGrid);

            contentStack.Children.Add(optionsGrid);

            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);

            // --- Buttons ---
            var buttonsBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(18, 17, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 0, 24, 0)
            };

            var buttonsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 36,
                FontSize = 12.5,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 12, 0)
            };
            cancelBtn.Click += (s, e) => Close();

            _queueButton = new Button
            {
                Content = "📥 Queue All (0 items)",
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                IsEnabled = false
            };
            _queueButton.Click += OnQueueAllClicked;

            buttonsStack.Children.Add(cancelBtn);
            buttonsStack.Children.Add(_queueButton);
            buttonsBorder.Child = buttonsStack;

            Grid.SetRow(buttonsBorder, 2);
            mainGrid.Children.Add(buttonsBorder);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private List<string> GetCleanUrls()
        {
            string text = _urlsTextBox.Text ?? "";
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var validUrls = new List<string>();

            foreach (var line in lines)
            {
                string u = line.Trim();
                if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    validUrls.Add(u);
                }
            }

            return validUrls;
        }

        private void UpdateDetectedUrls()
        {
            var urls = GetCleanUrls();
            _detectedCountTextBlock.Text = $"{urls.Count} valid URL{(urls.Count == 1 ? "" : "s")} detected";
            _queueButton.Content = $"📥 Queue All ({urls.Count} item{(urls.Count == 1 ? "" : "s")})";
            _queueButton.IsEnabled = urls.Count > 0;
        }

        private void OnQueueAllClicked(object sender, RoutedEventArgs e)
        {
            var urls = GetCleanUrls();
            if (urls.Count == 0) return;

            string folder = _folderTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("Please select a valid destination folder.", "Folder Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int formatIndex = _formatComboBox.SelectedIndex;
            bool isAudioOnly = formatIndex == 1 || formatIndex == 2;
            string audioFormat = formatIndex == 1 ? "mp3" : "best";
            string formatCode = formatIndex switch
            {
                1 => "bestaudio/best",
                2 => "bestaudio/best",
                3 => "bestvideo[height<=1080]+bestaudio/best",
                4 => "bestvideo[height<=720]+bestaudio/best",
                _ => "bestvideo+bestaudio/best"
            };

            ResultItems.Clear();
            int i = 0;
            foreach (var url in urls)
            {
                i++;
                ResultItems.Add(new DownloadQueueItem
                {
                    Url = url,
                    Title = $"Batch Item #{i}",
                    DestinationFolder = folder,
                    IsAudioOnly = isAudioOnly,
                    AudioFormat = audioFormat,
                    FormatCode = formatCode,
                    Status = QueueItemStatus.Queued,
                    StatusText = "Queued"
                });
            }

            Confirmed = true;
            Close();
        }
    }
}
