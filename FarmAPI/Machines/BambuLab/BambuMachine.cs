
using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using FarmAPI.Slicing;
using FarmAPI.Slicing.BambuStudio;
using FluentFTP;
using FluentFTP.GnuTLS;

namespace FarmAPI.Machines.BambuLab
{
    public record BambuMachineConfiguration(string SerialNumber, string AccessCode, string IPAddress, string? Nickname = null) : MachineConfiguration(MachineConfigurationKind.BambuLab);

    public class BambuMachine : Machine, IMachineSliceable, IMachinePrintable, IMachineControllable
    {
        public string SerialNumber { get; }

        // BambuLab does not offer any other machine than FDM.
        public override MachineTechnology Technology { get; } = MachineTechnology.FDM;

        public string AccessCode { get; }

        public string IPAddress { get; }

        public bool HasSDCard { get; protected set; }

        public override string Brand => "BBL";

        public override string Model { get; }

        public override MachineSize Size { get; }

        public Dictionary<int, Filament[]> FilamentsInAMS { get; protected set; } = new(4);

        public Filament? ExternalFilament { get; protected set; }

        public BambuMachine(string serialNumber, string accessCode, string ipAddress, string? nickname = null)
        {
            this.SerialNumber = serialNumber;
            this.Model = GetModel(this.SerialNumber);

            this.AccessCode = accessCode;
            this.IPAddress = ipAddress;
            this.Nickname = nickname;
            this.Size = GetBedSize(this.Model);

            _ = MQTTConnectionPool.Listen(serialNumber, (sender, report) => this.Update(report));
        }

        public BambuMachine(BambuMachineConfiguration config) : this(config.SerialNumber, config.AccessCode, config.IPAddress, config.Nickname) { }

        private void Update(JsonDocument recivedData)
        {
            if (!recivedData.RootElement.TryGetProperty("print", out var printElement))
            {

                //Debug("Unexpected data", ConsoleColor.Red);
                //Console.ForegroundColor = ConsoleColor.DarkGray;
                //Console.WriteLine(recivedData.RootElement);
                //Console.ResetColor();
                return;
            }

            LastUpdated = DateTime.Now;

            //Debug("Recived data");
            //Console.ForegroundColor = ConsoleColor.DarkGray;
            //Console.WriteLine(recivedData.RootElement);
            //Console.ResetColor();

            try
            {
                MachineState prevState = Status;
                if (printElement.TryGetProperty("gcode_state", out var updatedStateElem))
                {
                    Status = updatedStateElem.GetString()!.ToLower() switch
                    {
                        "idle" => MachineState.Idle,
                        "running" => MachineState.Printing,
                        "pause" => MachineState.Paused,
                        "finish" => MachineState.Printed,
                        "failed" => MachineState.Error,
                        _ => MachineState.Unknown,
                    };

                }

                // https://github.com/greghesp/ha-bambulab/blob/main/custom_components/bambu_lab/pybambu/const.py#L37
                if (Status == MachineState.Printing 
                    && printElement.TryGetProperty("stg_cur", out var stageElem))
                {
                    int stage = stageElem.GetInt32();

                    if (stage == -1 || stage == 255)
                    {
                        // Do nothing, in idle mode!
                    }
                    else if (stage == 0)
                    {
                        Status = MachineState.Printing;
                    }
                    else
                    {
                        Status = MachineState.Preparing;
                    }

                    Debug(stageElem.GetInt32().ToString());
                }

                bool isStateModified = prevState != Status;
                if (isStateModified)
                {
                    Debug($"Machine has transitioned from {prevState} to {Status}");
                }

                // If the machine is printing/paused, check if we can update the progress and time remaining.
                {
                    if (printElement.TryGetProperty("mc_percent", out var progressElem))
                    {
                        var _progress = progressElem.GetInt32();
                        if (_progress != Progress)
                        {
                            Progress = _progress;

                            //Debug($"Updating progress to {Progress}%");
                        }
                    }

                    if (printElement.TryGetProperty("mc_remaining_time", out var timeRemainingElem))
                    {
                        var _timeRemaining = timeRemainingElem.GetInt32();
                        if (TimeRemaining != _timeRemaining)
                        {
                            TimeRemaining = _timeRemaining;

                            // Debug($"Updating time remaining to {TimeRemaining} minutes");
                        }
                    }

                    if (printElement.TryGetProperty("subtask_name", out var filenameElem))
                    {
                        string pFilename = filenameElem.GetString()!;
                        if (!string.IsNullOrWhiteSpace(pFilename) && pFilename != Filename)
                        {
                            Filename = pFilename;

                            //Debug($"Updating filename to {Filename}");
                        }
                    }
                }

                //if (printElement.TryGetProperty("layer_num", out var layerNumbElem))
                //{
                //    var layerNumber = layerNumbElem.GetInt32();
                //    if (layerNumber != PrintStage)
                //    {
                //        LayerNumber = layerNumber;

                //        Debug($"Updating layer number to {LayerNumber}");
                //    }
                //}

                if (printElement.TryGetProperty("sdcard", out var sdCardElem))
                {
                    this.HasSDCard = sdCardElem.GetBoolean();
                }

                if (prevState == MachineState.Printing && isStateModified)
                {
                    // Clear the progress and time remaining when the print has finished.
                    Progress = null;
                    TimeRemaining = null;

                    //Debug("Progress has been cleared.");
                }

                if (prevState == MachineState.Error && isStateModified)
                {
                    FailReason = null;
                }

                if (Status == MachineState.Error && printElement.TryGetProperty("fail_reason", out var failReasonElem))
                {
                    string reason = failReasonElem.GetString()!;

                    // 50348044 is canceled by user.

                    //if (string.IsNullOrEmpty(reason))
                    //{
                    //    Debug("Machine has failed with an unknown reason. Switching to Idle!");

                    //    State = BambuMachineState.Idle;
                    //}

                    // fail_reason is "0" when an error has not occurred.
                    if (!string.Equals(reason, "0"))
                    {
                        //Debug($"Machine has failed with reason {reason}");

                        FailReason = reason;
                    }
                }
            }

            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(this.ToString());
                Console.Error.WriteLine($" An issue occurred updating machine state!\n{ex}");
                Console.ResetColor();
            }

            try
            {
                if (printElement.TryGetProperty("ams", out var amsElement))
                {
                    // For an unknown reason, the ams object is nested in another ams object.
                    if (amsElement.TryGetProperty("ams", out var innnerAMSElement))
                    {
                        foreach (var ams in innnerAMSElement.EnumerateArray())
                        {
                            int amsId = int.Parse(ams.GetProperty("id").GetString()!);

                            List<Filament> filamentsInAMS = new(4);

                            foreach (var trayEntry in ams.GetProperty("tray").EnumerateArray())
                            {
                                if (trayEntry.TryGetProperty("tray_type", out var trayType))
                                {
                                    Filament filamentInSlot = new(trayType.GetString()!, trayEntry.GetProperty("tray_color").GetString()!);

                                    filamentsInAMS.Add(filamentInSlot);
                                }
                            }
                            FilamentsInAMS[amsId] = [.. filamentsInAMS];
                        }
                    }
                    else
                    {
                        Debug($"Unexpected AMS object structure!\n{amsElement}", ConsoleColor.Yellow);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(this.ToString());
                Console.Error.WriteLine($" An issue occurred updating AMS information!\n{ex}");
                Console.ResetColor();
            }

            try
            {
                // Sometimes the VT tray is present even with AMS? Only AMS would be able to be used then.
                if (printElement.TryGetProperty("vt_tray", out var vtTrayElem))
                {
                    // The VT tray (virtual tray/slot) is the external filament holder.
                    ExternalFilament = new Filament(vtTrayElem.GetProperty("tray_type").GetString()!, vtTrayElem.GetProperty("tray_color").GetString()!);

                    //Debug($"Updating information for VT Tray to {ExternalFilament}");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(this.ToString());
                Console.Error.WriteLine($" An issue occurred updating VT Tray information!\n{ex}");
                Console.ResetColor();
            }
        }

        private void Debug(string content, ConsoleColor color = ConsoleColor.Green)
        {
            Console.ForegroundColor = color;
            Console.Write(this.Identifier);
            Console.Write(" ");
            Console.ResetColor();
            Console.WriteLine(content);
        }

        public override Task Update()
        {
            // This is an optional method of the IMachine interface. It is not required to be implemented.
            // await MQTTConnectionPool.RebaseMachine(this.SerialNumber);
            return Task.CompletedTask;
        }

        [JsonConverter(typeof(MachineFilamentsWithLocationConverter))]
        public override IDictionary<FilamentLocation, Filament> Filaments
        {
            get
            {
                var loadedFilaments = new Dictionary<FilamentLocation, Filament>();

                if (FilamentsInAMS.Count != 0)
                {
                    foreach (var (amsId, filaments) in FilamentsInAMS)
                    {
                        for (int i = 0; i < filaments.Length; i++)
                        {
                            loadedFilaments.Add(FilamentLocation.InAMS(amsId, i), filaments[i]);
                        }
                    }
                }
                else if (ExternalFilament.HasValue)
                {
                    loadedFilaments.Add(FilamentLocation.External(), ExternalFilament.Value);
                }

                return loadedFilaments;
            }
        }

        protected async Task<AsyncFtpClient> CreateFTPConnection()
        {
            var ftpClient = new AsyncFtpClient(this.IPAddress, "bblp", this.AccessCode, 990, new FtpConfig()
            {
                EncryptionMode = FtpEncryptionMode.Explicit,
                CustomStream = typeof(GnuTlsStream),
                CustomStreamConfig = new GnuConfig(),
                ValidateAnyCertificate = true,
                //ConnectTimeout = 5000,
            });

            //ftpClient.Config.LogToConsole = true;
            await ftpClient.AutoConnect();
            return ftpClient;
        }

        protected async Task Upload3MF(Stream dataStream, string filename)
        {
            if (!HasSDCard)
            {
                throw new Exception($"Unable to upload, machine {this} does not have an SD Card!");
            }

            var client = await CreateFTPConnection();
            var uploadTask = await client.UploadStream(dataStream, $"/{Path.ChangeExtension(filename, ".3mf")}");

            // Yay we "completed" in time!
            if (uploadTask != FtpStatus.Success)
            {
                throw new InvalidOperationException($"An issue occurred uploading 3MF to {this}: {Enum.GetName(uploadTask)}");
            }
        }

        public async Task<Stream> Slice(Stream modelStream, string fileName, SlicingOptions slicingOptions)
        {
            if (slicingOptions is not SlicingOptions.FDM FDMOptions)
            {
                throw new ArgumentException("Options must be FDM!", nameof(slicingOptions));
            }

            string modelFilePath = Paths.GetTempFileName("stl");
            using (var modelFile = File.OpenWrite(modelFilePath))
            {
                // Copy content into a file which Bambu Studio can use.
                await modelStream.CopyToAsync(modelFile);
            }

            var slicedFilePath = await Slicers.BambuStudio().Slice(this.Brand, this.Model, modelFilePath, FDMOptions);
            return File.OpenRead(slicedFilePath);
        }

        public async Task<SlicedMetadata> ReadMetadata(Stream slicedStream)
        {
            return await Slicers.BambuStudio().ParseMetadata(slicedStream);
        }

        public async Task Print(Stream dataStream, string filename, FilamentLocation filament)
        {

            Debug($"Printing {filename} using filament {filament}...", ConsoleColor.Yellow);
            await Upload3MF(dataStream, filename);

            Debug($"Sending to {this} as {filename}!");

            var printCommand = BambuMQTTCommands.Print(Path.ChangeExtension(filename, "3mf"), filament);

            await MQTTConnectionPool.Request(this.SerialNumber, printCommand);
        }

        public override Task MarkAsBedCleared()
        {
            if (this.Status == MachineState.Printed || this.Status == MachineState.Error)
            {
                this.Status = MachineState.Idle;
            }
            throw new NotSupportedException("Machine status must be Printing or Errored!");
        }

        public Task Stop()
        {
            return MQTTConnectionPool.Request(this.SerialNumber, BambuMQTTCommands.Stop);
        }

        public Task Resume()
        {
            return MQTTConnectionPool.Request(this.SerialNumber, BambuMQTTCommands.Resume);
        }

        public Task Pause()
        {
            return MQTTConnectionPool.Request(this.SerialNumber, BambuMQTTCommands.Pause);
        }

        public static string GetModel(string serialNumber)
        {
            return serialNumber[..3] switch
            {
                "00M" => "X1C",
                "00W" => "X1",
                "03W" => "X1E",
                "01S" => "P1P",
                "01P" => "P1S",
                "030" => "A1M",
                "039" => "A1",
                _ => throw new Exception($"Unknown model of {serialNumber} ({serialNumber[..3]})! Is the serial number correct?")
            };
        }

        public static MachineSize GetBedSize(string model)
        {
            return model switch
            {
                "A1M" => new MachineSize(180, 180, 180),
                _ => new MachineSize(256, 256, 256)
            };
        }
    }
}
