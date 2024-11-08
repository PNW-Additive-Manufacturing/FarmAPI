namespace FarmAPI.Machines
{
    public interface IMachineFilamentMutable
    {
        public Task SetFilament(Filament? filament);
    }
}
