using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

            ImageSource imageSource = Imaging.CreateBitmapSourceFromHBitmap(
                Properties.Resources.IconPng.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            Logo.Source = imageSource;
        }

        private void Github_OnClick(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/Jamir-boop/AngryMouse");
        }
    }
}
