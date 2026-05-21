using AngryMouse.Mouse;
using AngryMouse.Screen;
using AngryMouse.Util;
using AngryMouse.Startup;
using AngryMouse.Cursors;
using CommandLine;
using Gma.System.MouseKeyHook;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace AngryMouse
{
    /// <summary>
    /// Main app
    /// </summary>
    public partial class App
    {
        private const string SingleInstanceMutexName = @"Global\JamirBoop.AngryMouse.SingleInstance";
        private const string SingleInstanceOpenSettingsEventName = @"Global\JamirBoop.AngryMouse.OpenSettings";

        private Mutex _singleInstanceMutex;

        private EventWaitHandle _singleInstanceOpenSettingsEvent;

        private Thread _singleInstanceSignalThread;

        private volatile bool _singleInstanceSignalListenerStopping;

        private bool _singleInstanceReady;

        /// <summary>
        /// Debug mode
        /// </summary>
        private bool _debug;

        /// <summary>
        /// Notification icon in the task bar.
        /// </summary>
        private NotifyIcon _notifyIcon;

        /// <summary>
        /// The thing that detects shakes.
        /// </summary>
        private MouseShakeDetector _detector;

        /// <summary>
        /// The list of screens.
        /// </summary>
        private List<ScreenInfo> _screenInfos;

        /// <summary>
        /// The list of overlay windows that draw the big mouse.
        /// </summary>
        private readonly List<OverlayWindow> _overlayWindows = new List<OverlayWindow>();

        private SettingsWindow _settingsWindow;

        private AboutWindow _aboutWindow;

        private string _lastCursorIdentity;

        private int _pendingMouseX;

        private int _pendingMouseY;

        private bool _mouseMoveQueued;

        private readonly object _mouseMoveLock = new object();

        private CancellationTokenSource _prewarmCancellation;

        private bool _detectorShaking;

        private DateTime _lastDetectorShakeTimestamp = DateTime.Now;

        private bool _testPreviewActive;

        private string _testPreviewRoleKey;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (!TryAcquireSingleInstance())
            {
                SignalExistingInstance();
                Shutdown(0);
                return;
            }

            SystemCursorHider.Restore();
            RegisterSystemCursorRestoreHandlers();
            StartSingleInstanceSignalListener();

            base.OnStartup(e);
            AppTheme.Initialize();

            ParserResult<Options> parserResult = Parser.Default.ParseArguments<Options>(e.Args);

            parserResult.WithParsed((options) => { _debug = options.Debug; });

            CursorCollectionManager.InitializeDefaults();

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Icon = AngryMouse.Properties.Resources.icon,
                Text = "AngryMouse"
            };

            CreateContextMenu();

            _detector = new MouseShakeDetector();
            _detector.MouseShake += OnMouseShake;
            _detector.MouseMove += OnMouseMove;
            AngryMouse.Properties.Settings.Default.PropertyChanged += SettingsOnPropertyChanged;
            CursorCollectionManager.CollectionChanged += CursorCollectionManagerOnCollectionChanged;

            _screenInfos = GetScreenInfos();

            if (_debug)
            {
                // Debug window. Only shown when the -d option is used.
                var debugInfoWindow = new DebugInfoWindow(_detector, _screenInfos);
                debugInfoWindow.Show();
            }

            // Create and load windows on the secondary screens.
            foreach (var screen in _screenInfos)
            {
                var window = new OverlayWindow(screen, _debug);
                window.Show();

                _overlayWindows.Add(window);
            }

            ApplyCurrentCursorVisual(force: true);
            StartActiveCollectionPrewarm();
            _singleInstanceReady = true;
            OpenSettingsWindow();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemCursorHider.Restore();
            _singleInstanceReady = false;
            StopSingleInstanceSignalListener();
            AppTheme.Dispose();
            ReleaseSingleInstance();
            base.OnExit(e);
        }

        private void RegisterSystemCursorRestoreHandlers()
        {
            DispatcherUnhandledException += (sender, args) => SystemCursorHider.Restore();
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => SystemCursorHider.Restore();
        }

        private bool TryAcquireSingleInstance()
        {
            bool createdNew;

            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (createdNew)
            {
                return true;
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return false;
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (var signal = EventWaitHandle.OpenExisting(SingleInstanceOpenSettingsEventName))
                {
                    signal.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void StartSingleInstanceSignalListener()
        {
            _singleInstanceOpenSettingsEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                SingleInstanceOpenSettingsEventName);
            _singleInstanceSignalListenerStopping = false;
            _singleInstanceSignalThread = new Thread(WaitForSingleInstanceSignals)
            {
                IsBackground = true,
                Name = "AngryMouseSingleInstanceSignal"
            };
            _singleInstanceSignalThread.Start();
        }

        private void WaitForSingleInstanceSignals()
        {
            while (!_singleInstanceSignalListenerStopping)
            {
                try
                {
                    _singleInstanceOpenSettingsEvent.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (_singleInstanceSignalListenerStopping)
                {
                    return;
                }

                Current.Dispatcher.BeginInvoke(new Action(OpenSettingsWindowFromSingleInstanceSignal));
            }
        }

        private void OpenSettingsWindowFromSingleInstanceSignal()
        {
            if (!_singleInstanceReady)
            {
                return;
            }

            OpenSettingsWindow();
        }

        private void StopSingleInstanceSignalListener()
        {
            _singleInstanceSignalListenerStopping = true;

            if (_singleInstanceOpenSettingsEvent == null)
            {
                return;
            }

            try
            {
                _singleInstanceOpenSettingsEvent.Set();
            }
            catch (ObjectDisposedException)
            {
            }

            _singleInstanceOpenSettingsEvent.Dispose();
            _singleInstanceOpenSettingsEvent = null;
            _singleInstanceSignalThread = null;
        }

        private void ReleaseSingleInstance()
        {
            if (_singleInstanceMutex == null)
            {
                return;
            }

            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        /// <summary>
        /// Create the context menu for the notification icon.
        /// </summary>
        private void CreateContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            menu.Items.Add("Settings").Click += (s, e) => OpenSettingsWindow();
            menu.Items.Add("About").Click += (s, e) =>
            {
                if (_aboutWindow != null)
                {
                    _aboutWindow.Focus();
                }
                else
                {
                    _aboutWindow = new AboutWindow();
                    _aboutWindow.Show();
                    _aboutWindow.Closed += (sender, args) => { _aboutWindow = null; };
                }
            };
            var startUpItem = new ToolStripMenuItem("Run at Windows startup");
            startUpItem.Checked = RunOnStartup.isRunOnStartup();
            startUpItem.Click += (s, e) =>
            {
                startUpItem.Checked = !startUpItem.Checked;
                if (startUpItem.Checked)
                {
                    RunOnStartup.setRunOnStartup(true);
                }
                else
                {
                    RunOnStartup.setRunOnStartup(false);
                }
            };
            menu.Items.Add(startUpItem);
            menu.Items.Add(new ToolStripSeparator());
            
            menu.Items.Add("Exit").Click += (s, e) => ExitApp();

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => Current.Dispatcher.BeginInvoke(new Action(OpenSettingsWindow));
        }

        private void OpenSettingsWindow()
        {
            if (_settingsWindow != null)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Show();
                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }

            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (sender, args) => { _settingsWindow = null; };
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        internal void BeginCursorTestPreview(string roleKey)
        {
            if (!Current.Dispatcher.CheckAccess())
            {
                Current.Dispatcher.BeginInvoke(new Action(() => BeginCursorTestPreview(roleKey)));
                return;
            }

            _testPreviewActive = true;
            _testPreviewRoleKey = string.IsNullOrWhiteSpace(roleKey) ? "arrow" : roleKey;
            _lastCursorIdentity = null;
            if (!UpdateSystemCursorVisibility() && !SystemCursorHider.IsHidden)
            {
                ApplyCurrentCursorVisual(force: true);
            }

            var position = System.Windows.Forms.Cursor.Position;
            _overlayWindows.ForEach(window =>
            {
                window.UpdateMousePosition(position.X, position.Y);
                window.SetMouseShake(true, DateTime.Now);
            });
        }

        internal void EndCursorTestPreview()
        {
            if (!Current.Dispatcher.CheckAccess())
            {
                Current.Dispatcher.BeginInvoke(new Action(EndCursorTestPreview));
                return;
            }

            if (!_testPreviewActive)
            {
                return;
            }

            _testPreviewActive = false;
            _testPreviewRoleKey = null;
            _lastCursorIdentity = null;
            _overlayWindows.ForEach(window => window.SetMouseShake(_detectorShaking, _lastDetectorShakeTimestamp));
            if (!UpdateSystemCursorVisibility() && !SystemCursorHider.IsHidden)
            {
                ApplyCurrentCursorVisual(force: true);
            }
        }

        /// <summary>
        /// Close the windows and remove the notification icon.
        /// </summary>
        private void ExitApp()
        {
            SystemCursorHider.Restore();
            AngryMouse.Properties.Settings.Default.PropertyChanged -= SettingsOnPropertyChanged;
            CursorCollectionManager.CollectionChanged -= CursorCollectionManagerOnCollectionChanged;
            CancelActiveCollectionPrewarm();
            _detector.MouseMove -= OnMouseMove;
            _detector.MouseShake -= OnMouseShake;
            _detector.Dispose();
            Current.Shutdown();
        }

        /// <summary>
        /// Called when mouse shake is detected or when shaking is stopped.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMouseShake(object sender, MouseShakeArgs e)
        {
            if (!Current.Dispatcher.CheckAccess())
            {
                Current.Dispatcher.BeginInvoke(new Action(() => OnMouseShake(sender, e)));
                return;
            }

            _detectorShaking = e.IsShaking;
            _lastDetectorShakeTimestamp = e.Timestamp;
            UpdateSystemCursorVisibility();
            if (_testPreviewActive)
            {
                return;
            }

            _overlayWindows.ForEach(window => window.SetMouseShake(e.IsShaking, e.Timestamp));
        }

        private void OnMouseMove(object sender, MouseEventExtArgs e)
        {
            lock (_mouseMoveLock)
            {
                _pendingMouseX = e.X;
                _pendingMouseY = e.Y;

                if (_mouseMoveQueued)
                {
                    return;
                }

                _mouseMoveQueued = true;
            }

            Current.Dispatcher.BeginInvoke(new Action(ProcessPendingMouseMove));
        }

        private void ProcessPendingMouseMove()
        {
            int x;
            int y;

            lock (_mouseMoveLock)
            {
                x = _pendingMouseX;
                y = _pendingMouseY;
                _mouseMoveQueued = false;
            }

            ApplyCurrentCursorVisual(force: false);
            _overlayWindows.ForEach(window => window.UpdateMousePosition(x, y));
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CursorSourceMode" ||
                e.PropertyName == "CursorCollectionName")
            {
                _lastCursorIdentity = null;
                if (!SystemCursorHider.IsHidden)
                {
                    ApplyCurrentCursorVisual(force: true);
                }

                StartActiveCollectionPrewarm();
            }

            if (e.PropertyName == "HideBuiltInCursor")
            {
                UpdateSystemCursorVisibility();
            }
        }

        private void CursorCollectionManagerOnCollectionChanged(object sender, EventArgs e)
        {
            _lastCursorIdentity = null;
            if (!SystemCursorHider.IsHidden)
            {
                ApplyCurrentCursorVisual(force: true);
            }

            StartActiveCollectionPrewarm();
        }

        private bool UpdateSystemCursorVisibility()
        {
            if (!Current.Dispatcher.CheckAccess())
            {
                Current.Dispatcher.BeginInvoke(new Action(() => UpdateSystemCursorVisibility()));
                return false;
            }

            var shouldHide = AngryMouse.Properties.Settings.Default.HideBuiltInCursor &&
                             (_detectorShaking || _testPreviewActive);
            if (shouldHide)
            {
                if (SystemCursorHider.IsHidden)
                {
                    return false;
                }

                _lastCursorIdentity = null;
                ApplyCurrentCursorVisual(force: true);
                SystemCursorHider.Hide();
                return true;
            }

            if (!SystemCursorHider.IsHidden)
            {
                return false;
            }

            if (!SystemCursorHider.Restore())
            {
                return false;
            }

            _lastCursorIdentity = null;
            ApplyCurrentCursorVisual(force: true);
            return true;
        }

        private void ApplyCurrentCursorVisual(bool force)
        {
            if (!Current.Dispatcher.CheckAccess())
            {
                Current.Dispatcher.BeginInvoke(new Action(() => ApplyCurrentCursorVisual(force)));
                return;
            }

            var mode = AngryMouse.Properties.Settings.Default.CursorSourceMode;
            if (_testPreviewActive)
            {
                var previewRoleKey = string.IsNullOrWhiteSpace(_testPreviewRoleKey) ? "arrow" : _testPreviewRoleKey;
                var previewSystemCursorHandle = CursorVisualLoader.GetCurrentSystemCursorHandle();
                var previewIdentity = string.Equals(mode, CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase)
                    ? "test-system|" + previewSystemCursorHandle
                    : "test-collection|" + AngryMouse.Properties.Settings.Default.CursorCollectionName + "|" + previewRoleKey;

                if (!force && string.Equals(previewIdentity, _lastCursorIdentity, StringComparison.Ordinal))
                {
                    return;
                }

                _lastCursorIdentity = previewIdentity;
                var previewCursorVisual = string.Equals(mode, CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase)
                    ? CursorVisualLoader.LoadSystemCursor()
                    : CursorVisualLoader.LoadCollectionRole(previewRoleKey);
                _overlayWindows.ForEach(window => window.SetCursorVisual(previewCursorVisual));
                return;
            }

            string roleKey;
            var hasKnownRole = CursorVisualLoader.TryGetCurrentWindowsCursorRoleKey(out roleKey);
            var systemCursorHandle = CursorVisualLoader.GetCurrentSystemCursorHandle();
            var identity = string.Equals(mode, CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase)
                ? "system|" + systemCursorHandle
                : hasKnownRole
                    ? "collection|" + AngryMouse.Properties.Settings.Default.CursorCollectionName + "|" + roleKey
                    : "collection-system|" + systemCursorHandle;

            if (!force && string.Equals(identity, _lastCursorIdentity, StringComparison.Ordinal))
            {
                return;
            }

            _lastCursorIdentity = identity;
            var cursorVisual = !string.Equals(mode, CursorCollectionManager.SystemMode, StringComparison.OrdinalIgnoreCase) && !hasKnownRole
                ? CursorVisualLoader.LoadSystemCursor()
                : CursorVisualLoader.LoadFromSettings(roleKey);
            _overlayWindows.ForEach(window => window.SetCursorVisual(cursorVisual));
        }

        private void StartActiveCollectionPrewarm()
        {
            CancelActiveCollectionPrewarm();

            if (!string.Equals(
                    AngryMouse.Properties.Settings.Default.CursorSourceMode,
                    CursorCollectionManager.CollectionMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var source = new CancellationTokenSource();
            _prewarmCancellation = source;
            var collectionName = AngryMouse.Properties.Settings.Default.CursorCollectionName;

            CursorRenderPrewarmer.PrewarmCollectionAsync(collectionName, null, source.Token)
                .ContinueWith(task =>
                {
                    var isCurrent = ReferenceEquals(_prewarmCancellation, source);
                    source.Dispose();

                    if (task.IsFaulted)
                    {
                        var ignored = task.Exception;
                    }

                    if (isCurrent)
                    {
                        _prewarmCancellation = null;
                    }
                }, TaskScheduler.Default);
        }

        private void CancelActiveCollectionPrewarm()
        {
            var source = _prewarmCancellation;
            if (source == null)
            {
                return;
            }

            _prewarmCancellation = null;
            source.Cancel();
        }

        private List<ScreenInfo> GetScreenInfos()
        {
            List<ScreenInfo> screenInfos = new List<ScreenInfo>();

            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                screenInfos.Add(new ScreenInfo()
                {
                    Name = screen.DeviceName,
                    BoundX = screen.Bounds.X,
                    BoundY = screen.Bounds.Y,
                    BoundWidth = screen.Bounds.Width,
                    BoundHeight = screen.Bounds.Height,
                    WorkX = screen.WorkingArea.X,
                    WorkY = screen.WorkingArea.Y,
                    WorkWidth = screen.WorkingArea.Width,
                    WorkHeight = screen.WorkingArea.Height,
                    Primary = screen.Primary,
                    Bpp = screen.BitsPerPixel
                });
            }

            return screenInfos;
        }
    }

    internal static class AppTheme
    {
        public const string DarkMode = "Dark";
        public const string LightMode = "Light";

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            ApplySavedTheme();
            AngryMouse.Properties.Settings.Default.PropertyChanged += SettingsOnPropertyChanged;
        }

        public static void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            AngryMouse.Properties.Settings.Default.PropertyChanged -= SettingsOnPropertyChanged;
            _initialized = false;
        }

        public static bool IsDarkMode(string mode)
        {
            return !string.Equals(mode, LightMode, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeThemeMode(string mode)
        {
            return IsDarkMode(mode) ? DarkMode : LightMode;
        }

        public static void ApplySavedTheme()
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                return;
            }

            if (!app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(ApplySavedTheme));
                return;
            }

            ApplyTheme(AngryMouse.Properties.Settings.Default.ThemeMode);
        }

        private static void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ThemeMode")
            {
                ApplySavedTheme();
            }
        }

        private static void ApplyTheme(string mode)
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                return;
            }

            var light = !IsDarkMode(mode);
            var resources = app.Resources;

            SetBrush(resources, "Theme.WindowBackgroundBrush", light ? "#FFF7F7F7" : "#FF1E1F23");
            SetBrush(resources, "Theme.ControlBackgroundBrush", light ? "#FFFFFFFF" : "#FF25272D");
            SetBrush(resources, "Theme.ControlBackgroundHoverBrush", light ? "#FFF3F4F6" : "#FF30333B");
            SetBrush(resources, "Theme.ControlForegroundBrush", light ? "#FF111827" : "#FFE5E7EB");
            SetBrush(resources, "Theme.SecondaryForegroundBrush", light ? "#FF4B5563" : "#FFB4BBC7");
            SetBrush(resources, "Theme.BorderBrush", light ? "#FFB9C0C9" : "#FF4B5563");
            SetBrush(resources, "Theme.InputBackgroundBrush", light ? "#FFFFFFFF" : "#FF15171B");
            SetBrush(resources, "Theme.InputForegroundBrush", light ? "#FF111827" : "#FFE5E7EB");
            SetBrush(resources, "Theme.ButtonBackgroundBrush", light ? "#FFFFFFFF" : "#FF2D3038");
            SetBrush(resources, "Theme.ButtonHoverBrush", light ? "#FFE8EEF8" : "#FF3A3F4A");
            SetBrush(resources, "Theme.ButtonPressedBrush", light ? "#FFD7E3F5" : "#FF474E5C");
            SetBrush(resources, "Theme.DisabledForegroundBrush", light ? "#FF8B95A3" : "#FF737A86");
            SetBrush(resources, "Theme.SelectionBrush", light ? "#FF2563EB" : "#FF60A5FA");
            SetBrush(resources, "Theme.SelectionForegroundBrush", "#FFFFFFFF");
            SetBrush(resources, "Theme.GridHeaderBrush", light ? "#FFF0F2F5" : "#FF2B2E36");
            SetBrush(resources, "Theme.GridAltBrush", light ? "#FFF8FAFC" : "#FF202228");
            SetBrush(resources, "Theme.AccentBlueBrush", light ? "#FF2563EB" : "#FF60A5FA");
            SetBrush(resources, "Theme.AccentRedBrush", light ? "#FFDC2626" : "#FFF87171");
            SetBrush(resources, "Theme.PreviewCheckerBaseBrush", light ? "#FFE5E7EB" : "#FF181A1F");
            SetBrush(resources, "Theme.PreviewCheckerAltBrush", light ? "#FFFFFFFF" : "#FF252830");

            SetBrush(resources, SystemColors.WindowBrushKey, light ? "#FFFFFFFF" : "#FF15171B");
            SetBrush(resources, SystemColors.WindowTextBrushKey, light ? "#FF111827" : "#FFE5E7EB");
            SetBrush(resources, SystemColors.ControlBrushKey, light ? "#FFF7F7F7" : "#FF25272D");
            SetBrush(resources, SystemColors.ControlTextBrushKey, light ? "#FF111827" : "#FFE5E7EB");
            SetBrush(resources, SystemColors.HighlightBrushKey, light ? "#FF2563EB" : "#FF60A5FA");
            SetBrush(resources, SystemColors.HighlightTextBrushKey, "#FFFFFFFF");
            SetBrush(resources, SystemColors.GrayTextBrushKey, light ? "#FF8B95A3" : "#FF737A86");
            SetBrush(resources, SystemColors.MenuBrushKey, light ? "#FFFFFFFF" : "#FF15171B");
            SetBrush(resources, SystemColors.MenuTextBrushKey, light ? "#FF111827" : "#FFE5E7EB");
            SetBrush(resources, SystemColors.InfoBrushKey, light ? "#FFFFFFE1" : "#FF25272D");
            SetBrush(resources, SystemColors.InfoTextBrushKey, light ? "#FF111827" : "#FFE5E7EB");
        }

        private static void SetBrush(ResourceDictionary resources, object key, string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
