namespace FarmAPI.Machines
{
    public interface IMachinePrintable
    {
        Task Print(Stream stream, string fileName, FilamentLocation filamentLocation);
    }
}
