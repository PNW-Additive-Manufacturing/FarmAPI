using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Internal;
using MQTTnet.Server;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FarmAPI.Machines.BambuLab
{
    public struct MQTTCredentials
    {
        /// <summary>
        /// Username is prefixed with u_ and contains many numbers (An ID).
        /// </summary>
        public string Username;

        /// <summary>
        /// This is a session token which a user recives when authenticated.
        /// </summary>
        public string Token;
    }

    public static class MQTTConnectionPool

    {
        private static readonly IMqttClient mqttClient = new MqttFactory().CreateMqttClient();

        /// <summary>
        /// An event dictionary that is triggered when a report is received from a Bambu Machine via the Bambu Cloud.
        /// </summary>
        private static readonly Dictionary<string, EventHandler<JsonDocument>> OnReportCallbacks = [];

        static MQTTConnectionPool()
        {
            mqttClient.ApplicationMessageReceivedAsync += (message) =>
            {
                if (message.ApplicationMessage.Topic.EndsWith("/report"))
                {
                    string targetSerialNumber = message.ApplicationMessage.Topic.Split('/').ElementAt(1);

                    if (OnReportCallbacks.TryGetValue(targetSerialNumber, out var targetCallback))
                    {
                        var recivedData = JsonDocument.Parse(message.ApplicationMessage.PayloadSegment);

                        targetCallback(null, recivedData);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Recived message for {targetSerialNumber} but NONE registered!", nameof(MQTTConnectionPool));
                        Console.ResetColor();
                    }
                }
                return Task.CompletedTask;
            };

            mqttClient.ConnectedAsync += async (ev) =>
            {
                Console.WriteLine("Connected!");
                foreach (var serialNumber in OnReportCallbacks.Keys)
                {
                    Console.WriteLine("Subscribing to " + BambuMQTTTopics.Report(serialNumber));
                    await mqttClient.SubscribeAsync(BambuMQTTTopics.Report(serialNumber));
                }

                await RebaseMachines();
            };

            mqttClient.DisconnectedAsync += (ev) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Disconnected from Bambu Lab MQTT!");
                Console.ResetColor();
                return Task.CompletedTask;
            };
        }

        public static void EnsureConnected()
        {
            if (!mqttClient.IsConnected)
            {
                throw new InvalidOperationException("MQTT is not connected. Have you ran Connect()?");
            }
        }

        internal static void EnsureHasMachine(string serialNumber)
        {
            if (!OnReportCallbacks.ContainsKey(serialNumber))
            {
                throw new Exception($"Machine {serialNumber} has not been registered on the {nameof(MQTTConnectionPool)}!");
            }
        }

        /// <summary>
        /// Listen for a report from a specified Bambu Machine.
        /// </summary>
        public static async Task Listen(string serialNumber, EventHandler<JsonDocument> eventHandler)
        {
            OnReportCallbacks.Add(serialNumber, eventHandler);

            if (mqttClient.IsConnected)
            {
                await mqttClient.SubscribeAsync(BambuMQTTTopics.Report(serialNumber));
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Registered {serialNumber} on MQTT");
            Console.ResetColor();

            if (mqttClient.IsConnected)
            {
                // Rebase (PUSHALL) the machine seperately.
                await RebaseMachine(serialNumber);
            }
        }

        /// <summary>
        /// Sends a PUSHALL command to the specified Bambu Machine.
        /// </summary>
        public static async Task RebaseMachine(string serialNumber)
        {
            EnsureConnected(); 

            await Request(serialNumber, BambuMQTTCommands.PushAll);
        }

        public static async Task RebaseMachines()
        {
            EnsureConnected();

            await Task.WhenAll(OnReportCallbacks.Keys.Select(RebaseMachine).ToArray());
        }

        internal static async Task Request(string serialNumber, string content)
        {
            EnsureConnected();
            EnsureHasMachine(serialNumber);

            await mqttClient.PublishStringAsync(BambuMQTTTopics.Request(serialNumber), content);
        }

        public static async Task Connect(MQTTCredentials credentials)
        {
            // Initialize the MQTT connection pool.
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer("us.mqtt.bambulab.com", 8883)
                .WithCredentials(credentials.Username, credentials.Token)
                .WithTimeout(TimeSpan.FromSeconds(30))
                .WithTlsOptions(new MqttClientTlsOptions()
                {
                    UseTls = true
                })
               .Build();

            await mqttClient.ConnectAsync(options);
        }
    }

    internal static class BambuMQTTCommands
    {
        public static string PushAll { get; } = $"{{\"pushing\": {{\"sequence_id\": \"0\", \"command\": \"pushall\"}}}}";

        public static string GetVersion { get; } = $"{{\"info\": {{\"sequence_id\": \"0\", \"command\": \"get_version\"}}}}";

        public static string Print(string filename, FilamentLocation location, bool levelBed = true, bool vibration = true, bool flowCalibration = true, bool layerInspection = true)
        {
            return "{\"print\":{\"sequence_id\":0,\"command\":\"project_file\",\"param\":\"Metadata/plate_1.gcode\",\"subtask_name\":\"" + filename + "\",\"url\":\"ftp://" + filename + "\",\"timelapse\":false,\"bed_leveling\":" + (levelBed ? "true" : "false") + ",\"flow_cali\":" + (flowCalibration ? "true" : "false") + ",\"vibration_cali\":" + (vibration ? "true" : "false") + ",\"layer_inspect\":" + (layerInspection ? "true" : "false") + ",\"use_ams\":" + (location.IsInAMS ? "true" : "false") + ", \"ams_mapping\": [" + ((location.AMS * 3) + location.GlobalSlot) + "]}}";
        }
    }

    internal static class BambuMQTTTopics
    {
        public static string Request(string serialNumber) => $"device/{serialNumber}/request";
        public static string Report(string serialNumber) => $"device/{serialNumber}/report";
    }
}
