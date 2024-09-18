using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace FarmAPI.Slicing.BambuStudio
{
    /// <summary>
    /// Represents the configuration for each JSON overview file for supported brands in Bambu Studio profiles.
    /// </summary>
    public class BambuStudioManufacturerProfile
    {
        [JsonPropertyName("name")]
        public required string Manufacturer { get; set; }

        public class ProfileEntry
        {
            public required string Name { get; set; }

            [JsonPropertyName("sub_path")]
            public required string SubPath { get; set; }
        }

        [JsonPropertyName("machine_list")]
        public ProfileEntry[] SupportedMachines { get; set; } = [];

        [JsonPropertyName("process_list")]
        public ProfileEntry[] SupportedProcesses { get; set; } = [];

        [JsonPropertyName("filament_list")]
        public ProfileEntry[] SupportedFilaments { get; set; } = [];

        public string GetDirectory(BambuStudio bambuStudio)
        {
            return Path.Join(bambuStudio.ProfilesPath, this.Manufacturer == "Bambu Lab" ? "BBL" : this.Manufacturer);
        }

        public BambuStudioManufacturerProfile(string manufacturer, ProfileEntry[] supportedMachines, ProfileEntry[] supportedProcesses, ProfileEntry[] supportedFilaments)
        {
            string modifiedManufactor = manufacturer;
            if (modifiedManufactor == "Bambulab") modifiedManufactor = "Bambu Lab";

            Manufacturer = modifiedManufactor;
            SupportedMachines = supportedMachines;
            SupportedProcesses = supportedProcesses;
            SupportedFilaments = supportedFilaments;
        }

        public string GetRelativePath(string name, ProfileType profileType)
        {
            return profileType switch
            {
                ProfileType.Machine => GetMachineRelativePath(name),
                ProfileType.Process => GetProcessRelativePath(name),
                ProfileType.Filament => GetFilamentRelativePath(name),
                _ => throw new Exception($"Profile type {profileType} is not supported.")
            };
        }

        public string GetFilamentRelativePath(string name)
        {
            var filamentPath = SupportedFilaments.FirstOrDefault(filament => filament.Name.Equals(name));
            if (filamentPath == null)
            {
                throw new Exception($"Filament {name} is not supported in {Manufacturer} profiles.");
            }
            else return filamentPath.SubPath;
        }

        public string GetFilamentRelativePath(string machineModel, float machineNozzle, string filamentMaterial)
        {
            var filamentFileName = new StringBuilder();
            if (this.Manufacturer == "Bambu Lab")
            {
                // Bambu Filament Schema
                // Edge-cases: A1 mini becomes A1M
                // FILE: Bambu [Material] @BBL [Model].json
                // FILE: Bambu [Material] @BBL [Model] [NozzleDia ex: 0.2] nozzle.json

                // Generic Filament Schema
                // Edge-cases: A1 mini becomes A1M
                // FILE: Generic [Material].json
                // FILE: Generic [Material] @BBL [Model].json
                // FILE: Generic [Material] @BBL [Model] [NozzleDia ex:0.2] nozzle.json
                // NOTE: Not available for some printers? Such as the X1E. Nothing changed?

                // We will be using the Generic Filament Schema for now.

                string modifiedModel = machineModel;
                if (modifiedModel == "A1 mini") modifiedModel = "A1M";

                filamentFileName.Append($"Generic {filamentMaterial}");

                if (new string[] { "P1P", "A1M", "A1" }.Contains(modifiedModel))
                {
                    filamentFileName.Append($" @BBL {modifiedModel}");

                    if (machineNozzle != 0.4f)
                    {
                        filamentFileName.Append($" {machineModel} nozzle");
                    }
                }
            }
            else
            {
                filamentFileName.Append($"Generic {filamentMaterial} @{this.Manufacturer}");
            }

            var targetFilament = SupportedFilaments.FirstOrDefault(filament => filament.Name.Equals(filamentFileName.ToString()));
            if (targetFilament == null)
            {
                // Console.WriteLine(filamentFileName);
                throw new Exception($"Filament {filamentMaterial} using {machineModel} with a {machineNozzle} nozzle is not supported in {Manufacturer} profiles.");
            }
            else return targetFilament.SubPath;
        }

        public string GetMachineRelativePath(string name)
        {
            var machinePath = SupportedMachines.FirstOrDefault(machine => machine.Name.Equals(name));
            if (machinePath == null)
            {
                throw new Exception($"Machine {name} is not supported in {Manufacturer} profiles.");
            }
            else return machinePath.SubPath;
        }

        public string GetMachineRelativePath(string machineModel, float machineNozzle)
        {
            // Bambu Machine Schema
            // Edge-cases: A1M becomes A1 mini & X1C becomes X1 Carbon 
            // FILE: Bambu Lab [Model] [NozzleDia ex: 0.2] nozzle

            string modifiedModel = machineModel;
            if (modifiedModel == "A1M") modifiedModel = "A1 mini";
            if (modifiedModel == "X1C") modifiedModel = "X1 Carbon";

            string machineFileName = $"{this.Manufacturer} {modifiedModel} {machineNozzle} nozzle";

            var targetMachine = SupportedMachines.FirstOrDefault(machine => machine.Name.Equals(machineFileName));
            if (targetMachine == null)
            {
                // Console.WriteLine(machineFileName);
                throw new Exception($"Machine {machineModel} with a {machineNozzle} nozzle is not supported in {Manufacturer} profiles.");
            }
            else return targetMachine.SubPath;
        }

        public string GetProcessRelativePath(string name)
        {
            var processPath = SupportedProcesses.FirstOrDefault(process => process.Name.Equals(name));
            if (processPath == null)
            {
                throw new Exception($"Process {name} is not supported in {Manufacturer} profiles.");
            }
            else return processPath.SubPath;
        }

        public string GetProcessRelativePath(string machineModel, float machineNozzel, float layerHeight)
        {
            // Bambu Process Schema
            // Edge-cases: Layer height requires two decimal places.
            // FILE: [LayerHeight ex: 0.20]mm {DESC ex: Standard} @BBL [Model].json
            // FILE: [LayerHeight ex: 0.20]mm {DESC ex: Standard} @BBL [Model] [NozzleDia ex: 0.6] nozzle.json

            string? processFileName;

            if (this.Manufacturer == "Bambu Lab")
            {
                string modifiedModel = machineModel;
                if (modifiedModel == "X1E") modifiedModel = "X1C";
                if (modifiedModel == "P1S") modifiedModel = "X1C";

                var endingString = new StringBuilder($"@BBL {modifiedModel}");
                if (machineNozzel != 0.4f)
                {
                    endingString.Append($" {machineNozzel} nozzle");
                }

                // Console.WriteLine($"{layerHeight:N2}mm");
                // Console.WriteLine(endingString);

                processFileName = SupportedProcesses.FirstOrDefault(process => process.Name.StartsWith($"{layerHeight:N2}mm") && process.Name.EndsWith(endingString.ToString()))?.Name;
            }
            else
            {
                processFileName = $"{layerHeight:N2}mm Standard @{machineModel}";
            }

            var targetProcess = SupportedProcesses.FirstOrDefault(process => process.Name.Equals(processFileName));
            if (targetProcess == null)
            {
                throw new Exception($"Process using {machineModel} with a {machineNozzel} nozzle at {layerHeight}mm is not supported in {Manufacturer} profiles.");
            }
            else return targetProcess.SubPath;
        }
    }
}