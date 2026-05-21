using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AngryMouse
{
    public static class AppIcon
    {
        private static readonly Lazy<ImageSource> LazyImageSource = new Lazy<ImageSource>(CreateImageSource);

        public static ImageSource ImageSource => LazyImageSource.Value;

        private static ImageSource CreateImageSource()
        {
            using (var icon = (System.Drawing.Icon)Properties.Resources.icon.Clone())
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
        }
    }
}
