using FarmAPI.Machines;
using FarmAPI.Machines.BambuLab;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FarmAPI
{
    public partial class Program
    {
        public static class FarmAPIConfiguration
        {
            private static string? machinesConfigPath;

            public static BambuCloudCredentials? BambuCloudCredentials { get; set; }
            public static string? BambuStudioPath { get; set; }
            public static string? BambuStudioExecutablePath { get; set; }

            public static MachineCollection Machines { get; private set; } = new MachineCollection();

            public static bool HasBambuStudio => BambuCloudCredentials != null && BambuStudioPath != null;

            private static void LoadFromEnvironmentRaw()
            {
                var bsCredEmail = Environment.GetEnvironmentVariable("BAMBU_CLOUD_EMAIL");
                var bsCredPassword = Environment.GetEnvironmentVariable("BAMBU_CLOUD_PASSWORD");
                var bsPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_PATH");
                var bsExePath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_EXECUTABLE_PATH");
                var bsMachinesPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_MACHINES_PATH");

                BambuCloudCredentials = bsCredEmail != null && bsCredPassword != null
                        ? new BambuCloudCredentials(bsCredEmail, bsCredPassword)
                        : null;

                BambuStudioPath = bsPath;
                BambuStudioExecutablePath = bsExePath;
                machinesConfigPath = bsMachinesPath;
            }

            public static void LoadFromEnvironment()
            {
                LoadFromEnvironmentRaw();

                BambuStudioExecutablePath ??= Path.Join(BambuStudioPath, "BambuStudio");
                Machines = MachineCollection.Parse(machinesConfigPath ?? throw new InvalidOperationException($"{nameof(Machines)} is required"));
            }

            public static void LoadFromFile(string path)
            {
                LoadFromEnvironmentRaw();
                using var farmConfigFile = File.OpenRead(path);
                var jsonConfig = JsonDocument.Parse(farmConfigFile).RootElement;

                BambuCloudCredentials ??= JsonSerializer.Deserialize<BambuCloudCredentials>(
                    element: jsonConfig.GetProperty("bambuCloudCredentials"),
                    options: Config.SerializerOptions
                );

                if (BambuStudioPath == null && jsonConfig.TryGetProperty("bambuStudioPath", out var bambuStudioPathElem))
                {
                    BambuStudioPath = bambuStudioPathElem.GetString();
                }

                if (BambuStudioExecutablePath == null && jsonConfig.TryGetProperty("bambuStudioExePath", out var bambuStudioExePathElem))
                {
                    BambuStudioExecutablePath = bambuStudioExePathElem.GetString();
                }

                if (jsonConfig.TryGetProperty("machines", out var machinesElem))
                {
                    Machines = MachineCollection.Parse(machinesElem);
                }
                else
                {
                    throw new InvalidOperationException($"{nameof(Machines)} is required");
                }
            }
        }
    }
}
