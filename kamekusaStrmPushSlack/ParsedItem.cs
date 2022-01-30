using System;

namespace kamekusaStreamPushSlack
{
    public class ParsedItem
    {
        public string Timestamp { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Moved { get; set; }
        public string Direction { get; set; }
    }
}
