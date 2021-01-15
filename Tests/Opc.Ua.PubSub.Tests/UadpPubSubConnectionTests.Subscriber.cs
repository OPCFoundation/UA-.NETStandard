/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Uadp;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for UadpPubSubConnection class - Subscriber ")]
    public partial class UadpPubSubConnectionTests
    {
        private static object m_lock = new object();
        private byte[] m_sentBytes;

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber unicast ip - Publisher unicast ip")]
        public void ValidateUadpPubSubConnectionNetworkMessageReceiveFromUnicast()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(SubscriberConfigurationFileName);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = "opc.udp://" + localhost.Address.ToString() + ":4840";
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UadpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberConnection.UadpMessageReceived += DataReceived;

            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://" + localhost.Address.ToString() + ":4840";
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UadpPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);

            // physical network ip is mandatory on UdpClientUnicast as parameter
            UdpClient udpUnicastClient = new UdpClientUnicast(localhost.Address, 4840);
            Assert.IsNotNull(udpUnicastClient, "udpUnicastClient is null");

            // first physical network ip = unicast address ip
            IPEndPoint remoteEndPoint = new IPEndPoint(localhost.Address, 4840);
            Assert.IsNotNull(remoteEndPoint, "remoteEndPoint is null");

            m_sentBytes = PrepareData(publisherConnection);
            int sentBytesLen = udpUnicastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(EstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("Subscriber unicast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber unicast ip - Publisher broadcast ip")]
        public void ValidateUadpPubSubConnectionNetworkMessageReceiveFromBroadcast()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(SubscriberConfigurationFileName);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = "opc.udp://" + localhost.Address.ToString() + ":4840";
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UadpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberConnection.UadpMessageReceived += DataReceived;

            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            IPAddress broadcastIPAddress = GetFirstNicLastIPByteChanged(255);
            Assert.IsNotNull(broadcastIPAddress, "broadcastIPAddress is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://" + broadcastIPAddress.ToString() + ":4840";
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UadpPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareData(publisherConnection);

            // first physical network ip is mandatory on UdpClientBroadcast as parameter
            UdpClient udpBroadcastClient = new UdpClientBroadcast(localhost.Address, 4840, UsedInContext.Publisher);
            Assert.IsNotNull(udpBroadcastClient, "udpBroadcastClient is null");

            IPEndPoint remoteEndPoint = new IPEndPoint(broadcastIPAddress, 4840);
            int sentBytesLen = udpBroadcastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(EstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("Subscriber broadcast error ... published data not received");
            }

            subscriberConnection.Stop();
        }

        [Test(Description = "Validate subscriber data on first nic;" +
                            "Subscriber multicast ip - Publisher multicast ip;" +
                            "Setting Subscriber as unicast or broadcast not functional. Just multicast to multicast works fine;")]
        public void ValidateUadpPubSubConnectionNetworkMessageReceiveFromMulticast()
        {
            // Arrange
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 239, 0, 0, 1 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            PubSubConfigurationDataType subscriberConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(SubscriberConfigurationFileName);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration is null");

            NetworkAddressUrlDataType subscriberAddress = new NetworkAddressUrlDataType();
            subscriberAddress.Url = "opc.udp://" + multicastIPAddress.ToString() + ":4840";
            subscriberConfiguration.Connections[0].Address = new ExtensionObject(subscriberAddress);
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication is null");

            UadpPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(subscriberConnection, "subscriberConnection is null");

            subscriberConnection.UadpMessageReceived += DataReceived;

            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://" + multicastIPAddress.ToString() + ":4840";
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UadpPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            //Act  
            subscriberConnection.Start();
            m_shutdownEvent = new ManualResetEvent(false);
            m_sentBytes = PrepareData(publisherConnection);

            // first physical network ip is mandatory on UdpClientMulticast as parameter, for multicast publisher the port must not be 4840
            UdpClient udpMulticastClient = new UdpClientMulticast(localhost.Address, multicastIPAddress, 0);
            Assert.IsNotNull(udpMulticastClient, "udpMulticastClient is null");

            IPEndPoint remoteEndPoint = new IPEndPoint(multicastIPAddress, 4840);
            int sentBytesLen = udpMulticastClient.Send(m_sentBytes, m_sentBytes.Length, remoteEndPoint);
            Assert.AreEqual(sentBytesLen, m_sentBytes.Length, "Sent bytes size not equal to published bytes size!");

            Thread.Sleep(EstimatedPublishingTime);

            // Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
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
        private void DataReceived(object sender, UadpDataEventArgs e)
        {
            lock (m_lock)
            {
                // Assert
                var localhost = GetFirstNic();
                Assert.IsNotNull(localhost, "localhost is null");
                Assert.IsNotNull(localhost.Address, "localhost.Address is null");

                Assert.IsNotNull(e.SourceEndPoint.Address, "Udp address received should not be null");
                if (localhost.Address.ToString() != e.SourceEndPoint.Address.ToString())
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
        private byte[] PrepareData(UadpPubSubConnection publisherConnection)
        {
            try
            {
                WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration.WriterGroups[0];
                UaNetworkMessage message = publisherConnection.CreateNetworkMessage(writerGroup0);

                ServiceMessageContext messageContext = new ServiceMessageContext();
                BinaryEncoder encoder = new BinaryEncoder(messageContext);
                message.Encode(encoder);
                byte[] bytes = ReadBytes(encoder.BaseStream);
                encoder.Dispose();

                return bytes;
            }
            catch (Exception)
            {
            }

            return new byte[0];
        }

        /// <summary>
        /// Read All bytes from a given stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private byte[] ReadBytes(Stream stream)
        {
            stream.Position = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
