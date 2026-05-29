using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.Timers;
using System.Windows;

namespace AngryMouse.Mouse
{
    /// <summary>
    /// Detects mouse shaking using the global mouse hook.
    /// </summary>
    public class MouseShakeDetector : IDisposable
    {
        /// <summary>
        /// Minimum milliseconds between recording a mouse event.
        /// </summary>
        private const int MouseEventRate = 10;

        /// <summary>
        /// The hook into mouse events
        /// </summary>
        private readonly IKeyboardMouseEvents _mouseEvents;

        /// <summary>
        /// The last time we received a mouse event.
        /// </summary>
        private DateTime _lastMouseEvent = DateTime.MinValue;

        private DateTime _visibleUntil = DateTime.MinValue;

        /// <summary>
        /// Stores the recorded mouse positions.
        /// </summary>
        private readonly LinkedList<MousePosition> _mousePositions = new LinkedList<MousePosition>();

        /// <summary>
        /// Indicates whether the mouse is currently shaking or not.
        /// </summary>
        private bool _shaking;

        /// <summary>
        /// Handler for mouse shaking events.
        /// </summary>
        public event EventHandler<MouseShakeArgs> MouseShake;

        /// <summary>
        /// Handler for mouse movement events.
        /// </summary>
        public event EventHandler<MouseEventExtArgs> MouseMove;

        /// <summary>
        /// Timer for hiding the mouse when it's not moving.
        /// </summary>
        private readonly Timer _timer = new Timer();

        /// <summary>
        /// Main constructor.
        /// </summary>
        public MouseShakeDetector()
        {
            _mouseEvents = StaticHook.GlobalEvents();

            _mouseEvents.MouseMoveExt += OnMouseMove;

            _timer.Interval = 100;
            _timer.Elapsed += Timer_Tick;
            _timer.Enabled = true;
        }

        /// <summary>
        /// Global hook callback.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">parameters of the mouse</param>
        private void OnMouseMove(object sender, MouseEventExtArgs e)
        {
            var currentTime = DateTime.Now;
            if (currentTime.AddMilliseconds(-MouseEventRate) > _lastMouseEvent)
            {
                MouseMove?.Invoke(this, e);

                _lastMouseEvent = currentTime;

                while (_mousePositions.Count > 0 &&
                       e.Timestamp - TrackingInterval > _mousePositions.Last.Value.Timestamp)
                {
                    // Remove old positions
                    _mousePositions.RemoveLast();
                }

                _mousePositions.AddFirst(e);

                if (IsShaking())
                {
                    _visibleUntil = currentTime.AddMilliseconds(VisibleDuration);
                    SetShaking(true);
                    if (!_timer.Enabled)
                    {
                        _timer.Enabled = true;
                    }
                }
                // Note: we do not disable timer here because we need it to turn off shaking state
                // Timer will be disabled in Timer_Tick when _shaking becomes false
            }
        }

        /// <summary>
        /// Check the list of mouse positions to see if the mouse was shaking or not.
        /// </summary>
        /// <returns></returns>
        private bool IsShaking()
        {
            // At least 10 positions needed
            if (_mousePositions.Count < 10)
            {
                return false;
            }

            double speedSum = 0;
            int sharpTurns = 0;

            LinkedListNode<MousePosition> current = _mousePositions.First;

            // Loop thought the linked list, skipping the last element
            while (current.Next != null)
            {
                MousePosition p1 = current.Value;
                MousePosition p2 = current.Next.Value;
                MousePosition p0 = current.Previous?.Value;

                // Distance between the current and the next point.
                double dx = p1.X - p2.X;
                double dy = p1.Y - p2.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);

                // Speed between the current and the next point.
                int dt = p1.Timestamp - p2.Timestamp;
                double v = dt == 0 ? 0 : d / dt;

                speedSum += v;

                // Check the movement angle in the point
                if (p0 != null && p1.Dot(p0, p2) < 0)
                {
                    sharpTurns++;
                }

                current = current.Next;
            }

            // Average mouse speed
            double avgSpeed = speedSum / (_mousePositions.Count - 1);

            return avgSpeed >= MinimumSpeed && sharpTurns >= MinimumTurns;
        }

        private static int TrackingInterval => Math.Max(1, Properties.Settings.Default.ShakeTrackingInterval);

        private static double MinimumSpeed => Math.Max(0, Properties.Settings.Default.ShakeMinimumSpeed);

        private static int MinimumTurns => Math.Max(1, Properties.Settings.Default.ShakeMinimumTurns);

        private static int VisibleDuration => Math.Max(1, Properties.Settings.Default.CursorVisibleDuration);

        private void SetShaking(bool shaking)
        {
            if (_shaking != shaking)
            {
                _shaking = shaking;
                MouseShakeArgs args = new MouseShakeArgs(shaking, DateTime.Now);
                MouseShake?.Invoke(this, args);
            }
        }

        private void Timer_Tick(object sender, ElapsedEventArgs e)
        {
            if (DateTime.Now < _visibleUntil)
            {
                return;
            }

            // Non-blocking: never block the hook thread on the UI thread.
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (DateTime.Now >= _visibleUntil)
                {
                    SetShaking(false);

                    // Idle: stop the 100ms wakeups. OnMouseMove re-enables on the next shake.
                    if (!_shaking)
                    {
                        _timer.Enabled = false;
                    }
                }
            }));
        }

        public void Dispose()
        {
            _mouseEvents.MouseMoveExt -= OnMouseMove;
            _timer.Enabled = false;
            _timer.Dispose();
        }
    }
}
