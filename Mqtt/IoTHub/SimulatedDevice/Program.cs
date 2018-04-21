using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;

namespace SimulatedDevice
{
    class Program
    {
        static DeviceClient deviceClient;
        static string iotHubUri = "opcf-prototype-iothub.azure-devices.net";
        static string deviceId = "mqtt-prototype-sharedkeys";
        static string deviceKey = "8xW/ttOlWLiwktSCrHtZNvlk2pj9XdizYniMtdqoSeQ=";

        private static async void SendDeviceToCloudMessagesAsync()
        {
            double minTemperature = 20;
            double minHumidity = 60;
            int messageId = 1;
            Random rand = new Random();

            while (true)
            {
                double currentTemperature = minTemperature + rand.NextDouble() * 15;
                double currentHumidity = minHumidity + rand.NextDouble() * 20;

                var telemetryDataPoint = new
                {
                    messageId = messageId++,
                    deviceId = deviceId,
                    temperature = currentTemperature,
                    humidity = currentHumidity
                };
                var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
                var message = new Message(Encoding.ASCII.GetBytes(messageString));
                message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");

                await deviceClient.SendEventAsync(message);
                Console.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);

                await Task.Delay(1000);
            }
        }

        static void Main(string[] args)
        {
            // var key = new DeviceAuthenticationWithX509Certificate(deviceId, new System.Security.Cryptography.X509Certificates.X509Certificate2(@"C:\Work\opcuanet\pki\own\private\mqtt-prototype-redopal [9EBEC9414DC8A9FA990907761FC3CFCFEB7F57D1].pfx"));
            var key = new DeviceAuthenticationWithRegistrySymmetricKey(deviceId, deviceKey);
            Console.WriteLine("Simulated device\n");
            deviceClient = DeviceClient.Create(iotHubUri, key, TransportType.Mqtt);
            deviceClient.ProductInfo = "HappyPath_Simulated-CSharp";
            SendDeviceToCloudMessagesAsync();
            Console.ReadLine();
        }
    }
}
