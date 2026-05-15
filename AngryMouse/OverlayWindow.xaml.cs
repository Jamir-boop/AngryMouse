using AngryMouse.Animation;
using AngryMouse.Cursors;
using AngryMouse.Mouse;
using AngryMouse.Screen;
using Gma.System.MouseKeyHook;
using System;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        /// <summary>
        /// Last active Windows cursor handle rendered in System mode.
        /// </summary>
        private IntPtr _lastSystemCursorHandle = IntPtr.Zero;

        /// <summary>
        /// We also subscribe to mouse move events so we know where to draw.
        /// </summary>
        private readonly IKeyboardMouseEvents _mouseEvents;

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

            _mouseEvents = StaticHook.GlobalEvents();
            _mouseEvents.MouseMoveExt += OnMouseMove;
            Properties.Settings.Default.PropertyChanged += SettingsOnPropertyChanged;
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
            RefreshCursorVisual(force: true);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _mouseEvents.MouseMoveExt -= OnMouseMove;
            Properties.Settings.Default.PropertyChanged -= SettingsOnPropertyChanged;
        }

        /// <summary>
        /// Called when the position of the mouse is changed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMouseMove(object sender, MouseEventExtArgs e)
        {
            var mouseInScreen = e.X >= _screen.BoundX && e.X <= _screen.BoundX + _screen.BoundWidth &&
                                e.Y >= _screen.BoundY && e.Y <= _screen.BoundY + _screen.BoundHeight;
            if (_debug)
            {
                var infoBuilder = new StringBuilder();
                infoBuilder
                    .AppendFormat("Name {0}", _screen.Name).AppendLine()
                    .AppendFormat("Primary {0}", _screen.Primary).AppendLine()
                    .AppendFormat("PixelsPerDip {0}", _dpiInfo.PixelsPerDip).AppendLine()
                    .AppendFormat("Mouse {0},{1}", e.X, e.Y).AppendLine()
                    .AppendFormat("InScreen {0}", mouseInScreen).AppendLine()
                    .AppendFormat("Draw {0},{1}", e.X - _screen.BoundX, e.Y - _screen.BoundY);
                DebugInfo.Content = infoBuilder.ToString();

                Canvas.SetTop(MousePosDebug, e.Y - _screen.BoundY);
                Canvas.SetLeft(MousePosDebug, e.X - _screen.BoundX);
            }

            CursorHost.Visibility = mouseInScreen ? Visibility.Visible : Visibility.Hidden;
            if (!mouseInScreen)
            {
                return;
            }

            RefreshSystemCursorVisual();

            _cursorTranslate.X = e.X - _screen.BoundX;
            _cursorTranslate.Y = e.Y - _screen.BoundY;
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CursorSourceMode" ||
                e.PropertyName == "CustomCursorPath" ||
                e.PropertyName == "CustomCursorHotspotX" ||
                e.PropertyName == "CustomCursorHotspotY")
            {
                if (Dispatcher.CheckAccess())
                {
                    RefreshCursorVisual(force: true);
                }
                else
                {
                    Dispatcher.Invoke(() => RefreshCursorVisual(force: true));
                }
            }
        }

        private void RefreshSystemCursorVisual()
        {
            if (!string.Equals(
                    Properties.Settings.Default.CursorSourceMode,
                    "System",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cursorHandle = CursorVisualLoader.GetCurrentSystemCursorHandle();
            if (cursorHandle == _lastSystemCursorHandle)
            {
                return;
            }

            _lastSystemCursorHandle = cursorHandle;
            RefreshCursorVisual(force: false);
        }

        private void RefreshCursorVisual(bool force)
        {
            if (!force &&
                !string.Equals(
                    Properties.Settings.Default.CursorSourceMode,
                    "System",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cursorVisual = CursorVisualLoader.LoadFromSettings();

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
            _mouseAnimator.SetMouseShake(shaking, timestamp);
        }
    }
}
