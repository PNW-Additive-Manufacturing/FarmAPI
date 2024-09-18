using System.Collections;
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

        public (Machine, FilamentLocation)? FindAvailableMachineWithFilament(Filament filament)
        {
            foreach (var machine in Machines.Values)
            {
                if (machine.IsHealthy && machine.Status == MachineState.Idle)
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