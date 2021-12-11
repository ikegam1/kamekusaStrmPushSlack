using System;
using Newtonsoft.Json;

namespace kamekusaStreamPushSlack
{
    public class AnalyticsItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("partitionKey")]
        public string PartitionKey { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("timestamp")]
        public ulong Timestamp { get; set; }

        [JsonProperty("_ts")]
        public uint Ts { get; set; }

        [JsonProperty("bbox")]
        public float[] BBox { get; set; }
    }
}
