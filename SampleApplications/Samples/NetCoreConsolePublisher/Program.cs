/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Publisher;
using Opc.Ua.Sample;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetCoreConsolePublisher
{
    [DataContract(Name = "NodeLookup", Namespace = Namespaces.OpcUaXsd)]
    public partial class NodeLookup
    {
        public NodeLookup()
        {
        }

        [DataMember(Name = "EndpointUrl", IsRequired = true, Order = 0)]
        public Uri EndPointURL;

        [DataMember(Name = "NodeId", IsRequired = true, Order = 1)]
        public NodeId NodeID;
    }

    [CollectionDataContract(Name = "ListOfPublishedNodes", Namespace = Namespaces.OpcUaConfig, ItemName = "NodeLookup")]
    public partial class PublishedNodesCollection : List<NodeLookup>
    {
        public PublishedNodesCollection()
        {
        }

        public static PublishedNodesCollection Load(ApplicationConfiguration configuration)
        {
            return configuration.ParseExtension<PublishedNodesCollection>();
        }
    }

    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (ask)
            {
                message += " (y/n, default y): ";
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }

            if (ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r'));
                }
                catch
                {
                    // intentionally fall through
                }
            }

            return await Task.FromResult(true);
        }
    }

    public class Program
    {
        private static AmqpConnectionCollection m_publishers = null;
        private static ApplicationConfiguration m_configuration = null;
        private static List<Session> m_sessions = new List<Session>();

        public static void Main(string[] args)
        {
            bool started = false;
            try
            {
                Task t = ConsoleSamplePublisher();
                t.Wait();
                Console.WriteLine("Publisher started. Press any key to exit...");
                started = true;
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                Console.WriteLine("Press any key to exit...");
            }

            try {
                Console.ReadKey(true);
            }
            catch
            {
                if (started)
                {
                    // wait forever if there is no console
                    Thread.Sleep(Timeout.Infinite);
                }
            }

            CleanupPublisher();
        }

        private static async Task ConsoleSamplePublisher()
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationName = "UA AMQP Publisher";
            application.ApplicationType = ApplicationType.ClientAndServer;
            application.ConfigSectionName = "Opc.Ua.Publisher";

            // load the application configuration.
            m_configuration = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool certOK = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!certOK)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // start the server.
            await application.Start(new SampleServer());

             // get a list of persisted endpoint URLs and create a session for each.
            List<Uri> endpointUrls = new List<Uri>();
            PublishedNodesCollection nodesLookups = PublishedNodesCollection.Load(m_configuration);
            foreach (NodeLookup nodeLookup in nodesLookups)
            {
                if (!endpointUrls.Contains(nodeLookup.EndPointURL))
                {
                    endpointUrls.Add(nodeLookup.EndPointURL);
                }
            }

            // start publishers.
            m_publishers = AmqpConnectionCollection.Load(m_configuration);
            foreach (AmqpConnection publisher in m_publishers)
            {
                await publisher.OpenAsync();
            }

            // publish preconfigured nodes
            try
            {
                List<Task> connectionAttempts = new List<Task>();
                foreach (Uri endpointUrl in endpointUrls)
                {
                    connectionAttempts.Add(EndpointConnect(endpointUrl));
                }

                // Wait for all sessions to be connected
                Task.WaitAll(connectionAttempts.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.ToString() + "\r\n" + ex.InnerException != null? ex.InnerException.ToString() : null );
            }

            // publish preconfigured nodes
            foreach (NodeLookup nodeLookup in nodesLookups)
            {
                CreateMonitoredItem(nodeLookup);
            }
        }

    	private static void CleanupPublisher()
        {
            if (m_publishers != null)
            {
                foreach (var publisher in m_publishers)
                {
                    publisher.Close();
                }
            }

            foreach (Session session in m_sessions)
            {
                // Disconnect and dispose
                session.Dispose();
            }

            m_sessions.Clear();
        }

       private static async Task EndpointConnect(Uri endpointUrl)
        {
            EndpointDescription selectedEndpoint = SelectUaTcpEndpoint(DiscoverEndpoints(m_configuration, endpointUrl, 10));
            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(selectedEndpoint.Server, EndpointConfiguration.Create(m_configuration));
            configuredEndpoint.Update(selectedEndpoint);

            Session newSession = await Session.Create(
                m_configuration,
                configuredEndpoint,
                true,
                false,
                m_configuration.ApplicationName,
                60000,
                new UserIdentity(new AnonymousIdentityToken()),
                null);

            if (newSession != null)
            {
                Console.WriteLine("Created session with updated endpoint " + selectedEndpoint.EndpointUrl + " from server!");
                newSession.KeepAlive += new KeepAliveEventHandler(StandardClient_KeepAlive);
                m_sessions.Add(newSession);
            }
        }
        public static void CreateMonitoredItem(NodeLookup nodeLookup)
        {
            // find the right session using our lookup
            Session matchingSession = null;
            foreach(Session session in m_sessions)
            {
                if (session.Endpoint.EndpointUrl == nodeLookup.EndPointURL.ToString())
                {
                    matchingSession = session;
                    break;
                }
            }

            if (matchingSession != null)
            {
                Subscription subscription = matchingSession.DefaultSubscription;
                if (matchingSession.AddSubscription(subscription))
                {
                    subscription.Create();
                }

                // add the new monitored item.
                MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);

                monitoredItem.StartNodeId = nodeLookup.NodeID;
                monitoredItem.AttributeId = Attributes.Value;
                monitoredItem.DisplayName = nodeLookup.NodeID.Identifier.ToString();
                monitoredItem.MonitoringMode = MonitoringMode.Reporting;
                monitoredItem.SamplingInterval = 0;
                monitoredItem.QueueSize = 0;
                monitoredItem.DiscardOldest = true;

                monitoredItem.Notification += new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);
                subscription.AddItem(monitoredItem);
                subscription.ApplyChanges();
            }
            else
            {
                Console.WriteLine("ERROR: Could not find endpoint URL " + nodeLookup.EndPointURL.ToString() + " in active server sessions, NodeID " + nodeLookup.NodeID.Identifier.ToString() + " NOT published!");
                Console.WriteLine("To fix this, please update your config.xml with the updated enpoint URL!");
            }
        }

        private static void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                if (e.NotificationValue == null || monitoredItem.Subscription.Session == null)
                {
                    return;
                }

                JsonEncoder encoder = new JsonEncoder(monitoredItem.Subscription.Session.MessageContext, false);
                string hostname = monitoredItem.Subscription.Session.ConfiguredEndpoint.EndpointUrl.DnsSafeHost;
                if (hostname == "localhost")
                {
                    hostname = Utils.GetHostName();
                }
                encoder.WriteString("HostName", hostname);
                encoder.WriteNodeId("MonitoredItem", monitoredItem.ResolvedNodeId);
                e.NotificationValue.Encode(encoder);

                string json = encoder.CloseAndReturnText();
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);

                // publish to all publishers
                foreach (AmqpConnection publisher in m_publishers)
                {
                    try
                    {
                        publisher.Publish(new ArraySegment<byte>(bytes));
                    }
                    catch (Exception ex)
                    {
                        Utils.Trace(ex, "Failed to publish message, dropping....");
                    }
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Error processing monitored item notification.");
            }
        }

        /// <summary>
        /// Updates the status control when a keep alive event occurs.
        /// </summary>
        static void StandardClient_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (sender != null && sender.Endpoint != null)
            {
                Console.WriteLine(Utils.Format(
                    "{0} ({1}) {2}",
                    sender.Endpoint.EndpointUrl,
                    sender.Endpoint.SecurityMode,
                    (sender.EndpointConfiguration.UseBinaryEncoding) ? "UABinary" : "XML"));
            }

            if (e != null && sender != null)
            {
                if (ServiceResult.IsGood(e.Status))
                {
                    Console.WriteLine(Utils.Format(
                        "Server Status: {0} {1:yyyy-MM-dd HH:mm:ss} {2}/{3}",
                        e.CurrentState,
                        e.CurrentTime.ToLocalTime(),
                        sender.OutstandingRequestCount,
                        sender.DefunctRequestCount));
                }
                else
                {
                    Console.WriteLine(String.Format(
                        "{0} {1}/{2}", e.Status,
                        sender.OutstandingRequestCount,
                        sender.DefunctRequestCount));
                }
            }
        }

        private static EndpointDescriptionCollection DiscoverEndpoints(ApplicationConfiguration config, Uri discoveryUrl, int timeout)
        {
            // use a short timeout.
            EndpointConfiguration configuration = EndpointConfiguration.Create(config);
            configuration.OperationTimeout = timeout;

            using (DiscoveryClient client = DiscoveryClient.Create(
                discoveryUrl,
                EndpointConfiguration.Create(config)))
            {
                try
                {
                    EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
                    ReplaceLocalHostWithRemoteHost(endpoints, discoveryUrl);
                    return endpoints;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Could not fetch endpoints from url: {0}", discoveryUrl);
                    Console.WriteLine("Reason = {0}", e.Message);
                    throw e;
                }
            }
        }

        private static void ReplaceLocalHostWithRemoteHost(EndpointDescriptionCollection endpoints, Uri discoveryUrl)
        {
            foreach (EndpointDescription endpoint in endpoints)
            {
                endpoint.EndpointUrl = Utils.ReplaceLocalhost(endpoint.EndpointUrl, discoveryUrl.DnsSafeHost);
                StringCollection updatedDiscoveryUrls = new StringCollection();

                foreach (string url in endpoint.Server.DiscoveryUrls)
                {
                    updatedDiscoveryUrls.Add(Utils.ReplaceLocalhost(url, discoveryUrl.DnsSafeHost));
                }

                endpoint.Server.DiscoveryUrls = updatedDiscoveryUrls;
            }
        }

        private static EndpointDescription SelectUaTcpEndpoint(EndpointDescriptionCollection endpointCollection)
        {
            EndpointDescription bestEndpoint = null;
            foreach (EndpointDescription endpoint in endpointCollection)
            {
                if (endpoint.TransportProfileUri == Profiles.UaTcpTransport)
                {
                    if ((bestEndpoint == null) ||
                        (endpoint.SecurityLevel > bestEndpoint.SecurityLevel))
                    {
                        bestEndpoint = endpoint;
                    }
                }
            }

            return bestEndpoint;
        }
    }
}
