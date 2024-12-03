namespace FarmAPI.Machines
{
    public interface IMachineControllable
    {
        Task Stop();

        Task Resume();

        Task Pause();
    }
}
