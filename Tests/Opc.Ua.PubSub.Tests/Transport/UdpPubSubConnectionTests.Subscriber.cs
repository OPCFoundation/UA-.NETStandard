/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transport;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for UdpPubSubConnection class - Subscriber ")]
#if !CUSTOM_TESTS
    [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
    public partial class UdpPubSubConnectionTests
    {
        private static object s_lock = new object();
        private byte[] m_sentBytes;

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber unicast ip - Publisher unicast ip"), Order(1)]
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromUnicast()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, localhost.Address.ToString());
            subscriberConfiguration.Connections.First().Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberApplication.RawDataReceived += RawDataReceived;

            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, localhost.Address.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);

            // physical network ip is mandatory on UdpClientUnicast as parameter
            UdpClient udpUnicastClient = new UdpClientUnicast(localhost.Address, kDiscoveryPortNo);
            Assert.IsNotNull(udpUnicastClient, "udpUnicastClient is null");

            // first physical network ip = unicast address ip
            IPEndPoint remoteEndPoint = new IPEndPoint(localhost.Address, kDiscoveryPortNo);
            Assert.IsNotNull(remoteEndPoint, "remoteEndPoint is null");

            m_sentBytes = BuildNetworkMessages(publisherConnection);
            int sentBytesLen = udpUnicastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber unicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber unicast ip - Publisher broadcast ip"), Order(2)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromBroadcast()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, localhost.Address.ToString());
            subscriberConfiguration.Connections.First().Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberApplication.RawDataReceived += RawDataReceived;

            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            IPAddress broadcastIPAddress = GetFirstNicLastIPByteChanged(255);
            Assert.IsNotNull(broadcastIPAddress, "broadcastIPAddress is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, broadcastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = BuildNetworkMessages(publisherConnection);

            // first physical network ip is mandatory on UdpClientBroadcast as parameter
            UdpClient udpBroadcastClient = new UdpClientBroadcast(localhost.Address, kDiscoveryPortNo, UsedInContext.Publisher);
            Assert.IsNotNull(udpBroadcastClient, "udpBroadcastClient is null");

            IPEndPoint remoteEndPoint = new IPEndPoint(broadcastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpBroadcastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber broadcast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber multicast ip - Publisher multicast ip;" +
                            "Setting Subscriber as unicast or broadcast not functional. Just multicast to multicast works fine;"), Order(3)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromMulticast()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 239, 0, 0, 1 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberApplication.RawDataReceived += RawDataReceived;

            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = BuildNetworkMessages(publisherConnection);

            // first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(localhost.Address, multicastIPAddress, 0);
            Assert.IsNotNull(udpMulticastClient, "udpMulticastClient is null");

            IPEndPoint remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpMulticastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber multicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber multicast ip - Publisher multicast ip;" +
                            "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"), Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_DataSetMetadata()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 224, 0, 2, 14 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            //set subscriber configuration
            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            //set address and create subscriber
            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            //subscribe to event handlers
            subscriberApplication.RawDataReceived += RawDataReceived_NoRequests;
            subscriberApplication.MetaDataReceived += MetaDataReceived;

            //set publisher cofiguration
            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            //set address and create publisher
            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //start subcriber and prepare the message
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = BuildNetworkMessages(publisherConnection, UdpConnectionType.Discovery);

            subscriberConnection.RequestDataSetMetaData();

            //create multicast client
            // first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(localhost.Address, multicastIPAddress, 0);
            Assert.IsNotNull(udpMulticastClient, "udpMulticastClient is null");

            //set endpoint and send message
            IPEndPoint remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpMulticastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);

            //manually create dataset metadata message and trigger metadata reveived event for test
            DataSetMetaDataType metaData = m_uaPublisherApplication.DataCollector.GetPublishedDataSet(m_uaPublisherApplication.UaPubSubConfigurator.PubSubConfiguration.PublishedDataSets.First().Name)?.DataSetMetaData;
            WriterGroupDataType writerConfig = m_uaPublisherApplication.PubSubConnections.First().PubSubConnectionConfiguration.WriterGroups.First();
            UadpNetworkMessage networkMessage = new UadpNetworkMessage(writerConfig, metaData) { PublisherId = m_uaPublisherApplication.ApplicationId, DataSetWriterId = writerConfig.DataSetWriters.First().DataSetWriterId };
            SubscribedDataEventArgs subscribedDataEventArgs = new SubscribedDataEventArgs()
            {
                NetworkMessage = networkMessage,
            };
            subscriberApplication.RaiseMetaDataReceivedEvent(subscribedDataEventArgs);


            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber multicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                           "Subscriber multicast ip - Publisher multicast ip;" +
                           "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"), Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUadpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_DataSetWriterConfig()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 224, 0, 2, 14 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            //set configuration
            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            //set address and create subscriber
            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            //subscribe the event handlers
            subscriberApplication.RawDataReceived += RawDataReceived_NoRequests;
            subscriberApplication.DataSetWriterConfigurationReceived += DatasetWriterConfigurationReceived;

            //set publisher configuration an create publisher
            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //start the subscriber and prepare message
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareDataSetWriterConfigurationMessage(publisherConnection);

            //prepare multicast client
            UdpClient udpMulticastClient = new UdpClientMulticast(localhost.Address, multicastIPAddress, 0);
            Assert.IsNotNull(udpMulticastClient, "udpMulticastClient is null");

            //set endpoint and send message
            IPEndPoint remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpMulticastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);

            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.DataSetWriterConfigurationReceived -= DatasetWriterConfigurationReceived;
            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber multicast ip - Publisher multicast ip;" +
                            "Publisher holds a DataSetWriterConfiguration, Subscriber requests the configuration;" +
                            "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"), Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_SubscriberRequestDataSetWriterConfiguration()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 224, 0, 2, 14 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberApplication.DataSetWriterConfigurationReceived += DatasetWriterConfigurationReceived;

            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            m_shutdownEvent = new ManualResetEvent(false);

            publisherConnection.Start();
            // Add DataSetWriterConfiguration on Publisher
            if (publisherConnection is IUadpDiscoveryMessages)
            {
                // set the DataSetWriterConfiguration callback waiting for a Subscriber request to grab them
                ((IUadpDiscoveryMessages)publisherConnection).GetDataSetWriterConfigurationCallback(GetDataSetWriterConfiguration);
            }

            //Act 
            subscriberConnection.Start();

            subscriberConnection.RequestDataSetWriterConfiguration();

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.DataSetWriterConfigurationReceived -= DatasetWriterConfigurationReceived;

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber multicast ip - Publisher multicast ip;" +
                            "Publisher holds a PublisherEndpoints collection, Subscriber request available PublisherEndpoints;" +
                            "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"), Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_SubscriberRequestPublisherEndpoints()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 224, 0, 2, 14 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberApplication.PublisherEndpointsReceived += PublisherEndpointsReceived;

            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            m_shutdownEvent = new ManualResetEvent(false);
                        
            publisherConnection.Start();
            // Add several PublisherEndpoints on Publisher
            if (publisherConnection is IUadpDiscoveryMessages)
            {
                // set the publisher callback (feed with several demo PublisherEndpoints) waiting for a Subscriber request to grab them
                ((IUadpDiscoveryMessages)publisherConnection).GetPublisherEndpointsCallback(GetPublisherEndpoints);
            }
            
            //Act 
            subscriberConnection.Start();

            subscriberConnection.RequestPublisherEndpoints();

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.PublisherEndpointsReceived -= PublisherEndpointsReceived;

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber multicast ip - Publisher multicast ip;" +
                            "Publisher send a PublisherEndpoints collection to the Subscriber, Subscriber only listen for PublisherEndpoints;" +
                            "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"), Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_PublisherTriggerEndpoints()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 224, 0, 2, 14 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(m_subscriberConfigurationFileName, true, true, false);
            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UdpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberApplication.PublisherEndpointsReceived += PublisherEndpointsReceived;

            configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configurationFile);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = Utils.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            
            m_shutdownEvent = new ManualResetEvent(false);

            // Prepare NetworkMessage with PublisherEndpoints 
            m_sentBytes = PreparePublisherEndpointsMessage(publisherConnection, UdpConnectionType.Discovery);

            // Publisher: first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(localhost.Address, multicastIPAddress, 0);
            Assert.IsNotNull(udpMulticastClient, "udpMulticastClient is null");

            IPEndPoint remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            // Publisher: trigger PublishNetworkMessage including PublisherEndpoints data
            int sentBytesLen = udpMulticastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert.Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.PublisherEndpointsReceived -= PublisherEndpointsReceived;

            subscriberConnection.Stop();
        }

        /// <summary>
        /// Subscriber callback that listen for Publisher uadp notifications 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RawDataReceived(object sender, RawDataReceivedEventArgs e)
        {
            lock (s_lock)
            {
                // Assert
                var localhost = GetFirstNic();
                Assert.IsNotNull(localhost, "localhost is null");
                Assert.IsNotNull(localhost.Address, "localhost.Address is null");

                Assert.IsNotNull(e.Source, "Udp address received should not be null");
                if (localhost.Address.ToString() != e.Source.ToString())
                {
                    // the message comes from the network but was not initiated by test
                    return;
                }

                byte[] bytes = e.Message;
                Assert.AreEqual(m_sentBytes.Length, bytes.Length, "Sent bytes size: {0} does not match received bytes size: {1}", m_sentBytes.Length, bytes.Length);

                string sentBytesStr = BitConverter.ToString(m_sentBytes);
                string bytesStr = BitConverter.ToString(bytes);

                Assert.AreEqual(sentBytesStr, bytesStr, "Sent bytes: {0} and received bytes: {1} content are not equal", sentBytesStr, bytesStr);

                m_shutdownEvent.Set();
            }
        }

        /// <summary>
        /// Subscriber callback that listen for Publisher uadp notifications but does not test requests
        /// </summary>
        /// <param name="sender">the sender</param>
        /// <param name="e">the event args</param>
        private void RawDataReceived_NoRequests(object sender, RawDataReceivedEventArgs e)
        {
            lock (s_lock)
            {
                // Assert
                var localhost = GetFirstNic();
                Assert.IsNotNull(localhost, "localhost is null");
                Assert.IsNotNull(localhost.Address, "localhost.Address is null");

                Assert.IsNotNull(e.Source, "Udp address received should not be null");
                if (localhost.Address.ToString() != e.Source.ToString())
                {
                    // the message comes from the network but was not initiated by test
                    return;
                }

                byte[] bytes = e.Message;
                if (bytes.Length > 12)
                {
                    Assert.AreEqual(m_sentBytes.Length, bytes.Length, "Sent bytes size: {0} does not match received bytes size: {1}", m_sentBytes.Length, bytes.Length);

                    string sentBytesStr = BitConverter.ToString(m_sentBytes);
                    string bytesStr = BitConverter.ToString(bytes);

                    Assert.AreEqual(sentBytesStr, bytesStr, "Sent bytes: {0} and received bytes: {1} content are not equal", sentBytesStr, bytesStr);
                }
                m_shutdownEvent.Set();
            }
        }

        /// <summary>
        /// Handler for MetaDataDataReceived event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MetaDataReceived(object sender, SubscribedDataEventArgs e)
        {
            lock (s_lock)
            {
                Console.WriteLine("Metadata received:");
                bool isNetworkMessage = e.NetworkMessage is UadpNetworkMessage;
                Assert.IsTrue(isNetworkMessage);
                if (isNetworkMessage)
                {
                    if (e.NetworkMessage.IsMetaDataMessage)
                    {
                        UadpNetworkMessage message = (UadpNetworkMessage)e.NetworkMessage;

                        Assert.IsNotNull(message.PublisherId);
                        Assert.IsNotNull(message.DataSetWriterId);
                        Assert.IsNotNull(message.DataSetMetaData);
                        Assert.IsNotNull(message.DataSetMetaData.Fields);
                        Assert.IsTrue(message.DataSetMetaData.Fields.Count > 0);


                        Assert.IsNotNull(message.DataSetMetaData.Name);
                        Assert.IsNotNull(message.DataSetMetaData.ConfigurationVersion);

                        for (int i = 0; i < message.DataSetMetaData.Fields.Count; i++)
                        {
                            FieldMetaData field = message.DataSetMetaData.Fields[i];
                            Assert.IsNotNull(field.Name);
                            Assert.IsNotNull(field.DataType);
                            Assert.IsNotNull(field.ValueRank);
                            Assert.IsNotNull(field.TypeId);
                            Assert.IsNotNull(field.Properties);
                        }
                    }
                }
                m_shutdownEvent.Set();
            }
        }

        /// <summary>
        /// Validate received publisher endpoints
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PublisherEndpointsReceived(object sender, PublisherEndpointsEventArgs e)
        {
            lock (s_lock)
            {
                Assert.AreEqual(3, e.PublisherEndpoints.Length, "Send PublisherEndpoints: {0} and received PublisherEndpoints: {1} are not equal", 3, e.PublisherEndpoints.Length);

                foreach (EndpointDescription ep in e.PublisherEndpoints)
                {
                    Assert.IsNotNull(ep.SecurityMode);
                    Assert.IsNotEmpty(ep.SecurityPolicyUri);
                    Assert.IsNotEmpty(ep.EndpointUrl);
                    Assert.IsNotNull(ep.Server);
                }
                m_shutdownEvent.Set();
            }
        }

        /// <summary>
        /// Prepare data / metadata for network messages
        /// </summary>
        /// <param name="publisherConnection">the connection</param>
        /// <param name="udpConnectionType">the connection's type</param>
        /// <param name="networkMessageIndex">the network message index</param>
        /// <returns></returns>
        private byte[] BuildNetworkMessages(UdpPubSubConnection publisherConnection, UdpConnectionType udpConnectionType = UdpConnectionType.Discovery, int networkMessageIndex = 0)
        {
            try
            {
                WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration.WriterGroups.First();

                IList<UaNetworkMessage> networkMessages = null;
                if (udpConnectionType == UdpConnectionType.Discovery)
                {
                    List<UInt16> dataSetWriterIds = new List<UInt16>();
                    foreach (DataSetWriterDataType dataSetWriterDataType in writerGroup0.DataSetWriters)
                    {
                        dataSetWriterIds.Add(dataSetWriterDataType.DataSetWriterId);
                    }
                    networkMessages = publisherConnection.CreateDataSetMetaDataNetworkMessages(dataSetWriterIds.ToArray());
                }
                else
                {
                    networkMessages = publisherConnection.CreateNetworkMessages(writerGroup0, new WriterGroupPublishState());
                }
                Assert.IsNotNull(networkMessages, "CreateNetworkMessages returned null");

                Assert.IsTrue(networkMessages.Count > networkMessageIndex, "networkMessageIndex is outside of bounds");

                UaNetworkMessage message = networkMessages[networkMessageIndex];

                byte[] bytes = message.Encode(ServiceMessageContext.GlobalContext);

                return bytes;
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Prepare Publisher UADP Discovery request with PublisherEndpoints data
        /// </summary>
        /// <param name="publisherConnection"></param>
        /// <param name="udpConnectionType"></param>
        /// <returns></returns>
        private byte[] PreparePublisherEndpointsMessage(UdpPubSubConnection publisherConnection, UdpConnectionType udpConnectionType = UdpConnectionType.Networking)
        {
            try
            {
                UaNetworkMessage networkMessage = null;
                if (udpConnectionType == UdpConnectionType.Discovery)
                {
                    List<EndpointDescription> endpointDescriptions = CreatePublisherEndpoints();
                    
                    networkMessage = publisherConnection.CreatePublisherEndpointsNetworkMessage(endpointDescriptions.ToArray(),
                        StatusCodes.Good, publisherConnection.PubSubConnectionConfiguration.PublisherId.Value);
                    Assert.IsNotNull(networkMessage, "uaNetworkMessage shall not return null");

                    return networkMessage.Encode(ServiceMessageContext.GlobalContext);
                }

                return null;
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// UADP Discovery: Provide Publisher demo PublisherEndpoints setting GetPublisherEndpointsCallback method to deliver them during a Subscriber request
        /// </summary>
        /// <returns></returns>
        private List<EndpointDescription> GetPublisherEndpoints()
        {
            return CreatePublisherEndpoints();
        }

        /// <summary>
        /// UADP Discovery: Create demo PublisherEndpoints
        /// </summary>
        /// <returns></returns>
        private List<EndpointDescription> CreatePublisherEndpoints()
        {
            return new List<EndpointDescription>()
            {
                new EndpointDescription() {
                    EndpointUrl = "opc.tcp://server1:4840/Test",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#None",
                    Server = new ApplicationDescription() { ApplicationName = "Test security mode None", ApplicationUri = "urn:localhost:Server" }
                },
                new EndpointDescription()
                {
                    EndpointUrl = "opc.tcp://server1:4840/Test",
                    SecurityMode = MessageSecurityMode.Sign,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                    Server = new ApplicationDescription() { ApplicationName = "Test security mode Sign", ApplicationUri = "urn:localhost:Server" }
                },
                new EndpointDescription()
                {
                    EndpointUrl = "opc.tcp://server1:4840/Test",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                    Server = new ApplicationDescription() { ApplicationName = "Test security mode SignAndEncrypt", ApplicationUri = "urn:localhost:Server" }
                }
            };
        }

        /// <summary>
        /// Prepare data for a DataSetWriterConfigurationMessage
        /// </summary>
        /// <param name="publisherConnection">Publisher connection</param>
        /// <returns></returns>
        private byte[] PrepareDataSetWriterConfigurationMessage(UdpPubSubConnection publisherConnection)
        {
            try
            {
                WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration.WriterGroups.First();

                UaNetworkMessage networkMessage = null;

                List<UInt16> dataSetWriterIds = new List<UInt16>();
                foreach (DataSetWriterDataType dataSetWriterDataType in writerGroup0.DataSetWriters)
                {
                    dataSetWriterIds.Add(dataSetWriterDataType.DataSetWriterId);
                }
                networkMessage = publisherConnection.CreateDataSetWriterCofigurationMessage(dataSetWriterIds.ToArray()).First();

                Assert.IsNotNull(networkMessage, "CreateDataSetWriterCofigurationMessages returned null");

                byte[] bytes = networkMessage.Encode(ServiceMessageContext.GlobalContext);

                return bytes;
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Handler for DatasetWriterConfigurationReceived event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DatasetWriterConfigurationReceived(object sender, DataSetWriterConfigurationEventArgs e)
        {
            lock (s_lock)
            {
                Console.WriteLine("DataSetWriterConfig received:");

                if (e.DataSetWriterConfiguration != null)
                {
                    WriterGroupDataType config = e.DataSetWriterConfiguration;

                    Assert.IsNotEmpty(config.Name);
                    Assert.IsNotNull(config.SecurityKeyServices);
                    Assert.IsNotNull(config.GroupProperties);
                    Assert.IsNotNull(config.SecurityMode);
                    Assert.IsNotNull(config.TransportSettings);
                    Assert.IsNotNull(config.MessageSettings);
                    Assert.IsNotEmpty(config.HeaderLayoutUri);
                    Assert.IsTrue(config.DataSetWriters != null);

                    foreach (DataSetWriterDataType writer in config.DataSetWriters)
                    {
                        Assert.IsNotEmpty(writer.Name);
                        Assert.IsNotNull(writer.DataSetWriterProperties);
                        Assert.IsNotNull(writer.MessageSettings);
                        Assert.IsNotEmpty(writer.DataSetName);
                    }
                    m_shutdownEvent.Set();
                }
            }
        }

        /// <summary>
        /// UADP Discovery: Provide DataSetWriterConfiguration setting GetDataSetWriterConfigurationCallback method to deliver them during a Subscriber request
        /// </summary>
        /// <returns></returns>
        private IList<UInt16> GetDataSetWriterConfiguration(UaPubSubApplication uaPubSubApplication)
        {
            return CreateDataSetWriterIdsList(uaPubSubApplication);
        }

        /// <summary>
        /// Create data set writer ids list from the PubSubConnectionDataType configuration
        /// </summary>
        /// <param name="uaPubSubApplication"></param>
        /// <returns></returns>
        private static IList<UInt16> CreateDataSetWriterIdsList(UaPubSubApplication uaPubSubApplication)
        {
            List<UInt16> ids = new List<UInt16>();

            foreach (var connection in uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.Connections)
            {
                ids.AddRange(connection.WriterGroups
                    .Select(group => group.DataSetWriters)
                    .SelectMany(writer => writer.Select(x => x.DataSetWriterId))
                    .ToList());
            }
            return ids;
        }
    }
}
