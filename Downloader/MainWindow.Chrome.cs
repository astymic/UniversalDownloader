using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private const int WM_NCLBUTTONDBLCLK = 0x00A3; 
        private const int HTCAPTION = 0x2;            
        
        private bool _isManuallyPseudoMaximized = false;
        private Rect _normalWindowBoundsBeforePseudoMaximize;
        
        private Point _startPointMaximizedDrag;
        private double _maximizedWindowWidthForDrag;

        private Screen GetScreenFromWindow()
        {
            WindowInteropHelper windowInteropHelper = new WindowInteropHelper(this);
            return Screen.FromHandle(windowInteropHelper.Handle);
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCLBUTTONDBLCLK)
            {
                if (wParam.ToInt32() == HTCAPTION)
                {
                    Dispatcher.Invoke(() =>
                    {
                        HandleDoubleClickMaximizeRestore();
                    });
                    handled = true;
                    return (IntPtr)1;
                }
            }
            return IntPtr.Zero;
        }

        private void HandleDoubleClickMaximizeRestore()
        {
            if (this.WindowState == WindowState.Maximized || _isManuallyPseudoMaximized)
            {
                MaximizeRestoreButton_Click(null, new RoutedEventArgs());
            }
            else
            {
                GoToPseudoMaximize(true);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                if (this.WindowState == WindowState.Maximized || _isManuallyPseudoMaximized)
                {
                    _startPointMaximizedDrag = e.GetPosition(this);
                    _maximizedWindowWidthForDrag = this.ActualWidth;

                    if (this.WindowState == WindowState.Maximized)
                    {
                        this.WindowState = WindowState.Normal;
                    }
                    else if (_isManuallyPseudoMaximized)
                    {
                        RestoreFromPseudoMaximize();
                    }

                    Point currentScreenMousePosition = new Point(Control.MousePosition.X, Control.MousePosition.Y);
                    if (this.ActualWidth > 0 && _maximizedWindowWidthForDrag > 0)
                    {
                        this.Left = currentScreenMousePosition.X - (_startPointMaximizedDrag.X * (this.ActualWidth / _maximizedWindowWidthForDrag));
                    }
                    else
                    {
                        this.Left = currentScreenMousePosition.X - _startPointMaximizedDrag.X;
                    }
                    this.Top = currentScreenMousePosition.Y - _startPointMaximizedDrag.Y;

                    this.DragMove();
                }
                else
                {
                    this.DragMove();
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void UpdateMaximizeRestoreButtonAndBorder(bool isMaximizedOrPseudo)
        {
            if (isMaximizedOrPseudo)
            {
                if (MaximizeRestoreButton != null)
                {
                    MaximizeRestoreButton.Content = "";
                    MaximizeRestoreButton.ToolTip = "Restore";
                }
                if (MainWindowRootBorder != null)
                {
                    MainWindowRootBorder.CornerRadius = new CornerRadius(0);
                    MainWindowRootBorder.Effect = null;
                }
            }
            else
            {
                if (MaximizeRestoreButton != null)
                {
                    MaximizeRestoreButton.Content = "";
                    MaximizeRestoreButton.ToolTip = "Maximize";
                }
                if (MainWindowRootBorder != null)
                {
                    MainWindowRootBorder.CornerRadius = new CornerRadius(16);
                    MainWindowRootBorder.Effect = (System.Windows.Media.Effects.DropShadowEffect)FindResource("WindowShadow");
                }
            }
        }

        private void RestoreFromPseudoMaximize()
        {
            if (!_isManuallyPseudoMaximized) return;

            this.Left = _normalWindowBoundsBeforePseudoMaximize.Left;
            this.Top = _normalWindowBoundsBeforePseudoMaximize.Top;
            this.Width = _normalWindowBoundsBeforePseudoMaximize.Width;
            this.Height = _normalWindowBoundsBeforePseudoMaximize.Height;
            _isManuallyPseudoMaximized = false;
            UpdateMaximizeRestoreButtonAndBorder(false);
        }

        private void GoToPseudoMaximize(bool fromUserAction = false)
        {
            if (fromUserAction || !_isManuallyPseudoMaximized)
            {
                _normalWindowBoundsBeforePseudoMaximize = new Rect(this.Left, this.Top, this.Width, this.Height);
            }

            Screen currentScreen = GetScreenFromWindow();
            if (currentScreen != null)
            {
                
                this.Left = currentScreen.WorkingArea.Left;
                this.Top = currentScreen.WorkingArea.Top;
                this.Width = currentScreen.WorkingArea.Width;
                this.Height = currentScreen.WorkingArea.Height;
            }
            else
            {
                this.Left = SystemParameters.WorkArea.Left;
                this.Top = SystemParameters.WorkArea.Top;
                this.Width = SystemParameters.WorkArea.Width;
                this.Height = SystemParameters.WorkArea.Height;
                System.Diagnostics.Debug.WriteLine("Warning: Could not determine current screen for pseudo-maximize. Falling back to primary.");
            }

            _isManuallyPseudoMaximized = true;

            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            UpdateMaximizeRestoreButtonAndBorder(true);
        }


        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                if (!_isManuallyPseudoMaximized)
                {
                    _normalWindowBoundsBeforePseudoMaximize = this.RestoreBounds;
                }
                this.WindowState = WindowState.Normal;
                GoToPseudoMaximize(true);
            }
            else if (_isManuallyPseudoMaximized)
            {
                RestoreFromPseudoMaximize();
            }
            else
            {
                GoToPseudoMaximize(true);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                _isManuallyPseudoMaximized = false;
                UpdateMaximizeRestoreButtonAndBorder(true);
            }
            else if (this.WindowState == WindowState.Normal)
            {
                if (!_isManuallyPseudoMaximized)
                {
                    UpdateMaximizeRestoreButtonAndBorder(false);
                }
            }
        }
    }
}