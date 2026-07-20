using AngryMouse.Cursors;
using AngryMouse.Animation;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Input = System.Windows.Input;

namespace AngryMouse
{
    public partial class CursorRoleAdjustWindow
    {
        private const double PreviewFallbackWidth = 880;
        private const double PreviewFallbackHeight = 460;
        private const double PreviewPadding = 32;
        private const double PreviewMinScale = 0.01;
        private const uint MonitorDefaultToNearest = 2;

        private readonly string _collectionName;
        private readonly string _roleKey;
        private readonly string _filePath;
        private readonly DispatcherTimer _placementTimer;
        private bool _loading = true;
        private CursorRoleDefinition _role;
        private CursorVisualInfo _systemCursorVisual;
        private CursorVisualInfo _previewVisual;
        private bool _draggingPreview;
        private System.Drawing.Point _previewCursorPosition;
        private Point _dragStartPosition;
        private double _dragStartOffsetX;
        private double _dragStartOffsetY;
        private double _dragScale = 1;

        public CursorRoleAdjustWindow(string collectionName, string roleKey, string filePath)
        {
            InitializeComponent();

            _collectionName = collectionName;
            _roleKey = roleKey;
            _filePath = filePath;

            _placementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _placementTimer.Tick += PlacementTimer_OnTick;
        }

        protected override void OnClosed(EventArgs e)
        {
            _placementTimer.Stop();
            base.OnClosed(e);
        }

        protected override void OnDpiChanged(DpiScale oldDpiScaleInfo, DpiScale newDpiScaleInfo)
        {
            base.OnDpiChanged(oldDpiScaleInfo, newDpiScaleInfo);
            RefreshPlacementIfReady();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            RefreshPlacementIfReady();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _role = CursorCollectionManager.GetRole(_roleKey);
            var settings = CursorCollectionManager.GetRoleSettings(_collectionName, _roleKey);

            RoleTextBlock.Text = _role.DisplayName + " - " + Path.GetFileName(_filePath);
            _previewCursorPosition = System.Windows.Forms.Cursor.Position;
            _systemCursorVisual = CursorVisualLoader.LoadSystemCursorRole(_role.WindowsCursorId);
            SystemCursorImage.Source = _systemCursorVisual.Bitmap;
            SystemCursorImage.Visibility = _systemCursorVisual.HasBitmap ? Visibility.Visible : Visibility.Hidden;
            HotspotOffsetXTextBox.Text = settings.HotspotOffsetX.ToString(CultureInfo.InvariantCulture);
            HotspotOffsetYTextBox.Text = settings.HotspotOffsetY.ToString(CultureInfo.InvariantCulture);

            _loading = false;
            RefreshPreview();
            _placementTimer.Start();
        }

        private void HotspotOffset_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            RefreshPreview();
        }

        private void PreviewCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshPlacementIfReady();
        }

        private void RefreshPlacementIfReady()
        {
            if (_loading || _previewVisual == null)
            {
                return;
            }

            CursorRoleRenderSettings settings;
            if (TryReadSettings(out settings))
            {
                UpdatePreviewPlacement(settings);
            }
        }

        private void ResetButton_OnClick(object sender, RoutedEventArgs e)
        {
            _loading = true;
            HotspotOffsetXTextBox.Text = "0";
            HotspotOffsetYTextBox.Text = "0";
            _loading = false;

            RefreshPreview();
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            CursorRoleRenderSettings settings;
            if (!TryReadSettings(out settings))
            {
                MessageBox.Show(this, "Hotspot offsets must be numbers.", "Invalid cursor adjustment", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CursorCollectionManager.SaveRoleSettings(_collectionName, _roleKey, settings);
            DialogResult = true;
        }

        private void PlacementTimer_OnTick(object sender, EventArgs e)
        {
            if (_draggingPreview)
            {
                return;
            }

            _previewCursorPosition = System.Windows.Forms.Cursor.Position;
            RefreshPlacementIfReady();
        }

        private void RefreshPreview()
        {
            CursorRoleRenderSettings settings;
            if (!TryReadSettings(out settings))
            {
                StatusTextBlock.Text = "Hotspot offsets must be numbers.";
                return;
            }

            try
            {
                _previewVisual = CursorVisualCache.GetRuntimeVisual(_collectionName, _roleKey, _filePath, settings);
                if (_previewVisual == null || !_previewVisual.HasBitmap)
                {
                    StatusTextBlock.Text = "Preview failed.";
                    return;
                }

                PreviewImage.Source = _previewVisual.Bitmap;
            }
            catch (Exception)
            {
                StatusTextBlock.Text = "Preview failed.";
                return;
            }

            UpdatePreviewPlacement(settings);
        }

        private void UpdatePreviewPlacement(CursorRoleRenderSettings settings)
        {
            if (_previewVisual == null || _previewVisual.Bitmap == null || _role == null)
            {
                return;
            }

            var adjustedHotspot = _previewVisual.Hotspot;
            var systemBitmap = _systemCursorVisual == null ? null : _systemCursorVisual.Bitmap;
            var systemHotspot = _systemCursorVisual == null ? new Point(0, 0) : _systemCursorVisual.Hotspot;

            var canvasWidth = GetPreviewCanvasWidth();
            var canvasHeight = GetPreviewCanvasHeight();
            var bitmapWidth = Math.Max(1, _previewVisual.Bitmap.PixelWidth);
            var bitmapHeight = Math.Max(1, _previewVisual.Bitmap.PixelHeight);
            var systemWidth = systemBitmap == null ? 0 : systemBitmap.PixelWidth;
            var systemHeight = systemBitmap == null ? 0 : systemBitmap.PixelHeight;
            var mousePosition = _previewCursorPosition;
            var screen = System.Windows.Forms.Screen.FromPoint(mousePosition);
            // Canvas DIP<->pixel conversion uses this window's DPI; the runtime cursor scale uses
            // the DPI of the monitor under the mouse (which may differ on a mixed-DPI setup).
            var windowDpi = GetPreviewDpi(VisualTreeHelper.GetDpi(this));
            var previewDpi = GetMonitorDpiScale(mousePosition, windowDpi);
            var runtimeScale = MouseAnimator.GetTargetScale(bitmapHeight, previewDpi);
            var pngBounds = new Rect(
                mousePosition.X - adjustedHotspot.X * runtimeScale,
                mousePosition.Y - adjustedHotspot.Y * runtimeScale,
                bitmapWidth * runtimeScale,
                bitmapHeight * runtimeScale);
            var systemBounds = systemBitmap == null
                ? new Rect(mousePosition.X, mousePosition.Y, 0, 0)
                : new Rect(
                    mousePosition.X - systemHotspot.X,
                    mousePosition.Y - systemHotspot.Y,
                    systemWidth,
                    systemHeight);
            var screenBounds = new Rect(
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height);
            var viewportBounds = GetViewportBounds(pngBounds, systemBounds, screenBounds, PreviewPadding * previewDpi);
            var viewportScale = ComputeViewportScale(canvasWidth, canvasHeight, viewportBounds, windowDpi);
            var displayScale = viewportScale / windowDpi;

            var viewportLeft = (canvasWidth - viewportBounds.Width * displayScale) / 2;
            var viewportTop = (canvasHeight - viewportBounds.Height * displayScale) / 2;
            var targetX = viewportLeft + (mousePosition.X - viewportBounds.Left) * displayScale;
            var targetY = viewportTop + (mousePosition.Y - viewportBounds.Top) * displayScale;
            var imageWidth = pngBounds.Width * displayScale;
            var imageHeight = pngBounds.Height * displayScale;
            var currentLeft = viewportLeft + (pngBounds.Left - viewportBounds.Left) * displayScale;
            var currentTop = viewportTop + (pngBounds.Top - viewportBounds.Top) * displayScale;
            _dragScale = runtimeScale * displayScale;

            SetSystemCursorBounds(systemBounds, viewportBounds, viewportLeft, viewportTop, displayScale);
            SetImageBounds(PreviewImage, currentLeft, currentTop, imageWidth, imageHeight);
            SetCrosshair(TargetHorizontal, TargetVertical, TargetDot, targetX, targetY, canvasWidth, canvasHeight);

            StatusTextBlock.Text =
                "Screen: " +
                screen.DeviceName +
                " " +
                screen.Bounds.Width.ToString(CultureInfo.InvariantCulture) +
                "x" +
                screen.Bounds.Height.ToString(CultureInfo.InvariantCulture) +
                " px. DPI: " +
                FormatNumber(previewDpi) +
                "x. Runtime scale: " +
                FormatNumber(runtimeScale) +
                "x. View: " +
                FormatNumber(viewportScale) +
                "x. Mouse: " +
                mousePosition.X.ToString(CultureInfo.InvariantCulture) +
                ", " +
                mousePosition.Y.ToString(CultureInfo.InvariantCulture) +
                " px. Viewport: " +
                FormatNumber(viewportBounds.Width) +
                "x" +
                FormatNumber(viewportBounds.Height) +
                " px. PNG hotspot: " +
                FormatNumber(adjustedHotspot.X) +
                ", " +
                FormatNumber(adjustedHotspot.Y) +
                " px. Windows hotspot: " +
                FormatNumber(systemHotspot.X) +
                ", " +
                FormatNumber(systemHotspot.Y) +
                " px. Offset: " +
                FormatSignedNumber(settings.HotspotOffsetX) +
                ", " +
                FormatSignedNumber(settings.HotspotOffsetY) +
                " pixels. Runtime bitmap: " +
                _previewVisual.Bitmap.PixelWidth.ToString(CultureInfo.InvariantCulture) +
                "x" +
                _previewVisual.Bitmap.PixelHeight.ToString(CultureInfo.InvariantCulture) +
                " px.";
        }

        private static Rect GetViewportBounds(Rect pngBounds, Rect systemBounds, Rect screenBounds, double margin)
        {
            var viewport = Rect.Union(pngBounds, systemBounds);
            viewport.Inflate(margin, margin);
            return ClampViewportToScreen(viewport, screenBounds);
        }

        private static Rect ClampViewportToScreen(Rect viewport, Rect screenBounds)
        {
            var width = Math.Max(1, Math.Min(viewport.Width, screenBounds.Width));
            var height = Math.Max(1, Math.Min(viewport.Height, screenBounds.Height));
            var left = Clamp(viewport.Left, screenBounds.Left, screenBounds.Right - width);
            var top = Clamp(viewport.Top, screenBounds.Top, screenBounds.Bottom - height);
            return new Rect(left, top, width, height);
        }

        private static double ComputeViewportScale(double canvasWidth, double canvasHeight, Rect viewportBounds, double canvasDpi)
        {
            var viewportWidth = Math.Max(1, viewportBounds.Width);
            var viewportHeight = Math.Max(1, viewportBounds.Height);
            var scale = Math.Min(
                1,
                Math.Min(
                    GetFitScale(canvasWidth * canvasDpi, viewportWidth),
                    GetFitScale(canvasHeight * canvasDpi, viewportHeight)));

            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                return 1;
            }

            return Math.Max(PreviewMinScale, scale);
        }

        private static double GetFitScale(double available, double extent)
        {
            if (extent <= 0)
            {
                return 1;
            }

            return available / extent;
        }

        private void SetSystemCursorBounds(Rect systemBounds, Rect viewportBounds, double viewportLeft, double viewportTop, double displayScale)
        {
            var bitmap = _systemCursorVisual == null ? null : _systemCursorVisual.Bitmap;
            if (bitmap == null)
            {
                SystemCursorImage.Visibility = Visibility.Hidden;
                return;
            }

            var width = systemBounds.Width * displayScale;
            var height = systemBounds.Height * displayScale;
            var left = viewportLeft + (systemBounds.Left - viewportBounds.Left) * displayScale;
            var top = viewportTop + (systemBounds.Top - viewportBounds.Top) * displayScale;

            SetImageBounds(SystemCursorImage, left, top, width, height);
            SystemCursorImage.Visibility = Visibility.Visible;
        }

        private void PreviewImage_OnMouseLeftButtonDown(object sender, Input.MouseButtonEventArgs e)
        {
            CursorRoleRenderSettings settings;
            if (!TryReadSettings(out settings))
            {
                return;
            }

            _draggingPreview = true;
            _dragStartPosition = e.GetPosition(PreviewCanvas);
            _dragStartOffsetX = settings.HotspotOffsetX;
            _dragStartOffsetY = settings.HotspotOffsetY;
            PreviewImage.CaptureMouse();
            e.Handled = true;
        }

        private void PreviewImage_OnMouseMove(object sender, Input.MouseEventArgs e)
        {
            if (!_draggingPreview || _dragScale <= 0)
            {
                return;
            }

            var position = e.GetPosition(PreviewCanvas);
            var offsetX = _dragStartOffsetX - (position.X - _dragStartPosition.X) / _dragScale;
            var offsetY = _dragStartOffsetY - (position.Y - _dragStartPosition.Y) / _dragScale;

            SetHotspotOffsetText(offsetX, offsetY);
            RefreshPreview();
            e.Handled = true;
        }

        private void PreviewImage_OnMouseLeftButtonUp(object sender, Input.MouseButtonEventArgs e)
        {
            StopPreviewDrag();
            e.Handled = true;
        }

        private void PreviewImage_OnLostMouseCapture(object sender, Input.MouseEventArgs e)
        {
            _draggingPreview = false;
        }

        private void StopPreviewDrag()
        {
            if (!_draggingPreview)
            {
                return;
            }

            _draggingPreview = false;
            if (PreviewImage.IsMouseCaptured)
            {
                PreviewImage.ReleaseMouseCapture();
            }
        }

        private void SetHotspotOffsetText(double offsetX, double offsetY)
        {
            _loading = true;
            HotspotOffsetXTextBox.Text = FormatOffsetInput(offsetX);
            HotspotOffsetYTextBox.Text = FormatOffsetInput(offsetY);
            _loading = false;
        }

        private static void SetImageBounds(FrameworkElement image, double left, double top, double width, double height)
        {
            image.Width = width;
            image.Height = height;
            Canvas.SetLeft(image, left);
            Canvas.SetTop(image, top);
        }

        private static void SetCrosshair(
            System.Windows.Shapes.Line horizontal,
            System.Windows.Shapes.Line vertical,
            FrameworkElement dot,
            double x,
            double y,
            double canvasWidth,
            double canvasHeight)
        {
            horizontal.X1 = 0;
            horizontal.X2 = canvasWidth;
            horizontal.Y1 = y;
            horizontal.Y2 = y;

            vertical.X1 = x;
            vertical.X2 = x;
            vertical.Y1 = 0;
            vertical.Y2 = canvasHeight;

            Canvas.SetLeft(dot, x - dot.Width / 2);
            Canvas.SetTop(dot, y - dot.Height / 2);
        }

        private static double GetPreviewDpi(DpiScale dpiInfo)
        {
            return Math.Max(0.01, dpiInfo.PixelsPerDip);
        }

        private static double GetMonitorDpiScale(System.Drawing.Point point, double fallback)
        {
            try
            {
                var monitor = MonitorFromPoint(new NativePoint(point.X, point.Y), MonitorDefaultToNearest);
                if (monitor != IntPtr.Zero)
                {
                    uint dpiX;
                    uint dpiY;
                    if (GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out dpiX, out dpiY) == 0 && dpiX > 0)
                    {
                        return dpiX / 96.0;
                    }
                }
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return min;
            }

            return Math.Min(Math.Max(value, min), max);
        }

        private double GetPreviewCanvasWidth()
        {
            return PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth : PreviewFallbackWidth;
        }

        private double GetPreviewCanvasHeight()
        {
            return PreviewCanvas.ActualHeight > 0 ? PreviewCanvas.ActualHeight : PreviewFallbackHeight;
        }

        private bool TryReadSettings(out CursorRoleRenderSettings settings)
        {
            settings = null;

            double offsetX;
            double offsetY;
            if (!TryParseDouble(HotspotOffsetXTextBox.Text, out offsetX) ||
                !TryParseDouble(HotspotOffsetYTextBox.Text, out offsetY))
            {
                return false;
            }

            settings = new CursorRoleRenderSettings(offsetX, offsetY);
            return true;
        }

        private static bool TryParseDouble(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static string FormatNumber(double value)
        {
            return Math.Round(value, 1).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string FormatSignedNumber(double value)
        {
            var rounded = Math.Round(value, 1);
            return rounded.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        }

        private static string FormatOffsetInput(double value)
        {
            return Math.Round(value, 1).ToString("0.0", CultureInfo.InvariantCulture);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

        private enum MonitorDpiType
        {
            EffectiveDpi = 0
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X;

            public int Y;
        }
    }
}
