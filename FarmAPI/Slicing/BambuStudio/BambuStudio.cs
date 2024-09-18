using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FarmAPI.Slicing.BambuStudio
{
    public class SlicingConfigurationPaths
    {
        public required string MachinePath { get; set; }

        public required string ProcessPath { get; set; }

        public required string FilamentPath { get; set; }
    }

    public partial class BambuStudio
    {
        public string BaseDir { get; }

        public string ExecutablePath { get; }

        public string ResourcesPath { get; }

        public string ProfilesPath { get; }

        protected Dictionary<string, BambuStudioManufacturerProfile> ManufacturerProfiles { get; } = [];

        public BambuStudio(string baseDir, string executablePath, string resourcesPath)
        {
            BaseDir = baseDir;
            ExecutablePath = executablePath;
            ResourcesPath = resourcesPath;
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
        public async Task<SlicingConfigurationPaths> CreateSlicingConfigurations(string manufacturer, string model, string filamentMaterial, float layerHeight, float nozzle, int? wallLoops = null, bool? useSupports = null, string? supportStyle = null)
        {
            if (!ManufacturerProfiles.TryGetValue(manufacturer, out var manufacturerProfile))
            {
                throw new ArgumentException(model, $"Manufacturer {manufacturer} is not supported by Bambu Studio.");
            }

            string machineFullPath = Path.Join(ProfilesPath, manufacturer, manufacturerProfile.GetMachineRelativePath(model, nozzle));
            string filamentFullPath = Path.Join(ProfilesPath, manufacturer, manufacturerProfile.GetFilamentRelativePath(model, nozzle, filamentMaterial));
            string processFullPath = Path.Join(ProfilesPath, manufacturer, manufacturerProfile.GetProcessRelativePath(model, nozzle, layerHeight));

            Console.WriteLine($"Machine: {machineFullPath}");
            Console.WriteLine($"Filament: {filamentFullPath}");
            Console.WriteLine($"Process: {processFullPath}");

            var machineData = ProfileData.Load(machineFullPath).Resolve(this, manufacturerProfile);
            var filamentData = ProfileData.Load(filamentFullPath).Resolve(this, manufacturerProfile);
            var processDatta = ProfileData.Load(processFullPath).Resolve(this, manufacturerProfile);

            if (useSupports != null) processDatta.Supports = useSupports.Value;
            if (supportStyle != null) processDatta.SupportType = supportStyle;

            if (wallLoops != null) processDatta.Walls = wallLoops.Value;

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

        public async Task<string> Slice(string machineBrand, string machineModel, string modelPath, int quantity, string filamentMaterial, float layerHeight, bool? useSupports = null, string? supportStyle = null, int? wallLoops = null)
        {
            var configurationPaths = await CreateSlicingConfigurations(machineBrand, machineModel, filamentMaterial, layerHeight, 0.4f, wallLoops, useSupports, supportStyle);

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
            arguments.Insert(arguments.Length, $"{modelPath} ", quantity);

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
        public async Task<GCodeMetadata> ParseMetadata(Stream stream)
        {
            var zip3MF = new ZipArchive(stream);

            var plateEntry = zip3MF.Entries.First(e =>
            {
                // Console.WriteLine(e.FullName);

                return e.FullName.Equals("Metadata/plate_1.gcode");
            });

            TimeSpan? totalEstimatedTime = null;

            using (var gcodeReader = new StreamReader(plateEntry.Open()))
            {
                while (!gcodeReader.EndOfStream)
                {
                    string currentLine = (await gcodeReader.ReadLineAsync())!;

                    int index = currentLine.LastIndexOf("total estimated time: ");

                    if (index == -1) continue;

                    string timeString = currentLine.Substring(index + "total estimated time: ".Length);

                    Console.WriteLine($"Estimated Time: {timeString}");

                    totalEstimatedTime = ParseDuration(timeString);

                    break;
                }
            }

            return new GCodeMetadata
            {
                Duration = totalEstimatedTime ?? throw new Exception("Failed to parse the estimated time from the GCode."),
            };
        }

        /// <summary>
        /// Parses the internal GCODE file (ONLY PLATE #1) from the 3MF zip archive.
        /// </summary>
        public Task<GCodeMetadata> ParseMetadata(string path3MF)
        {
            return ParseMetadata(File.OpenRead(path3MF));
        }

        private static TimeSpan ParseDuration(string input)
        {
            int days = 0, hours = 0, minutes = 0, seconds = 0;

            Match match = new Regex(@"(?:(\d+)d)?\s*(?:(\d+)h)?\s*(?:(\d+)m)?\s*(?:(\d+)s)?").Match(input);

            if (match.Success)
            {
                if (match.Groups[1].Success) days = int.Parse(match.Groups[1].Value);

                if (match.Groups[2].Success) hours = int.Parse(match.Groups[2].Value);

                if (match.Groups[3].Success) minutes = int.Parse(match.Groups[3].Value);

                if (match.Groups[4].Success) seconds = int.Parse(match.Groups[4].Value);
            }
            return new TimeSpan(days, hours, minutes, seconds);
        }
    }
}
