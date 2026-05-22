using AngryMouse.Animation;
using AngryMouse.Cursors;
using AngryMouse.Screen;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static AngryMouse.Util.WindowUtil;

namespace AngryMouse
{
    /// <summary>
    /// Interaction logic for OverlayWindow.xaml
    /// </summary>
    public partial class OverlayWindow
    {
        /// <summary>
        /// Show debug info
        /// </summary>
        private readonly bool _debug;

        /// <summary>
        /// Moves the cursor hotspot around the canvas.
        /// </summary>
        private readonly TranslateTransform _cursorTranslate = new TranslateTransform();

        /// <summary>
        /// Moves the cursor image so its hotspot lands on the mouse position before scaling.
        /// </summary>
        private readonly TranslateTransform _cursorHotspotTranslate = new TranslateTransform();

        /// <summary>
        /// Scales the cursor.
        /// </summary>
        private readonly ScaleTransform _cursorScale = new ScaleTransform
        {
            ScaleX = 0,
            ScaleY = 0
        };

        /// <summary>
        /// The screen this window is open in.
        /// </summary>
        private readonly ScreenInfo _screen;

        /// <summary>
        /// DPI scale info of the current screen.
        /// </summary>
        private DpiScale _dpiInfo;

        /// <summary>
        /// Animates mouse growing and shrinking
        /// </summary>
        private MouseAnimator _mouseAnimator;

        private readonly DispatcherTimer _topmostRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private bool _systemCursorOverrideActive;

        /// <summary>
        /// Main constructor.
        /// </summary>
        /// <param name="screen">The window to show the screen in.</param>
        /// <param name="debug">Show debug information on screens</param>
        public OverlayWindow(ScreenInfo screen, bool debug = false)
        {
            InitializeComponent();

            _debug = debug;
            _screen = screen;
            _topmostRefreshTimer.Tick += TopmostRefreshTimer_Tick;

        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Do not capture any mouse events
            // TODO I suspect this is the reason we cannot replace the cursor (hide it) since
            // the cursor draws on top of the big cursor.
            // Also hide window from alt-tab menu
            SetWindowStyles(this, ExtendedWindowStyles.WS_EX_TOOLWINDOW | ExtendedWindowStyles.WS_EX_TRANSPARENT);
        }

        protected override void OnDpiChanged(DpiScale oldDpiScaleInfo, DpiScale newDpiScaleInfo)
        {
            _dpiInfo = newDpiScaleInfo;
            if (_mouseAnimator != null)
            {
                _mouseAnimator.DpiInfo = newDpiScaleInfo;
            }
        }

        /// <summary>
        /// Called when the window is successfully loaded. Does some view initialization.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_debug)
            {
                Root.Children.Remove(DebugInfo);
                OverlayCanvas.Children.Remove(MousePosDebug);
            }

            TransformGroup transformGroup = new TransformGroup();

            transformGroup.Children.Add(_cursorHotspotTranslate);
            transformGroup.Children.Add(_cursorScale);
            transformGroup.Children.Add(_cursorTranslate);

            CursorHost.RenderTransform = transformGroup;

            // Open this window maximized on the appropriate screen
            Top = _screen.BoundY;
            Left = _screen.BoundX;
            WindowState = WindowState.Maximized;

            OverlayCanvas.Width = _screen.BoundWidth;
            OverlayCanvas.Height = _screen.BoundHeight;

            _dpiInfo = VisualTreeHelper.GetDpi(this);

            Viewbox.Width = _screen.BoundWidth / _dpiInfo.PixelsPerDip;
            Viewbox.Height = _screen.BoundHeight / _dpiInfo.PixelsPerDip;

            if (_debug)
            {
                MousePosDebug.Width = MousePosDebug.Width * _dpiInfo.PixelsPerDip;
                MousePosDebug.Height = MousePosDebug.Height * _dpiInfo.PixelsPerDip;
            }

            _mouseAnimator = new MouseAnimator(_cursorScale, _dpiInfo);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _topmostRefreshTimer.Stop();
            _topmostRefreshTimer.Tick -= TopmostRefreshTimer_Tick;
        }

        public void UpdateMousePosition(int x, int y)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateMousePosition(x, y)));
                return;
            }

            var mouseInScreen = x >= _screen.BoundX && x <= _screen.BoundX + _screen.BoundWidth &&
                                y >= _screen.BoundY && y <= _screen.BoundY + _screen.BoundHeight;
            if (_debug)
            {
                var infoBuilder = new StringBuilder();
                infoBuilder
                    .AppendFormat("Name {0}", _screen.Name).AppendLine()
                    .AppendFormat("Primary {0}", _screen.Primary).AppendLine()
                    .AppendFormat("PixelsPerDip {0}", _dpiInfo.PixelsPerDip).AppendLine()
                    .AppendFormat("Mouse {0},{1}", x, y).AppendLine()
                    .AppendFormat("InScreen {0}", mouseInScreen).AppendLine()
                    .AppendFormat("Draw {0},{1}", x - _screen.BoundX, y - _screen.BoundY);
                DebugInfo.Content = infoBuilder.ToString();

                Canvas.SetTop(MousePosDebug, y - _screen.BoundY);
                Canvas.SetLeft(MousePosDebug, x - _screen.BoundX);
            }

            var shouldShowCursor = mouseInScreen && !_systemCursorOverrideActive;
            CursorHost.Visibility = shouldShowCursor ? Visibility.Visible : Visibility.Hidden;
            if (!shouldShowCursor)
            {
                return;
            }

            _cursorTranslate.X = x - _screen.BoundX;
            _cursorTranslate.Y = y - _screen.BoundY;
        }

        internal void SetCursorVisual(CursorVisualInfo cursorVisual)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetCursorVisual(cursorVisual)));
                return;
            }

            if (cursorVisual == null)
            {
                cursorVisual = CursorVisualLoader.BuiltIn();
            }

            CursorImage.Source = cursorVisual.Bitmap;
            CursorImage.Width = cursorVisual.Width;
            CursorImage.Height = cursorVisual.Height;
            CursorImage.Visibility = cursorVisual.HasBitmap ? Visibility.Visible : Visibility.Hidden;
            BuiltInCursor.Visibility = cursorVisual.HasBitmap ? Visibility.Hidden : Visibility.Visible;

            CursorHost.Width = cursorVisual.Width;
            CursorHost.Height = cursorVisual.Height;

            _cursorHotspotTranslate.X = -cursorVisual.Hotspot.X;
            _cursorHotspotTranslate.Y = -cursorVisual.Hotspot.Y;

            if (_mouseAnimator != null)
            {
                _mouseAnimator.CursorVisualHeight = cursorVisual.Height;
            }
        }

        /// <summary>
        /// Causes the big mouse to appear or disappear depending on the parameter and the current state
        /// of the mouse.
        /// </summary>
        /// <param name="shaking">Whether the mouse is shaking or not.</param>
        /// <param name="timestamp">The timestamp the shake change occured at.</param>
        public void SetMouseShake(bool shaking, DateTime timestamp)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetMouseShake(shaking, timestamp)));
                return;
            }

            if (_mouseAnimator == null)
            {
                return;
            }

            if (shaking && !_systemCursorOverrideActive)
            {
                RefreshTopmost();
                if (!_topmostRefreshTimer.IsEnabled)
                {
                    _topmostRefreshTimer.Start();
                }
            }
            else
            {
                _topmostRefreshTimer.Stop();
            }

            _mouseAnimator.SetMouseShake(shaking, timestamp);
        }

        private void TopmostRefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshTopmost();
        }

        private void RefreshTopmost()
        {
            SetTopmostNoActivate(this);
        }

        internal void SetSystemCursorOverrideActive(bool active)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetSystemCursorOverrideActive(active)));
                return;
            }

            _systemCursorOverrideActive = active;
            if (active)
            {
                _topmostRefreshTimer.Stop();
                CursorHost.Visibility = Visibility.Hidden;
            }
        }
    }
}
