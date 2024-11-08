using FarmAPI.Slicing;

namespace FarmAPI.Machines
{
    public interface IMachineSliceable
    {
        public Task<Stream> Slice(Stream modelStream, string fileName, SlicingOptions options);

        public Task<SlicedMetadata> ReadMetadata(Stream slicedStream);

        public static async Task<SlicedMetadata> SliceThenReadMetadata(IMachineSliceable machineSliceable, Stream modelStream, string fileName, Filament filament, SlicingOptions options)
        {
            Stream slicedStream = await machineSliceable.Slice(modelStream, fileName, options);
            return await machineSliceable.ReadMetadata(slicedStream);
        }
    }
}
