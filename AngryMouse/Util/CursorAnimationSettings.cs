using System;

namespace AngryMouse.Util
{
    internal static class CursorAnimationSettings
    {
        public const int MinimumAnimationLength = 50;

        public static int NormalizeLength(int value)
        {
            return Math.Max(MinimumAnimationLength, value);
        }

        public static int GetEffectiveLength()
        {
            return NormalizeLength(Properties.Settings.Default.CursorAnimationLength);
        }
    }
}
