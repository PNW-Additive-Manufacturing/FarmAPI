namespace FarmAPI.Machines
{
    public readonly record struct FilamentLocation(int AMS, int Slot)
    {
        public readonly bool IsExternal => GlobalSlot == -1;
        public readonly bool IsInAMS => GlobalSlot >= 0;

        /// <summary>
        /// The slot number of the filament in the AMS or -1 if External.
        /// </summary
        public int GlobalSlot { get; } = (AMS * 4) + Slot;

        public static FilamentLocation External() => new(0, -1);

        public static FilamentLocation InAMS(int amsNum, int slotNum) => new(amsNum, slotNum);

        public override string ToString()
        {
            return IsExternal ? $"External" : $"AMS #{AMS} in Slot #{GlobalSlot}";
        }
    }
}
