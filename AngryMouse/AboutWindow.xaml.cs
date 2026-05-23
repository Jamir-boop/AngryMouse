using System;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace AngryMouse
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var assembly = Assembly.GetExecutingAssembly();

            AppName.Content = assembly.GetName().Name;
            AppVersion.Content = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version.ToString(3);
            AppCopyright.Content = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;
            var dpi = VisualTreeHelper.GetDpi(this);
            DpiScaleTextBlock.Text = dpi.DpiScaleX.ToString("0.##", CultureInfo.InvariantCulture) + "x";
            ScreenCountTextBlock.Text = Forms.Screen.AllScreens.Length.ToString(CultureInfo.InvariantCulture);
            CursorSourceModeTextBlock.Text = Properties.Settings.Default.CursorSourceMode;
            CollectionNameTextBlock.Text = Properties.Settings.Default.CursorCollectionName;

            var hBitmap = Properties.Resources.IconPng.GetHbitmap();
            try {
                ImageSource imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                Logo.Source = imageSource;
            } finally {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private void Github_OnClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/Jamir-boop/AngryMouse");
        }
    }
}
