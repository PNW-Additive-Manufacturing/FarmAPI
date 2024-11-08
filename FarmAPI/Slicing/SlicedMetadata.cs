using FarmAPI.Machines;
using System.Text.Json.Serialization;

namespace FarmAPI.Slicing
{
    public class SlicedMetadata
    {
        public required TimeSpan Duration { get; init; }

        [JsonPropertyName("weightInGrams")]
        public required float Weight { get; init; }
    }
}