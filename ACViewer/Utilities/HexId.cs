using System;
using System.Globalization;

namespace ACViewer.Utilities
{
    public static class HexId
    {
        public static string Format(uint value) => $"0x{value:X8}";

        public static bool TryParse(string text, out uint value)
        {
            value = 0;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
                return false;

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];

            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                   || uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static uint Parse(string text)
        {
            if (TryParse(text, out var value))
                return value;

            throw new FormatException($"Invalid data id '{text}'. Use hex like 0x0500000C or decimal.");
        }
    }
}
