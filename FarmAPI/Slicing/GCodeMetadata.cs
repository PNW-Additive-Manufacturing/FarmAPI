using FarmAPI.Machines;
using System.Text.Json.Serialization;

namespace FarmAPI.Slicing
{
    public class GCodeMetadata
    {
        [JsonConverter(typeof(TimeSpanSecondConverter))]
        [JsonPropertyName("durationAsSeconds")]
        public required TimeSpan Duration { get; init; }
    }
}