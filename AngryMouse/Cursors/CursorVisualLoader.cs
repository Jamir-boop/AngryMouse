using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AngryMouse.Cursors
{
    class CursorVisualLoader
    {
        public const double BuiltInCursorWidth = 164;
        public const double BuiltInCursorHeight = 254;

        private const int CursorShowing = 0x00000001;
        private const uint ImageCursor = 2;
        private const uint LoadFromFile = 0x00000010;
        private const uint CreateDibSection = 0x00002000;

        public static CursorVisualInfo BuiltIn(string status = "Using built-in cursor.")
        {
            return new CursorVisualInfo(null, new Point(0, 0), status);
        }

        public static CursorVisualInfo LoadFromSettings()
        {
            var mode = Properties.Settings.Default.CursorSourceMode;

            if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            {
                return LoadSystemCursor();
            }

            if (string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                return LoadCustomCursor(
                    Properties.Settings.Default.CustomCursorPath,
                    Properties.Settings.Default.CustomCursorHotspotX,
                    Properties.Settings.Default.CustomCursorHotspotY);
            }

            return BuiltIn();
        }

        public static CursorVisualInfo LoadSystemCursor()
        {
            var cursorHandle = GetCurrentSystemCursorHandle();
            if (cursorHandle == IntPtr.Zero)
            {
                return BuiltIn("System cursor unavailable. Using built-in cursor.");
            }

            return LoadCursorHandle(cursorHandle, copyHandle: true, "Using active Windows cursor.");
        }

        public static IntPtr GetCurrentSystemCursorHandle()
        {
            var cursorInfo = new CursorInfo
            {
                cbSize = Marshal.SizeOf(typeof(CursorInfo))
            };

            if (!GetCursorInfo(ref cursorInfo) ||
                cursorInfo.hCursor == IntPtr.Zero ||
                (cursorInfo.flags & CursorShowing) != CursorShowing)
            {
                return IntPtr.Zero;
            }

            return cursorInfo.hCursor;
        }

        public static CursorVisualInfo LoadCustomCursor(string path, int hotspotX, int hotspotY)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BuiltIn("No custom cursor selected. Using built-in cursor.");
            }

            if (!File.Exists(path))
            {
                return BuiltIn("Custom cursor file not found. Using built-in cursor.");
            }

            var extension = Path.GetExtension(path)?.ToLowerInvariant();

            try
            {
                if (extension == ".cur")
                {
                    return LoadCurFile(path);
                }

                if (extension == ".png" || extension == ".ico")
                {
                    return LoadBitmapFile(path, hotspotX, hotspotY);
                }

                return BuiltIn("Unsupported cursor format. Use PNG, ICO, or CUR.");
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is NotSupportedException ||
                ex is InvalidOperationException ||
                ex is ArgumentException ||
                ex is COMException)
            {
                return BuiltIn("Custom cursor failed to load. Using built-in cursor.");
            }
        }

        private static CursorVisualInfo LoadBitmapFile(string path, int hotspotX, int hotspotY)
        {
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var frame = decoder.Frames
                    .OrderByDescending(item => item.PixelWidth * item.PixelHeight)
                    .FirstOrDefault();

                if (frame == null)
                {
                    return BuiltIn("Custom cursor image has no frames. Using built-in cursor.");
                }

                frame.Freeze();

                return new CursorVisualInfo(
                    frame,
                    new Point(Math.Max(0, hotspotX), Math.Max(0, hotspotY)),
                    "Using custom cursor.");
            }
        }

        private static CursorVisualInfo LoadCurFile(string path)
        {
            var size = ReadLargestCursorSize(path);
            var handle = LoadImage(
                IntPtr.Zero,
                path,
                ImageCursor,
                size.Width,
                size.Height,
                LoadFromFile | CreateDibSection);

            if (handle == IntPtr.Zero)
            {
                return BuiltIn("CUR file failed to load. Using built-in cursor.");
            }

            return LoadCursorHandle(handle, copyHandle: false, "Using custom CUR cursor.");
        }

        private static CursorVisualInfo LoadCursorHandle(IntPtr cursorHandle, bool copyHandle, string status)
        {
            var ownedHandle = copyHandle ? CopyIcon(cursorHandle) : cursorHandle;
            if (ownedHandle == IntPtr.Zero)
            {
                return BuiltIn("Cursor handle unavailable. Using built-in cursor.");
            }

            try
            {
                IconInfo iconInfo;
                if (!GetIconInfo(ownedHandle, out iconInfo))
                {
                    return BuiltIn("Cursor metadata unavailable. Using built-in cursor.");
                }

                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        ownedHandle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();

                    return new CursorVisualInfo(
                        bitmap,
                        new Point(iconInfo.xHotspot, iconInfo.yHotspot),
                        status);
                }
                finally
                {
                    DeleteObject(iconInfo.hbmColor);
                    DeleteObject(iconInfo.hbmMask);
                }
            }
            finally
            {
                DestroyIcon(ownedHandle);
            }
        }

        private static CursorSize ReadLargestCursorSize(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 2)
                    {
                        return CursorSize.Default;
                    }

                    var count = reader.ReadUInt16();
                    var best = CursorSize.Default;
                    var bestArea = 0;

                    for (var index = 0; index < count; index++)
                    {
                        var width = DecodeIconDimension(reader.ReadByte());
                        var height = DecodeIconDimension(reader.ReadByte());
                        reader.ReadByte();
                        reader.ReadByte();
                        reader.ReadUInt16();
                        reader.ReadUInt16();
                        reader.ReadUInt32();
                        reader.ReadUInt32();

                        var area = width * height;
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = new CursorSize(width, height);
                        }
                    }

                    return best;
                }
            }
            catch (IOException)
            {
                return CursorSize.Default;
            }
            catch (UnauthorizedAccessException)
            {
                return CursorSize.Default;
            }
        }

        private static int DecodeIconDimension(byte value)
        {
            return value == 0 ? 256 : value;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CursorInfo pci);

        [DllImport("user32.dll")]
        private static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(
            IntPtr hinst,
            string lpszName,
            uint uType,
            int cxDesired,
            int cyDesired,
            uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public PointStruct ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointStruct
        {
            public int X;
            public int Y;
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

        private struct CursorSize
        {
            public static readonly CursorSize Default = new CursorSize(0, 0);

            public CursorSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; }

            public int Height { get; }
        }
    }
}
