/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transport;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for UdpPubSubConnection class - Subscriber ")]
#if !CUSTOM_TESTS
    [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
    public partial class UdpPubSubConnectionTests
    {
        private static readonly Lock s_lock = new();
        private byte[] m_sentBytes;

        [Test(Description = "Validate subscriber data on first nic;Subscriber unicast ip - Publisher unicast ip")]
        [Order(1)]
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromUnicast()
        {
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    localhost.Address.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            subscriberApplication.RawDataReceived += RawDataReceived;

            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    localhost.Address.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            //Act
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);

            // physical network ip is mandatory on UdpClientUnicast as parameter
            UdpClient udpUnicastClient = new UdpClientUnicast(
                localhost.Address,
                kDiscoveryPortNo,
                m_messageContext.Telemetry);
            Assert.That(udpUnicastClient, Is.Not.Null, "udpUnicastClient is null");

            // first physical network ip = unicast address ip
            var remoteEndPoint = new IPEndPoint(localhost.Address, kDiscoveryPortNo);
            Assert.That(remoteEndPoint, Is.Not.Null, "remoteEndPoint is null");

            m_sentBytes = BuildNetworkMessages(publisherConnection);
            int sentBytesLen = udpUnicastClient.Send(
                m_sentBytes,
                m_sentBytes.Length,
                remoteEndPoint);
            Assert.That(
                m_sentBytes,
                Has.Length.EqualTo(sentBytesLen),
                "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber unicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;Subscriber unicast ip - Publisher broadcast ip")]
        [Order(2)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromBroadcast()
        {
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);

            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    localhost.Address.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            subscriberApplication.RawDataReceived += RawDataReceived;

            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            IPAddress broadcastIPAddress = GetFirstNicLastIPByteChanged(255);
            Assert.That(broadcastIPAddress, Is.Not.Null, "broadcastIPAddress is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    broadcastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            //Act
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = BuildNetworkMessages(publisherConnection);

            // first physical network ip is mandatory on UdpClientBroadcast as parameter
            UdpClient udpBroadcastClient = new UdpClientBroadcast(
                localhost.Address,
                kDiscoveryPortNo,
                UsedInContext.Publisher,
                m_messageContext.Telemetry);
            Assert.That(udpBroadcastClient, Is.Not.Null, "udpBroadcastClient is null");

            var remoteEndPoint = new IPEndPoint(broadcastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpBroadcastClient.Send(
                m_sentBytes,
                m_sentBytes.Length,
                remoteEndPoint);
            Assert.That(
                m_sentBytes,
                Has.Length.EqualTo(sentBytesLen),
                "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber broadcast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(
                Description = "Validate subscriber data on first nic;Subscriber multicast ip - Publisher multicast ip;" +
                    "Setting Subscriber as unicast or broadcast not functional. Just multicast to multicast works fine;"
            )]
        [Order(3)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromMulticast()
        {
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            var multicastIPAddress = new IPAddress([239, 0, 0, 1]);
            Assert.That(multicastIPAddress, Is.Not.Null, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            subscriberApplication.RawDataReceived += RawDataReceived;

            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            //Act
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = BuildNetworkMessages(publisherConnection);

            // first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(
                localhost.Address,
                multicastIPAddress,
                0,
                m_messageContext.Telemetry);
            Assert.That(udpMulticastClient, Is.Not.Null, "udpMulticastClient is null");

            var remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpMulticastClient.Send(
                m_sentBytes,
                m_sentBytes.Length,
                remoteEndPoint);
            Assert.That(
                m_sentBytes,
                Has.Length.EqualTo(sentBytesLen),
                "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber multicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(
                Description = "Validate subscriber data on first nic;Subscriber multicast ip - Publisher multicast ip;" +
                    "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"
            )]
        [Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_DataSetMetadata()
        {
            ILogger logger = m_messageContext.Telemetry.CreateLogger<UdpPubSubConnectionTests>();

            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            var multicastIPAddress = new IPAddress([224, 0, 2, 14]);
            Assert.That(multicastIPAddress, Is.Not.Null, "multicastIPAddress is null");

            //set subscriber configuration
            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            //set address and create subscriber
            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            //subscribe to event handlers
            subscriberApplication.RawDataReceived += RawDataReceived_NoRequests;
            subscriberApplication.MetaDataReceived += MetaDataReceived;

            //set publisher cofiguration
            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            //set address and create publisher
            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            //start subscriber and prepare the message
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = BuildNetworkMessages(publisherConnection, UdpConnectionType.Discovery);

            subscriberConnection.RequestDataSetMetaData();

            //create multicast client
            // first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(
                localhost.Address,
                multicastIPAddress,
                0,
                m_messageContext.Telemetry);
            Assert.That(udpMulticastClient, Is.Not.Null, "udpMulticastClient is null");

            //set endpoint and send message
            var remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpMulticastClient.Send(
                m_sentBytes,
                m_sentBytes.Length,
                remoteEndPoint);

            //manually create dataset metadata message and trigger metadata reveived event for test
            DataSetMetaDataType metaData = m_uaPublisherApplication
                .DataCollector.GetPublishedDataSet(
                    m_uaPublisherApplication.UaPubSubConfigurator.PubSubConfiguration
                        .PublishedDataSets[0]
                        .Name
                )?
                .DataSetMetaData;
            WriterGroupDataType writerConfig = m_uaPublisherApplication
                .PubSubConnections[0]
                .PubSubConnectionConfiguration
                .WriterGroups[0];
            var networkMessage = new UadpNetworkMessage(
                writerConfig,
                metaData,
                m_messageContext.Telemetry.CreateLogger<UdpPubSubConnectionTests>())
            {
                PublisherId = m_uaPublisherApplication.ApplicationId,
                DataSetWriterId = writerConfig.DataSetWriters[0].DataSetWriterId
            };
            var subscribedDataEventArgs = new SubscribedDataEventArgs
            {
                NetworkMessage = networkMessage
            };
            subscriberApplication.RaiseMetaDataReceivedEvent(subscribedDataEventArgs);

            Assert.That(
                m_sentBytes,
                Has.Length.EqualTo(sentBytesLen),
                "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber multicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(
                Description = "Validate subscriber data on first nic;Subscriber multicast ip - Publisher multicast ip;" +
                    "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"
            )]
        [Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUadpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_DataSetWriterConfig()
        {
            ILogger logger = m_messageContext.Telemetry.CreateLogger<UdpPubSubConnectionTests>();
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            var multicastIPAddress = new IPAddress([224, 0, 2, 14]);
            Assert.That(multicastIPAddress, Is.Not.Null, "multicastIPAddress is null");

            //set configuration
            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            //set address and create subscriber
            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            //subscribe the event handlers
            subscriberApplication.RawDataReceived += RawDataReceived_NoRequests;
            subscriberApplication.DataSetWriterConfigurationReceived
                += DatasetWriterConfigurationReceived;

            //set publisher configuration an create publisher
            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            //start the subscriber and prepare message
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareDataSetWriterConfigurationMessage(publisherConnection);

            //prepare multicast client
            UdpClient udpMulticastClient = new UdpClientMulticast(
                localhost.Address,
                multicastIPAddress,
                0,
                m_messageContext.Telemetry);
            Assert.That(udpMulticastClient, Is.Not.Null, "udpMulticastClient is null");

            //set endpoint and send message
            var remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            int sentBytesLen = udpMulticastClient.Send(
                m_sentBytes,
                m_sentBytes.Length,
                remoteEndPoint);

            Assert.That(
                m_sentBytes,
                Has.Length.EqualTo(sentBytesLen),
                "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.DataSetWriterConfigurationReceived
                -= DatasetWriterConfigurationReceived;
            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(
                Description = "Validate subscriber data on first nic;Subscriber multicast ip - Publisher multicast ip;" +
                    "Publisher holds a DataSetWriterConfiguration, Subscriber requests the configuration;" +
                    "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"
            )]
        [Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_SubscriberRequestDataSetWriterConfiguration()
        {
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            var multicastIPAddress = new IPAddress([224, 0, 2, 14]);
            Assert.That(multicastIPAddress, Is.Not.Null, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            subscriberApplication.DataSetWriterConfigurationReceived
                += DatasetWriterConfigurationReceived;

            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            m_shutdownEvent = new ManualResetEvent(false);

            publisherConnection.Start();
            // Add DataSetWriterConfiguration on Publisher
            if (publisherConnection is IUadpDiscoveryMessages messages)
            {
                // set the DataSetWriterConfiguration callback waiting for a Subscriber request to grab them
                messages.GetDataSetWriterConfigurationCallback(GetDataSetWriterConfiguration);
            }

            //Act
            subscriberConnection.Start();

            subscriberConnection.RequestDataSetWriterConfiguration();

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.DataSetWriterConfigurationReceived
                -= DatasetWriterConfigurationReceived;

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(
                Description = "Validate subscriber data on first nic;" +
                    "Subscriber multicast ip - Publisher multicast ip;" +
                    "Publisher holds a PublisherEndpoints collection, Subscriber request available PublisherEndpoints;" +
                    "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"
            )]
        [Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_SubscriberRequestPublisherEndpoints()
        {
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            var multicastIPAddress = new IPAddress([224, 0, 2, 14]);
            Assert.That(multicastIPAddress, Is.Not.Null, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            subscriberApplication.PublisherEndpointsReceived += PublisherEndpointsReceived;

            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            m_shutdownEvent = new ManualResetEvent(false);

            publisherConnection.Start();
            // Add several PublisherEndpoints on Publisher
            if (publisherConnection is IUadpDiscoveryMessages uadpDiscoveryMessages)
            {
                // set the publisher callback (feed with several demo PublisherEndpoints) waiting for a Subscriber request to grab them
                uadpDiscoveryMessages.GetPublisherEndpointsCallback(GetPublisherEndpoints);
            }

            //Act
            subscriberConnection.Start();

            subscriberConnection.RequestPublisherEndpoints();

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.PublisherEndpointsReceived -= PublisherEndpointsReceived;

            subscriberConnection.Stop();
            publisherConnection.Stop();
        }

        [Test(
                Description = "Validate subscriber data on first nic;Subscriber multicast ip - Publisher multicast ip;" +
                    "Publisher send a PublisherEndpoints collection to the Subscriber, Subscriber only listen for PublisherEndpoints;" +
                    "Setting Subscriber as unicast or broadcast not functional. Just discovery request to multicast and response works fine;"
            )]
        [Order(4)]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse_PublisherTriggerEndpoints()
        {
            // Arrange
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
            Assert.That(localhost, Is.Not.Null, "localhost is null");
            Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

            //discovery IP address 224.0.2.14
            var multicastIPAddress = new IPAddress([224, 0, 2, 14]);
            Assert.That(multicastIPAddress, Is.Not.Null, "multicastIPAddress is null");

            string configurationFile = Utils.GetAbsoluteFilePath(
                m_subscriberConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType subscriberConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(subscriberConfiguration, Is.Not.Null, "subscriberConfiguration is null");

            var subscriberAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            using UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(
                subscriberConfiguration,
                m_messageContext.Telemetry);
            Assert.That(subscriberApplication, Is.Not.Null, "subscriberApplication is null");

            var subscriberConnection = subscriberApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null, "subscriberConnection is null");

            subscriberApplication.PublisherEndpointsReceived += PublisherEndpointsReceived;

            configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType publisherConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(
                    configurationFile,
                    m_messageContext.Telemetry);
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration is null");

            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = Utils.Format(
                    kUdpUrlFormat,
                    Utils.UriSchemeOpcUdp,
                    multicastIPAddress.ToString())
            };
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            using UaPubSubApplication publisherApplication = UaPubSubApplication.Create(
                publisherConfiguration,
                m_messageContext.Telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "publisherApplication is null");

            var publisherConnection = publisherApplication.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(publisherConnection, Is.Not.Null, "publisherConnection is null");

            //Act
            subscriberConnection.Start();

            m_shutdownEvent = new ManualResetEvent(false);

            // Prepare NetworkMessage with PublisherEndpoints
            m_sentBytes = PreparePublisherEndpointsMessage(
                publisherConnection,
                UdpConnectionType.Discovery);

            // Publisher: first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(
                localhost.Address,
                multicastIPAddress,
                0,
                m_messageContext.Telemetry);
            Assert.That(udpMulticastClient, Is.Not.Null, "udpMulticastClient is null");

            var remoteEndPoint = new IPEndPoint(multicastIPAddress, kDiscoveryPortNo);
            // Publisher: trigger PublishNetworkMessage including PublisherEndpoints data
            int sentBytesLen = udpMulticastClient.Send(
                m_sentBytes,
                m_sentBytes.Length,
                remoteEndPoint);
            Assert.That(
                m_sentBytes,
                Has.Length.EqualTo(sentBytesLen),
                "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(kEstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(kEstimatedPublishingTime))
            {
                Assert
                    .Fail("Subscriber multicast error ... published data not received");
            }

            subscriberApplication.PublisherEndpointsReceived -= PublisherEndpointsReceived;

            subscriberConnection.Stop();
        }

        /// <summary>
        /// Subscriber callback that listen for Publisher uadp notifications
        /// </summary>
        private void RawDataReceived(object sender, RawDataReceivedEventArgs e)
        {
            lock (s_lock)
            {
                // Assert
                System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
                Assert.That(localhost, Is.Not.Null, "localhost is null");
                Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

                Assert.That(e.Source, Is.Not.Null, "Udp address received should not be null");
                if (localhost.Address.ToString() != e.Source)
                {
                    // the message comes from the network but was not initiated by test
                    return;
                }

                byte[] bytes = e.Message;
                Assert.That(
                    bytes,
                    Has.Length.EqualTo(m_sentBytes.Length),
                    $"Sent bytes size: {m_sentBytes.Length} does not match received bytes size: {bytes.Length}");

                string sentBytesStr = BitConverter.ToString(m_sentBytes);
                string bytesStr = BitConverter.ToString(bytes);

                Assert.That(
                    bytesStr,
                    Is.EqualTo(sentBytesStr),
                    $"Sent bytes: {sentBytesStr} and received bytes: {bytesStr} content are not equal");

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
                System.Net.NetworkInformation.UnicastIPAddressInformation localhost = GetFirstNic();
                Assert.That(localhost, Is.Not.Null, "localhost is null");
                Assert.That(localhost.Address, Is.Not.Null, "localhost.Address is null");

                Assert.That(e.Source, Is.Not.Null, "Udp address received should not be null");
                if (localhost.Address.ToString() != e.Source)
                {
                    // the message comes from the network but was not initiated by test
                    return;
                }

                byte[] bytes = e.Message;
                if (bytes.Length > 12)
                {
                    Assert.That(
                        bytes,
                        Has.Length.EqualTo(m_sentBytes.Length),
                        $"Sent bytes size: {m_sentBytes.Length} does not match received bytes size: {bytes.Length}");

                    string sentBytesStr = BitConverter.ToString(m_sentBytes);
                    string bytesStr = BitConverter.ToString(bytes);

                    Assert.That(
                        bytesStr,
                        Is.EqualTo(sentBytesStr),
                        $"Sent bytes: {sentBytesStr} and received bytes: {bytesStr} content are not equal");
                }
                m_shutdownEvent.Set();
            }
        }

        /// <summary>
        /// Handler for MetaDataDataReceived event.
        /// </summary>
        private void MetaDataReceived(object sender, SubscribedDataEventArgs e)
        {
            lock (s_lock)
            {
                m_logger.LogInformation("Metadata received:");
                bool isNetworkMessage = e.NetworkMessage is UadpNetworkMessage;
                Assert.That(isNetworkMessage, Is.True);
                if (isNetworkMessage && e.NetworkMessage.IsMetaDataMessage)
                {
                    var message = (UadpNetworkMessage)e.NetworkMessage;

                    Assert.That(message.PublisherId.IsNull, Is.False);
                    Assert.That(message.DataSetWriterId, Is.Not.Null);
                    Assert.That(message.DataSetMetaData, Is.Not.Null);
                    Assert.That(message.DataSetMetaData.Fields.IsNull, Is.False);
                    Assert.That(message.DataSetMetaData.Fields.Count, Is.GreaterThan(0));

                    Assert.That(message.DataSetMetaData.Name, Is.Not.Null);
                    Assert.That(message.DataSetMetaData.ConfigurationVersion, Is.Not.Null);

                    for (int i = 0; i < message.DataSetMetaData.Fields.Count; i++)
                    {
                        FieldMetaData field = message.DataSetMetaData.Fields[i];
                        Assert.That(field.Name, Is.Not.Null);
                        Assert.That(field.DataType.IsNull, Is.False);
                        Assert.That(field.TypeId.IsNull, Is.False);
                        Assert.That(field.Properties.IsNull, Is.False);
                    }
                }
                m_shutdownEvent.Set();
            }
        }

        /// <summary>
        /// Validate received publisher endpoints
        /// </summary>
        private void PublisherEndpointsReceived(object sender, PublisherEndpointsEventArgs e)
        {
            lock (s_lock)
            {
                Assert.That(
                    e.PublisherEndpoints.Count,
                    Is.EqualTo(3),
                    $"Send PublisherEndpoints: {3} and received PublisherEndpoints: {e.PublisherEndpoints.Count} are not equal");

                foreach (EndpointDescription ep in e.PublisherEndpoints)
                {
                    Assert.That(ep.SecurityPolicyUri, Is.Not.Empty);
                    Assert.That(ep.EndpointUrl, Is.Not.Empty);
                    Assert.That(ep.Server, Is.Not.Null);
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
        private byte[] BuildNetworkMessages(
            UdpPubSubConnection publisherConnection,
            UdpConnectionType udpConnectionType = UdpConnectionType.Discovery,
            int networkMessageIndex = 0)
        {
            try
            {
                WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration
                    .WriterGroups[0];

                IList<UaNetworkMessage> networkMessages = null;
                if (udpConnectionType == UdpConnectionType.Discovery)
                {
                    var dataSetWriterIds = new List<ushort>();
                    foreach (DataSetWriterDataType dataSetWriterDataType in writerGroup0
                        .DataSetWriters)
                    {
                        dataSetWriterIds.Add(dataSetWriterDataType.DataSetWriterId);
                    }
                    networkMessages = publisherConnection.CreateDataSetMetaDataNetworkMessages(
                        [.. dataSetWriterIds]);
                }
                else
                {
                    networkMessages = publisherConnection.CreateNetworkMessages(
                        writerGroup0,
                        new WriterGroupPublishState());
                }
                Assert.That(networkMessages, Is.Not.Null, "CreateNetworkMessages returned null");

                Assert.That(
                    networkMessages,
                    Has.Count.GreaterThan(networkMessageIndex),
                    "networkMessageIndex is outside of bounds");

                UaNetworkMessage message = networkMessages[networkMessageIndex];

                return message.Encode(m_messageContext);
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
        private byte[] PreparePublisherEndpointsMessage(
            UdpPubSubConnection publisherConnection,
            UdpConnectionType udpConnectionType = UdpConnectionType.Networking)
        {
            try
            {
                UaNetworkMessage networkMessage = null;
                if (udpConnectionType == UdpConnectionType.Discovery)
                {
                    List<EndpointDescription> endpointDescriptions = CreatePublisherEndpoints();

                    networkMessage = publisherConnection.CreatePublisherEndpointsNetworkMessage(
                        [.. endpointDescriptions],
                        StatusCodes.Good,
                        publisherConnection.PubSubConnectionConfiguration.PublisherId);
                    Assert.That(networkMessage, Is.Not.Null, "uaNetworkMessage shall not return null");

                    return networkMessage.Encode(m_messageContext);
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
        /// UADP Discovery: Provide Publisher demo PublisherEndpoints setting GetPublisherEndpointsCallback
        /// method to deliver them during a Subscriber request
        /// </summary>
        private List<EndpointDescription> GetPublisherEndpoints()
        {
            return CreatePublisherEndpoints();
        }

        /// <summary>
        /// UADP Discovery: Create demo PublisherEndpoints
        /// </summary>
        private static List<EndpointDescription> CreatePublisherEndpoints()
        {
            return
            [
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://server1:4840/Test",
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#None",
                    Server = new ApplicationDescription
                    {
                        ApplicationName = LocalizedText.From("Test security mode None"),
                        ApplicationUri = "urn:localhost:Server"
                    }
                },
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://server1:4840/Test",
                    SecurityMode = MessageSecurityMode.Sign,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                    Server = new ApplicationDescription
                    {
                        ApplicationName = LocalizedText.From("Test security mode Sign"),
                        ApplicationUri = "urn:localhost:Server"
                    }
                },
                new EndpointDescription
                {
                    EndpointUrl = "opc.tcp://server1:4840/Test",
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                    Server = new ApplicationDescription
                    {
                        ApplicationName = LocalizedText.From("Test security mode SignAndEncrypt"),
                        ApplicationUri = "urn:localhost:Server"
                    }
                }
            ];
        }

        /// <summary>
        /// Prepare data for a DataSetWriterConfigurationMessage
        /// </summary>
        /// <param name="publisherConnection">Publisher connection</param>
        private byte[] PrepareDataSetWriterConfigurationMessage(
            UdpPubSubConnection publisherConnection)
        {
            try
            {
                WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration
                    .WriterGroups[0];

                UaNetworkMessage networkMessage = null;

                var dataSetWriterIds = new List<ushort>();
                foreach (DataSetWriterDataType dataSetWriterDataType in writerGroup0.DataSetWriters)
                {
                    dataSetWriterIds.Add(dataSetWriterDataType.DataSetWriterId);
                }
                networkMessage = publisherConnection
                    .CreateDataSetWriterCofigurationMessage([.. dataSetWriterIds])
                    .First();

                Assert.That(
                    networkMessage,
                    Is.Not.Null,
                    "CreateDataSetWriterCofigurationMessages returned null");

                return networkMessage.Encode(m_messageContext);
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
        private void DatasetWriterConfigurationReceived(
            object sender,
            DataSetWriterConfigurationEventArgs e)
        {
            lock (s_lock)
            {
                m_logger.LogInformation("DataSetWriterConfig received:");

                if (e.DataSetWriterConfiguration != null)
                {
                    WriterGroupDataType config = e.DataSetWriterConfiguration;

                    Assert.That(config.Name, Is.Not.Empty);
                    Assert.That(config.SecurityKeyServices.IsNull, Is.False);
                    Assert.That(config.GroupProperties.IsNull, Is.False);
                    Assert.That(config.TransportSettings.IsNull, Is.False);
                    Assert.That(config.MessageSettings.IsNull, Is.False);
                    Assert.That(config.HeaderLayoutUri, Is.Not.Empty);
                    Assert.That(config.DataSetWriters.IsNull, Is.False);

                    foreach (DataSetWriterDataType writer in config.DataSetWriters)
                    {
                        Assert.That(writer.Name, Is.Not.Empty);
                        Assert.That(writer.DataSetWriterProperties.IsNull, Is.False);
                        Assert.That(writer.MessageSettings.IsNull, Is.False);
                        Assert.That(writer.DataSetName, Is.Not.Empty);
                    }
                    m_shutdownEvent.Set();
                }
            }
        }

        /// <summary>
        /// UADP Discovery: Provide DataSetWriterConfiguration setting GetDataSetWriterConfigurationCallback method to deliver them during a Subscriber request
        /// </summary>
        private IList<ushort> GetDataSetWriterConfiguration(UaPubSubApplication uaPubSubApplication)
        {
            return CreateDataSetWriterIdsList(uaPubSubApplication);
        }

        /// <summary>
        /// Create data set writer ids list from the PubSubConnectionDataType configuration
        /// </summary>
        private static List<ushort> CreateDataSetWriterIdsList(
            UaPubSubApplication uaPubSubApplication)
        {
            var ids = new List<ushort>();
            foreach (
                PubSubConnectionDataType connection in uaPubSubApplication
                    .UaPubSubConfigurator
                    .PubSubConfiguration
                    .Connections)
            {
                ids.AddRange(
                    connection
                        .WriterGroups
                        .ToList()
                        .Select(group => group.DataSetWriters)
                        .SelectMany(writer => writer.ToList().Select(x => x.DataSetWriterId)));
            }
            return ids;
        }
    }
}
