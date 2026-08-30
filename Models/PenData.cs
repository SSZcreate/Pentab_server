using System.Text.Json.Serialization;

namespace PentabServer.Models
{
    public static class ToolType
    {
        public const int Unknown = 0;
        public const int Finger = 1;
        public const int Stylus = 2;
        public const int Eraser = 3;
        public const int Mouse = 4;
    }

    public static class ActionType
    {
        public const string Down = "DOWN";
        public const string Move = "MOVE";
        public const string Up = "UP";
        public const string HoverMove = "HOVER_MOVE";
        public const string HoverEnter = "HOVER_ENTER";
        public const string HoverExit = "HOVER_EXIT";
        public const string Cancel = "CANCEL";
        public const string Click = "CLICK";
        public const string Scroll = "SCROLL";
        public const string DownLeft = "DOWN_LEFT";
        public const string UpLeft = "UP_LEFT";
        public const string DownRight = "DOWN_RIGHT";
        public const string UpRight = "UP_RIGHT";
    }

    public class PenData
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "ABSOLUTE"; // "ABSOLUTE" or "TRACKPAD"

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("dx")]
        public float Dx { get; set; }

        [JsonPropertyName("dy")]
        public float Dy { get; set; }

        [JsonPropertyName("pressure")]
        public float Pressure { get; set; }

        [JsonPropertyName("tiltX")]
        public float TiltX { get; set; }

        [JsonPropertyName("tiltY")]
        public float TiltY { get; set; }

        [JsonPropertyName("toolType")]
        public int ToolType { get; set; }

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("clickType")]
        public string ClickType { get; set; } = string.Empty; // "LEFT", "RIGHT", "DOUBLE_LEFT"

        [JsonPropertyName("buttonState")]
        public int ButtonState { get; set; }

        [JsonPropertyName("scrollDelta")]
        public int ScrollDelta { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }
}

