using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using UniversalDownloader.Models;

namespace UniversalDownloader.Controls
{
    public class GifTextLabelControl : Grid
    {
        public GifTextLabel Model { get; }

        private readonly Border _border;
        private readonly TextBlock _displayBlock;
        private readonly TextBox _editBox;
        private readonly Button _deleteButton;

        // 8 Resize Thumbs
        private readonly Thumb _thumbTopLeft;
        private readonly Thumb _thumbTop;
        private readonly Thumb _thumbTopRight;
        private readonly Thumb _thumbLeft;
        private readonly Thumb _thumbRight;
        private readonly Thumb _thumbBottomLeft;
        private readonly Thumb _thumbBottom;
        private readonly Thumb _thumbBottomRight;

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private Point _controlStartPos;

        public event Action<GifTextLabelControl>? DeleteRequested;
        public event Action<GifTextLabelControl>? Selected;
        public event Action<Point>? CreateNewRequested;
        public event Action<GifTextLabelControl>? PositionChanged;
        public event Action<GifTextLabelControl>? FormattingChanged;

        public Func<bool>? IsCursorToolActive;
        public Func<bool>? IsTextToolActive;

        public GifTextLabelControl(GifTextLabel model)
        {
            Model = model;

            ClipToBounds = false;
            Cursor = Cursors.Arrow;

            // Border container with selection state
            _border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4)
            };

            // TextBlock for crisp display with drop shadow
            _displayBlock = new TextBlock
            {
                Text = model.Text,
                TextAlignment = model.Alignment,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 6,
                    ShadowDepth = 1.5,
                    Opacity = 0.95
                }
            };

            // Inline TextBox for editing
            _editBox = new TextBox
            {
                Text = model.Text,
                Background = new SolidColorBrush(Color.FromArgb(200, 24, 24, 27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderThickness = new Thickness(1),
                CaretBrush = Brushes.White,
                Padding = new Thickness(4, 2, 4, 2),
                AcceptsReturn = true,
                Visibility = Visibility.Collapsed,
                MinWidth = 60
            };

            _editBox.KeyDown += EditBox_KeyDown;
            _editBox.LostFocus += EditBox_LostFocus;
            _editBox.GotFocus += (s, e) =>
            {
                SetSelected(true);
                Selected?.Invoke(this);
            };
            _editBox.PreviewMouseLeftButtonDown += (s, e) =>
            {
                SetSelected(true);
                Selected?.Invoke(this);
            };

            // Small delete button at top right
            _deleteButton = new Button
            {
                Content = "✕",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(220, 239, 68, 68)),
                BorderThickness = new Thickness(0),
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -12, -12, 0),
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed
            };
            _deleteButton.Click += (s, e) =>
            {
                e.Handled = true;
                DeleteRequested?.Invoke(this);
            };

            var contentGrid = new Grid();
            contentGrid.Children.Add(_displayBlock);
            contentGrid.Children.Add(_editBox);

            _border.Child = contentGrid;
            Children.Add(_border);
            Children.Add(_deleteButton);

            // Initialize 8 resize thumbs
            _thumbTopLeft = CreateResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-5, -5, 0, 0), "TopLeft");
            _thumbTop = CreateResizeThumb(Cursors.SizeNS, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, -5, 0, 0), "Top");
            _thumbTopRight = CreateResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -5, -5, 0), "TopRight");
            _thumbLeft = CreateResizeThumb(Cursors.SizeWE, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(-5, 0, 0, 0), "Left");
            _thumbRight = CreateResizeThumb(Cursors.SizeWE, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, -5, 0), "Right");
            _thumbBottomLeft = CreateResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-5, 0, 0, -5), "BottomLeft");
            _thumbBottom = CreateResizeThumb(Cursors.SizeNS, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, -5), "Bottom");
            _thumbBottomRight = CreateResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -5, -5), "BottomRight");

            Children.Add(_thumbTopLeft);
            Children.Add(_thumbTop);
            Children.Add(_thumbTopRight);
            Children.Add(_thumbLeft);
            Children.Add(_thumbRight);
            Children.Add(_thumbBottomLeft);
            Children.Add(_thumbBottom);
            Children.Add(_thumbBottomRight);

            RefreshFormatting();

            PreviewMouseLeftButtonDown += Control_PreviewMouseLeftButtonDown;
            MouseLeftButtonDown += Control_MouseLeftButtonDown;
            MouseMove += Control_MouseMove;
            MouseLeftButtonUp += Control_MouseLeftButtonUp;
        }

        private Thumb CreateResizeThumb(Cursor cursor, HorizontalAlignment hAlign, VerticalAlignment vAlign, Thickness margin, string tag)
        {
            var thumb = new Thumb
            {
                Width = 9,
                Height = 9,
                Cursor = cursor,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Margin = margin,
                Tag = tag,
                Visibility = Visibility.Collapsed
            };

            // Custom thumb template for crisp rectangular handle
            var template = new ControlTemplate(typeof(Thumb));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(56, 189, 248)));
            borderFactory.SetValue(Border.BorderBrushProperty, Brushes.Black);
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            template.VisualTree = borderFactory;
            thumb.Template = template;

            thumb.DragDelta += ResizeThumb_DragDelta;
            return thumb;
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Tag is not string tag) return;

            switch (tag)
            {
                case "BottomRight":
                    ApplyFontSizeDelta((e.HorizontalChange + e.VerticalChange) * 0.4);
                    break;
                case "Right":
                    ApplyFontSizeDelta(e.HorizontalChange * 0.4);
                    break;
                case "Bottom":
                    ApplyFontSizeDelta(e.VerticalChange * 0.4);
                    break;
                case "TopRight":
                {
                    double oldH = ActualHeight;
                    ApplyFontSizeDelta((e.HorizontalChange - e.VerticalChange) * 0.4);
                    UpdateLayout();
                    double hDiff = ActualHeight - oldH;
                    double curTop = Canvas.GetTop(this);
                    if (double.IsNaN(curTop)) curTop = Model.Y;
                    Canvas.SetTop(this, curTop - hDiff);
                    Model.Y = curTop - hDiff;
                    break;
                }
                case "BottomLeft":
                {
                    double oldW = ActualWidth;
                    ApplyFontSizeDelta((-e.HorizontalChange + e.VerticalChange) * 0.4);
                    UpdateLayout();
                    double wDiff = ActualWidth - oldW;
                    double curLeft = Canvas.GetLeft(this);
                    if (double.IsNaN(curLeft)) curLeft = Model.X;
                    Canvas.SetLeft(this, curLeft - wDiff);
                    Model.X = curLeft - wDiff;
                    break;
                }
                case "Left":
                {
                    double oldW = ActualWidth;
                    ApplyFontSizeDelta(-e.HorizontalChange * 0.4);
                    UpdateLayout();
                    double wDiff = ActualWidth - oldW;
                    double curLeft = Canvas.GetLeft(this);
                    if (double.IsNaN(curLeft)) curLeft = Model.X;
                    Canvas.SetLeft(this, curLeft - wDiff);
                    Model.X = curLeft - wDiff;
                    break;
                }
                case "Top":
                {
                    double oldH = ActualHeight;
                    ApplyFontSizeDelta(-e.VerticalChange * 0.4);
                    UpdateLayout();
                    double hDiff = ActualHeight - oldH;
                    double curTop = Canvas.GetTop(this);
                    if (double.IsNaN(curTop)) curTop = Model.Y;
                    Canvas.SetTop(this, curTop - hDiff);
                    Model.Y = curTop - hDiff;
                    break;
                }
                case "TopLeft":
                {
                    double oldW = ActualWidth;
                    double oldH = ActualHeight;
                    ApplyFontSizeDelta((-e.HorizontalChange - e.VerticalChange) * 0.4);
                    UpdateLayout();
                    double wDiff = ActualWidth - oldW;
                    double hDiff = ActualHeight - oldH;
                    double curLeft = Canvas.GetLeft(this);
                    double curTop = Canvas.GetTop(this);
                    if (double.IsNaN(curLeft)) curLeft = Model.X;
                    if (double.IsNaN(curTop)) curTop = Model.Y;
                    Canvas.SetLeft(this, curLeft - wDiff);
                    Canvas.SetTop(this, curTop - hDiff);
                    Model.X = curLeft - wDiff;
                    Model.Y = curTop - hDiff;
                    break;
                }
            }

            PositionChanged?.Invoke(this);
            e.Handled = true;
        }

        public void ApplyFontSize(double newSize)
        {
            Model.FontSize = Math.Clamp(Math.Round(newSize), 8.0, 180.0);
            _displayBlock.FontSize = Model.FontSize;
            _editBox.FontSize = Model.FontSize;
            FormattingChanged?.Invoke(this);
        }

        private void ApplyFontSizeDelta(double delta)
        {
            ApplyFontSize(Model.FontSize + delta);
        }

        public void RefreshFormatting()
        {
            try
            {
                var fontName = string.IsNullOrWhiteSpace(Model.FontFamily) ? "Segoe UI" : Model.FontFamily;
                var ff = new FontFamily($"{fontName}, Segoe UI, Segoe UI Emoji, sans-serif");
                _displayBlock.FontFamily = ff;
                _editBox.FontFamily = ff;
            }
            catch
            {
                _displayBlock.FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, sans-serif");
                _editBox.FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji, sans-serif");
            }

            _displayBlock.FontSize = Model.FontSize;
            _editBox.FontSize = Model.FontSize;

            var weight = Model.IsBold ? FontWeights.Bold : FontWeights.Normal;
            var style = Model.IsItalic ? FontStyles.Italic : FontStyles.Normal;
            _displayBlock.FontWeight = weight;
            _editBox.FontWeight = weight;
            _displayBlock.FontStyle = style;
            _editBox.FontStyle = style;

            var decorations = new TextDecorationCollection();
            if (Model.IsUnderline) decorations.Add(TextDecorations.Underline);
            if (Model.IsStrikethrough) decorations.Add(TextDecorations.Strikethrough);
            _displayBlock.TextDecorations = decorations;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(Model.TextColor);
                var brush = new SolidColorBrush(color);
                _displayBlock.Foreground = brush;
                _editBox.Foreground = brush;
            }
            catch
            {
                _displayBlock.Foreground = Brushes.White;
                _editBox.Foreground = Brushes.White;
            }

            _displayBlock.TextAlignment = Model.Alignment;
            _editBox.TextAlignment = Model.Alignment;

            FormattingChanged?.Invoke(this);
        }

        private void UpdateHandlesVisibility()
        {
            bool showHandles = Model.IsSelected && !Model.IsEditing;
            var vis = showHandles ? Visibility.Visible : Visibility.Collapsed;

            _thumbTopLeft.Visibility = vis;
            _thumbTop.Visibility = vis;
            _thumbTopRight.Visibility = vis;
            _thumbLeft.Visibility = vis;
            _thumbRight.Visibility = vis;
            _thumbBottomLeft.Visibility = vis;
            _thumbBottom.Visibility = vis;
            _thumbBottomRight.Visibility = vis;
        }

        public void SetSelected(bool selected)
        {
            bool wasSelected = Model.IsSelected;
            Model.IsSelected = selected;
            if (selected)
            {
                _border.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                _border.Background = new SolidColorBrush(Color.FromArgb(40, 56, 189, 248));
                _deleteButton.Visibility = Visibility.Visible;
                UpdateHandlesVisibility();
                if (!wasSelected)
                {
                    Selected?.Invoke(this);
                }
            }
            else
            {
                _border.BorderBrush = Brushes.Transparent;
                _border.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                _deleteButton.Visibility = Visibility.Collapsed;
                UpdateHandlesVisibility();
                if (Model.IsEditing)
                {
                    ExitEditMode();
                }
            }
        }

        public void EnterEditMode()
        {
            Model.IsEditing = true;
            _displayBlock.Visibility = Visibility.Collapsed;
            _editBox.Visibility = Visibility.Visible;
            _editBox.Text = Model.Text;
            _editBox.Focus();
            _editBox.SelectAll();
            UpdateHandlesVisibility();
            SetSelected(true);
            Selected?.Invoke(this);
        }

        public void ExitEditMode()
        {
            if (!Model.IsEditing) return;

            Model.IsEditing = false;
            string newText = _editBox.Text?.Trim() ?? "";

            // If empty or draft with no text, delete the label immediately!
            if (string.IsNullOrWhiteSpace(newText) || (Model.IsDraft && string.IsNullOrWhiteSpace(newText)))
            {
                DeleteRequested?.Invoke(this);
                return;
            }

            Model.IsDraft = false;
            Model.Text = newText;
            _displayBlock.Text = newText;
            _editBox.Visibility = Visibility.Collapsed;
            _displayBlock.Visibility = Visibility.Visible;
            UpdateHandlesVisibility();
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                ExitEditMode();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ExitEditMode();
            }
        }

        private void EditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
        }

        private void Control_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsTextToolActive?.Invoke() == true)
            {
                // When text tool is active, clicking on an existing label creates a new label on top!
                Point clickInCanvas = e.GetPosition(Parent as IInputElement);
                CreateNewRequested?.Invoke(clickInCanvas);
                e.Handled = true;
                return;
            }

            if (IsCursorToolActive?.Invoke() != true) return;

            // If clicking on delete button or resize thumb, let their handlers take care of it
            if (e.OriginalSource is DependencyObject dep)
            {
                if (IsChildOf(dep, _deleteButton)) return;
                if (FindVisualParent<Thumb>(dep) != null) return;
            }

            // Immediately mark selected & notify MainWindow so the formatting ribbon appears and syncs!
            SetSelected(true);
            Selected?.Invoke(this);

            if (e.ClickCount == 2)
            {
                // Double click with cursor tool -> Enter edit mode
                e.Handled = true;
                EnterEditMode();
                return;
            }
        }

        private void Control_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsCursorToolActive?.Invoke() != true) return;
            if (Model.IsEditing) return;

            _isDragging = true;
            _dragStartPoint = e.GetPosition(Parent as IInputElement);
            _controlStartPos = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
            if (double.IsNaN(_controlStartPos.X)) _controlStartPos.X = Model.X;
            if (double.IsNaN(_controlStartPos.Y)) _controlStartPos.Y = Model.Y;

            CaptureMouse();
            e.Handled = true;
        }

        private void Control_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && IsMouseCaptured)
            {
                Point currentPoint = e.GetPosition(Parent as IInputElement);
                double deltaX = currentPoint.X - _dragStartPoint.X;
                double deltaY = currentPoint.Y - _dragStartPoint.Y;

                double newX = _controlStartPos.X + deltaX;
                double newY = _controlStartPos.Y + deltaY;

                Canvas.SetLeft(this, newX);
                Canvas.SetTop(this, newY);

                Model.X = newX;
                Model.Y = newY;

                PositionChanged?.Invoke(this);
                e.Handled = true;
            }
        }

        private void Control_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                PositionChanged?.Invoke(this);
                e.Handled = true;
            }
        }

        private static bool IsChildOf(DependencyObject? child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
