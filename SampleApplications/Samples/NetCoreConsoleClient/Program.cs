using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using System.Net;

namespace NetCoreConsoleClient
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(".NetCore Opc.Ua Console Client sample ");
            Task t = ConsoleSampleClient();
            t.Wait();
        }

        public static async Task ConsoleSampleClient()
        {
            Console.WriteLine("1 - Create an Application Configuration.");

            var config = new ApplicationConfiguration()
            {
                ApplicationName = "UA Sample Client",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = @"Directory",
                        StorePath = @".",
                        SubjectName = Utils.Format(@"CN={0}, DC={1}", "UA Sample Client", Dns.GetHostName())
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = @".",
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = @".",
                    },
                    NonceLength = 32,
                    AutoAcceptUntrustedCertificates = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };
            await config.Validate(ApplicationType.Client);

            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            Console.WriteLine("2 - Create a session with .Net UA Sample server.");
            string endpointURL = "opc.tcp://" + Dns.GetHostName() + ":51210/UA/SampleServer";
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, new EndpointDescription(endpointURL));

            var session = await Session.Create(config, endpoint, true, "", 60000, null, null);

            Console.WriteLine("3 - Browse the server namespace.");
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            references = session.FetchReferences(ObjectIds.ObjectsFolder);

            session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);

            Console.WriteLine(" DisplayName, BrowseName, NodeClass");
            foreach (var rd in references)
            {
                Console.WriteLine(" {0}, {1}, {2}", rd.DisplayName, rd.BrowseName, rd.NodeClass);
                ReferenceDescriptionCollection nextRefs;
                byte[] nextCp;
                session.Browse(
                    null,
                    null,
                    ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris),
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    out nextCp,
                    out nextRefs);

                foreach (var nextRd in nextRefs)
                {
                    Console.WriteLine("   + {0}, {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                }
            }

            Console.WriteLine("4 - Create a subscription with publishing interval of 1 second.");
            var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };

            Console.WriteLine("5 - Add a list of items (server current time and status) to the subscription.");
            var list = new List<MonitoredItem> { new MonitoredItem(subscription.DefaultItem) { DisplayName = "ServerStatusCurrentTime", StartNodeId = "i=2258" } };
            list.ForEach(i => i.Notification += OnNotification);
            subscription.AddItems(list);

            Console.WriteLine("6 - Add the subscription to the session.");
            session.AddSubscription(subscription);
            subscription.Create();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);

        }

        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
            }
        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted);
        }

    }
}
