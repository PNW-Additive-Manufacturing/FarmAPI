namespace FarmAPI.Machines
{
    public static class MachineExtensions
    {
        public static Task Print(this Machine machine, Stream dataStream, string filename, Filament filament)
        {
            var locations = machine.LocateMatchingFilament(filament);

            if (locations.Length == 0)
            {
                throw new ArgumentException($"Filament not found in machine {machine.Identifier} matching {filament}", nameof(filament));
            }
            return machine.Print(dataStream, filename, locations.First());
        }
    }
}
