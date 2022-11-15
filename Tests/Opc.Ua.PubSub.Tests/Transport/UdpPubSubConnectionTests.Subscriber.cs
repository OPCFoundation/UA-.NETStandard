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
            subscriberAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, localhost.Address.ToString());
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
            publisherAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, localhost.Address.ToString());
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

            m_sentBytes = PrepareData(publisherConnection);
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
            subscriberAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, localhost.Address.ToString());
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
            publisherAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, broadcastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareData(publisherConnection);

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
            subscriberAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
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
            publisherAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareData(publisherConnection);

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
        public void ValidateUdpPubSubConnectionNetworkMessageReceiveFromDiscoveryResponse()
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
            subscriberAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
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
            publisherAddress.Url = string.Format(kUdpUrlFormat, Utils.UriSchemeOpcUdp, multicastIPAddress.ToString());
            publisherConfiguration.Connections.First().Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UdpPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareData(publisherConnection, UdpConnectionType.Discovery);

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
        /// Prepare data 
        /// </summary>
        /// <param name="publisherConnection"></param>
        /// <returns></returns>
        private byte[] PrepareData(UdpPubSubConnection publisherConnection, UdpConnectionType udpConnectionType = UdpConnectionType.Networking, int networkMessageIndex = 0)
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
            catch
            {
                return Array.Empty<byte>();
            }
        }
    }
}
