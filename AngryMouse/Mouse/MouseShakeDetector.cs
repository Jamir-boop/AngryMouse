using AngryMouse.Util;
using Gma.System.MouseKeyHook;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Timers;
using System.Windows;
using System.Windows.Threading;
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
        private const int DoubleControlHoldGraceMilliseconds = 10;
        private const int ControlScanCode = 0x1D;
        private const int AltScanCode = 0x38;

        /// <summary>
        /// The hook into mouse events.
        /// </summary>
        private readonly IKeyboardMouseEvents _mouseEvents;

        /// <summary>
        /// The last time we received a mouse event.
        /// </summary>
        private int _lastMouseEventTimestamp;
        private bool _hasLastMouseEvent;

        private DateTime _shakeVisibleUntil = DateTime.MinValue;
        private int _shakeVisibleStartedAt;
        private int _shakeVisibleDuration;
        private bool _shakeVisibleActive;
        private bool _hotkeyHeld;
        private bool _hotkeyMatched;
        private bool _toggleActive;
        private bool _shakeGestureActive;
        private bool _recordingActive;
        private readonly HashSet<Forms.Keys> _pressedKeys = new HashSet<Forms.Keys>();
        private readonly DoubleControlGestureTracker _doubleControlGesture = new DoubleControlGestureTracker();
        private bool _leftControlKeyDown;
        private bool _rightControlKeyDown;
        private bool _leftWindowsKeyDown;
        private bool _rightWindowsKeyDown;
        private bool _rightAltKeyDown;
        private bool _effectiveStateUpdateQueued;
        private bool _disposed;

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
        private readonly DispatcherTimer _doubleControlHoldTimer;

        /// <summary>
        /// Main constructor.
        /// </summary>
        public MouseShakeDetector()
        {
            DoubleControlGestureTracker.RunDebugSelfCheck();
            RunKeyboardAdapterDebugSelfCheck();
            _doubleControlHoldTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(DoubleControlHoldGraceMilliseconds)
            };
            _doubleControlHoldTimer.Tick += DoubleControlHoldTimer_Tick;
            _mouseEvents = StaticHook.GlobalEvents();

            _mouseEvents.MouseMoveExt += OnMouseMove;
            _mouseEvents.KeyDown += OnKeyDown;
            _mouseEvents.KeyUp += OnKeyUp;
            Properties.Settings.Default.PropertyChanged += SettingsOnPropertyChanged;
            ShellUiDetector.DesktopSwitched += OnDesktopSwitched;
            SystemEvents.SessionSwitch += OnSessionSwitch;

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
            if (!_hasLastMouseEvent || unchecked((uint)(e.Timestamp - _lastMouseEventTimestamp)) > MouseEventRate)
            {
                MouseMove?.Invoke(this, e);

                _lastMouseEventTimestamp = e.Timestamp;
                _hasLastMouseEvent = true;

                if (!ShakeActivationEnabled)
                {
                    _mousePositions.Clear();
                    _shakeGestureActive = false;
                    ClearShakeVisibility();
                    QueueEffectiveStateUpdate();
                    return;
                }

                while (_mousePositions.Count > 0 &&
                       unchecked((uint)(e.Timestamp - _mousePositions.Last.Value.Timestamp)) > TrackingInterval)
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
                        _shakeVisibleDuration = Math.Max(0, VisibleDuration);
                        _shakeVisibleStartedAt = Environment.TickCount;
                        _shakeVisibleUntil = currentTime.AddMilliseconds(_shakeVisibleDuration);
                        _shakeVisibleActive = true;
                        QueueEffectiveStateUpdate();
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
                uint dt = unchecked((uint)(p1.Timestamp - p2.Timestamp));
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

        private static string HotkeyActivationMethod =>
            HotkeySettings.NormalizeActivationMethod(Properties.Settings.Default.HotkeyActivationMethod);

        private static bool DoubleControlActivation =>
            HotkeySettings.IsDoubleControlMethod(HotkeyActivationMethod);

        private static bool DoubleControlRequiresWindowsKey =>
            Properties.Settings.Default.HotkeyDoubleControlRequireWindowsKey;

        private static bool ToggleMode => string.Equals(
            HotkeySettings.NormalizeActivationMode(Properties.Settings.Default.HotkeyActivationMode),
            HotkeySettings.ToggleMode,
            StringComparison.OrdinalIgnoreCase);

        private static string HotkeyModifiers => HotkeySettings.NormalizeModifiers(Properties.Settings.Default.HotkeyModifiers);

        private static string HotkeyKey => HotkeySettings.NormalizeKey(Properties.Settings.Default.HotkeyKey);

        /// <summary>
        /// While the settings window is recording a shortcut, the detector must not track keys
        /// or activate the overlay. Clears held-key state so a shortcut recorded with keys still
        /// down does not immediately arm the hotkey when recording ends.
        /// </summary>
        public void SetRecordingActive(bool active)
        {
            _recordingActive = active;
            ResetTransientInputState("hotkey recording " + (active ? "started" : "stopped"));
        }

        private void OnDesktopSwitched()
        {
            QueueTransientInputReset("desktop switch");
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            QueueTransientInputReset("session switch: " + e.Reason);
        }

        private void QueueTransientInputReset(string reason)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                ResetTransientInputState(reason);
                return;
            }

            dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ResetTransientInputState(reason)));
        }

        private void ResetTransientInputState(string reason)
        {
            if (_disposed)
            {
                return;
            }

            CancelDoubleControlGesture();
            _pressedKeys.Clear();
            _leftControlKeyDown = false;
            _rightControlKeyDown = false;
            _leftWindowsKeyDown = false;
            _rightWindowsKeyDown = false;
            _rightAltKeyDown = false;
            _hotkeyMatched = false;
            _hotkeyHeld = false;
            DebugLog.Write(
                "Transient input state reset: reason=" + reason +
                ", togglePreserved=" + (_toggleActive ? "On" : "Off"));
            QueueEffectiveStateUpdate();
        }

        private void OnKeyDown(object sender, Forms.KeyEventArgs e)
        {
            if (_recordingActive)
            {
                return;
            }

            if (IsRightAltKey(e))
            {
                _rightAltKeyDown = true;
                CancelDoubleControlGesture();
                RemoveAltGrKeys();
                _leftControlKeyDown = false;
                UpdateHotkeyState();
                return;
            }

            if (IsWindowsKey(e.KeyCode))
            {
                SetWindowsKeyState(e.KeyCode, true);
                if (DoubleControlActivation && !DoubleControlRequiresWindowsKey)
                {
                    CancelDoubleControlGesture();
                }

                _pressedKeys.Add(e.KeyCode);
                UpdateHotkeyState();
                return;
            }

            if (IsControlKey(e.KeyCode))
            {
                ControlKeySide side;
                var sideKnown = TryGetControlKeySide(e, out side);
                if (_rightAltKeyDown)
                {
                    CancelDoubleControlGesture();
                    RemoveAltGrKeys();
                    if (sideKnown)
                    {
                        SetControlKeyState(side, false);
                    }

                    UpdateHotkeyState();
                    return;
                }

                if (DoubleControlActivation)
                {
                    if (sideKnown)
                    {
                        HandleDoubleControlKeyDown(side, GetKeyboardTimestamp(e));
                        SetControlKeyState(side, true);
                    }
                    else
                    {
                        _leftControlKeyDown = false;
                        _rightControlKeyDown = false;
                        CancelDoubleControlGesture();
                    }
                }

                _pressedKeys.Add(Forms.Keys.ControlKey);
                UpdateHotkeyState();
                return;
            }

            if (DoubleControlActivation)
            {
                CancelDoubleControlGesture();
            }

            _pressedKeys.Add(NormalizeKey(e.KeyCode));
            UpdateHotkeyState();
        }

        private void OnKeyUp(object sender, Forms.KeyEventArgs e)
        {
            if (_recordingActive)
            {
                return;
            }

            if (IsRightAltKey(e))
            {
                _rightAltKeyDown = false;
                CancelDoubleControlGesture();
                RemoveAltGrKeys();
                _leftControlKeyDown = false;
                UpdateHotkeyState();
                return;
            }

            if (IsWindowsKey(e.KeyCode))
            {
                _pressedKeys.Remove(e.KeyCode);
                SetWindowsKeyState(e.KeyCode, false);
                if (DoubleControlActivation && DoubleControlRequiresWindowsKey && !IsWindowsKeyDown)
                {
                    CancelDoubleControlGesture();
                }

                UpdateHotkeyState();
                return;
            }

            if (IsControlKey(e.KeyCode))
            {
                _pressedKeys.Remove(Forms.Keys.ControlKey);
                if (DoubleControlActivation)
                {
                    ControlKeySide side;
                    if (TryGetControlKeySide(e, out side))
                    {
                        SetControlKeyState(side, false);
                        if (IsSelectedControlSide(side))
                        {
                            ApplyDoubleControlResult(_doubleControlGesture.OnControlUp(side));
                        }
                        else
                        {
                            CancelDoubleControlGesture();
                        }
                    }
                    else
                    {
                        _leftControlKeyDown = false;
                        _rightControlKeyDown = false;
                        CancelDoubleControlGesture();
                    }
                }

                UpdateHotkeyState();
                return;
            }

            _pressedKeys.Remove(NormalizeKey(e.KeyCode));
            UpdateHotkeyState();
        }

        private void UpdateHotkeyState()
        {
            if (DoubleControlActivation)
            {
                _hotkeyMatched = false;
                if (!HotkeyActivationEnabled)
                {
                    CancelDoubleControlGesture();
                    SetHotkeyHeld(false, "Double Ctrl disabled");
                }

                return;
            }

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
            SetHotkeyHeld(matched, "Hotkey");
        }

        private void SetHotkeyHeld(bool held, string source)
        {
            if (_hotkeyHeld != held)
            {
                _hotkeyHeld = held;
                QueueDebugLog(
                    source + " hold changed: held=" +
                    (_hotkeyHeld ? "On" : "Off") +
                    ", " +
                    GetActivationSummary(DateTime.Now));
                QueueEffectiveStateUpdate();
            }
        }

        private void HandleDoubleControlKeyDown(ControlKeySide side, int timestamp)
        {
            if (!HotkeyActivationEnabled)
            {
                CancelDoubleControlGesture();
                return;
            }

            if (IsControlKeyDown(side))
            {
                return;
            }

            if (!IsSelectedControlSide(side) || IsOtherControlKeyDown(side) || HasDisqualifyingDoubleControlKey())
            {
                CancelDoubleControlGesture();
                return;
            }

            var maximumInterval = Math.Max(1, Forms.SystemInformation.DoubleClickTime);
            var minimumInterval = Math.Min(100, Math.Max(1, maximumInterval / 5));
            ApplyDoubleControlResult(_doubleControlGesture.OnControlDown(
                side,
                timestamp,
                !ToggleMode,
                !DoubleControlRequiresWindowsKey || IsWindowsKeyDown,
                minimumInterval,
                maximumInterval));
        }

        private void ApplyDoubleControlResult(DoubleControlGestureResult result)
        {
            switch (result)
            {
                case DoubleControlGestureResult.HoldPending:
                    _doubleControlHoldTimer.Stop();
                    _doubleControlHoldTimer.Start();
                    break;
                case DoubleControlGestureResult.Toggle:
                    if (HotkeyActivationEnabled && ToggleMode && DoubleControlActivation)
                    {
                        ToggleActivation("Double Ctrl");
                    }
                    break;
                case DoubleControlGestureResult.HoldStarted:
                    if (HotkeyActivationEnabled && !ToggleMode && DoubleControlActivation)
                    {
                        SetHotkeyHeld(true, "Double Ctrl");
                    }
                    break;
                case DoubleControlGestureResult.HoldEnded:
                    _doubleControlHoldTimer.Stop();
                    SetHotkeyHeld(false, "Double Ctrl");
                    break;
            }
        }

        private void CancelDoubleControlGesture()
        {
            _doubleControlHoldTimer.Stop();
            ApplyDoubleControlResult(_doubleControlGesture.Cancel());
        }

        private void DoubleControlHoldTimer_Tick(object sender, EventArgs e)
        {
            _doubleControlHoldTimer.Stop();
            if (_recordingActive ||
                _rightAltKeyDown ||
                !HotkeyActivationEnabled ||
                ToggleMode ||
                !DoubleControlActivation ||
                (DoubleControlRequiresWindowsKey && !IsWindowsKeyDown))
            {
                CancelDoubleControlGesture();
                return;
            }

            ApplyDoubleControlResult(_doubleControlGesture.ConfirmPendingHold());
        }

        private bool HasDisqualifyingDoubleControlKey()
        {
            foreach (var key in _pressedKeys)
            {
                // The selected Control key is already down when Windows emits key-repeat.
                // The tracker validates whether the repeated event is from the same side.
                if (key == Forms.Keys.ControlKey)
                {
                    continue;
                }

                if (DoubleControlRequiresWindowsKey && IsWindowsKey(key))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static int GetKeyboardTimestamp(Forms.KeyEventArgs e)
        {
            var extendedArgs = e as KeyEventArgsExt;
            return extendedArgs?.Timestamp ?? Environment.TickCount;
        }

        private static bool TryGetControlKeySide(Forms.KeyEventArgs e, out ControlKeySide side)
        {
            var extendedArgs = e as KeyEventArgsExt;
            return TryResolveControlKeySide(
                e.KeyCode,
                extendedArgs?.ScanCode ?? 0,
                extendedArgs?.IsExtendedKey ?? false,
                extendedArgs != null,
                out side);
        }

        private static bool TryResolveControlKeySide(
            Forms.Keys key,
            int scanCode,
            bool isExtendedKey,
            bool hasHookMetadata,
            out ControlKeySide side)
        {
            if (key == Forms.Keys.RControlKey)
            {
                side = ControlKeySide.Right;
                return true;
            }

            if (key == Forms.Keys.LControlKey)
            {
                side = ControlKeySide.Left;
                return true;
            }

            if (IsControlKey(key) && hasHookMetadata && scanCode == ControlScanCode)
            {
                side = isExtendedKey ? ControlKeySide.Right : ControlKeySide.Left;
                return true;
            }

            side = ControlKeySide.Left;
            return false;
        }

        [Conditional("DEBUG")]
        private static void RunKeyboardAdapterDebugSelfCheck()
        {
            ExpectControlSide("explicit left", Forms.Keys.LControlKey, 0, false, false, ControlKeySide.Left);
            ExpectControlSide("explicit right", Forms.Keys.RControlKey, 0, false, false, ControlKeySide.Right);
            ExpectControlSide("generic left", Forms.Keys.ControlKey, ControlScanCode, false, true, ControlKeySide.Left);
            ExpectControlSide("generic right", Forms.Keys.ControlKey, ControlScanCode, true, true, ControlKeySide.Right);

            ControlKeySide ignored;
            if (TryResolveControlKeySide(Forms.Keys.ControlKey, 0, false, true, out ignored) ||
                TryResolveControlKeySide(Forms.Keys.ControlKey, ControlScanCode, false, false, out ignored))
            {
                throw new InvalidOperationException("Ambiguous generic Control input must be rejected.");
            }
        }

        private static void ExpectControlSide(
            string name,
            Forms.Keys key,
            int scanCode,
            bool isExtendedKey,
            bool hasHookMetadata,
            ControlKeySide expected)
        {
            ControlKeySide actual;
            if (!TryResolveControlKeySide(key, scanCode, isExtendedKey, hasHookMetadata, out actual) ||
                actual != expected)
            {
                throw new InvalidOperationException(
                    "Control side self-check failed: " + name +
                    ", expected=" + expected +
                    ", actual=" + actual);
            }
        }

        private static bool IsSelectedControlSide(ControlKeySide side)
        {
            switch (HotkeyActivationMethod)
            {
                case HotkeySettings.DoubleLeftControlMethod:
                    return side == ControlKeySide.Left;
                case HotkeySettings.DoubleRightControlMethod:
                    return side == ControlKeySide.Right;
                case HotkeySettings.DoubleEitherControlMethod:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsWindowsKeyDown => _leftWindowsKeyDown || _rightWindowsKeyDown;

        private bool IsControlKeyDown(ControlKeySide side)
        {
            return side == ControlKeySide.Left ? _leftControlKeyDown : _rightControlKeyDown;
        }

        private bool IsOtherControlKeyDown(ControlKeySide side)
        {
            return side == ControlKeySide.Left ? _rightControlKeyDown : _leftControlKeyDown;
        }

        private void SetControlKeyState(ControlKeySide side, bool down)
        {
            if (side == ControlKeySide.Left)
            {
                _leftControlKeyDown = down;
            }
            else
            {
                _rightControlKeyDown = down;
            }
        }

        private void SetWindowsKeyState(Forms.Keys key, bool down)
        {
            if (key == Forms.Keys.LWin)
            {
                _leftWindowsKeyDown = down;
            }
            else if (key == Forms.Keys.RWin)
            {
                _rightWindowsKeyDown = down;
            }
        }

        private static bool IsWindowsKey(Forms.Keys key)
        {
            return key == Forms.Keys.LWin || key == Forms.Keys.RWin;
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

        private static bool IsRightAltKey(Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Forms.Keys.RMenu)
            {
                return true;
            }

            var extendedArgs = e as KeyEventArgsExt;
            return (e.KeyCode == Forms.Keys.Menu || e.KeyCode == Forms.Keys.Alt) &&
                   extendedArgs != null &&
                   extendedArgs.ScanCode == AltScanCode &&
                   extendedArgs.IsExtendedKey;
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

        private void ToggleActivation(string source)
        {
            _toggleActive = !_toggleActive;
            ClearShakeVisibility();
            _hotkeyHeld = false;
            QueueDebugLog(
                "Activation toggled: source=" +
                source +
                ", toggleActive=" +
                (_toggleActive ? "On" : "Off") +
                ", " +
                GetActivationSummary(DateTime.Now));
            QueueEffectiveStateUpdate();
        }

        private void QueueEffectiveStateUpdate()
        {
            if (_disposed || _effectiveStateUpdateQueued)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            _effectiveStateUpdateQueued = true;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _effectiveStateUpdateQueued = false;
                if (!_disposed)
                {
                    ApplyEffectiveState(DateTime.Now);
                }
            }));
        }

        private void QueueDebugLog(string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (_disposed || dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_disposed)
                {
                    DebugLog.Write(message);
                }
            }));
        }

        private void ApplyEffectiveState(DateTime now)
        {
            if (ToggleMode)
            {
                SetShaking(_toggleActive);
                return;
            }

            var active = (ShakeActivationEnabled && IsShakeVisible()) || _hotkeyHeld;
            SetShaking(active);
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "ShakeActivationEnabled":
                case "HotkeyActivationEnabled":
                case "HotkeyActivationMode":
                case "HotkeyActivationMethod":
                case "HotkeyDoubleControlRequireWindowsKey":
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
            CancelDoubleControlGesture();
            _leftControlKeyDown = false;
            _rightControlKeyDown = false;
            _rightAltKeyDown = false;
            if (!ShakeActivationEnabled || ToggleMode)
            {
                ClearShakeVisibility();
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
                _hotkeyHeld = HotkeyActivationEnabled &&
                              !DoubleControlActivation &&
                              IsHotkeyMatched();
            }

            _hotkeyMatched = HotkeyActivationEnabled &&
                             !DoubleControlActivation &&
                             (ToggleMode ? AreHotkeyCoreKeysPressed() : IsHotkeyMatched());
            DebugLog.Write("Activation settings applied: " + GetActivationSummary(now));
            QueueEffectiveStateUpdate();
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

            if (IsShakeVisible())
            {
                return;
            }

            // Non-blocking: never block the hook thread on the UI thread.
            var application = Application.Current;
            if (application == null)
            {
                // App is shutting down; stop the timer so it stops firing.
                _timer.Enabled = false;
                return;
            }

            application.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ToggleMode && !IsShakeVisible())
                {
                    ClearShakeVisibility();
                    QueueEffectiveStateUpdate();

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
                   ", hotkeyMethod=" +
                   HotkeySettings.FormatActivationMethod(HotkeyActivationMethod) +
                   ", doubleControlWinGuard=" +
                   (DoubleControlRequiresWindowsKey ? "On" : "Off") +
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

        private bool IsShakeVisible()
        {
            return _shakeVisibleActive &&
                   unchecked((uint)(Environment.TickCount - _shakeVisibleStartedAt)) < (uint)_shakeVisibleDuration;
        }

        private void ClearShakeVisibility()
        {
            _shakeVisibleUntil = DateTime.MinValue;
            _shakeVisibleDuration = 0;
            _shakeVisibleActive = false;
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mouseEvents.MouseMoveExt -= OnMouseMove;
            _mouseEvents.KeyDown -= OnKeyDown;
            _mouseEvents.KeyUp -= OnKeyUp;
            Properties.Settings.Default.PropertyChanged -= SettingsOnPropertyChanged;
            ShellUiDetector.DesktopSwitched -= OnDesktopSwitched;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _doubleControlHoldTimer.Stop();
            _doubleControlHoldTimer.Tick -= DoubleControlHoldTimer_Tick;
            _timer.Enabled = false;
            _timer.Elapsed -= Timer_Tick;
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
