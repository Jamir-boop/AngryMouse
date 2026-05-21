using System.Windows;

namespace AngryMouse.Cursors
{
    internal sealed class CursorRoleDefinition
    {
        public CursorRoleDefinition(
            string key,
            string displayName,
            string defaultFileName,
            int windowsCursorId,
            Point hotspot)
        {
            Key = key;
            DisplayName = displayName;
            DefaultFileName = defaultFileName;
            WindowsCursorId = windowsCursorId;
            Hotspot = hotspot;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public string DefaultFileName { get; }

        public int WindowsCursorId { get; }

        public Point Hotspot { get; }
    }
}
