using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Text.RegularExpressions;

namespace FarmAPI.Slicing.BambuStudio
{
    public class SlicingConfigurationPaths
    {
        public required string MachinePath { get; set; }

        public required string ProcessPath { get; set; }

        public required string FilamentPath { get; set; }
    }

    // https://github.com/bambulab/BambuStudio/blob/4d64b2d65249c141eac0e3cb43712989622012c7/src/libslic3r/Utils.hpp#L64

    public partial class BambuStudio
    {
        public string BaseDir { get; }

        public string ExecutablePath { get; }

        public string ResourcesPath { get; }

        public string ProfilesPath { get; }

        protected Dictionary<string, BambuStudioManufacturerProfile> ManufacturerProfiles { get; } = [];

        public BambuStudio(string baseDir, string executablePath)
        {
            BaseDir = baseDir;
            ExecutablePath = executablePath;
            ResourcesPath = Path.Join(BaseDir, "resources");
            ProfilesPath = Path.Join(ResourcesPath, "profiles");

            // Load all brand availability files.
            {
                var files = Directory.GetFiles(ProfilesPath, "*.json", SearchOption.TopDirectoryOnly);
                var ignoreFiles = new string[] { "blacklist.json" };

                foreach (var file in files)
                {
                    if (ignoreFiles.Contains(Path.GetFileName(file))) continue;

                    using var stream = File.OpenRead(file);
                    var brandAvailability = JsonSerializer.Deserialize<BambuStudioManufacturerProfile>(stream, Config.SerializerOptions);
                    ManufacturerProfiles.Add(Path.GetFileNameWithoutExtension(file), brandAvailability);
                }
            }
        }

        /// <summary>
        /// Retrieves the file paths for slicing configurations based on the provided brand, model, filament material, and layer height.
        /// </summary>
        public async Task<SlicingConfigurationPaths> CreateSlicingConfigurations(string manufacturer, string model, SlicingOptions.FDM options)
        {
            if (!ManufacturerProfiles.TryGetValue(manufacturer, out var manufacturerProfile))
            {
                throw new ArgumentException(model, $"Manufacturer {manufacturer} is not supported by Bambu Studio.");
            }

            string machineFullPath = Path.Join(ProfilesPath, manufacturer, manufacturerProfile.GetMachineRelativePath(model, 0.4f));
            string filamentFullPath = Path.Join(ProfilesPath, manufacturer, manufacturerProfile.GetFilamentRelativePath(model, 0.4f, options.Material));
            string processFullPath = Path.Join(ProfilesPath, manufacturer, manufacturerProfile.GetProcessRelativePath(model, 0.4f, options.LayerHeight));

            Console.WriteLine($"Machine: {machineFullPath}");
            Console.WriteLine($"Filament: {filamentFullPath}");
            Console.WriteLine($"Process: {processFullPath}");

            var machineData = ProfileData.Load(machineFullPath).Resolve(this, manufacturerProfile);
            var filamentData = ProfileData.Load(filamentFullPath).Resolve(this, manufacturerProfile);
            var processDatta = ProfileData.Load(processFullPath).Resolve(this, manufacturerProfile);

            processDatta.Supports = options.UseSupports;
            if (options.SupportStyle != null) processDatta.SupportType = options.SupportStyle;
            if (options.WallLoops != null) processDatta.Walls = options.WallLoops.Value;

            var tmpResolvedMachineDataPath = Paths.GetTempFileName("json");
            var tmpResolvedFilamentDataPath = Paths.GetTempFileName("json");
            var tmpResolvedProcessDataPath = Paths.GetTempFileName("json");

            await File.WriteAllTextAsync(tmpResolvedMachineDataPath, machineData.InnerOject.ToJsonString());
            await File.WriteAllTextAsync(tmpResolvedFilamentDataPath, filamentData.InnerOject.ToJsonString());
            await File.WriteAllTextAsync(tmpResolvedProcessDataPath, processDatta.InnerOject.ToJsonString());

            return new SlicingConfigurationPaths
            {
                MachinePath = tmpResolvedMachineDataPath,
                ProcessPath = tmpResolvedProcessDataPath,
                FilamentPath = tmpResolvedFilamentDataPath
            };
        }

        public async Task<string> Slice(string machineBrand, string machineModel, string modelPath, SlicingOptions.FDM options)
        {
            var configurationPaths = await CreateSlicingConfigurations(machineBrand, machineModel, options);

            if (OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException("Bambu Studio CLI is not supported on Windows!");
            }

            Console.WriteLine($"Slicing with Bambu Studio...\nMachine:\t{configurationPaths.MachinePath}\nProcess:\t{configurationPaths.ProcessPath}\nFilament:\t{configurationPaths.FilamentPath}");

            string outputPath = Paths.GetTempFileName("3mf");

            if (File.Exists(outputPath))
            {
                // Must delete the file so we can check if the slicing was successful.
                File.Delete(outputPath);
            }

            StringBuilder arguments = new($"--slice 0 --export-3mf \"{outputPath}\" ");
            arguments.Append("--curr-bed-type \"Textured PEI Plate\"");
            arguments.Append(' ');
            arguments.Append($"--load-settings \"{configurationPaths.ProcessPath};{configurationPaths.MachinePath}\"");
            arguments.Append(' ');
            arguments.Append($"--load-filaments \"{configurationPaths.FilamentPath}\"");
            arguments.Append(' ');
            arguments.Insert(arguments.Length, $"{modelPath} ", options.Quantity);

            Console.WriteLine($"Slicing arguments: {arguments}");

            var bambuStudioProcess = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this.ExecutablePath,
                    Arguments = arguments.ToString(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            bambuStudioProcess.Start();

            // Wait for the process to finish
            var executionTask = bambuStudioProcess.WaitForExitAsync();

            await executionTask.WaitAsync(TimeSpan.FromSeconds(30));
            if (!executionTask.IsCompleted)
            {
                bambuStudioProcess.Kill();
                throw new TimeoutException("Failed to slice the model. The process took too long to complete.");
            }

            if (bambuStudioProcess.ExitCode != 0)
            {
                Console.WriteLine($"BambuStudio exited with status code {bambuStudioProcess.ExitCode}");
            }

            if (!File.Exists(outputPath))
            {
                string error = bambuStudioProcess.StandardOutput.ReadToEnd();

                throw new InvalidOperationException($"Slicing failed as output does not exist!\nCLI Output: {error}");
            }

            Console.WriteLine("Slicing completed.");

            _ = Task.Run(() => File.Delete(configurationPaths.MachinePath));
            _ = Task.Run(() => File.Delete(configurationPaths.FilamentPath));
            _ = Task.Run(() => File.Delete(configurationPaths.ProcessPath));

            return outputPath;
        }

        /// <inheritdoc cref="ParseMetadata(String)"/>
        public async Task<SlicedMetadata> ParseMetadata(Stream stream)
        {
            var zip3MF = new ZipArchive(stream);

            var plateEntry = zip3MF.Entries.First(e => e.FullName.Equals("Metadata/slice_info.config"));

            using Stream slicingInfo = plateEntry.Open();

            var document = new XmlDocument();
            document.Load(slicingInfo);

            var weightInGrams = float.Parse(document.SelectSingleNode("/config/plate/metadata[@key=\"weight\"]")?.Attributes?["value"]?.Value);
            var durationInSeconds = long.Parse(document.SelectSingleNode("/config/plate/metadata[@key=\"prediction\"]")?.Attributes?["value"]?.Value);

            return new SlicedMetadata
            {
                Duration = TimeSpan.FromSeconds(durationInSeconds),
                Weight = weightInGrams
            };
        }

        /// <summary>
        /// Parses the internal GCODE file (ONLY PLATE #1) from the 3MF zip archive.
        /// </summary>
        public Task<SlicedMetadata> ParseMetadata(string path3MF)
        {
            return ParseMetadata(File.OpenRead(path3MF));
        }
    }
}
