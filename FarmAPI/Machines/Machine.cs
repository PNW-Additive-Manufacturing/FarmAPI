using FarmAPI.Slicing.BambuStudio;
using MQTTnet.Client;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

namespace FarmAPI.Machines
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MachineState
    {
        Unknown,
        Idle,
        Printing,
        /// <summary>
        /// Machine is preparing to being printing.
        /// This is a temporary states when calibration is occurring before a print begins.
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

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MachineTechnology
    {
        FDM,
        SLA
    }

    [Flags]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MachineFeatures
    {
        FilamentMutable,
        /// <summary>
        /// The FarmAPI is able to slice a given model-file into a machine specific instruction file.
        /// </summary>
        Sliceable,
        /// <summary>
        /// Prints (such as GCODE, 3MF or GOO) can be sent and started remotely through using the FarmAPI.
        /// </summary>
        Printable
    }

    public abstract class Machine : IComparer<Machine>
    {
        [JsonConverter(typeof(MachineFilamentsWithLocationConverter))]
        public abstract IDictionary<FilamentLocation, Filament> Filaments { get; }

        public virtual FilamentLocation[] LocateMatchingFilament(Filament filament)
        {
            return Filaments.Where(x => x.Value.Equals(filament)).Select(x => x.Key).ToArray();
        }

        /// <summary>
        /// An optional method to forcibly update the machine's state if supported.
        /// </summary>
        /// <returns></returns>
        public abstract Task Update();

        public abstract MachineTechnology Technology { get; }

        public abstract MachineSize Size { get; }

        public bool IsHealthy => this.LastUpdated + this.HealthTimeout > DateTime.Now;

        [JsonIgnore]
        public virtual TimeSpan HealthTimeout { get; set; } = TimeSpan.FromMinutes(5);

        public DateTime LastUpdated { get; protected set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MachineState Status { get; protected set; } = MachineState.Unknown;

        public string? FailReason { get; protected set; }

        public abstract string Brand { get; }

        public abstract string Model { get; }

        public string? Nickname { get; protected set; }

        public virtual string Identifier => Nickname ?? Model;

        public int? Progress { get; protected set; }

        /// <summary>
        /// Time remaining in minutes.
        /// </summary>
        public int? TimeRemaining { get; protected set; }

        public string? Filename { get; protected set; }

        /// <summary>
        /// An optional override which changes the state from <see cref="MachineState.Printed"/> to <see cref="MachineState.Idle"/> 
        /// if the machine does not automatically do so.
        /// </summary>
        public abstract Task MarkAsBedCleared();

        private MachineFeatures? CachedFeatures = null;

        public MachineFeatures Features
        {
            get
            {
                if (CachedFeatures == null)
                {
                    CachedFeatures = default(MachineFeatures);

                    Type instanceType = this.GetType();
                    if (instanceType.GetInterface(nameof(IMachineFilamentMutable)) != null)
                    {

                        this.CachedFeatures |= MachineFeatures.FilamentMutable;
                    }
                    if (instanceType.GetInterface(nameof(IMachinePrintable)) != null)
                    {

                        this.CachedFeatures |= MachineFeatures.Printable;
                    }
                    if (instanceType.GetInterface(nameof(IMachineSliceable)) != null)
                    {
                        this.CachedFeatures |= MachineFeatures.Sliceable;
                    }
                }
                return this.CachedFeatures!.Value;
            }
        }

        public int Compare(Machine? x, Machine? y)
        {
            return x!.Identifier.CompareTo(y!.Identifier);
        }
    }
}
