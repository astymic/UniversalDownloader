using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using UniversalDownloader.Services;

namespace UniversalDownloader.Controls
{
    public class ShutdownCountdownDialog : Window
    {
        private readonly PowerAction _action;
        private readonly string _sourceName;
        private int _remainingSeconds;
        private readonly int _totalSeconds;
        private readonly DispatcherTimer _timer;
        private readonly TextBlock _countdownTextBlock;
        private readonly ProgressBar _progressBar;
        public bool IsConfirmed { get; private set; } = false;

        public static bool Show(PowerAction action, string sourceName, int seconds = 60, Window? owner = null)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => Show(action, sourceName, seconds, owner));
            }

            Window? parentWindow = owner ?? Application.Current?.MainWindow;
            if (parentWindow != null && (!parentWindow.IsLoaded || !parentWindow.IsVisible))
            {
                parentWindow = null;
            }

            var dialog = new ShutdownCountdownDialog(action, sourceName, seconds, parentWindow);
            dialog.ShowDialog();
            return dialog.IsConfirmed;
        }

        private ShutdownCountdownDialog(PowerAction action, string sourceName, int seconds, Window? owner)
        {
            _action = action;
            _sourceName = sourceName;
            _totalSeconds = Math.Max(5, seconds);
            _remainingSeconds = _totalSeconds;

            string actionName = SystemPowerService.GetActionDisplayName(_action);

            Title = $"Automatic {actionName}";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = true;
            Focusable = true;

            if (owner != null && owner.IsVisible)
            {
                Owner = owner;
            }

            // Outer drop shadow host
            var shadowGrid = new Grid
            {
                Margin = new Thickness(16)
            };

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)), // #18181B
                BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)), // #27272A
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 20, 24, 24),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 8,
                    BlurRadius = 24,
                    Opacity = 0.65
                }
            };
            mainBorder.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var contentStack = new StackPanel();

            // Header with glowing icon and title
            var headerGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 16)
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Glowing Power Icon
            var iconBorder = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(35, 245, 158, 11)), // semi-transparent amber
                BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var iconText = new TextBlock
            {
                Text = "⏻",
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBorder.Child = iconText;
            Grid.SetColumn(iconBorder, 0);
            headerGrid.Children.Add(iconBorder);

            // Title and subtitle
            var titleStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            var titleText = new TextBlock
            {
                Text = $"Automatic System {actionName}",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var subtitleText = new TextBlock
            {
                Text = $"All items in {_sourceName} have completed.",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)), // #A1A1AA
                TextWrapping = TextWrapping.Wrap
            };
            titleStack.Children.Add(titleText);
            titleStack.Children.Add(subtitleText);
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            contentStack.Children.Add(headerGrid);

            // Countdown text
            _countdownTextBlock = new TextBlock
            {
                Text = $"{actionName} in {_remainingSeconds} seconds...",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 12)
            };
            contentStack.Children.Add(_countdownTextBlock);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = _totalSeconds,
                Value = _remainingSeconds,
                Height = 6,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 22)
            };
            contentStack.Children.Add(_progressBar);

            // Buttons
            var buttonsGrid = new Grid();
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            buttonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cancelBtn = new Button
            {
                Content = "Cancel Action",
                Height = 40,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red accent
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 239, 68, 68)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => Cancel();
            Grid.SetColumn(cancelBtn, 0);
            buttonsGrid.Children.Add(cancelBtn);

            var actionNowBtn = new Button
            {
                Content = $"{actionName} Now",
                Height = 40,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                Foreground = Brushes.Black,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            actionNowBtn.Click += (s, e) => ConfirmAndExecute();
            Grid.SetColumn(actionNowBtn, 2);
            buttonsGrid.Children.Add(actionNowBtn);

            contentStack.Children.Add(buttonsGrid);
            mainBorder.Child = contentStack;
            shadowGrid.Children.Add(mainBorder);
            Content = shadowGrid;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Cancel();
                }
            };

            // Setup timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            Loaded += (s, e) =>
            {
                try { SystemSounds.Exclamation.Play(); } catch { }
                _timer.Start();
            };

            Closing += (s, e) =>
            {
                _timer.Stop();
            };
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                ConfirmAndExecute();
                return;
            }

            string actionName = SystemPowerService.GetActionDisplayName(_action);
            _countdownTextBlock.Text = $"{actionName} in {_remainingSeconds} second{(_remainingSeconds == 1 ? "" : "s")}...";
            _progressBar.Value = _remainingSeconds;

            if (_remainingSeconds <= 5)
            {
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
        }

        private void Cancel()
        {
            _timer.Stop();
            IsConfirmed = false;
            Close();
        }

        private void ConfirmAndExecute()
        {
            _timer.Stop();
            IsConfirmed = true;
            Close();
            SystemPowerService.ExecutePowerAction(_action);
        }
    }
}
