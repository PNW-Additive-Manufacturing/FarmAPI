using FarmAPI.Machines;
using FarmAPI.Machines.BambuLab;
using FarmAPI.Slicing.BambuStudio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace FarmAPI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var farmConfigPath = Environment.GetEnvironmentVariable("FARMAPI_CONFIG_PATH") ?? Path.Join(Environment.CurrentDirectory, "config.json");

            FarmAPIConfiguration farmConfig;
            if (farmConfigPath != null && File.Exists(farmConfigPath))
            {
                Console.WriteLine("Using Configuration File");
                farmConfig = FarmAPIConfiguration.LoadFromFile(farmConfigPath);
            }
            else
            {
                Console.WriteLine("Using Environment Variables");
                farmConfig = FarmAPIConfiguration.LoadFromEnvironment();
            }

            if (farmConfig.Machines.Any(m => m is BambuMachine))
            {
                // Setup the BambuLab MQTTConnectionPool with credentials from the Farm Config.
                var credentials = await BambuCloudUtilities.FetchMQTTCredentials(farmConfig.BambuCloudCredentials ?? throw new Exception("BambuCloudCredentials must be provided!"));
                
                await MQTTConnectionPool.Connect(credentials);
                await MQTTConnectionPool.RebaseMachines();
            }

            var slicer = new BambuStudio(
                baseDir: farmConfig.BambuStudioPath,
                executablePath: farmConfig.BambuStudioExecutablePath,
                resourcesPath: farmConfig.BambuStudioResourcesPath
            );

            var builder = WebApplication.CreateBuilder(args);

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.Configure<KestrelServerOptions>(options =>
            {
                // This must be set to true due to ZipArichve not supporting async operations.
                options.AllowSynchronousIO = true;
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(); 
            }

            app.Map("/printers", () =>
            {
                return JsonSerializer.Serialize(farmConfig.Machines.Machines, Config.SerializerOptions);

            }).WithName("Printers").WithOpenApi();

            app.MapPost("/slicing/slice/info", async ([FromBody] Stream stream, [FromQuery] string fileName, [FromQuery] string manufacturer, [FromQuery] string model, [FromQuery] string material, [FromQuery] string colorHex, [FromQuery] float layerHeight, [FromQuery] bool? useSupports = null, [FromQuery] string? supportStyle = null, [FromQuery] int quantity = 1, [FromQuery] int? wallLoops = null) =>
            {
                var targetFilament = new Filament(material, colorHex);

                string modelFilePath = Paths.GetTempFileName("stl");
                using (var modelFile = File.OpenWrite(modelFilePath))
                {
                    await stream.CopyToAsync(modelFile);
                }

                // Slice using BambuSlicer! (Only option for now)
                var slicedFilePath = await slicer.Slice(manufacturer, model, modelFilePath, quantity, targetFilament.Material, layerHeight, useSupports, supportStyle, wallLoops);

                return Results.Ok(new
                {
                    success = true,
                    (await slicer.ParseMetadata(slicedFilePath)).Duration
                });
            });

            //app.MapPost("/slicing/info", async ([FromBody] Stream stream) => Results.Ok(await slicer.ParseMetadata(stream)));

            app.MapPost("/printers/{identity}/markAsCleared", async ([FromRoute] string identity) =>
            {
                Console.WriteLine("Marking as cleared!");

                if (!farmConfig.Machines.Machines.TryGetValue(identity, out var machine))
                {
                    return Results.NotFound();
                }

                try
                {
                    await machine.MarkAsBedCleared();
                }
                catch (Exception ex)
                {
                    return Results.NotFound(new
                    {
                        Success = false,
                        Message = ex.Message,
                    });
                }

                return Results.Ok(new
                {
                    Success = true
                });
            });

            app.MapPost("/printers/print", async ([FromBody] Stream stream, [FromQuery] string fileName, [FromQuery] string material, [FromQuery] string colorHex, [FromQuery] float layerHeight, [FromQuery] bool? useSupports = null, [FromQuery] string? supportStyle = null, [FromQuery] int quantity = 1, [FromQuery] int? wallLoops = null) => 
            {
                Console.WriteLine($"Requested to print {fileName} with {material} and {colorHex}!");
                var targetFilament = new Filament(material, colorHex);

                try
                {
                    var machine = farmConfig.Machines.FindAvailableMachineWithFilament(targetFilament);
                    if (!machine.HasValue)
                    {
                        return Results.Ok(new
                        {
                            Success = false,
                            Message = "No available machines with matching filament!"
                        });
                    }

                    string modelFilePath = Paths.GetTempFileName("stl");
                    using (var modelFile = File.OpenWrite(modelFilePath))
                    {
                        await stream.CopyToAsync(modelFile);
                    }

                    // Slice using BambuSlicer! (Only option for now)
                    var slicedFilePath = await slicer.Slice(machine.Value.Item1.Brand, machine.Value.Item1.Model, modelFilePath, quantity, targetFilament.Material, layerHeight, useSupports, supportStyle, wallLoops);

                    Console.WriteLine($"Printing {fileName} on {machine.Value.Item1.Identifier} at slot {machine.Value.Item2.GlobalSlot}");

                    // Send the sliced file to the machine.
                    using (var slicedFile = File.OpenRead(slicedFilePath))
                    {
                        await machine.Value.Item1.Print(slicedFile, fileName, machine.Value.Item2);
                    }

                    return Results.Ok(new
                    {
                        Success = true,
                        Printer = machine.Value.Item1.Identifier,
                    });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new
                    {
                        Success = false,
                        ex.Message
                    });
                }
            });

            app.MapGet("/printers/find-available", ([FromQuery(Name = "material")] string material, [FromQuery(Name = "color")] string color) =>
            {
                Filament targetFilament = new(material, color);

                var what = farmConfig.Machines.FindAvailableMachineWithFilament(targetFilament);

                return Results.Ok(what.HasValue ? what!.Value.Item1 : null);
            });

            app.Run();
        }

        public class FarmAPIConfiguration
        {
            private string? machinesConfigPath;

            public BambuCloudCredentials? BambuCloudCredentials { get; set; }

            public string BambuStudioPath { get; set; } = null!;

            public string BambuStudioExecutablePath { get; set; } = null!;

            public string BambuStudioResourcesPath { get; set; } = null!;

            public MachineCollection Machines { get; set; } = null!;

            private FarmAPIConfiguration() { }

            private static FarmAPIConfiguration LoadFromEnvironmentRaw()
            {
                var bsCredEmail = Environment.GetEnvironmentVariable("BAMBU_CLOUD_EMAIL");
                var bsCredPassword = Environment.GetEnvironmentVariable("BAMBU_CLOUD_PASSWORD");
                var bsPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_PATH");
                var bsExePath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_EXECUTABLE_PATH");
                var bsResourcesPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_RESOURCES_PATH");
                var bsMachinesPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_MACHINES_PATH");

                var e = new FarmAPIConfiguration
                {
                    BambuCloudCredentials = bsCredEmail != null && bsCredPassword != null
                        ? new BambuCloudCredentials(bsCredEmail, bsCredPassword)
                        : null,
                    BambuStudioPath = bsPath!,
                    BambuStudioExecutablePath = bsExePath!,
                    BambuStudioResourcesPath = bsResourcesPath!,
                    machinesConfigPath = bsMachinesPath
                };

                return e;
            }

            public static FarmAPIConfiguration LoadFromEnvironment()
            {
                var cfg = LoadFromEnvironmentRaw();
                if (cfg.BambuStudioPath == null)
                {
                    throw new InvalidOperationException($"{nameof(BambuStudioPath)} is required");
                }

                cfg.BambuStudioExecutablePath ??= Path.Join(cfg.BambuStudioPath, "BambuStudio");
                cfg.BambuStudioResourcesPath ??= Path.Join(cfg.BambuStudioPath, "resources");
                cfg.Machines = MachineCollection.Parse(
                    cfg.machinesConfigPath
                        ?? throw new InvalidOperationException($"{nameof(Machines)} is required")
                );

                return cfg;
            }

            public static FarmAPIConfiguration LoadFromFile(string path)
            {
                var env = LoadFromEnvironmentRaw();
                using var farmConfigFile = File.OpenRead(path);
                var jsonConfig = JsonDocument.Parse(farmConfigFile).RootElement;
                var cfg = new FarmAPIConfiguration();

                var bambuCloudCredentials = JsonSerializer.Deserialize<BambuCloudCredentials>(
                    element: jsonConfig.GetProperty("bambuCloudCredentials"),
                    options: Config.SerializerOptions
                );
                cfg.BambuCloudCredentials = bambuCloudCredentials;

                {
                    string? bsPath = env.BambuStudioPath;
                    if (bsPath == null && jsonConfig.TryGetProperty("bambuStudioPath", out var el))
                    {
                        bsPath = el.GetString();
                    }
                    if (bsPath == null)
                    {
                        throw new InvalidOperationException($"{nameof(BambuStudioPath)} is required");
                    }
                    cfg.BambuStudioPath = bsPath;
                }

                {
                    string bsExePath = env.BambuStudioExecutablePath;
                    if (bsExePath == null && jsonConfig.TryGetProperty("bambuStudioExePath", out var el))
                    {
                        bsExePath = el.GetString()
                            ?? throw new InvalidOperationException($"{nameof(BambuStudioExecutablePath)} is required");
                    }
                    bsExePath ??= Path.Join(env.BambuStudioPath ?? cfg.BambuStudioPath, "BambuStudio");
                    cfg.BambuStudioExecutablePath = bsExePath;
                }

                {
                    string bsResourcesPath = env.BambuStudioResourcesPath;
                    if (bsResourcesPath == null && jsonConfig.TryGetProperty("bambuStudioResourcesPath", out var el))
                    {
                        bsResourcesPath = el.GetString()
                            ?? throw new InvalidOperationException($"{nameof(BambuStudioResourcesPath)} is required");
                    }
                    bsResourcesPath ??= Path.Join(env.BambuStudioPath ?? cfg.BambuStudioPath, "resources");
                    cfg.BambuStudioResourcesPath = bsResourcesPath;
                }

                {
                    if (jsonConfig.TryGetProperty("machines", out var el))
                    {
                        cfg.Machines = MachineCollection.Parse(el);
                    }
                    else
                    {
                        throw new InvalidOperationException($"{nameof(Machines)} is required");
                    }
                }

                return cfg;
            }
        }
    }
}
