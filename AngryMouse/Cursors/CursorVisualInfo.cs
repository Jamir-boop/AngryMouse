using System.Windows;
using System.Windows.Media.Imaging;

namespace AngryMouse.Cursors
{
    class CursorVisualInfo
    {
        public CursorVisualInfo(BitmapSource bitmap, Point hotspot, string status)
        {
            Bitmap = bitmap;
            Hotspot = hotspot;
            Status = status;
        }

        public BitmapSource Bitmap { get; }

        public Point Hotspot { get; }

        public string Status { get; }

        public double Width => Bitmap?.PixelWidth ?? CursorVisualLoader.BuiltInCursorWidth;

        public double Height => Bitmap?.PixelHeight ?? CursorVisualLoader.BuiltInCursorHeight;

        public bool HasBitmap => Bitmap != null;
    }
}
