using System;
using AngryMouse.Mouse;
using AngryMouse.Screen;
using Gma.System.MouseKeyHook;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace AngryMouse
{
    /// <summary>
    /// Interaction logic for DebugInfoWindow.xaml
    /// </summary>
    public partial class DebugInfoWindow
    {
        /// <summary>
        /// Globa hook to show debug information
        /// </summary>
        private readonly IKeyboardMouseEvents _mouseEvents;

        private readonly MouseShakeDetector _detector;
        private int _pendingMouseX;
        private int _pendingMouseY;
        private bool _mouseMoveQueued;
        private bool _closed;

        /// <summary>
        /// List of screens.
        /// </summary>
        private ObservableCollection<ScreenInfo> ScreenInfos { get; }

        /// <summary>
        /// Main constructor.
        /// </summary>
        /// <param name="detector">detector to show info from</param>
        /// <param name="screenInfos">screen infos that will be shown in a table</param>
        public DebugInfoWindow(MouseShakeDetector detector, List<ScreenInfo> screenInfos)
        {
            InitializeComponent();

            _detector = detector;
            _detector.MouseShake += OnMouseShake;

            _mouseEvents = StaticHook.GlobalEvents();
            _mouseEvents.MouseMoveExt += OnMouseMove;

            ScreenInfos = new ObservableCollection<ScreenInfo>();

            ScreensTable.ItemsSource = ScreenInfos;

            UpdateScreens(screenInfos);
        }

        internal void UpdateScreens(IEnumerable<ScreenInfo> screenInfos)
        {
            ScreenInfos.Clear();
            foreach (var screenInfo in screenInfos)
            {
                ScreenInfos.Add(screenInfo);
            }

            VirtualScreen.Content = SystemParameters.VirtualScreenWidth + "x" + SystemParameters.VirtualScreenHeight;
            VirtualScreenTopLeft.Content = SystemParameters.VirtualScreenLeft + "x" + SystemParameters.VirtualScreenTop;
        }

        private void OnMouseShake(object sender, MouseShakeArgs e)
        {
            IsShaking.Content = e.IsShaking;
        }

        private void OnMouseMove(object sender, MouseEventExtArgs e)
        {
            if (_closed)
            {
                return;
            }

            _pendingMouseX = e.X;
            _pendingMouseY = e.Y;
            if (_mouseMoveQueued)
            {
                return;
            }

            _mouseMoveQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _mouseMoveQueued = false;
                if (!_closed)
                {
                    Coordinates.Content = _pendingMouseX + "," + _pendingMouseY;
                }
            }));
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _closed = true;
            _detector.MouseShake -= OnMouseShake;
            _mouseEvents.MouseMoveExt -= OnMouseMove;
        }
    }
}
