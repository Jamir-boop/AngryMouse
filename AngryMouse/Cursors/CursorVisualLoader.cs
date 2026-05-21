using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

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

        private static readonly Dictionary<IntPtr, string> CursorRolesByHandle = CreateCursorRolesByHandle();

        public static CursorVisualInfo BuiltIn(string status = "Using built-in cursor.")
        {
            return new CursorVisualInfo(null, new Point(0, 0), status);
        }

        public static CursorVisualInfo LoadFromSettings()
        {
            return LoadFromSettings(GetCurrentWindowsCursorRoleKey());
        }

        public static CursorVisualInfo LoadFromSettings(string roleKey)
        {
            var mode = Properties.Settings.Default.CursorSourceMode;

            if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            {
                return LoadSystemCursor();
            }

            return LoadCollectionRole(roleKey);
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

        public static string GetCurrentWindowsCursorRoleKey()
        {
            string roleKey;
            return TryGetCurrentWindowsCursorRoleKey(out roleKey) ? roleKey : "arrow";
        }

        public static bool TryGetCurrentWindowsCursorRoleKey(out string roleKey)
        {
            roleKey = null;

            var cursorHandle = GetCurrentSystemCursorHandle();
            if (cursorHandle == IntPtr.Zero)
            {
                return false;
            }

            return CursorRolesByHandle.TryGetValue(cursorHandle, out roleKey);
        }

        public static CursorVisualInfo LoadCollectionCursor()
        {
            var roleKey = GetCurrentWindowsCursorRoleKey();
            return LoadCollectionRole(roleKey);
        }

        public static CursorVisualInfo LoadCollectionRole(string roleKey)
        {
            return CursorVisualCache.GetRuntimeVisual(roleKey);
        }

        public static BitmapSource LoadSvgPreview(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                return CursorVisualCache.GetPreview(path);
            }
            catch (Exception)
            {
                return null;
            }
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

        public static BitmapSource LoadSvgBitmap(string path, int targetHeight)
        {
            return RenderSvgBitmap(path, targetHeight, false).Bitmap;
        }

        public static CursorSvgRenderResult RenderSvgBitmap(string path, int targetHeight, bool trimTransparentPadding)
        {
            var settings = new WpfDrawingSettings
            {
                IncludeRuntime = false,
                TextAsGeometry = true,
                EnsureViewboxSize = true,
                EnsureViewboxPosition = false
            };

            DrawingGroup drawing;
            using (var reader = new FileSvgReader(settings))
            {
                drawing = reader.Read(path);
            }

            if (drawing == null)
            {
                throw new InvalidOperationException("SVG drawing is empty.");
            }

            var viewport = ReadSvgViewport(path);
            var bounds = viewport ?? drawing.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("SVG bounds are empty.");
            }

            var scale = targetHeight > 0 ? targetHeight / bounds.Height : 1;
            var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var transform = new TransformGroup();
                if (!viewport.HasValue)
                {
                    transform.Children.Add(new TranslateTransform(-bounds.Left, -bounds.Top));
                }

                transform.Children.Add(new ScaleTransform(scale, scale));

                context.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
                context.PushTransform(transform);
                context.DrawDrawing(drawing);
                context.Pop();
                context.Pop();
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            if (!trimTransparentPadding)
            {
                return new CursorSvgRenderResult(bitmap, 0, 0, width, height);
            }

            return TrimTransparentPadding(bitmap);
        }

        private static CursorSvgRenderResult TrimTransparentPadding(BitmapSource bitmap)
        {
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return new CursorSvgRenderResult(bitmap, 0, 0, bitmap?.PixelWidth ?? 0, bitmap?.PixelHeight ?? 0);
            }

            var source = bitmap.Format == PixelFormats.Pbgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);

            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var alpha = pixels[row + x * 4 + 3];
                    if (alpha == 0)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return new CursorSvgRenderResult(bitmap, 0, 0, width, height);
            }

            if (minX == 0 && minY == 0 && maxX == width - 1 && maxY == height - 1)
            {
                return new CursorSvgRenderResult(bitmap, 0, 0, width, height);
            }

            var crop = new CroppedBitmap(source, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
            crop.Freeze();

            return new CursorSvgRenderResult(crop, minX, minY, width, height);
        }

        internal static Rect? ReadSvgViewport(string path)
        {
            var document = XDocument.Load(path);
            var root = document.Root;
            if (root == null)
            {
                return null;
            }

            var viewBox = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                var parts = viewBox
                    .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToArray();

                double left;
                double top;
                double width;
                double height;
                if (parts.Length == 4 &&
                    TryParseSvgNumber(parts[0], out left) &&
                    TryParseSvgNumber(parts[1], out top) &&
                    TryParseSvgNumber(parts[2], out width) &&
                    TryParseSvgNumber(parts[3], out height) &&
                    width > 0 &&
                    height > 0)
                {
                    return new Rect(left, top, width, height);
                }
            }

            double viewportWidth;
            double viewportHeight;
            if (TryParseSvgLength(root.Attribute("width")?.Value, out viewportWidth) &&
                TryParseSvgLength(root.Attribute("height")?.Value, out viewportHeight) &&
                viewportWidth > 0 &&
                viewportHeight > 0)
            {
                return new Rect(0, 0, viewportWidth, viewportHeight);
            }

            return null;
        }

        private static bool TryParseSvgLength(string value, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            var length = 0;
            while (length < trimmed.Length)
            {
                var ch = trimmed[length];
                if ((ch >= '0' && ch <= '9') || ch == '-' || ch == '+' || ch == '.' || ch == 'e' || ch == 'E')
                {
                    length++;
                    continue;
                }

                break;
            }

            return length > 0 && TryParseSvgNumber(trimmed.Substring(0, length), out result);
        }

        private static bool TryParseSvgNumber(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static Dictionary<IntPtr, string> CreateCursorRolesByHandle()
        {
            var rolesByHandle = new Dictionary<IntPtr, string>();
            foreach (var role in CursorCollectionManager.Roles)
            {
                var handle = LoadCursor(IntPtr.Zero, new IntPtr(role.WindowsCursorId));
                if (handle != IntPtr.Zero && !rolesByHandle.ContainsKey(handle))
                {
                    rolesByHandle.Add(handle, role.Key);
                }
            }

            return rolesByHandle;
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
        private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

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
