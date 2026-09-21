using System;
using Newtonsoft.Json;

namespace Desktop_Frames.Notes.Models
{
    /// <summary>
    /// Represents a single desktop sticky note (POCO data entity).
    /// </summary>
    public class NoteItem
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonProperty("content")]
        public string Content { get; set; } = "";

        [JsonProperty("x")]
        public double X { get; set; } = 100;

        [JsonProperty("y")]
        public double Y { get; set; } = 100;

        [JsonProperty("width")]
        public double Width { get; set; } = 300;

        [JsonProperty("height")]
        public double Height { get; set; } = 240;

        [JsonProperty("color")]
        public string Color { get; set; } = "yellow";

        [JsonProperty("alwaysOnTop")]
        public bool AlwaysOnTop { get; set; } = false;

        [JsonProperty("isLocked")]
        public bool IsLocked { get; set; } = false;

        [JsonProperty("isVisible")]
        public bool IsVisible { get; set; } = true;

        [JsonProperty("fontSize")]
        public double FontSize { get; set; } = 14.0;

        [JsonProperty("fontFamily")]
        public string FontFamily { get; set; } = "Microsoft JhengHei";
    }
}
