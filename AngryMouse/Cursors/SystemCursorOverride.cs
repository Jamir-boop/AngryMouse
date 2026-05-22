using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AngryMouse.Cursors
{
    internal static class SystemCursorOverride
    {
        private const int MaxCursorHeight = 256;
        private const int MinCursorHeight = 16;
        private const uint BiRgb = 0;
        private const uint DibRgbColors = 0;

        private static readonly object SyncRoot = new object();
        private static bool _isActive;

        public static bool IsActive
        {
            get
            {
                lock (SyncRoot)
                {
                    return _isActive;
                }
            }
        }

        public static bool ApplyScaledCursors()
        {
            lock (SyncRoot)
            {
                if (_isActive)
                {
                    return true;
                }

                SystemCursorHider.Restore();

                foreach (var role in CursorCollectionManager.Roles)
                {
                    var cursor = CreateScaledCursor(role);
                    if (cursor == IntPtr.Zero)
                    {
                        Restore();
                        return false;
                    }

                    if (SetSystemCursor(cursor, (uint)role.WindowsCursorId))
                    {
                        continue;
                    }

                    DestroyIcon(cursor);
                    Restore();
                    return false;
                }

                _isActive = true;
                return true;
            }
        }

        public static bool Restore()
        {
            lock (SyncRoot)
            {
                var restored = SystemCursorHider.Restore();
                if (restored)
                {
                    _isActive = false;
                }

                return restored;
            }
        }

        private static IntPtr CreateScaledCursor(CursorRoleDefinition role)
        {
            var visual = LoadRoleVisual(role);
            if (visual == null || !visual.HasBitmap)
            {
                return IntPtr.Zero;
            }

            var scaled = ScaleCursorVisual(visual);
            if (scaled == null || scaled.Bitmap == null)
            {
                return IntPtr.Zero;
            }

            return CreateCursorHandle(scaled.Bitmap, scaled.Hotspot);
        }

        private static CursorVisualInfo LoadRoleVisual(CursorRoleDefinition role)
        {
            CursorVisualInfo visual;
            if (string.Equals(
                    Properties.Settings.Default.CursorSourceMode,
                    CursorCollectionManager.CollectionMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                visual = CursorVisualCache.GetRuntimeVisual(role.Key);
                if (visual != null && visual.HasBitmap)
                {
                    return visual;
                }
            }

            visual = CursorVisualLoader.LoadSystemCursorRole(role.WindowsCursorId);
            return visual != null && visual.HasBitmap ? visual : null;
        }

        private static ScaledCursorVisual ScaleCursorVisual(CursorVisualInfo visual)
        {
            var source = visual.Bitmap;
            if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
            {
                return null;
            }

            var targetHeight = GetTargetCursorHeight();
            var scale = targetHeight / (double)source.PixelHeight;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();

            var hotspot = new Point(
                Clamp((int)Math.Round(visual.Hotspot.X * scale), 0, Math.Max(0, scaled.PixelWidth - 1)),
                Clamp((int)Math.Round(visual.Hotspot.Y * scale), 0, Math.Max(0, scaled.PixelHeight - 1)));

            return new ScaledCursorVisual(scaled, hotspot);
        }

        private static int GetTargetCursorHeight()
        {
            var requested = (int)Math.Round(
                CursorVisualLoader.BuiltInCursorHeight *
                (Properties.Settings.Default.CursorSize / 10.0));
            return Clamp(requested, MinCursorHeight, MaxCursorHeight);
        }

        private static IntPtr CreateCursorHandle(BitmapSource source, Point hotspot)
        {
            var bitmap = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            IntPtr colorBitmap = IntPtr.Zero;
            IntPtr maskBitmap = IntPtr.Zero;
            try
            {
                colorBitmap = CreateColorBitmap(width, height, pixels);
                if (colorBitmap == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                maskBitmap = CreateMaskBitmap(width, height, pixels);
                if (maskBitmap == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                var iconInfo = new IconInfo
                {
                    fIcon = false,
                    xHotspot = (int)hotspot.X,
                    yHotspot = (int)hotspot.Y,
                    hbmMask = maskBitmap,
                    hbmColor = colorBitmap
                };

                return CreateIconIndirect(ref iconInfo);
            }
            finally
            {
                if (colorBitmap != IntPtr.Zero)
                {
                    DeleteObject(colorBitmap);
                }

                if (maskBitmap != IntPtr.Zero)
                {
                    DeleteObject(maskBitmap);
                }
            }
        }

        private static IntPtr CreateColorBitmap(int width, int height, byte[] pixels)
        {
            var info = new BitmapInfo
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BiRgb,
                    biSizeImage = (uint)pixels.Length
                }
            };

            IntPtr bits;
            var bitmap = CreateDIBSection(IntPtr.Zero, ref info, DibRgbColors, out bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            Marshal.Copy(pixels, 0, bits, pixels.Length);
            return bitmap;
        }

        private static IntPtr CreateMaskBitmap(int width, int height, byte[] pixels)
        {
            var stride = ((width + 15) / 16) * 2;
            var mask = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                var sourceRow = y * width * 4;
                var maskRow = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var alpha = pixels[sourceRow + x * 4 + 3];
                    if (alpha >= 16)
                    {
                        continue;
                    }

                    mask[maskRow + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            return CreateBitmap(width, height, 1, 1, mask);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconIndirect(ref IconInfo piconinfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc,
            ref BitmapInfo pbmi,
            uint usage,
            out IntPtr ppvBits,
            IntPtr hSection,
            uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateBitmap(
            int nWidth,
            int nHeight,
            uint cPlanes,
            uint cBitsPerPel,
            byte[] lpvBits);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private sealed class ScaledCursorVisual
        {
            public ScaledCursorVisual(BitmapSource bitmap, Point hotspot)
            {
                Bitmap = bitmap;
                Hotspot = hotspot;
            }

            public BitmapSource Bitmap { get; }

            public Point Hotspot { get; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }
    }
}
