using System.Windows.Media.Imaging;

namespace AngryMouse.Cursors
{
    internal sealed class CursorCachedBitmap
    {
        public CursorCachedBitmap(
            BitmapSource bitmap,
            int cropLeft,
            int cropTop,
            int uncroppedWidth,
            int uncroppedHeight)
        {
            Bitmap = bitmap;
            CropLeft = cropLeft;
            CropTop = cropTop;
            UncroppedWidth = uncroppedWidth;
            UncroppedHeight = uncroppedHeight;
        }

        public BitmapSource Bitmap { get; }

        public int CropLeft { get; }

        public int CropTop { get; }

        public int UncroppedWidth { get; }

        public int UncroppedHeight { get; }
    }
}
