
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using uPLibrary.Networking.M2Mqtt;
using Mono.Options;

namespace MqttSamplePublisher
{
    public class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine((Utils.IsRunningOnMono() ? "Mono" : ".NET Core") + " OPC UA MQTT Publisher");

            bool showHelp = false;

            ApplicationStartSettings settings = new ApplicationStartSettings();

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => settings.AutoAccept = a != null },
                { "s|source=", "the source used for the data to publish.", s => settings.EndpointUrl = s },
                { "c|config=", "the configuration file to use.", c => settings.ConfigFile = c }
            };

            IList<string> extraArgs = null;

            try
            {
                extraArgs = options.Parse(args);

                if (extraArgs.Count > 1)
                {
                    foreach (string extraArg in extraArgs)
                    {
                        Console.WriteLine("Error: Unknown option: {0}", extraArg);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                // show some app description message
                Console.Write(Utils.IsRunningOnMono()?"Usage: mono " : "Usage: dotnet ");
                Console.WriteLine("MqttSamplePublisher.exe [OPTIONS]");
                Console.WriteLine();

                // output the options
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                Console.ReadKey();
                return -1;
            }
            
            try
            {
                Run(settings).Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exit due to Exception: {0}", e.Message);
            }

            Console.ReadKey();
            return -1;
        }

        public static async Task Run(ApplicationStartSettings settings)
        {
            PubSubApplication application = new PubSubApplication();
            application.LogMessage += Application_LogMessage;

            // start application.
            await application.Start(settings);

            // wait for timeout or Ctrl-C
            WaitForQuit();

            // stop application.
            await application.Stop();
        }

        private static void WaitForQuit()
        {
            using (ManualResetEvent quitEvent = new ManualResetEvent(false))
            {
                try
                {
                    Console.CancelKeyPress += (sender, eArgs) =>
                    {
                        eArgs.Cancel = true;
                    };
                }
                catch (Exception e)
                {
                    Console.WriteLine("[{0}] {1}", e.GetType().Name, e.Message);
                }

                quitEvent.WaitOne();
            }
        }

        private static void Application_LogMessage(object sender, LogMessageEventArgs e)
        {
            Console.WriteLine(e.Message);
        }
    }
}

/*
namespace MqttPublisher
{
    public class Program
    {
        public static int Main(string[] args)
        {
            // create client instance
            MqttClient client = new MqttClient("127.0.0.1");

            string clientId = Guid.NewGuid().ToString();
            int result = client.Connect(clientId);
            Console.WriteLine("Connect: {0}", result);

            client.MqttMsgPublished += Client_MqttMsgPublished;
            client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
            client.MqttMsgSubscribed += Client_MqttMsgSubscribed;
            client.MqttMsgUnsubscribed += Client_MqttMsgUnsubscribed;
            string strValue = Convert.ToString("Hello World2");

            // publish a message on "/home/temperature" topic with QoS 2
            result = client.Publish("/home/temperature", Encoding.UTF8.GetBytes(strValue), uPLibrary.Networking.M2Mqtt.Messages.MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, true);
            Console.WriteLine("Publish: {0}", result);

            client.Disconnect();
            Console.WriteLine("Disconnect");
        }

        private static void Client_MqttMsgUnsubscribed(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgUnsubscribedEventArgs e)
        {
            Console.WriteLine("Unsubscribe: {0}", e.MessageId);
        }

        private static void Client_MqttMsgSubscribed(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgSubscribedEventArgs e)
        {
            Console.WriteLine("Subscribe: {0}", e.MessageId);
        }

        private static void Client_MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            Console.WriteLine("Publish Received: {0} {1}", e.Topic, e.DupFlag);
        }

        private static void Client_MqttMsgPublished(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishedEventArgs e)
        {
            Console.WriteLine("Published: {0} {1}", e.MessageId, e.IsPublished);
        }
    }
}
*/