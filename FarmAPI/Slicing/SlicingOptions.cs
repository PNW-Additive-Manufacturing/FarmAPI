namespace FarmAPI.Slicing
{
    // https://spencerfarley.com/2021/03/26/unions-in-csharp/
    public record SlicingOptions
    {
        public record FDM(
            string Material,
            int Quantity,
            float LayerHeight,
            int? WallLoops,
            bool UseSupports,
            string? SupportStyle) : SlicingOptions();

        public record SLA() : SlicingOptions();
        // SLA is NOT implemented as this time.

        private SlicingOptions() { }
    }
}
