using System;
using Newtonsoft.Json;

namespace kamekusaStreamPushSlack
{
    public class AnalyticsItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("_ts")]
        public uint Ts { get; set; }

        [JsonProperty("bbox")]
        public float[] BBox { get; set; }
    }
}
