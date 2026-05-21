using System;
using System.Collections.Generic;
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
        private static Dictionary<IntPtr, string> _hiddenCursorRolesByHandle = new Dictionary<IntPtr, string>();
        private static Dictionary<string, CursorVisualInfo> _originalVisualsByRoleKey =
            new Dictionary<string, CursorVisualInfo>(StringComparer.OrdinalIgnoreCase);

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

                ClearHiddenCursorState();

                var changedAny = false;
                var hiddenCursorRolesByHandle = new Dictionary<IntPtr, string>();
                var originalVisualsByRoleKey = SnapshotOriginalCursorVisuals();
                foreach (var role in CursorCollectionManager.Roles)
                {
                    var cursor = CreateTransparentCursor();
                    if (cursor == IntPtr.Zero)
                    {
                        if (!RestoreSystemCursors() && changedAny)
                        {
                            _isHidden = true;
                            _hiddenCursorRolesByHandle = hiddenCursorRolesByHandle;
                            _originalVisualsByRoleKey = originalVisualsByRoleKey;
                        }
                        else
                        {
                            ClearHiddenCursorState();
                        }

                        return false;
                    }

                    if (SetSystemCursor(cursor, (uint)role.WindowsCursorId))
                    {
                        changedAny = true;
                        hiddenCursorRolesByHandle[cursor] = role.Key;
                        continue;
                    }

                    DestroyCursor(cursor);
                    if (!RestoreSystemCursors() && changedAny)
                    {
                        _isHidden = true;
                        _hiddenCursorRolesByHandle = hiddenCursorRolesByHandle;
                        _originalVisualsByRoleKey = originalVisualsByRoleKey;
                    }
                    else
                    {
                        ClearHiddenCursorState();
                    }

                    return false;
                }

                _hiddenCursorRolesByHandle = hiddenCursorRolesByHandle;
                _originalVisualsByRoleKey = originalVisualsByRoleKey;
                _isHidden = true;
                return true;
            }
        }

        public static bool Restore()
        {
            lock (SyncRoot)
            {
                var restored = RestoreSystemCursors();
                if (restored)
                {
                    ClearHiddenCursorState();
                }

                return restored;
            }
        }

        public static bool TryGetHiddenCursorRoleKey(IntPtr cursorHandle, out string roleKey)
        {
            roleKey = null;
            if (cursorHandle == IntPtr.Zero)
            {
                return false;
            }

            lock (SyncRoot)
            {
                return _hiddenCursorRolesByHandle.TryGetValue(cursorHandle, out roleKey);
            }
        }

        public static bool TryGetOriginalCursorVisual(IntPtr hiddenCursorHandle, out CursorVisualInfo visual)
        {
            visual = null;
            if (hiddenCursorHandle == IntPtr.Zero)
            {
                return false;
            }

            lock (SyncRoot)
            {
                string roleKey;
                return _hiddenCursorRolesByHandle.TryGetValue(hiddenCursorHandle, out roleKey) &&
                       _originalVisualsByRoleKey.TryGetValue(roleKey, out visual) &&
                       visual != null;
            }
        }

        private static Dictionary<string, CursorVisualInfo> SnapshotOriginalCursorVisuals()
        {
            var visualsByRoleKey = new Dictionary<string, CursorVisualInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in CursorCollectionManager.Roles)
            {
                visualsByRoleKey[role.Key] = CursorVisualLoader.LoadSystemCursorRole(role.WindowsCursorId);
            }

            return visualsByRoleKey;
        }

        private static void ClearHiddenCursorState()
        {
            _hiddenCursorRolesByHandle = new Dictionary<IntPtr, string>();
            _originalVisualsByRoleKey = new Dictionary<string, CursorVisualInfo>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool RestoreSystemCursors()
        {
            var restored = SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0);
            if (restored)
            {
                _isHidden = false;
            }

            return restored;
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
