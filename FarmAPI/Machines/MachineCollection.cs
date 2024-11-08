using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FarmAPI.Machines.BambuLab;

namespace FarmAPI.Machines
{
    public partial class MachineCollection : IEnumerable<Machine>
    {
        public SortedDictionary<string, Machine> Machines { get; } = [];

        public MachineCollection() { }

        public MachineCollection(IEnumerable<Machine> machines) 
        {
            this.Machines = new SortedDictionary<string, Machine>(machines.ToDictionary(m => m.Identifier));
        }

        public bool TryGetMachine(string identity, [NotNullWhen(true)] out Machine? machine)
        {
            return Machines.TryGetValue(identity, out machine);
        }

        public (Machine, FilamentLocation)? FindAvailableMachineWithFilament(MachineTechnology technology, Filament filament)
        {
            foreach (var machine in Machines.Values)
            {
                if (machine.IsHealthy && machine.Technology == technology && machine.Status == MachineState.Idle 
                    && machine.Features == MachineFeatures.Sliceable
                    && machine.Features == MachineFeatures.Printable)
                {
                    var locations = machine.LocateMatchingFilament(filament);
                    if (locations.Length > 0) return new(machine, locations.First());
                }
            }
            return null;
        }

        public IEnumerator<Machine> GetEnumerator() => Machines.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}