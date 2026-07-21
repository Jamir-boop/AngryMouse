using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AngryMouse.Util
{
    internal static class ShellUiDetector
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int GW_HWNDNEXT = 2;

        private static readonly HashSet<string> ShellProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StartMenuExperienceHost",
                "ShellExperienceHost"
            };

        private static readonly HashSet<string> ShellClassNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Windows.UI.Core.CoreWindow",
                "XamlExplorerHostIslandWindow",
                "MultitaskingViewFrame",
                "TaskSwitcherWnd",
                "TaskListThumbnailWnd"
            };

        private static readonly WinEventDelegate WinEventProc = WinEventCallback;
        private static readonly DispatcherTimer PollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private static IntPtr _eventHook;
        private static IntPtr _desktopSwitchHook;
        private static bool _started;
        private static bool _active;
        private static bool _shellUiActive;

        public static event Action<bool> ShellUiActiveChanged;
        public static event Action DesktopSwitched;

        public static void Start()
        {
            if (_started)
            {
                return;
            }

            PollTimer.Tick += PollTimer_Tick;
            _eventHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                WinEventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);
            _desktopSwitchHook = SetWinEventHook(
                EVENT_SYSTEM_DESKTOPSWITCH,
                EVENT_SYSTEM_DESKTOPSWITCH,
                IntPtr.Zero,
                WinEventProc,
                0,
                0,
                WINEVENT_OUTOFCONTEXT);
            _started = true;
        }

        public static void Stop()
        {
            _started = false;
            SetActive(false);
            PollTimer.Tick -= PollTimer_Tick;

            if (_eventHook != IntPtr.Zero)
            {
                UnhookWinEvent(_eventHook);
                _eventHook = IntPtr.Zero;
            }

            if (_desktopSwitchHook != IntPtr.Zero)
            {
                UnhookWinEvent(_desktopSwitchHook);
                _desktopSwitchHook = IntPtr.Zero;
            }
        }

        public static void SetActive(bool active)
        {
            _active = active;
            if (active)
            {
                EvaluateShellUi();
                if (!PollTimer.IsEnabled)
                {
                    PollTimer.Start();
                }

                return;
            }

            PollTimer.Stop();
            SetShellUiActive(false);
        }

        private static void PollTimer_Tick(object sender, EventArgs e)
        {
            if (_active)
            {
                EvaluateShellUi();
            }
        }

        private static void WinEventCallback(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (!_started)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            if (eventType == EVENT_SYSTEM_DESKTOPSWITCH)
            {
                dispatcher.BeginInvoke(new Action(() => DesktopSwitched?.Invoke()));
                return;
            }

            if (_active)
            {
                dispatcher.BeginInvoke(new Action(EvaluateShellUi));
            }
        }

        private static void EvaluateShellUi()
        {
            SetShellUiActive(IsShellForegroundWindow(GetForegroundWindow()) ||
                             EnumerateTopWindows(8).Any(IsShellClassWindow));
        }

        private static IEnumerable<IntPtr> EnumerateTopWindows(int limit)
        {
            var hwnd = GetTopWindow(IntPtr.Zero);
            var scanned = 0;
            var yielded = 0;
            while (hwnd != IntPtr.Zero && scanned < 200 && yielded < limit)
            {
                if (IsWindowVisible(hwnd))
                {
                    yielded++;
                    yield return hwnd;
                }

                hwnd = GetWindow(hwnd, GW_HWNDNEXT);
                scanned++;
            }
        }

        private static bool IsShellForegroundWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || !HasVisibleBounds(hwnd))
            {
                return false;
            }

            if (ShellClassNames.Contains(GetClassName(hwnd)))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            return ShellProcessNames.Contains(GetProcessName(processId));
        }

        private static bool IsShellClassWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero &&
                   IsWindowVisible(hwnd) &&
                   HasVisibleBounds(hwnd) &&
                   ShellClassNames.Contains(GetClassName(hwnd));
        }

        private static void SetShellUiActive(bool active)
        {
            if (_shellUiActive == active)
            {
                return;
            }

            _shellUiActive = active;
            ShellUiActiveChanged?.Invoke(active);
        }

        private static string GetClassName(IntPtr hwnd)
        {
            var builder = new StringBuilder(256);
            var length = GetClassName(hwnd, builder, builder.Capacity);
            return length > 0 ? builder.ToString() : string.Empty;
        }

        private static string GetProcessName(uint processId)
        {
            if (processId == 0)
            {
                return string.Empty;
            }

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    return process.ProcessName;
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        private static bool HasVisibleBounds(IntPtr hwnd)
        {
            RectStruct rect;
            return GetWindowRect(hwnd, out rect) &&
                   rect.Right > rect.Left &&
                   rect.Bottom > rect.Top;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, int uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RectStruct lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct RectStruct
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
