using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Desktop_Frames.Notes
{
    public class NoteColorPalette
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Brush HeaderBrush { get; set; } = Brushes.Transparent;
        public Brush BodyBrush { get; set; } = Brushes.Transparent;
        public Brush BorderBrush { get; set; } = Brushes.Transparent;
        public Brush TextBrush { get; set; } = Brushes.Black;
    }

    public static class NoteColors
    {
        private static readonly Dictionary<string, NoteColorPalette> _palettes;

        static NoteColors()
        {
            _palettes = new Dictionary<string, NoteColorPalette>(StringComparer.OrdinalIgnoreCase)
            {
                ["yellow"] = CreatePalette("yellow", "經典黃", "#FFF59D", "#FFF9C4", "#FFEE58", "#212121"),
                ["green"] = CreatePalette("green", "嫩芽綠", "#C8E6C9", "#E8F5E9", "#A5D6A7", "#1B5E20"),
                ["blue"] = CreatePalette("blue", "天空藍", "#BBDEFB", "#E3F2FD", "#90CAF9", "#0D47A1"),
                ["pink"] = CreatePalette("pink", "櫻花粉", "#F8BBD0", "#FCE4EC", "#F48FB1", "#880E4F"),
                ["gray"] = CreatePalette("gray", "晨霧灰", "#EEEEEE", "#F5F5F5", "#E0E0E0", "#212121"),
                ["white"] = CreatePalette("white", "極簡白", "#F0F0F0", "#FFFFFF", "#E0E0E0", "#212121")
            };
        }

        private static NoteColorPalette CreatePalette(string key, string displayName, string headerHex, string bodyHex, string borderHex, string textHex)
        {
            var converter = new BrushConverter();
            var header = converter.ConvertFromString(headerHex) as Brush ?? throw new InvalidOperationException("Invalid note header color.");
            var body = converter.ConvertFromString(bodyHex) as Brush ?? throw new InvalidOperationException("Invalid note body color.");
            var border = converter.ConvertFromString(borderHex) as Brush ?? throw new InvalidOperationException("Invalid note border color.");
            var text = converter.ConvertFromString(textHex) as Brush ?? throw new InvalidOperationException("Invalid note text color.");

            header.Freeze();
            body.Freeze();
            border.Freeze();
            text.Freeze();

            return new NoteColorPalette
            {
                Key = key,
                DisplayName = displayName,
                HeaderBrush = header,
                BodyBrush = body,
                BorderBrush = border,
                TextBrush = text
            };
        }

        public static NoteColorPalette GetPalette(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !_palettes.TryGetValue(key, out var palette))
            {
                return _palettes["yellow"];
            }
            return palette;
        }

        public static IEnumerable<NoteColorPalette> AllPalettes => _palettes.Values;
    }
}
