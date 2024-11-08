using static FarmAPI.Program;

namespace FarmAPI.Slicing
{
    /// <summary>
    /// A class to define the supported and in-use Slicers by FarmAPI within the current configuration.
    /// </summary>
    internal class Slicers
    {
        private static BambuStudio.BambuStudio? _existingBambuStudio;

        public static BambuStudio.BambuStudio BambuStudio()
        {
            if (_existingBambuStudio == null)
            {
                if (!FarmAPIConfiguration.HasBambuStudio)
                {
                    throw new InvalidOperationException($"BambuStudio Slicer must be provided. Refer the documentation to include the settings required!");
                }

                _existingBambuStudio = new BambuStudio.BambuStudio(
                    FarmAPIConfiguration.BambuStudioPath!,
                    FarmAPIConfiguration.BambuStudioExecutablePath!);
            }
            return _existingBambuStudio;
        }

        //public static async Task<SlicedMetadata> SliceThenReadMetadata(Stream modelStream, string fileName, Filament filament, SlicingOptions options)
        //{
        //    if (options is SlicingOptions.FDM FDM)
        //    {
                
        //    }
        //}
    }
}
