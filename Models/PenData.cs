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
    }

    public class PenData
    {
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

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

        [JsonPropertyName("buttonState")]
        public int ButtonState { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }
}
