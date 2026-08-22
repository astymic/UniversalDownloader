using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace UniversalDownloader.Controls
{
    public enum ModernDialogType
    {
        Information,
        Warning,
        Error,
        Question,
        Success
    }

    public class ModernMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public static MessageBoxResult Show(
            string message, 
            string title = "Notification", 
            MessageBoxButton buttons = MessageBoxButton.OK, 
            MessageBoxImage icon = MessageBoxImage.Information, 
            Window? owner = null)
        {
            ModernDialogType type = icon switch
            {
                MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => ModernDialogType.Error,
                MessageBoxImage.Warning or MessageBoxImage.Exclamation => ModernDialogType.Warning,
                MessageBoxImage.Question => ModernDialogType.Question,
                MessageBoxImage.Information or MessageBoxImage.Asterisk => ModernDialogType.Information,
                _ => ModernDialogType.Information
            };

            return ShowInternal(message, title, buttons, type, owner);
        }

        public static MessageBoxResult Show(
            string message, 
            string title, 
            MessageBoxButton buttons, 
            ModernDialogType type, 
            Window? owner = null)
        {
            return ShowInternal(message, title, buttons, type, owner);
        }

        public static MessageBoxResult ShowInfo(string message, string title = "Information", Window? owner = null)
        {
            return Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information, owner);
        }

        public static MessageBoxResult ShowWarning(string message, string title = "Warning", Window? owner = null)
        {
            return Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning, owner);
        }

        public static MessageBoxResult ShowError(string message, string title = "Error", Window? owner = null)
        {
            return Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error, owner);
        }

        public static bool ShowConfirm(string message, string title = "Confirm", Window? owner = null)
        {
            var result = Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, owner);
            return result == MessageBoxResult.Yes;
        }

        private static MessageBoxResult ShowInternal(
            string message, 
            string title, 
            MessageBoxButton buttons, 
            ModernDialogType type, 
            Window? owner)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => ShowInternal(message, title, buttons, type, owner));
            }

            Window? parentWindow = owner ?? Application.Current?.MainWindow;
            if (parentWindow != null && (!parentWindow.IsLoaded || !parentWindow.IsVisible))
            {
                parentWindow = null;
            }

            var dialog = new ModernMessageBox(message, title, buttons, type, parentWindow);
            dialog.ShowDialog();
            return dialog.Result;
        }

        private ModernMessageBox(
            string message, 
            string title, 
            MessageBoxButton buttons, 
            ModernDialogType type, 
            Window? owner)
        {
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            MinHeight = 180;
            MaxHeight = 520;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            if (owner != null && owner.IsVisible)
            {
                Owner = owner;
            }
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = new FontFamily("Inter, Segoe UI, Arial");
            ShowInTaskbar = false;

            // Palette
            Color iconColor = type switch
            {
                ModernDialogType.Error => (Color)ColorConverter.ConvertFromString("#EF4444"),
                ModernDialogType.Warning => (Color)ColorConverter.ConvertFromString("#F59E0B"),
                ModernDialogType.Question => (Color)ColorConverter.ConvertFromString("#8B5CF6"),
                ModernDialogType.Success => (Color)ColorConverter.ConvertFromString("#22C55E"),
                _ => (Color)ColorConverter.ConvertFromString("#3B82F6")
            };

            Color badgeBgColor = type switch
            {
                ModernDialogType.Error => (Color)ColorConverter.ConvertFromString("#2A1215"),
                ModernDialogType.Warning => (Color)ColorConverter.ConvertFromString("#2A1F0C"),
                ModernDialogType.Question => (Color)ColorConverter.ConvertFromString("#22153B"),
                ModernDialogType.Success => (Color)ColorConverter.ConvertFromString("#0F281B"),
                _ => (Color)ColorConverter.ConvertFromString("#0F1E38")
            };

            string iconSymbol = type switch
            {
                ModernDialogType.Error => "✕",
                ModernDialogType.Warning => "⚠",
                ModernDialogType.Question => "?",
                ModernDialogType.Success => "✓",
                _ => "ℹ"
            };

            // Outer wrapper with padding for drop shadow
            var shadowBorder = new Border
            {
                Margin = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#09090B")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27272A")),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Color = Colors.Black,
                    Opacity = 0.75
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); // TitleBar
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // --- Custom Title Bar ---
            var titleBar = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121118")),
                CornerRadius = new CornerRadius(16, 16, 0, 0),
                Padding = new Thickness(18, 0, 12, 0),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F1E29")),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleTextBlock = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA")),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(titleTextBlock, 0);
            titleBarGrid.Children.Add(titleTextBlock);

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A1A1AA")),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            closeBtn.Click += (s, e) =>
            {
                Result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
                DialogResult = false;
                Close();
            };
            closeBtn.MouseEnter += (s, e) => closeBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            closeBtn.MouseLeave += (s, e) => closeBtn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A1A1AA"));

            Grid.SetColumn(closeBtn, 1);
            titleBarGrid.Children.Add(closeBtn);
            titleBar.Child = titleBarGrid;

            Grid.SetRow(titleBar, 0);
            mainGrid.Children.Add(titleBar);

            // --- Body Content ---
            var bodyGrid = new Grid
            {
                Margin = new Thickness(24, 20, 24, 16)
            };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Icon Badge
            var iconBadge = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(badgeBgColor),
                BorderBrush = new SolidColorBrush(iconColor),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            var iconText = new TextBlock
            {
                Text = iconSymbol,
                Foreground = new SolidColorBrush(iconColor),
                FontSize = type == ModernDialogType.Warning ? 18 : 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBadge.Child = iconText;
            Grid.SetColumn(iconBadge, 0);
            bodyGrid.Children.Add(iconBadge);

            // Message Text
            var messageTextBlock = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E4E4E7")),
                FontSize = 13.5,
                LineHeight = 21,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(messageTextBlock, 1);
            bodyGrid.Children.Add(messageTextBlock);

            Grid.SetRow(bodyGrid, 1);
            mainGrid.Children.Add(bodyGrid);

            // --- Bottom Action Buttons ---
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 0, 24, 20)
            };

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    var okBtn = CreatePrimaryButton("OK");
                    okBtn.Click += (s, e) => { Result = MessageBoxResult.OK; DialogResult = true; Close(); };
                    buttonsPanel.Children.Add(okBtn);
                    break;

                case MessageBoxButton.OKCancel:
                    var cancelBtn = CreateSecondaryButton("Cancel");
                    cancelBtn.Click += (s, e) => { Result = MessageBoxResult.Cancel; DialogResult = false; Close(); };
                    cancelBtn.Margin = new Thickness(0, 0, 10, 0);
                    buttonsPanel.Children.Add(cancelBtn);

                    var okBtn2 = CreatePrimaryButton("OK");
                    okBtn2.Click += (s, e) => { Result = MessageBoxResult.OK; DialogResult = true; Close(); };
                    buttonsPanel.Children.Add(okBtn2);
                    break;

                case MessageBoxButton.YesNo:
                    var noBtn = CreateSecondaryButton("No");
                    noBtn.Click += (s, e) => { Result = MessageBoxResult.No; DialogResult = false; Close(); };
                    noBtn.Margin = new Thickness(0, 0, 10, 0);
                    buttonsPanel.Children.Add(noBtn);

                    var yesBtn = CreatePrimaryButton("Yes");
                    yesBtn.Click += (s, e) => { Result = MessageBoxResult.Yes; DialogResult = true; Close(); };
                    buttonsPanel.Children.Add(yesBtn);
                    break;

                case MessageBoxButton.YesNoCancel:
                    var cancelBtn3 = CreateSecondaryButton("Cancel");
                    cancelBtn3.Click += (s, e) => { Result = MessageBoxResult.Cancel; DialogResult = false; Close(); };
                    cancelBtn3.Margin = new Thickness(0, 0, 10, 0);
                    buttonsPanel.Children.Add(cancelBtn3);

                    var noBtn3 = CreateSecondaryButton("No");
                    noBtn3.Click += (s, e) => { Result = MessageBoxResult.No; DialogResult = false; Close(); };
                    noBtn3.Margin = new Thickness(0, 0, 10, 0);
                    buttonsPanel.Children.Add(noBtn3);

                    var yesBtn3 = CreatePrimaryButton("Yes");
                    yesBtn3.Click += (s, e) => { Result = MessageBoxResult.Yes; DialogResult = true; Close(); };
                    buttonsPanel.Children.Add(yesBtn3);
                    break;
            }

            Grid.SetRow(buttonsPanel, 2);
            mainGrid.Children.Add(buttonsPanel);

            shadowBorder.Child = mainGrid;
            Content = shadowBorder;

            // Keyboard Shortcuts
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    if (buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel)
                    {
                        Result = MessageBoxResult.Yes;
                        DialogResult = true;
                    }
                    else
                    {
                        Result = MessageBoxResult.OK;
                        DialogResult = true;
                    }
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    if (buttons == MessageBoxButton.YesNo)
                    {
                        Result = MessageBoxResult.No;
                        DialogResult = false;
                    }
                    else
                    {
                        Result = MessageBoxResult.Cancel;
                        DialogResult = false;
                    }
                    Close();
                    e.Handled = true;
                }
            };
        }

        private Button CreatePrimaryButton(string text)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 90,
                Height = 36,
                Padding = new Thickness(18, 0, 18, 0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA")),
                BorderThickness = new Thickness(0)
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "btnBorder";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#8B5CF6"), 0.0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#6D28D9"), 1.0)
                }
            };
            borderFactory.SetValue(Border.BackgroundProperty, gradient);

            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenterFactory);
            template.VisualTree = borderFactory;

            // Trigger for hover
            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            var hoverGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#A78BFA"), 0.0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#7C3AED"), 1.0)
                }
            };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverGradient, "btnBorder"));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }

        private Button CreateSecondaryButton(string text)
        {
            var btn = new Button
            {
                Content = text,
                MinWidth = 80,
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Medium,
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D4D4D8")),
                BorderThickness = new Thickness(0)
            };

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "secBorder";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#18181B")));
            borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27272A")));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenterFactory);
            template.VisualTree = borderFactory;

            // Trigger for hover
            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27272A")), "secBorder"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F3F46")), "secBorder"));
            template.Triggers.Add(hoverTrigger);

            btn.Template = template;
            return btn;
        }
    }
}
