using System;
using AngryMouse.Cursors;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AngryMouse.Animation
{
    class MouseAnimator
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

        /// <summary>
        /// For scale animation
        /// </summary>
        private readonly DoubleAnimation _scaleAnimation;

        public MouseAnimator(ScaleTransform cursorScale, DpiScale dpiInfo)
        {
            _cursorScale = cursorScale;
            DpiInfo = dpiInfo;

            Properties.Settings.Default.PropertyChanged += DefaultOnPropertyChanged;

            _scaleAnimation = new DoubleAnimation
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(Properties.Settings.Default.CursorAnimationLength)),
                EasingFunction = new CubicEase {EasingMode = EasingMode.EaseInOut},
                RepeatBehavior = new RepeatBehavior(1)
            };
        }

        private void DefaultOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName.Equals("CursorAnimationLength"))
            {
                _scaleAnimation.Duration =
                    new Duration(TimeSpan.FromMilliseconds(Properties.Settings.Default.CursorAnimationLength));
            }

            if (e.PropertyName.Equals("CursorSize"))
            {
                RefreshVisibleScale();
            }
        }

        public void SetMouseShake(bool shaking, DateTime timestamp)
        {
            if (_shaking == shaking) return;
            _shaking = shaking;

            _scaleAnimation.From = _cursorScale.ScaleX;
            _scaleAnimation.To = shaking ? GetTargetScale() : 0;

            _cursorScale.BeginAnimation(ScaleTransform.ScaleXProperty, _scaleAnimation);
            _cursorScale.BeginAnimation(ScaleTransform.ScaleYProperty, _scaleAnimation);
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

            _cursorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _cursorScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _cursorScale.ScaleX = GetTargetScale();
            _cursorScale.ScaleY = GetTargetScale();
        }
    }
}
