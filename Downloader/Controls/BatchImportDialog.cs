using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
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

            _formatComboBox = CreateDarkComboBox();
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

            _queueButton = CreateQueueButton();
            _queueButton.Click += OnQueueAllClicked;

            buttonsStack.Children.Add(cancelBtn);
            buttonsStack.Children.Add(_queueButton);
            buttonsBorder.Child = buttonsStack;

            Grid.SetRow(buttonsBorder, 2);
            mainGrid.Children.Add(buttonsBorder);

            rootBorder.Child = mainGrid;
            return rootBorder;
        }

        private Button CreateQueueButton()
        {
            string xaml = @"
            <Button xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                    Height=""36""
                    Padding=""16,0,16,0""
                    FontSize=""12.5""
                    FontWeight=""SemiBold""
                    Cursor=""Hand""
                    IsEnabled=""False"">
                <Button.Style>
                    <Style TargetType=""Button"">
                        <Setter Property=""Template"">
                            <Setter.Value>
                                <ControlTemplate TargetType=""Button"">
                                    <Border x:Name=""btnBorder""
                                            Background=""#FAFAFA""
                                            BorderBrush=""#E4E4E7""
                                            BorderThickness=""1""
                                            CornerRadius=""8""
                                            Padding=""{TemplateBinding Padding}"">
                                        <ContentPresenter HorizontalAlignment=""Center"" 
                                                          VerticalAlignment=""Center"" 
                                                          TextBlock.Foreground=""#09090B""
                                                          TextBlock.FontWeight=""SemiBold""
                                                          x:Name=""content""/>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property=""IsMouseOver"" Value=""True"">
                                            <Setter TargetName=""btnBorder"" Property=""Background"" Value=""#FFFFFF""/>
                                            <Setter TargetName=""btnBorder"" Property=""BorderBrush"" Value=""#A1A1AA""/>
                                        </Trigger>
                                        <Trigger Property=""IsPressed"" Value=""True"">
                                            <Setter TargetName=""btnBorder"" Property=""Background"" Value=""#E4E4E7""/>
                                        </Trigger>
                                        <Trigger Property=""IsEnabled"" Value=""False"">
                                            <Setter TargetName=""btnBorder"" Property=""Background"" Value=""#27272A""/>
                                            <Setter TargetName=""btnBorder"" Property=""BorderBrush"" Value=""#3F3F46""/>
                                            <Setter TargetName=""content"" Property=""TextBlock.Foreground"" Value=""#71717A""/>
                                            <Setter Property=""Opacity"" Value=""0.7""/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </Button.Style>
                <Button.Content>📥 Queue All (0 items)</Button.Content>
            </Button>";

            return (Button)XamlReader.Parse(xaml);
        }

        private ComboBox CreateDarkComboBox()
        {
            string xaml = @"
            <ComboBox xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                      xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
                      Height=""34""
                      FontSize=""12.5""
                      Foreground=""#FAFAFA""
                      Background=""#18181B""
                      BorderBrush=""#3F3F46""
                      BorderThickness=""1""
                      Padding=""12,6""
                      VerticalAlignment=""Center""
                      MaxDropDownHeight=""220"">
                <ComboBox.ItemContainerStyle>
                    <Style TargetType=""ComboBoxItem"">
                        <Setter Property=""Foreground"" Value=""#FAFAFA""/>
                        <Setter Property=""Background"" Value=""Transparent""/>
                        <Setter Property=""Padding"" Value=""12,8""/>
                        <Setter Property=""FontSize"" Value=""12.5""/>
                        <Setter Property=""Cursor"" Value=""Hand""/>
                        <Setter Property=""Template"">
                            <Setter.Value>
                                <ControlTemplate TargetType=""ComboBoxItem"">
                                    <Border x:Name=""itemBorder"" Background=""{TemplateBinding Background}"" Padding=""{TemplateBinding Padding}"" CornerRadius=""6"" Margin=""4,2"">
                                        <ContentPresenter HorizontalAlignment=""Left"" VerticalAlignment=""Center""/>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property=""IsHighlighted"" Value=""True"">
                                            <Setter TargetName=""itemBorder"" Property=""Background"" Value=""#27272A""/>
                                        </Trigger>
                                        <Trigger Property=""IsSelected"" Value=""True"">
                                            <Setter TargetName=""itemBorder"" Property=""Background"" Value=""#2E2248""/>
                                            <Setter Property=""Foreground"" Value=""#A78BFA""/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </ComboBox.ItemContainerStyle>
                <ComboBox.Template>
                    <ControlTemplate TargetType=""ComboBox"">
                        <Grid>
                            <Border x:Name=""MainBorder"" 
                                    Background=""#18181B"" 
                                    BorderBrush=""#3F3F46"" 
                                    BorderThickness=""1"" 
                                    CornerRadius=""8"">
                                <Grid Margin=""10,4,8,4"">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width=""*""/>
                                        <ColumnDefinition Width=""Auto""/>
                                    </Grid.ColumnDefinitions>
                                    <ContentPresenter Grid.Column=""0"" 
                                                      IsHitTestVisible=""False"" 
                                                      Content=""{TemplateBinding SelectionBoxItem}"" 
                                                      VerticalAlignment=""Center"" 
                                                      HorizontalAlignment=""Left""
                                                      TextBlock.Foreground=""#FAFAFA""/>
                                    <ToggleButton Grid.Column=""1"" 
                                                  Focusable=""False"" 
                                                  IsChecked=""{Binding Path=IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"" 
                                                  ClickMode=""Press"" 
                                                  Width=""24"" 
                                                  Background=""Transparent""
                                                  BorderThickness=""0""
                                                  Cursor=""Hand"">
                                        <ToggleButton.Template>
                                            <ControlTemplate TargetType=""ToggleButton"">
                                                <Border Background=""Transparent"">
                                                    <Path x:Name=""Arrow"" Data=""M7,10L12,15L17,10"" Stroke=""#A1A1AA"" StrokeThickness=""1.8"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Stretch=""None""/>
                                                </Border>
                                                <ControlTemplate.Triggers>
                                                    <Trigger Property=""IsMouseOver"" Value=""True"">
                                                        <Setter TargetName=""Arrow"" Property=""Stroke"" Value=""#8B5CF6""/>
                                                    </Trigger>
                                                    <Trigger Property=""IsChecked"" Value=""True"">
                                                        <Setter TargetName=""Arrow"" Property=""Stroke"" Value=""#8B5CF6""/>
                                                        <Setter TargetName=""Arrow"" Property=""Data"" Value=""M7,15L12,10L17,15""/>
                                                    </Trigger>
                                                </ControlTemplate.Triggers>
                                            </ControlTemplate>
                                        </ToggleButton.Template>
                                    </ToggleButton>
                                </Grid>
                            </Border>
                            <Popup x:Name=""Popup"" Placement=""Bottom"" IsOpen=""{TemplateBinding IsDropDownOpen}"" AllowsTransparency=""True"" Focusable=""False"" PopupAnimation=""Slide"" StaysOpen=""False"">
                                <Grid MaxHeight=""{TemplateBinding MaxDropDownHeight}"" MinWidth=""{Binding ActualWidth, ElementName=MainBorder}"">
                                    <Border Background=""#18181B"" BorderBrush=""#3F3F46"" BorderThickness=""1"" CornerRadius=""8"" Margin=""0,4,0,4"" Padding=""2"">
                                        <Border.Effect>
                                            <DropShadowEffect BlurRadius=""16"" ShadowDepth=""6"" Color=""Black"" Opacity=""0.6""/>
                                        </Border.Effect>
                                        <ScrollViewer SnapsToDevicePixels=""True"" VerticalScrollBarVisibility=""Auto"">
                                            <StackPanel IsItemsHost=""True""/>
                                        </ScrollViewer>
                                    </Border>
                                </Grid>
                            </Popup>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property=""IsMouseOver"" Value=""True"">
                                <Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#71717A""/>
                            </Trigger>
                            <Trigger Property=""IsFocused"" Value=""True"">
                                <Setter TargetName=""MainBorder"" Property=""BorderBrush"" Value=""#8B5CF6""/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </ComboBox.Template>
            </ComboBox>";

            return (ComboBox)XamlReader.Parse(xaml);
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
