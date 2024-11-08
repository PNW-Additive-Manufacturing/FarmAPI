using FarmAPI.Machines.BambuLab;
using FarmAPI.Machines.ELEGOO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FarmAPI.Machines
{
    public enum MachineConfigurationKind
    {
        BambuLab,
        ELEGOO
    }

    public record MachineConfiguration(MachineConfigurationKind Kind)
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MachineConfigurationKind Kind { get; } = Kind;
    }

    public class MachineFilamentsWithLocationConverter : JsonConverter<IDictionary<FilamentLocation, Filament>>
    {
        public override IDictionary<FilamentLocation, Filament>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, IDictionary<FilamentLocation, Filament> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var (location, filament) in value)
            {
                writer.WriteStartObject();

                if (location.IsExternal)
                {
                    writer.WriteString("location", "External");
                }
                else if (location.IsInAMS)
                {
                    writer.WriteString("location", "AMS");
                    writer.WriteNumber("slot", location.GlobalSlot);
                }
                writer.WriteString(nameof(Filament.Color).ToLower(), filament.Color);
                writer.WriteString(nameof(Filament.Material).ToLower(), filament.Material);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
    }

    public class TimeSpanSecondConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.FromSeconds(reader.GetInt64());
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue((long)value.TotalSeconds);
        }
    }

    public partial class MachineCollection
    {
        private static readonly Dictionary<MachineConfigurationKind, (Type, Type)> ConfigurationConstructors = new()
        {
            { MachineConfigurationKind.BambuLab, (typeof(BambuMachineConfiguration), typeof(BambuMachine)) },
            { MachineConfigurationKind.ELEGOO, (typeof(ELEGOOMachineConfiguration), typeof(ELEGOOMachine)) }
        };

        private static Machine DeserializeElement(JsonElement element)
        {
            var baseConfiguration = element.Deserialize<MachineConfiguration>(Config.SerializerOptions)
                ?? throw new Exception($"Failed to deserialize {nameof(MachineConfiguration)}, is the JSON correct?");

            var (configurationType, machineType) = ConfigurationConstructors[baseConfiguration.Kind];

            var configuration = element.Deserialize(configurationType, Config.SerializerOptions)
                ?? throw new Exception($"Failed to deserialize {configurationType}, is the JSON correct?");

            try
            {
                return (Machine)Activator.CreateInstance(machineType, configuration)!;
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to create instance of {machineType} with {configurationType}!", e);
            }
        }

        public static MachineCollection Parse(string path) => Parse(File.OpenRead(path));

        public static MachineCollection Parse(Stream stream) => Parse(JsonDocument.Parse(stream).RootElement);

        public static MachineCollection Parse(JsonElement el)
        {
            return new MachineCollection(el.EnumerateArray().Select(DeserializeElement));
        }
    }
}
