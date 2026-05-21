using System;
using System.Runtime.InteropServices;

namespace AngryMouse.Cursors
{
    internal static class SystemCursorHider
    {
        private const int TransparentCursorWidth = 32;
        private const int TransparentCursorHeight = 32;
        private const int TransparentMaskBytes = TransparentCursorWidth * TransparentCursorHeight / 8;
        private const uint SpiSetCursors = 0x0057;

        private static readonly byte[] TransparentAndPlane = CreateTransparentAndPlane();
        private static readonly byte[] TransparentXorPlane = new byte[TransparentMaskBytes];
        private static readonly object SyncRoot = new object();

        private static bool _isHidden;

        public static bool IsHidden
        {
            get
            {
                lock (SyncRoot)
                {
                    return _isHidden;
                }
            }
        }

        public static bool Hide()
        {
            lock (SyncRoot)
            {
                if (_isHidden)
                {
                    return true;
                }

                var changedAny = false;
                foreach (var role in CursorCollectionManager.Roles)
                {
                    var cursor = CreateTransparentCursor();
                    if (cursor == IntPtr.Zero)
                    {
                        if (!Restore() && changedAny)
                        {
                            _isHidden = true;
                        }

                        return false;
                    }

                    if (SetSystemCursor(cursor, (uint)role.WindowsCursorId))
                    {
                        changedAny = true;
                        continue;
                    }

                    DestroyCursor(cursor);
                    if (!Restore() && changedAny)
                    {
                        _isHidden = true;
                    }

                    return false;
                }

                _isHidden = true;
                return true;
            }
        }

        public static bool Restore()
        {
            lock (SyncRoot)
            {
                var restored = SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0);
                if (restored)
                {
                    _isHidden = false;
                }

                return restored;
            }
        }

        private static IntPtr CreateTransparentCursor()
        {
            return CreateCursor(
                IntPtr.Zero,
                0,
                0,
                TransparentCursorWidth,
                TransparentCursorHeight,
                TransparentAndPlane,
                TransparentXorPlane);
        }

        private static byte[] CreateTransparentAndPlane()
        {
            var mask = new byte[TransparentMaskBytes];
            for (var index = 0; index < mask.Length; index++)
            {
                mask[index] = 0xFF;
            }

            return mask;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateCursor(
            IntPtr hInst,
            int xHotSpot,
            int yHotSpot,
            int nWidth,
            int nHeight,
            byte[] pvANDPlane,
            byte[] pvXORPlane);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyCursor(IntPtr hCursor);
    }
}
