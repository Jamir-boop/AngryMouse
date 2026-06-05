using AngryMouse.Util;
using Gma.System.MouseKeyHook;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;
using Forms = System.Windows.Forms;

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
        private const int VirtualKeyRightMenu = 0xA5;

        /// <summary>
        /// The hook into mouse events.
        /// </summary>
        private readonly IKeyboardMouseEvents _mouseEvents;

        /// <summary>
        /// The last time we received a mouse event.
        /// </summary>
        private DateTime _lastMouseEvent = DateTime.MinValue;

        private DateTime _shakeVisibleUntil = DateTime.MinValue;
        private bool _hotkeyHeld;
        private bool _hotkeyMatched;
        private bool _toggleActive;
        private bool _shakeGestureActive;
        private readonly HashSet<Forms.Keys> _pressedKeys = new HashSet<Forms.Keys>();

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
            _mouseEvents.KeyDown += OnKeyDown;
            _mouseEvents.KeyUp += OnKeyUp;
            Properties.Settings.Default.PropertyChanged += SettingsOnPropertyChanged;

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

                if (!ShakeActivationEnabled)
                {
                    _mousePositions.Clear();
                    _shakeGestureActive = false;
                    _shakeVisibleUntil = DateTime.MinValue;
                    ApplyEffectiveState(currentTime);
                    return;
                }

                while (_mousePositions.Count > 0 &&
                       e.Timestamp - TrackingInterval > _mousePositions.Last.Value.Timestamp)
                {
                    // Remove old positions.
                    _mousePositions.RemoveLast();
                }

                _mousePositions.AddFirst(e);

                if (IsShaking())
                {
                    if (ToggleMode)
                    {
                        if (!_shakeGestureActive)
                        {
                            ToggleActivation("Shake");
                        }
                    }
                    else
                    {
                        _shakeVisibleUntil = currentTime.AddMilliseconds(VisibleDuration);
                        ApplyEffectiveState(currentTime);
                        if (!_timer.Enabled)
                        {
                            _timer.Enabled = true;
                        }
                    }

                    _shakeGestureActive = true;
                }
                else
                {
                    _shakeGestureActive = false;
                }
            }
        }

        /// <summary>
        /// Check the list of mouse positions to see if the mouse was shaking or not.
        /// </summary>
        /// <returns></returns>
        private bool IsShaking()
        {
            // At least 10 positions needed.
            if (_mousePositions.Count < 10)
            {
                return false;
            }

            double speedSum = 0;
            int sharpTurns = 0;

            LinkedListNode<MousePosition> current = _mousePositions.First;

            // Loop through the linked list, skipping the last element.
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

                // Check the movement angle in the point.
                if (p0 != null && p1.Dot(p0, p2) < 0)
                {
                    sharpTurns++;
                }

                current = current.Next;
            }

            // Average mouse speed.
            double avgSpeed = speedSum / (_mousePositions.Count - 1);

            return avgSpeed >= MinimumSpeed && sharpTurns >= MinimumTurns;
        }

        private static int TrackingInterval => Math.Max(1, Properties.Settings.Default.ShakeTrackingInterval);

        private static double MinimumSpeed => Math.Max(0, Properties.Settings.Default.ShakeMinimumSpeed);

        private static int MinimumTurns => Math.Max(1, Properties.Settings.Default.ShakeMinimumTurns);

        private static int VisibleDuration => Math.Max(1, Properties.Settings.Default.CursorVisibleDuration);

        private static bool ShakeActivationEnabled
        {
            get
            {
                var settings = Properties.Settings.Default;
                return settings.ShakeActivationEnabled || !settings.HotkeyActivationEnabled;
            }
        }

        private static bool HotkeyActivationEnabled => Properties.Settings.Default.HotkeyActivationEnabled;

        private static bool ToggleMode => string.Equals(
            HotkeySettings.NormalizeActivationMode(Properties.Settings.Default.HotkeyActivationMode),
            HotkeySettings.ToggleMode,
            StringComparison.OrdinalIgnoreCase);

        private static string HotkeyModifiers => HotkeySettings.NormalizeModifiers(Properties.Settings.Default.HotkeyModifiers);

        private static string HotkeyKey => HotkeySettings.NormalizeKey(Properties.Settings.Default.HotkeyKey);

        private void OnKeyDown(object sender, Forms.KeyEventArgs e)
        {
            if (IsAltGrKey(e.KeyCode) || IsSyntheticControlForAltGr(e.KeyCode))
            {
                RemoveAltGrKeys();
                UpdateHotkeyState();
                return;
            }

            _pressedKeys.Add(NormalizeKey(e.KeyCode));
            UpdateHotkeyState();
        }

        private void OnKeyUp(object sender, Forms.KeyEventArgs e)
        {
            if (IsAltGrKey(e.KeyCode))
            {
                RemoveAltGrKeys();
                UpdateHotkeyState();
                return;
            }

            _pressedKeys.Remove(NormalizeKey(e.KeyCode));
            UpdateHotkeyState();
        }

        private void UpdateHotkeyState()
        {
            var matched = HotkeyActivationEnabled && IsHotkeyMatched();

            if (ToggleMode)
            {
                if (matched && !_hotkeyMatched)
                {
                    ToggleActivation("Hotkey");
                }

                _hotkeyMatched = HotkeyActivationEnabled && AreHotkeyCoreKeysPressed();
                _hotkeyHeld = false;
                return;
            }

            _hotkeyMatched = matched;
            if (_hotkeyHeld != matched)
            {
                _hotkeyHeld = matched;
                DebugLog.Write(
                    "Hotkey hold changed: held=" +
                    (_hotkeyHeld ? "On" : "Off") +
                    ", " +
                    GetActivationSummary(DateTime.Now));
                ApplyEffectiveState(DateTime.Now);
            }
        }

        private bool IsHotkeyMatched()
        {
            var requiredModifiers = GetRequiredModifiers(HotkeyModifiers);
            var key = ParseKey(HotkeyKey);
            if (requiredModifiers.Count == 0 && key == Forms.Keys.None)
            {
                requiredModifiers.Add(Forms.Keys.ControlKey);
            }

            foreach (var modifier in ModifierKeys)
            {
                if (_pressedKeys.Contains(modifier) != requiredModifiers.Contains(modifier))
                {
                    return false;
                }
            }

            return key == Forms.Keys.None || _pressedKeys.Contains(key);
        }

        private bool AreHotkeyCoreKeysPressed()
        {
            var requiredModifiers = GetRequiredModifiers(HotkeyModifiers);
            var key = ParseKey(HotkeyKey);
            if (requiredModifiers.Count == 0 && key == Forms.Keys.None)
            {
                requiredModifiers.Add(Forms.Keys.ControlKey);
            }

            foreach (var modifier in requiredModifiers)
            {
                if (!_pressedKeys.Contains(modifier))
                {
                    return false;
                }
            }

            return key == Forms.Keys.None || _pressedKeys.Contains(key);
        }

        private static HashSet<Forms.Keys> GetRequiredModifiers(string value)
        {
            var modifiers = new HashSet<Forms.Keys>();
            var parts = (value ?? string.Empty).Split(new[] { '+', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "control":
                    case "ctrl":
                        modifiers.Add(Forms.Keys.ControlKey);
                        break;
                    case "shift":
                        modifiers.Add(Forms.Keys.ShiftKey);
                        break;
                    case "alt":
                    case "menu":
                        modifiers.Add(Forms.Keys.Menu);
                        break;
                }
            }

            return modifiers;
        }

        private static Forms.Keys ParseKey(string value)
        {
            Forms.Keys key;
            if (Enum.TryParse(value ?? string.Empty, true, out key))
            {
                return NormalizeKey(key);
            }

            return Forms.Keys.None;
        }

        private static Forms.Keys NormalizeKey(Forms.Keys key)
        {
            switch (key)
            {
                case Forms.Keys.LControlKey:
                case Forms.Keys.RControlKey:
                case Forms.Keys.Control:
                    return Forms.Keys.ControlKey;
                case Forms.Keys.LShiftKey:
                case Forms.Keys.RShiftKey:
                case Forms.Keys.Shift:
                    return Forms.Keys.ShiftKey;
                case Forms.Keys.LMenu:
                case Forms.Keys.Alt:
                    return Forms.Keys.Menu;
                default:
                    return key;
            }
        }

        private void RemoveAltGrKeys()
        {
            _pressedKeys.Remove(Forms.Keys.RMenu);
            _pressedKeys.Remove(Forms.Keys.Menu);
            _pressedKeys.Remove(Forms.Keys.ControlKey);
        }

        private static bool IsAltGrKey(Forms.Keys key)
        {
            return key == Forms.Keys.RMenu ||
                   (key == Forms.Keys.Menu && IsPhysicalRightAltDown());
        }

        private static bool IsSyntheticControlForAltGr(Forms.Keys key)
        {
            return IsControlKey(key) && IsPhysicalRightAltDown();
        }

        private static bool IsControlKey(Forms.Keys key)
        {
            switch (key)
            {
                case Forms.Keys.LControlKey:
                case Forms.Keys.RControlKey:
                case Forms.Keys.Control:
                case Forms.Keys.ControlKey:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsPhysicalRightAltDown()
        {
            return (GetAsyncKeyState(VirtualKeyRightMenu) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private void ToggleActivation(string source)
        {
            _toggleActive = !_toggleActive;
            _shakeVisibleUntil = DateTime.MinValue;
            _hotkeyHeld = false;
            DebugLog.Write(
                "Activation toggled: source=" +
                source +
                ", toggleActive=" +
                (_toggleActive ? "On" : "Off") +
                ", " +
                GetActivationSummary(DateTime.Now));
            ApplyEffectiveState(DateTime.Now);
        }

        private void ApplyEffectiveState(DateTime now)
        {
            if (ToggleMode)
            {
                SetShaking(_toggleActive);
                return;
            }

            var active = (ShakeActivationEnabled && now < _shakeVisibleUntil) || _hotkeyHeld;
            SetShaking(active);
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "ShakeActivationEnabled":
                case "HotkeyActivationEnabled":
                case "HotkeyActivationMode":
                case "HotkeyModifiers":
                case "HotkeyKey":
                    DebugLog.Write("Activation setting changed: " + e.PropertyName);
                    ApplyActivationSettings();
                    break;
            }
        }

        private void ApplyActivationSettings()
        {
            var now = DateTime.Now;
            if (!ShakeActivationEnabled || ToggleMode)
            {
                _shakeVisibleUntil = DateTime.MinValue;
                _shakeGestureActive = false;
                _mousePositions.Clear();
            }

            if (ToggleMode)
            {
                _toggleActive = _shaking;
                _hotkeyHeld = false;
                _timer.Enabled = false;
            }
            else
            {
                _toggleActive = false;
                _hotkeyHeld = HotkeyActivationEnabled && IsHotkeyMatched();
            }

            _hotkeyMatched = HotkeyActivationEnabled &&
                             (ToggleMode ? AreHotkeyCoreKeysPressed() : IsHotkeyMatched());
            DebugLog.Write("Activation settings applied: " + GetActivationSummary(now));
            ApplyEffectiveState(now);
        }

        private void SetShaking(bool shaking)
        {
            if (_shaking != shaking)
            {
                _shaking = shaking;
                DebugLog.Write(
                    "MouseShakeDetector state changed: shaking=" +
                    (_shaking ? "On" : "Off") +
                    ", " +
                    GetActivationSummary(DateTime.Now));
                MouseShakeArgs args = new MouseShakeArgs(shaking, DateTime.Now);
                MouseShake?.Invoke(this, args);
            }
        }

        private void Timer_Tick(object sender, ElapsedEventArgs e)
        {
            if (ToggleMode)
            {
                _timer.Enabled = false;
                return;
            }

            if (DateTime.Now < _shakeVisibleUntil)
            {
                return;
            }

            // Non-blocking: never block the hook thread on the UI thread.
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ToggleMode && DateTime.Now >= _shakeVisibleUntil)
                {
                    _shakeVisibleUntil = DateTime.MinValue;
                    ApplyEffectiveState(DateTime.Now);

                    // Idle: stop the 100ms wakeups. OnMouseMove re-enables on the next shake.
                    if (!_shaking)
                    {
                        _timer.Enabled = false;
                    }
                }
            }));
        }

        private string GetActivationSummary(DateTime now)
        {
            return "mode=" +
                   (ToggleMode ? "Toggle" : "Hold") +
                   ", shakeEnabled=" +
                   (ShakeActivationEnabled ? "On" : "Off") +
                   ", hotkeyEnabled=" +
                   (HotkeyActivationEnabled ? "On" : "Off") +
                   ", hotkeyHeld=" +
                   (_hotkeyHeld ? "On" : "Off") +
                   ", hotkeyMatched=" +
                   (_hotkeyMatched ? "On" : "Off") +
                   ", toggleActive=" +
                   (_toggleActive ? "On" : "Off") +
                   ", shakeVisibleUntil=" +
                   FormatDateTime(_shakeVisibleUntil) +
                   ", now=" +
                   FormatDateTime(now) +
                   ", pressedKeys=" +
                   FormatPressedKeys();
        }

        private static string FormatDateTime(DateTime value)
        {
            return value == DateTime.MinValue
                ? "None"
                : value.ToString("O", CultureInfo.InvariantCulture);
        }

        private string FormatPressedKeys()
        {
            if (_pressedKeys.Count == 0)
            {
                return "None";
            }

            var keys = new List<string>();
            foreach (var key in _pressedKeys)
            {
                keys.Add(key.ToString());
            }

            keys.Sort(StringComparer.Ordinal);
            return string.Join("+", keys.ToArray());
        }

        public void Dispose()
        {
            _mouseEvents.MouseMoveExt -= OnMouseMove;
            _mouseEvents.KeyDown -= OnKeyDown;
            _mouseEvents.KeyUp -= OnKeyUp;
            Properties.Settings.Default.PropertyChanged -= SettingsOnPropertyChanged;
            _timer.Enabled = false;
            _timer.Dispose();
        }

        private static readonly Forms.Keys[] ModifierKeys =
        {
            Forms.Keys.ControlKey,
            Forms.Keys.ShiftKey,
            Forms.Keys.Menu
        };
    }
}
