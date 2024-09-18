using FarmAPI.Machines;
using System.Text.Json;

namespace FarmAPI
{
    public static class Config
    {
        public static JsonSerializerOptions SerializerOptions { get; } = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new MachineFilamentsWithLocationConverter()
            }
        };
    }
}
