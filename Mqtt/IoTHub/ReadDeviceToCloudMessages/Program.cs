using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;
using System.Threading;

namespace ReadDeviceToCloudMessages
{
    class Program
    {
        static string connectionString = "";
        static string monitoringEndpointName = "";
        static EventHubClient eventHubClient;

        static void Main(string[] args)
        {
            Console.WriteLine("Monitoring. Press Enter key to exit.\n");

            eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, monitoringEndpointName);
            var d2cPartitions = eventHubClient.GetRuntimeInformation().PartitionIds;
            CancellationTokenSource cts = new CancellationTokenSource();
            var tasks = new List<Task>();

            foreach (string partition in d2cPartitions)
            {
                tasks.Add(ReceiveMessagesFromDeviceAsync(partition, cts.Token));
            }

            Console.ReadLine();
            Console.WriteLine("Exiting...");
            cts.Cancel();
            Task.WaitAll(tasks.ToArray());
        }

        private static async Task ReceiveMessagesFromDeviceAsync(string partition, CancellationToken ct)
        {
            var eventHubReceiver = eventHubClient.GetDefaultConsumerGroup().CreateReceiver(partition, DateTime.UtcNow);
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    await eventHubReceiver.CloseAsync();
                    break;
                }

                EventData eventData = await eventHubReceiver.ReceiveAsync(new TimeSpan(0, 0, 10));

                if (eventData != null)
                {
                    string data = Encoding.UTF8.GetString(eventData.GetBytes());
                    Console.WriteLine("Message received. Partition: {0} Data: '{1}'", partition, data);
                }
            }
        }
    }

    /*
    class Program
    {
        static string connectionString = "";
        static string iotHubD2cEndpoint = "messages/events";
        static EventHubClient eventHubClient;

        private static async Task ReceiveMessagesFromDeviceAsync(string partition, CancellationToken ct)
        {
            var eventHubReceiver = eventHubClient.GetDefaultConsumerGroup().CreateReceiver(partition, DateTime.UtcNow);
            while (true)
            {
                if (ct.IsCancellationRequested) break;
                EventData eventData = await eventHubReceiver.ReceiveAsync();
                if (eventData == null) continue;

                string data = Encoding.UTF8.GetString(eventData.GetBytes());
                Console.WriteLine("Message received. Partition: {0} Data: '{1}'", partition, data);
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Receive messages. Ctrl-C to exit.\n");
            eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, iotHubD2cEndpoint);

            var d2cPartitions = eventHubClient.GetRuntimeInformation().PartitionIds;

            CancellationTokenSource cts = new CancellationTokenSource();

            System.Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Exiting...");
            };

            var tasks = new List<Task>();
            foreach (string partition in d2cPartitions)
            {
                tasks.Add(ReceiveMessagesFromDeviceAsync(partition, cts.Token));
            }

            Task.WaitAll(tasks.ToArray());
        }
    }
    */
}
