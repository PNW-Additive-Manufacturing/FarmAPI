using MQTTnet.Client;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

namespace FarmAPI.Machines
{
    public enum MachineState
    {
        Unknown,
        Idle,
        Printing,
        /// <summary>
        /// Machine is preparing to being printing.
        /// This is a temporary states when calibration is occuring before a print begins.
        /// </summary>
        Preparing,
        Printed,
        Paused,
        Error
    }

    public readonly struct MachineSize(int width, int length, int height)
    {
        public int Width { get; } = width;
        public int Length { get; } = length;
        public int Height { get; } = height;
    }

    public abstract class Machine : IComparer<Machine>
    {
        [JsonConverter(typeof(MachineFilamentsWithLocationConverter))]
        public abstract IDictionary<FilamentLocation, Filament> Filaments { get; }

        public abstract FilamentLocation[] LocateMatchingFilament(Filament filament);

        /// <summary>
        /// An optional method to forcibly update the machine's state if supported.
        /// </summary>
        /// <returns></returns>
        public abstract Task Update();

        public abstract MachineSize Size { get; }

        public bool IsHealthy => this.LastUpdated + this.HealthTimeout > DateTime.Now;

        [JsonIgnore]
        public virtual TimeSpan HealthTimeout { get; set; } = TimeSpan.FromMinutes(5);

        public DateTime LastUpdated { get; protected set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MachineState Status { get; protected set; } = MachineState.Unknown;

        public string? FailReason { get; protected set; }

        public abstract string Identifier { get; }

        public abstract string Brand { get; }

        public abstract string Model { get; }

        public int? Progress { get; protected set; }

        /// <summary>
        /// Time reamining in minutes.
        /// </summary>
        public int? TimeRemaining { get; protected set; }

        public string? Filename { get; protected set; }

        public abstract Task Print(Stream stream, string fileName, FilamentLocation location);

        /// <summary>
        /// An optional override which changes the state from <see cref="MachineState.Printed"/> to <see cref="MachineState.Idle"/> 
        /// if the machine does not automatically do so.
        /// </summary>
        public abstract Task MarkAsBedCleared();

        public int Compare(Machine? x, Machine? y)
        {
            return x!.Identifier.CompareTo(y!.Identifier);
        }
    }
}
