using AngryMouse.Cursors;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AngryMouse
{
    public partial class CursorRoleAdjustWindow
    {
        private readonly string _collectionName;
        private readonly string _roleKey;
        private readonly string _filePath;
        private readonly DispatcherTimer _renderTimer;
        private bool _loading = true;
        private CursorRoleDefinition _role;
        private CursorCachedBitmap _previewBitmap;

        public CursorRoleAdjustWindow(string collectionName, string roleKey, string filePath)
        {
            InitializeComponent();

            _collectionName = collectionName;
            _roleKey = roleKey;
            _filePath = filePath;

            _renderTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _renderTimer.Tick += RenderTimer_OnTick;
        }

        protected override void OnClosed(EventArgs e)
        {
            _renderTimer.Stop();
            base.OnClosed(e);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _role = CursorCollectionManager.GetRole(_roleKey);
            var settings = CursorCollectionManager.GetRoleSettings(_collectionName, _roleKey);

            RoleTextBlock.Text = _role.DisplayName + " - " + Path.GetFileName(_filePath);
            HotspotOffsetXTextBox.Text = settings.HotspotOffsetX.ToString(CultureInfo.InvariantCulture);
            HotspotOffsetYTextBox.Text = settings.HotspotOffsetY.ToString(CultureInfo.InvariantCulture);
            TrimTransparentPaddingCheckBox.IsChecked = settings.TrimTransparentPadding;

            _loading = false;
            RefreshPreview(renderBitmap: true);
        }

        private void HotspotOffset_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            RefreshPreview(renderBitmap: false);
        }

        private void TrimTransparentPadding_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            QueuePreviewRender();
        }

        private void ResetButton_OnClick(object sender, RoutedEventArgs e)
        {
            _loading = true;
            HotspotOffsetXTextBox.Text = "0";
            HotspotOffsetYTextBox.Text = "0";
            TrimTransparentPaddingCheckBox.IsChecked = true;
            _loading = false;

            _renderTimer.Stop();
            RefreshPreview(renderBitmap: true);
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

        private void QueuePreviewRender()
        {
            _renderTimer.Stop();
            _renderTimer.Start();
            StatusTextBlock.Text = "Rendering preview...";
        }

        private void RenderTimer_OnTick(object sender, EventArgs e)
        {
            _renderTimer.Stop();
            RefreshPreview(renderBitmap: true);
        }

        private void RefreshPreview(bool renderBitmap)
        {
            CursorRoleRenderSettings settings;
            if (!TryReadSettings(out settings))
            {
                StatusTextBlock.Text = "Hotspot offsets must be numbers.";
                return;
            }

            if (renderBitmap || _previewBitmap == null)
            {
                try
                {
                    _previewBitmap = CursorVisualCache.GetPreviewBitmap(_filePath, settings.TrimTransparentPadding);
                    if (_previewBitmap == null)
                    {
                        StatusTextBlock.Text = "Preview failed.";
                        return;
                    }

                    PreviewImage.Source = _previewBitmap.Bitmap;
                    ReferenceImage.Source = _previewBitmap.Bitmap;
                }
                catch (Exception)
                {
                    StatusTextBlock.Text = "Preview failed.";
                    return;
                }
            }

            UpdatePreviewPlacement(settings);
        }

        private void UpdatePreviewPlacement(CursorRoleRenderSettings settings)
        {
            if (_previewBitmap == null || _previewBitmap.Bitmap == null || _role == null)
            {
                return;
            }

            var referenceSettings = new CursorRoleRenderSettings(0, 0, settings.TrimTransparentPadding);
            var referenceHotspot = CursorVisualCache.GetPreviewHotspot(_filePath, _role.Hotspot, referenceSettings, _previewBitmap);
            var adjustedHotspot = CursorVisualCache.GetPreviewHotspot(_filePath, _role.Hotspot, settings, _previewBitmap);

            var canvasWidth = GetPreviewCanvasWidth();
            var canvasHeight = GetPreviewCanvasHeight();
            var bitmapWidth = Math.Max(1, _previewBitmap.Bitmap.PixelWidth);
            var bitmapHeight = Math.Max(1, _previewBitmap.Bitmap.PixelHeight);
            var scale = Math.Min(
                (canvasWidth - 72) / bitmapWidth,
                (canvasHeight - 72) / bitmapHeight);
            scale = Math.Max(0.1, scale);

            var imageWidth = bitmapWidth * scale;
            var imageHeight = bitmapHeight * scale;
            var targetX = canvasWidth / 2;
            var targetY = canvasHeight / 2;
            var referenceLeft = targetX - referenceHotspot.X * scale;
            var referenceTop = targetY - referenceHotspot.Y * scale;
            var currentLeft = targetX - adjustedHotspot.X * scale;
            var currentTop = targetY - adjustedHotspot.Y * scale;
            var normalHotspotX = currentLeft + referenceHotspot.X * scale;
            var normalHotspotY = currentTop + referenceHotspot.Y * scale;

            SetImageBounds(ReferenceImage, referenceLeft, referenceTop, imageWidth, imageHeight);
            SetImageBounds(PreviewImage, currentLeft, currentTop, imageWidth, imageHeight);
            SetCrosshair(DefaultHotspotHorizontal, DefaultHotspotVertical, DefaultHotspotDot, normalHotspotX, normalHotspotY, canvasWidth, canvasHeight);
            SetCrosshair(AdjustedHotspotHorizontal, AdjustedHotspotVertical, AdjustedHotspotDot, targetX, targetY, canvasWidth, canvasHeight);

            StatusTextBlock.Text =
                "Adjusted hotspot: " +
                FormatNumber(adjustedHotspot.X) +
                ", " +
                FormatNumber(adjustedHotspot.Y) +
                " px. Normal hotspot: " +
                FormatNumber(referenceHotspot.X) +
                ", " +
                FormatNumber(referenceHotspot.Y) +
                " px. Offset: " +
                FormatSignedNumber(settings.HotspotOffsetX) +
                ", " +
                FormatSignedNumber(settings.HotspotOffsetY) +
                " pixels. Bitmap: " +
                _previewBitmap.Bitmap.PixelWidth.ToString(CultureInfo.InvariantCulture) +
                "x" +
                _previewBitmap.Bitmap.PixelHeight.ToString(CultureInfo.InvariantCulture) +
                " px.";
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

        private double GetPreviewCanvasWidth()
        {
            return PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth : PreviewCanvas.Width;
        }

        private double GetPreviewCanvasHeight()
        {
            return PreviewCanvas.ActualHeight > 0 ? PreviewCanvas.ActualHeight : PreviewCanvas.Height;
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

            settings = new CursorRoleRenderSettings(
                offsetX,
                offsetY,
                TrimTransparentPaddingCheckBox.IsChecked == true);
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
    }
}
