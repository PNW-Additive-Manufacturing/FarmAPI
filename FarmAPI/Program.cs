using FarmAPI.Machines;
using FarmAPI.Machines.BambuLab;
using FarmAPI.Slicing;
using FarmAPI.Slicing.BambuStudio;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Principal;
using System.Text.Json;
using System.Xml;

namespace FarmAPI
{
    public partial class Program
    {
        public static async Task Main(string[] args)
        {
            var farmConfigPath = Environment.GetEnvironmentVariable("FARMAPI_CONFIG_PATH") ?? Path.Join(Environment.CurrentDirectory, "config.json");
            if (farmConfigPath != null && File.Exists(farmConfigPath))
            {
                Console.WriteLine("Using Configuration File");
                FarmAPIConfiguration.LoadFromFile(farmConfigPath);
            }
            else
            {
                Console.WriteLine("Using Environment Variables");
                FarmAPIConfiguration.LoadFromEnvironment();
            }

            if (FarmAPIConfiguration.Machines.Machines.Any(m => m.Value is BambuMachine))
            {
                try
                {
                    await ConnectToBambuMachines();
                }
                catch (Exception ex)
                {
                    throw new Exception("Cannot connect to Bambu Machines:", ex);
                }
            }

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
                return Results.Json(FarmAPIConfiguration.Machines.Machines, Config.SerializerOptions);

            }).WithName("Printers").WithOpenApi();

            app.Map("/updateAll", async () =>
            {
                // Update ALL Machines
                await Task.WhenAll(FarmAPIConfiguration.Machines.Machines.Select(m => m.Value.Update()));

                return JsonSerializer.Serialize(FarmAPIConfiguration.Machines.Machines, Config.SerializerOptions);

            }).WithOpenApi();

            app.MapPost("/printers/{identity}/slice/info", async ([FromBody] Stream stream, [FromRoute] string identity, [FromQuery] string fileName, [FromQuery] string material, [FromQuery] string colorHex, [FromQuery] float layerHeight, [FromQuery] bool useSupports, [FromQuery] string? supportStyle = null, [FromQuery] int quantity = 1, [FromQuery] int? wallLoops = null) =>
            {
                var targetFilament = new Filament(material, colorHex);

                if (!FarmAPIConfiguration.Machines.TryGetMachine(identity, out var machine))
                {
                    return Results.NotFound(new { Succes = false });
                }

                SlicedMetadata slicedMetadata;

                if (machine is IMachineSliceable machineSliceable)
                {
                    try
                    {
                        slicedMetadata = await IMachineSliceable.SliceThenReadMetadata(machineSliceable, stream, fileName, targetFilament, new SlicingOptions.FDM(
                        material,
                        quantity,
                        layerHeight,
                        wallLoops,
                        useSupports,
                        supportStyle));
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(new
                        {
                            Succes = false,
                            ex.Message
                        });
                    }
                }
                else
                {
                    return Results.Json(new
                    {
                        Success = false,
                        Message = "Machine does not support the SLICEABLE feature!"
                    });
                }

                return Results.Json(new
                {
                    Success = true,
                    slicedMetadata.Duration,
                    WeightInGrams = slicedMetadata.Weight
                });
            });

            app.MapPost("/printers/{identity}/markAsCleared", async ([FromRoute] string identity) =>
            {
                Console.WriteLine("Marking as cleared!");

                if (!FarmAPIConfiguration.Machines.TryGetMachine(identity, out var machine)) return Results.NotFound(new 
                { 
                    Succes = false 
                });

                try
                {
                    await machine.MarkAsBedCleared();
                }
                catch (Exception ex)
                {
                    return Results.Json(new
                    {
                        Success = false,
                        ex.Message
                    });
                }

                return Results.Json(new
                {
                    Success = true
                });
            });

            app.MapPost($"/printers/print/{Enum.GetName(MachineTechnology.FDM)}", async ([FromBody] Stream stream, [FromQuery] string fileName, [FromQuery] string material, [FromQuery] string colorHex, [FromQuery] float layerHeight, [FromQuery] bool useSupports, [FromQuery] string? supportStyle = null, [FromQuery] int quantity = 1, [FromQuery] int? wallLoops = null) => 
            {
                Console.WriteLine($"Requested to print {fileName} with {material} and {colorHex}!");
                var targetFilament = new Filament(material, colorHex);

                try
                {
                    var machine = FarmAPIConfiguration.Machines.FindAvailableMachineWithFilament(MachineTechnology.FDM, targetFilament);
                    if (!machine.HasValue)
                    {
                        return Results.Json(new
                        {
                            Success = false,
                            Message = "No available machines with matching filament!"
                        });
                    }

                    Stream slicedData;

                    if (machine.Value.Item1 is IMachineSliceable machineSliceable)
                    {
                        slicedData = await machineSliceable.Slice(stream, fileName, new SlicingOptions.FDM(
                            material,
                            quantity,
                            layerHeight,
                            wallLoops,
                            useSupports,
                            supportStyle));
                    }
                    else
                    {
                        return Results.Json(new
                        {
                            Success = false,
                            Message = "Machine does not support the SLICEABLE feature!"
                        });
                    }

                    if (machine.Value.Item1 is IMachinePrintable machinePrintable)
                    {
                        await machinePrintable.Print(slicedData, fileName, machine.Value.Item2);
                    }
                    else
                    {
                        return Results.Json(new
                        {
                            Success = false,
                            Message = "Machine does not support the PRINTABLE feature!"
                        });
                    }

                    return Results.Json(new
                    {
                        Success = true,
                        Printer = machine.Value.Item1.Identifier,
                    });
                }
                catch (Exception ex)
                {
                    return Results.Json(new
                    {
                        Success = false,
                        ex.Message
                    });
                }
            });

            app.MapPost("/printers/{identity}/setFilament", async ([FromRoute] string identity, [FromQuery(Name = "material")] string? material, [FromQuery(Name = "color")] string? color) =>
            {
                if (!FarmAPIConfiguration.Machines.TryGetMachine(identity, out var machine) || machine is not IMachineFilamentMutable machineWithMutableFilament)
                {
                    return Results.NotFound();
                }

                await machineWithMutableFilament.SetFilament(material != null && color != null ? new Filament(material, color) : null);

                return Results.Json(new {
                    Success = true
                });
            });

            app.MapGet("/printers/find/{technology}", ([FromRoute] MachineTechnology technology) =>
            {
                return Results.Json(FarmAPIConfiguration.Machines.Machines.Where(m => m.Value.Technology == technology));
            });

            app.MapGet("/printers/find/with-filename", ([FromQuery] string fileName) =>
            {
                string[] fileNames = fileName.Split(',');

                Machine? foundMachine = FarmAPIConfiguration.Machines.Machines.FirstOrDefault(m => fileNames.Contains(m.Value.Filename, StringComparer.OrdinalIgnoreCase)).Value;

                return foundMachine == null ? Results.NotFound() : Results.Json(foundMachine);
            });

            app.MapGet("/printers/find/{technology}/with-filament", ([FromRoute] MachineTechnology technology, [FromQuery(Name = "material")] string material, [FromQuery(Name = "color")] string color) =>
            {
                Filament targetFilament = new(material, color);

                var availableMachine = FarmAPIConfiguration.Machines.FindAvailableMachineWithFilament(technology, targetFilament);

                return Results.Json(availableMachine.HasValue ? availableMachine!.Value.Item1 : null);
            });

            app.Run();
        }
    
        public static async Task ConnectToBambuMachines()
        {
            // Setup the BambuLab MQTTConnectionPool with credentials from the Farm Config.
            var credentials = await BambuCloudUtilities.FetchMQTTCredentials(FarmAPIConfiguration.BambuCloudCredentials ?? throw new Exception("BambuCloudCredentials must be provided!"));

            await MQTTConnectionPool.Connect(credentials);
            await MQTTConnectionPool.RebaseMachines();       
        }
    }
}
