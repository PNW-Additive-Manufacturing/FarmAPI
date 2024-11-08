using FarmAPI.Machines.BambuLab;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using FarmAPI.Slicing;

namespace FarmAPI.Machines.ELEGOO
{
    public record ELEGOOMachineConfiguration(string IPAddress, string Model, string? Nickname = null) : MachineConfiguration(MachineConfigurationKind.ELEGOO);

    internal class ELEGOOMachineUDPCommands
    {
        public static string DiscoveryCommand => "M99999";
    }

    public class ELEGOOMachine : Machine, IMachineFilamentMutable, IDisposable
    {
        public override MachineTechnology Technology { get; }

        public override string Brand => "ELEGOO";

        public override string Model { get; }

        public override MachineSize Size { get; }


        private Filament? FilamentInTank;

        // TODO: Do not create new dictionaries every call on filaments.
        public override IDictionary<FilamentLocation, Filament> Filaments
        {
            get
            {
                if (FilamentInTank == null)
                {
                    return new Dictionary<FilamentLocation, Filament>();
                }
                else return new Dictionary<FilamentLocation, Filament>()
                {
                    { FilamentLocation.External(), FilamentInTank.Value } 
                };
            }
        }

        private readonly IPEndPoint MachineIPEndpoint;

        private readonly UdpClient UdpClient;

        private readonly CancellationTokenSource updateIntervalCancellationToken = new();

        public ELEGOOMachine(ELEGOOMachineConfiguration configuration)
        {
            this.Nickname = configuration.Nickname;
            this.Model = configuration.Model;

            this.MachineIPEndpoint = new IPEndPoint(IPAddress.Parse(configuration.IPAddress), 3000);
            this.UdpClient = new UdpClient();
            this.UdpClient.Client.ReceiveTimeout = 5000;
            this.UdpClient.Client.SendTimeout = 5000;

            switch(this.Model)
            {
                case "Mars 4 Ultra":
                {
                    this.Size = new MachineSize(153, 77, 165);
                    this.Technology = MachineTechnology.SLA;
                    break;
                }
                default:
                    throw new NotSupportedException($"ELEGOO Machine: {this.Model} is not supported!");
            };

            Console.WriteLine($"Registered ELEGOO machine: {this.Model}");

            Task.Run(async () =>
            {
                while (!updateIntervalCancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await this.Update();
                    }
                    catch (Exception ex)
                    {
                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Failed to fetch new information from ELEGOO Machine \"{this.Identifier}\":\n{ex}");
                            Console.ResetColor();
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            });
        }

        protected async Task<JsonDocument> SendDiscoverRequest()
        {
            byte[] blobDiscoveryCommand = Encoding.ASCII.GetBytes(ELEGOOMachineUDPCommands.DiscoveryCommand);

            // Send UDP Discovery
            try
            {
                await UdpClient.SendAsync(blobDiscoveryCommand, blobDiscoveryCommand.Length, this.MachineIPEndpoint);
                byte[] receiveBytes = (await UdpClient.ReceiveAsync()).Buffer;

                return JsonDocument.Parse(receiveBytes);
            }
            catch (JsonException jsonEx)
            {
                throw new Exception("Unable to parse the data received by ELEGOO Machine", jsonEx);
            }
            catch (SocketException socketEx)
            {
                throw new Exception("Unable to connect to ELEGOO Machine", socketEx);
            }
        }

        public override async Task Update()
        {
            JsonDocument machineDiscovery = await SendDiscoverRequest();
            JsonElement machineData = machineDiscovery.RootElement.GetProperty("Data").GetProperty("Status");

            int currentStatus = machineData.GetProperty("CurrentStatus").GetInt32();
            if (currentStatus == 1)
            {
                JsonElement printingStatus = machineData.GetProperty("PrintInfo");
                int printErrorCode = printingStatus.GetProperty("ErrorNumber").GetInt32();
                if (printErrorCode != 0)
                {
                    this.FailReason = $"Error Code: {printErrorCode}";
                }

                // ELEGOO is Printing
                this.Status = MachineState.Printing;
                this.Filename = printingStatus.GetProperty("Filename").GetString();

                int currentTicks = printingStatus.GetProperty("CurrentTicks").GetInt32();
                int totalTicks = printingStatus.GetProperty("TotalTicks").GetInt32();
                this.Progress = (currentTicks / totalTicks) * 100;
                this.TimeRemaining = (totalTicks - currentStatus) / 60000;
            }
            else if (currentStatus == 0 && this.Status == MachineState.Printing)
            {
                this.Status = MachineState.Printed;
                this.Progress = 100;
                this.TimeRemaining = 0;
            }
            else if (currentStatus == 0)
            {
                this.Status = MachineState.Idle;
            }
            else
            {
                this.Status = MachineState.Unknown;
            }

            this.LastUpdated = DateTime.Now;
        }

        public override Task MarkAsBedCleared()
        {
            this.Status = MachineState.Idle;
            return Task.CompletedTask;
        }

        public Task SetFilament(Filament? filament)
        {
            this.FilamentInTank = filament;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            this.updateIntervalCancellationToken.Cancel();
            this.UdpClient.Dispose();
        }
    }
}
