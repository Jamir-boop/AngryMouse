using System;
using AngryMouse.Cursors;
using AngryMouse.Util;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AngryMouse.Animation
{
    class MouseAnimator : IDisposable
    {
        /// <summary>
        /// Maximum cursor size.
        /// </summary>
        private const double MaxScale = CursorVisualLoader.BuiltInCursorHeight;

        /// <summary>
        /// Scales the cursor.
        /// </summary>
        private readonly ScaleTransform _cursorScale;

        /// <summary>
        /// Whether the mouse is currently shaking or not.
        /// </summary>
        private bool _shaking;
        private bool _disposed;

        private double _cursorVisualHeight = CursorVisualLoader.BuiltInCursorHeight;

        /// <summary>
        /// dpi info
        /// </summary>
        public DpiScale DpiInfo;

        public double CursorVisualHeight
        {
            get => _cursorVisualHeight;
            set
            {
                _cursorVisualHeight = Math.Max(1, value);
                RefreshVisibleScale();
            }
        }

        public MouseAnimator(ScaleTransform cursorScale, DpiScale dpiInfo)
        {
            _cursorScale = cursorScale;
            DpiInfo = dpiInfo;

            Properties.Settings.Default.PropertyChanged += DefaultOnPropertyChanged;
        }

        private void DefaultOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName.Equals("CursorAnimationLength"))
            {
                DebugLog.Write(
                    "Animation duration setting changed: configuredMs=" +
                    Properties.Settings.Default.CursorAnimationLength +
                    ", effectiveMs=" +
                    CursorAnimationSettings.GetEffectiveLength());
            }

            if (e.PropertyName.Equals("CursorSize"))
            {
                DebugLog.Write(
                    "Cursor size setting changed while animator active: shaking=" +
                    (_shaking ? "On" : "Off") +
                    ", targetScale=" +
                    FormatDouble(GetTargetScale()));
                RefreshVisibleScale();
            }
        }

        public void SetMouseShake(bool shaking, DateTime timestamp)
        {
            if (_shaking == shaking) return;
            _shaking = shaking;

            var fromX = _cursorScale.ScaleX;
            var fromY = _cursorScale.ScaleY;
            var to = shaking ? GetTargetScale() : 0;
            var durationMs = CursorAnimationSettings.GetEffectiveLength();

            DebugLog.Write(
                "MouseAnimator transition: shaking=" +
                (shaking ? "On" : "Off") +
                ", fromX=" +
                FormatDouble(fromX) +
                ", fromY=" +
                FormatDouble(fromY) +
                ", to=" +
                FormatDouble(to) +
                ", configuredMs=" +
                Properties.Settings.Default.CursorAnimationLength +
                ", effectiveMs=" +
                durationMs +
                ", cursorVisualHeight=" +
                FormatDouble(CursorVisualHeight) +
                ", pixelsPerDip=" +
                FormatDouble(DpiInfo.PixelsPerDip) +
                ", timestamp=" +
                timestamp.ToString("O", CultureInfo.InvariantCulture));

            _cursorScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateScaleAnimation(fromX, to, durationMs));
            _cursorScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateScaleAnimation(fromY, to, durationMs));
        }

        internal static double GetTargetScale(double cursorVisualHeight, DpiScale dpiInfo)
        {
            return GetTargetScale(cursorVisualHeight, dpiInfo.PixelsPerDip);
        }

        internal static double GetTargetScale(double cursorVisualHeight, double pixelsPerDip)
        {
            return MaxScale / Math.Max(1, cursorVisualHeight) *
                   (Properties.Settings.Default.CursorSize / 10.0) *
                   Math.Max(0.01, pixelsPerDip);
        }

        private double GetTargetScale()
        {
            return GetTargetScale(CursorVisualHeight, DpiInfo);
        }

        private void RefreshVisibleScale()
        {
            if (!_shaking)
            {
                return;
            }

            var targetScale = GetTargetScale();
            var fromX = _cursorScale.ScaleX;
            var fromY = _cursorScale.ScaleY;
            var durationMs = CursorAnimationSettings.GetEffectiveLength();
            DebugLog.Write(
                "MouseAnimator visible scale retargeted: fromX=" +
                FormatDouble(fromX) +
                ", fromY=" +
                FormatDouble(fromY) +
                ", target=" +
                FormatDouble(targetScale) +
                ", configuredMs=" +
                Properties.Settings.Default.CursorAnimationLength +
                ", effectiveMs=" +
                durationMs +
                ", cursorVisualHeight=" +
                FormatDouble(CursorVisualHeight) +
                ", pixelsPerDip=" +
                FormatDouble(DpiInfo.PixelsPerDip));

            _cursorScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateScaleAnimation(fromX, targetScale, durationMs));
            _cursorScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateScaleAnimation(fromY, targetScale, durationMs));
        }

        private static DoubleAnimation CreateScaleAnimation(double from, double to, int durationMs)
        {
            return new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase {EasingMode = EasingMode.EaseInOut},
                RepeatBehavior = new RepeatBehavior(1)
            };
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Properties.Settings.Default.PropertyChanged -= DefaultOnPropertyChanged;
            _disposed = true;
        }
    }
}
