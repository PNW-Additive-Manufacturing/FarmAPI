namespace FarmAPI.Machines
{
    /// <summary>
    /// Represents an automated machine which is able to automatically slice, and print!
    /// </summary>
    public interface IMachineAutomated : IMachineSliceable, IMachinePrintable { }
}
