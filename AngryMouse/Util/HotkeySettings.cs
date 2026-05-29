using System;
using System.Collections.Generic;
using System.Globalization;

namespace AngryMouse.Util
{
    internal static class HotkeySettings
    {
        public const string HoldMode = "Hold";
        public const string ToggleMode = "Toggle";
        public const string DefaultModifiers = "Control";
        public const string DefaultKey = "None";

        public static string NormalizeActivationMode(string value)
        {
            return string.Equals(value, ToggleMode, StringComparison.OrdinalIgnoreCase) ? ToggleMode : HoldMode;
        }

        public static string NormalizeModifiers(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "none":
                    return "None";
                case "alt":
                    return "Alt";
                case "shift":
                    return "Shift";
                case "control+shift":
                case "ctrl+shift":
                    return "Control+Shift";
                case "control+alt":
                case "ctrl+alt":
                    return "Control+Alt";
                case "alt+shift":
                    return "Alt+Shift";
                case "control+alt+shift":
                case "ctrl+alt+shift":
                    return "Control+Alt+Shift";
                default:
                    return DefaultModifiers;
            }
        }

        public static string NormalizeKey(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultKey;
            }

            if (normalized.Length == 1)
            {
                var c = char.ToUpperInvariant(normalized[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    return c.ToString();
                }

                if (c >= '0' && c <= '9')
                {
                    return "D" + c;
                }
            }

            for (var i = 0; i <= 9; i++)
            {
                if (string.Equals(normalized, "D" + i.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    return "D" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            for (var i = 1; i <= 12; i++)
            {
                if (string.Equals(normalized, "F" + i.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
                {
                    return "F" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (string.Equals(normalized, "Space", StringComparison.OrdinalIgnoreCase))
            {
                return "Space";
            }

            return string.Equals(normalized, "Escape", StringComparison.OrdinalIgnoreCase) ? "Escape" : DefaultKey;
        }

        public static bool IsEmpty(string modifiers, string key)
        {
            return string.Equals(NormalizeModifiers(modifiers), "None", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(NormalizeKey(key), DefaultKey, StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatDisplay(string modifiers, string key)
        {
            var parts = new List<string>();
            var normalizedModifiers = NormalizeModifiers(modifiers);
            if (!string.Equals(normalizedModifiers, "None", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var modifier in normalizedModifiers.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    parts.Add(string.Equals(modifier, DefaultModifiers, StringComparison.OrdinalIgnoreCase) ? "Ctrl" : modifier);
                }
            }

            var normalizedKey = NormalizeKey(key);
            if (!string.Equals(normalizedKey, DefaultKey, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(FormatKeyDisplay(normalizedKey));
            }

            return parts.Count == 0 ? "Ctrl" : string.Join("+", parts);
        }

        private static string FormatKeyDisplay(string key)
        {
            if (key != null && key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]))
            {
                return key[1].ToString();
            }

            return key ?? string.Empty;
        }
    }
}
