using AngryMouse.Util;
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

namespace AngryMouse.Cursors
{
    class CursorVisualLoader
    {
        public const double BuiltInCursorWidth = 164;
        public const double BuiltInCursorHeight = 254;

        private const int CursorShowing = 0x00000001;

        private static readonly object CursorRolesLock = new object();
        private static Dictionary<IntPtr, string> _cursorRolesByHandle = CreateCursorRolesByHandle();

        public static CursorVisualInfo BuiltIn(string status = "Using built-in cursor.")
        {
            return new CursorVisualInfo(null, new Point(0, 0), status);
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

            CursorVisualInfo originalVisual;
            if (SystemCursorHider.TryGetOriginalCursorVisual(cursorHandle, out originalVisual))
            {
                return originalVisual;
            }

            try
            {
                return LoadCursorHandle(cursorHandle, copyHandle: true, "Using active Windows cursor.");
            }
            catch (Exception ex) when (IsCursorLoadException(ex))
            {
                DebugLog.WriteException("System cursor load failed", ex);
                return BuiltIn("System cursor failed to load. Using built-in cursor.");
            }
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

        public static bool TryGetWindowsCursorRoleKey(IntPtr cursorHandle, out string roleKey)
        {
            roleKey = null;

            if (cursorHandle == IntPtr.Zero)
            {
                return false;
            }

            lock (CursorRolesLock)
            {
                if (_cursorRolesByHandle.TryGetValue(cursorHandle, out roleKey))
                {
                    return true;
                }
            }

            return SystemCursorHider.TryGetHiddenCursorRoleKey(cursorHandle, out roleKey);
        }

        internal static void RefreshSystemCursorRoles()
        {
            lock (CursorRolesLock)
            {
                _cursorRolesByHandle = CreateCursorRolesByHandle();
            }
        }

        public static CursorVisualInfo LoadCollectionRole(string roleKey)
        {
            return CursorVisualCache.GetRuntimeVisual(roleKey);
        }

        public static BitmapSource LoadPngPreview(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                return CursorVisualCache.GetPreview(path);
            }
            catch (Exception ex)
            {
                DebugLog.WriteException("Cursor PNG preview load failed: " + path, ex);
                return null;
            }
        }

        internal static CursorVisualInfo LoadSystemCursorRole(int windowsCursorId)
        {
            var cursorHandle = LoadCursor(IntPtr.Zero, new IntPtr(windowsCursorId));
            if (cursorHandle == IntPtr.Zero)
            {
                return BuiltIn("System cursor unavailable. Using built-in cursor.");
            }

            try
            {
                return LoadCursorHandle(cursorHandle, copyHandle: true, "Using saved Windows cursor.");
            }
            catch (Exception ex) when (IsCursorLoadException(ex))
            {
                DebugLog.WriteException("Saved Windows cursor load failed: " + windowsCursorId, ex);
                return BuiltIn("System cursor failed to load. Using built-in cursor.");
            }
        }

        private static bool IsCursorLoadException(Exception ex)
        {
            return ex is IOException ||
                   ex is UnauthorizedAccessException ||
                   ex is InvalidOperationException ||
                   ex is ArgumentException ||
                   ex is NotSupportedException ||
                   ex is COMException;
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

        public static BitmapSource LoadPngBitmap(string path, int targetHeight)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var frame = decoder.Frames.FirstOrDefault();
                if (frame == null)
                {
                    return null;
                }

                if (targetHeight <= 0)
                {
                    frame.Freeze();
                    return frame;
                }

                var scale = targetHeight / (double)frame.PixelHeight;

                var bitmap = new TransformedBitmap(
                    frame,
                    new ScaleTransform(scale, scale));
                bitmap.Freeze();
                return bitmap;
            }
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
    }
}
