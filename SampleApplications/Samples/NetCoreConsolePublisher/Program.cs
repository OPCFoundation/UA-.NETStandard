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
using System.Threading.Tasks;

namespace NetCoreConsolePublisher
{
    [CollectionDataContract(Name = "ListOfPublishedNodes", Namespace = Namespaces.OpcUaConfig, ItemName = "NodeId")]
    public partial class PublishedNodesCollection : List<NodeId>
    {
        public static PublishedNodesCollection Load(ApplicationConfiguration configuration)
        {
            return configuration.ParseExtension<PublishedNodesCollection>();
        }
    }

    public class Program
    {
        private static AmqpConnectionCollection m_publishers = null;
        private static ConfiguredEndpointCollection m_endpoints = null;
        private static MonitoredItemNotificationEventHandler m_MonitoredItem_Notification = null;
        private static ApplicationConfiguration m_configuration = null;
        private static Session m_session = null;

        public static void Main(string[] args)
        {
            ApplicationInstance application = new ApplicationInstance();
            application.ApplicationName = "UA AMQP Publisher";
            application.ApplicationType = ApplicationType.ClientAndServer;
            application.ConfigSectionName = "Opc.Ua.Publisher";

            try
            {
                // load the application configuration.
                Task<ApplicationConfiguration> task = application.LoadApplicationConfiguration(false);
                task.Wait();
                m_configuration = task.Result;

                // check the application certificate.
                Task<bool> task2 = application.CheckApplicationInstanceCertificate(false, 0);
                task2.Wait();
                bool certOK = task2.Result;
                if (!certOK)
                {
                    throw new Exception("Application instance certificate invalid!");
                }

                // start the server.
                Task task3 = application.Start(new SampleServer());
                task3.Wait();

                // get list of cached endpoints.
                m_endpoints = m_configuration.LoadCachedEndpoints(true);
                m_endpoints.DiscoveryUrls = m_configuration.ClientConfiguration.WellKnownDiscoveryUrls;

                // start publishers.
                m_publishers = AmqpConnectionCollection.Load(m_configuration);
                foreach (AmqpConnection publisher in m_publishers)
                {
                    Task t = publisher.OpenAsync();
                }

                m_MonitoredItem_Notification = new MonitoredItemNotificationEventHandler(MonitoredItem_Notification);

                // connect to a server.
                EndpointConnect();

                // publish preconfigured nodes
                PublishedNodesCollection nodes = PublishedNodesCollection.Load(m_configuration);
                foreach (NodeId node in nodes)
                {
                    CreateMonitoredItem(node);
                }

                Console.WriteLine("Publisher started. Press any key to exit...");
                Console.ReadKey(true);
            
                if (m_publishers != null)
                {
                    foreach (var publisher in m_publishers)
                    {
                        publisher.Close();
                    }
                }

                if (m_session != null)
                {
                    m_session.Close();
                }
            }
            catch (ServiceResultException ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exit due to Exception: {0}", ex.Message);
            }
        }

        public static void EndpointConnect()
        {
            // Connect to the first cached endpoint in our list
            if (m_endpoints.Count > 0)
            {
                Task<Session> task = Session.Create(
                m_configuration,
                m_endpoints[0],
                true,
                false,
                m_configuration.ApplicationName,
                60000,
                null,
                null);
                task.Wait();
                m_session = task.Result;

                if (m_session != null)
                {
                    m_session.KeepAlive += new KeepAliveEventHandler(StandardClient_KeepAlive);
                }
            }
        }

        public static void CreateMonitoredItem(NodeId nodeId)
        {
            if (m_session != null)
            {
                Subscription subscription = m_session.DefaultSubscription;
                if (m_session.AddSubscription(subscription))
                {
                    subscription.Create();
                }
                
                // add the new monitored item.
                MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);

                monitoredItem.StartNodeId = nodeId;
                monitoredItem.AttributeId = Attributes.Value;
                monitoredItem.DisplayName = nodeId.Identifier.ToString();
                monitoredItem.MonitoringMode = MonitoringMode.Reporting;
                monitoredItem.SamplingInterval = 0;
                monitoredItem.QueueSize = 0;
                monitoredItem.DiscardOldest = true;

                monitoredItem.Notification += MonitoredItem_Notification;
                subscription.AddItem(monitoredItem);
                subscription.ApplyChanges();
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

                JsonEncoder encoder = new JsonEncoder(
                    monitoredItem.Subscription.Session.MessageContext, false);
                encoder.WriteNodeId("MonitoredItem", monitoredItem.ResolvedNodeId);
                e.NotificationValue.Encode(encoder);

                string json = encoder.Close();
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);

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

            if (e != null && m_session != null)
            {
                if (ServiceResult.IsGood(e.Status))
                {
                    Console.WriteLine(Utils.Format(
                        "Server Status: {0} {1:yyyy-MM-dd HH:mm:ss} {2}/{3}",
                        e.CurrentState,
                        e.CurrentTime.ToLocalTime(),
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount));
                }
                else
                {
                    Console.WriteLine(String.Format(
                        "{0} {1}/{2}", e.Status,
                        m_session.OutstandingRequestCount,
                        m_session.DefunctRequestCount));
                }
            }
        }
    }
}
