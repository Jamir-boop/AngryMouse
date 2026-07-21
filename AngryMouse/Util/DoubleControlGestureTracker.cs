using System;
using System.Diagnostics;

namespace AngryMouse.Util
{
    internal enum ControlKeySide
    {
        Left,
        Right
    }

    internal enum DoubleControlGestureResult
    {
        None,
        Toggle,
        HoldPending,
        HoldStarted,
        HoldEnded
    }

    /// <summary>
    /// Tracks two presses of the same physical Control key. Toggle gestures complete on the
    /// second release; hold gestures are confirmed after a short grace period and end on release.
    /// </summary>
    internal sealed class DoubleControlGestureTracker
    {
        private GestureState _state;
        private ControlKeySide _side;
        private int _firstKeyDownTimestamp;
        private bool _holdMode;
        private bool _holdPending;
        private bool _holdActive;

        public DoubleControlGestureResult OnControlDown(
            ControlKeySide side,
            int timestamp,
            bool holdMode,
            bool guardSatisfied,
            int minimumInterval,
            int maximumInterval)
        {
            if (!guardSatisfied)
            {
                return Cancel();
            }

            switch (_state)
            {
                case GestureState.Idle:
                    BeginFirstPress(side, timestamp);
                    return DoubleControlGestureResult.None;

                case GestureState.FirstDown:
                    // Ignore keyboard auto-repeat for the first press. Pressing the other
                    // Control key at the same time invalidates the sequence.
                    return side == _side ? DoubleControlGestureResult.None : Cancel();

                case GestureState.FirstUp:
                {
                    var elapsed = unchecked((uint)(timestamp - _firstKeyDownTimestamp));
                    if (side == _side &&
                        elapsed >= (uint)Math.Max(0, minimumInterval) &&
                        elapsed <= (uint)Math.Max(minimumInterval, maximumInterval))
                    {
                        _state = GestureState.SecondDown;
                        _holdMode = holdMode;
                        _holdPending = holdMode;
                        _holdActive = false;
                        return holdMode
                            ? DoubleControlGestureResult.HoldPending
                            : DoubleControlGestureResult.None;
                    }

                    // The new press can still be the beginning of the next attempt.
                    BeginFirstPress(side, timestamp);
                    return DoubleControlGestureResult.None;
                }

                case GestureState.SecondDown:
                    // Ignore repeat for the second press; another Control key cancels it.
                    return side == _side ? DoubleControlGestureResult.None : Cancel();

                default:
                    return Cancel();
            }
        }

        public DoubleControlGestureResult OnControlUp(ControlKeySide side)
        {
            if (side != _side)
            {
                return DoubleControlGestureResult.None;
            }

            if (_state == GestureState.FirstDown)
            {
                _state = GestureState.FirstUp;
                return DoubleControlGestureResult.None;
            }

            if (_state != GestureState.SecondDown)
            {
                return DoubleControlGestureResult.None;
            }

            var result = _holdMode
                ? (_holdActive ? DoubleControlGestureResult.HoldEnded : DoubleControlGestureResult.None)
                : DoubleControlGestureResult.Toggle;
            Reset();
            return result;
        }

        public DoubleControlGestureResult ConfirmPendingHold()
        {
            if (_state != GestureState.SecondDown || !_holdPending)
            {
                return DoubleControlGestureResult.None;
            }

            _holdPending = false;
            _holdActive = true;
            return DoubleControlGestureResult.HoldStarted;
        }

        public DoubleControlGestureResult Cancel()
        {
            var result = _holdActive
                ? DoubleControlGestureResult.HoldEnded
                : DoubleControlGestureResult.None;
            Reset();
            return result;
        }

        private void BeginFirstPress(ControlKeySide side, int timestamp)
        {
            _state = GestureState.FirstDown;
            _side = side;
            _firstKeyDownTimestamp = timestamp;
            _holdMode = false;
            _holdPending = false;
            _holdActive = false;
        }

        private void Reset()
        {
            _state = GestureState.Idle;
            _firstKeyDownTimestamp = 0;
            _holdMode = false;
            _holdPending = false;
            _holdActive = false;
        }

        [Conditional("DEBUG")]
        public static void RunDebugSelfCheck()
        {
            var tracker = new DoubleControlGestureTracker();
            Expect("toggle first down", tracker.OnControlDown(ControlKeySide.Left, 0, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("toggle first up", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.None);
            Expect("toggle second down", tracker.OnControlDown(ControlKeySide.Left, 150, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("toggle second up", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.Toggle);

            Expect("hold first down", tracker.OnControlDown(ControlKeySide.Right, 1000, true, true, 100, 500), DoubleControlGestureResult.None);
            Expect("hold first up", tracker.OnControlUp(ControlKeySide.Right), DoubleControlGestureResult.None);
            Expect("hold pending", tracker.OnControlDown(ControlKeySide.Right, 1150, true, true, 100, 500), DoubleControlGestureResult.HoldPending);
            Expect("hold start", tracker.ConfirmPendingHold(), DoubleControlGestureResult.HoldStarted);
            Expect("hold end", tracker.OnControlUp(ControlKeySide.Right), DoubleControlGestureResult.HoldEnded);

            tracker.OnControlDown(ControlKeySide.Left, 1700, true, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            Expect("short hold pending", tracker.OnControlDown(ControlKeySide.Left, 1850, true, true, 100, 500), DoubleControlGestureResult.HoldPending);
            Expect("short hold release", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.None);
            Expect("released hold cannot start", tracker.ConfirmPendingHold(), DoubleControlGestureResult.None);

            tracker.OnControlDown(ControlKeySide.Left, 2000, false, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            Expect("mixed side", tracker.OnControlDown(ControlKeySide.Right, 2150, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("mixed side release", tracker.OnControlUp(ControlKeySide.Right), DoubleControlGestureResult.None);

            tracker.Cancel();
            Expect("repeat first down", tracker.OnControlDown(ControlKeySide.Left, 2500, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("repeat ignored", tracker.OnControlDown(ControlKeySide.Left, 2550, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("repeat first up", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.None);

            tracker.Cancel();
            tracker.OnControlDown(ControlKeySide.Left, 3000, false, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            Expect("bounce rejected", tracker.OnControlDown(ControlKeySide.Left, 3050, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("bounce release", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.None);

            tracker.Cancel();
            tracker.OnControlDown(ControlKeySide.Left, 4000, false, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            Expect("timeout rejected", tracker.OnControlDown(ControlKeySide.Left, 4600, false, true, 100, 500), DoubleControlGestureResult.None);
            Expect("timeout release", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.None);

            tracker.Cancel();
            Expect("guard required", tracker.OnControlDown(ControlKeySide.Left, 5000, false, false, 100, 500), DoubleControlGestureResult.None);
            Expect("cancel idle", tracker.Cancel(), DoubleControlGestureResult.None);

            tracker.OnControlDown(ControlKeySide.Left, 6000, true, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            Expect("cancel pending hold", tracker.OnControlDown(ControlKeySide.Left, 6150, true, true, 100, 500), DoubleControlGestureResult.HoldPending);
            Expect("cancel pending", tracker.Cancel(), DoubleControlGestureResult.None);

            tracker.OnControlDown(ControlKeySide.Left, 7000, true, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            tracker.OnControlDown(ControlKeySide.Left, 7150, true, true, 100, 500);
            tracker.ConfirmPendingHold();
            Expect("cancel active hold", tracker.Cancel(), DoubleControlGestureResult.HoldEnded);

            tracker.OnControlDown(ControlKeySide.Left, int.MaxValue - 50, false, true, 100, 500);
            tracker.OnControlUp(ControlKeySide.Left);
            Expect(
                "timestamp wrap",
                tracker.OnControlDown(ControlKeySide.Left, int.MinValue + 49, false, true, 100, 500),
                DoubleControlGestureResult.None);
            Expect("timestamp wrap toggle", tracker.OnControlUp(ControlKeySide.Left), DoubleControlGestureResult.Toggle);
        }

        private static void Expect(
            string name,
            DoubleControlGestureResult actual,
            DoubleControlGestureResult expected)
        {
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    "Double Control gesture self-check failed: " + name +
                    ", expected=" + expected +
                    ", actual=" + actual);
            }
        }

        private enum GestureState
        {
            Idle,
            FirstDown,
            FirstUp,
            SecondDown
        }
    }
}
