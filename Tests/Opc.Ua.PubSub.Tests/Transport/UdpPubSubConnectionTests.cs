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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transport;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for UdpPubSubConnection class")]
    public partial class UdpPubSubConnectionTests
    {
        #region Fields
        private const int EstimatedPublishingTime = 10000;

        private const string UdpUrlFormat = "{0}://{1}:4840";
        private const string UdpDiscoveryIp = "224.0.2.14";
        private const string UdpMulticastIp = "239.0.0.1";
        private const int DiscoveryPortNo = 4840;

        protected enum UdpConnectionType
        {
            Networking,
            Discovery
        }

        protected enum UdpAddressesType
        {
            Unicast,
            Broadcast,
            Multicast
        }

        protected enum UadpDiscoveryType
        {
            Request,
            Response
        }

        private string PublisherConfigurationFileName = Path.Combine("Configuration", "PublisherConfiguration.xml");
        private string SubscriberConfigurationFileName = Path.Combine("Configuration", "SubscriberConfiguration.xml");

        private PubSubConfigurationDataType m_publisherConfiguration;
        private UaPubSubApplication m_uaPublisherApplication;
        private UdpPubSubConnection m_udpPublisherConnection;

        private ManualResetEvent m_shutdownEvent;
        //private UdpAddressesType m_udpAddressesType = UdpAddressesType.Unicast;
        #endregion

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            // Create a publisher application
            string configurationFile = Utils.GetAbsoluteFilePath(PublisherConfigurationFileName, true, true, false);
            m_uaPublisherApplication = UaPubSubApplication.Create(configurationFile);
            Assert.IsNotNull(m_uaPublisherApplication, "m_publisherApplication should not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_uaPublisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_publisherConfiguration, "m_publisherConfiguration should not be null");

            // Get publisher connection
            Assert.IsNotNull(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be empty");
            m_udpPublisherConnection = m_uaPublisherApplication.PubSubConnections.First() as UdpPubSubConnection;
            Assert.IsNotNull(m_udpPublisherConnection, "m_uadpPublisherConnection should not be null");
        }

        [Test(Description = "Validate TransportProtocol value")]
        public void ValidateUdpPubSubConnectionTransportProtocol()
        {
            //Assert
            Assert.IsNotNull(m_udpPublisherConnection, "The UDP connection from standard configuration is invalid.");
            Assert.IsTrue(m_udpPublisherConnection.TransportProtocol == TransportProtocol.UDP,
                "The UADP connection has wrong TransportProtocol {0}", m_udpPublisherConnection.TransportProtocol);
        }

        [Test(Description = "Validate PubSubConnectionConfiguration value")]
        public void ValidateUdpPubSubConnectionPubSubConnectionConfiguration()
        {
            //Assert
            Assert.IsNotNull(m_udpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            PubSubConnectionDataType connectionConfiguration = m_udpPublisherConnection.PubSubConnectionConfiguration;
            PubSubConnectionDataType originalConnectionConfiguration = m_publisherConfiguration.Connections.First();
            Assert.IsNotNull(connectionConfiguration, "The UADP connection configuration from UADP connection object is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.Name, connectionConfiguration.Name, "The connection configuration Name is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.PublisherId, connectionConfiguration.PublisherId, "The connection configuration PublisherId is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.Address, connectionConfiguration.Address, "The connection configuration Address is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.Enabled, connectionConfiguration.Enabled, "The connection configuration Enabled is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.TransportProfileUri, connectionConfiguration.TransportProfileUri, "The connection configuration TransportProfileUri is invalid.");

        }

        [Test(Description = "Validate Application value")]
        public void ValidateUdpPubSubConnectionApplication()
        {
            //Assert
            Assert.IsNotNull(m_udpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            Assert.AreEqual(m_udpPublisherConnection.Application, m_uaPublisherApplication, "The UADP connection Application reference is invalid.");
        }

        [Test(Description = "Validate Publishers value")]
        public void ValidateUdpPubSubConnectionPublishers()
        {
            //Assert
            Assert.IsNotNull(m_udpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            Assert.IsNotNull(m_udpPublisherConnection.Publishers, "The UADP connection Publishers is invalid.");
            Assert.AreEqual(1, m_udpPublisherConnection.Publishers.Count, "The UADP connection Publishers.Count is invalid.");
            int index = 0;
            foreach (IUaPublisher publisher in m_udpPublisherConnection.Publishers)
            {
                Assert.IsTrue(publisher != null, "connection.Publishers[{0}] is null", index);
                Assert.IsTrue(publisher.PubSubConnection == m_udpPublisherConnection, "connection.Publishers[{0}].PubSubConnection is not set correctly", index);
                Assert.IsTrue(publisher.WriterGroupConfiguration.WriterGroupId == m_publisherConfiguration.Connections[0].WriterGroups[index].WriterGroupId, "connection.Publishers[{0}].WriterGroupConfiguration is not set correctly", index);
                index++;
            }
        }

        [Test(Description = "Validate CreateNetworkMessage")]
        public void ValidateUdpPubSubConnectionCreateNetworkMessage()
        {
            Assert.IsNotNull(m_udpPublisherConnection, "The UADP connection from standard configuration is invalid.");

            //Arrange
            WriterGroupDataType writerGroup0 = m_udpPublisherConnection.PubSubConnectionConfiguration.WriterGroups.First();
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup0.MessageSettings)
                as UadpWriterGroupMessageDataType;

            //Act  
            m_udpPublisherConnection.ResetSequenceNumber();

            var networkMessages = m_udpPublisherConnection.CreateNetworkMessages(writerGroup0, new WriterGroupPublishState());
            Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
            var networkMessagesNetworkType = networkMessages.FirstOrDefault(net => net.IsMetaDataMessage == false);
            Assert.IsNotNull(networkMessagesNetworkType, "connection.CreateNetworkMessages shall return only one network message");

            UadpNetworkMessage networkMessage0 = networkMessagesNetworkType as UadpNetworkMessage;
            Assert.IsNotNull(networkMessage0, "networkMessageEncode should not be null");

            //Assert
            Assert.IsNotNull(networkMessage0, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            Assert.AreEqual(networkMessage0.DataSetClassId, Guid.Empty, "UadpNetworkMessage.DataSetClassId is invalid.");
            Assert.AreEqual(networkMessage0.WriterGroupId, writerGroup0.WriterGroupId, "UadpNetworkMessage.WriterGroupId is invalid.");
            Assert.AreEqual(networkMessage0.UADPVersion, 1, "UadpNetworkMessage.UADPVersion is invalid.");
            Assert.AreEqual(networkMessage0.SequenceNumber, 1, "UadpNetworkMessage.SequenceNumber is not 1.");
            Assert.AreEqual(networkMessage0.GroupVersion, messageSettings.GroupVersion, "UadpNetworkMessage.GroupVersion is not valid.");
            Assert.AreEqual(networkMessage0.PublisherId, m_udpPublisherConnection.PubSubConnectionConfiguration.PublisherId.Value,
                "UadpNetworkMessage.PublisherId is not valid.");
            Assert.IsNotNull(networkMessage0.DataSetMessages, "UadpNetworkMessage.UadpDataSetMessages is null.");
            Assert.AreEqual(networkMessage0.DataSetMessages.Count, 3, "UadpNetworkMessage.UadpDataSetMessages.Count is not 3.");
            //validate flags
            Assert.AreEqual((uint)networkMessage0.NetworkMessageContentMask, messageSettings.NetworkMessageContentMask,
                "UadpNetworkMessage.messageSettings.NetworkMessageContentMask is not valid.");

        }

        [Test(Description = "Validate CreateNetworkMessage SequenceNumber increment")]
        public void ValidateUdpPubSubConnectionCreateNetworkMessageSequenceNumber()
        {
            Assert.IsNotNull(m_udpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            //Arrange
            WriterGroupDataType writerGroup0 = m_udpPublisherConnection.PubSubConnectionConfiguration.WriterGroups.First();

            //Act  
            m_udpPublisherConnection.ResetSequenceNumber();
            for (int i = 0; i < 10; i++)
            {
                // Create network message
                var networkMessages = m_udpPublisherConnection.CreateNetworkMessages(writerGroup0, new WriterGroupPublishState());
                Assert.IsNotNull(networkMessages, "connection.CreateNetworkMessages shall not return null");
                var networkMessagesNetworkType = networkMessages.FirstOrDefault(net => net.IsMetaDataMessage == false);
                Assert.IsNotNull(networkMessagesNetworkType, "connection.CreateNetworkMessages shall return only one network message");

                UadpNetworkMessage networkMessage = networkMessagesNetworkType as UadpNetworkMessage;
                Assert.IsNotNull(networkMessage, "networkMessageEncode should not be null");

                //Assert
                Assert.IsNotNull(networkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");
                Assert.AreEqual(networkMessage.SequenceNumber, i + 1, "UadpNetworkMessage.SequenceNumber for message {0} is not {0}.", i + 1);

                //validate dataset message sequence number
                Assert.IsNotNull(networkMessage.DataSetMessages, "CreateNetworkMessage did not return an UadpNetworkMessage.UadpDataSetMessages.");
                Assert.IsTrue(networkMessage.DataSetMessages.Count == 3, "CreateNetworkMessage did not return 3 UadpNetworkMessage.UadpDataSetMessages.");
                Assert.AreEqual(((UadpDataSetMessage)networkMessage.DataSetMessages[0]).SequenceNumber, i * 3 + 1, "UadpNetworkMessage.UadpDataSetMessages[0].SequenceNumber for message {0} is not {1}.", i + 1, i * 3 + 1);
                Assert.AreEqual(((UadpDataSetMessage)networkMessage.DataSetMessages[1]).SequenceNumber, i * 3 + 2, "UadpNetworkMessage.UadpDataSetMessages[1].SequenceNumber for message {0} is not {1}.", i + 1, i * 3 + 2);
                Assert.AreEqual(((UadpDataSetMessage)networkMessage.DataSetMessages[2]).SequenceNumber, i * 3 + 3, "UadpNetworkMessage.UadpDataSetMessages[2].SequenceNumber for message {0} is not {1}.", i + 1, i * 3 + 3);
            }
        }

        #region Public methods
        /// <summary>
        /// Get localhost address reference
        /// </summary>
        /// <returns></returns>
        public static UnicastIPAddressInformation GetFirstNic()
        {
            string activeIp = "127.0.0.1";

            IPAddress firstActiveIPAddr = GetFirstActiveNic();
            if (firstActiveIPAddr != null)
            {
                activeIp = firstActiveIPAddr.ToString();
            }

            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface nic in interfaces)
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.OperationalStatus == OperationalStatus.Up)
                {
                    var addreses = nic.GetIPProperties().UnicastAddresses;
                    foreach (UnicastIPAddressInformation addr in addreses)
                    {
                        if (addr.Address.ToString().Contains(activeIp))
                        {
                            // return specified address
                            return addr;
                        }
                    }
                }
                else { continue; }
            }

            return null;
        }
        #endregion

        #region Private methods
        /// <summary>
        /// Data received handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubApplication_DataReceived(object sender, SubscribedDataEventArgs e)
        {
            m_shutdownEvent.Set();
        }

        /// <summary>
        /// Get first active broadcast ip
        /// </summary>
        /// <returns></returns>
        private static IPAddress GetFirstNicLastIPByteChanged(byte lastIpByte)
        {
            IPAddress firstActiveIPAddr = GetFirstActiveNic();
            if (firstActiveIPAddr != null)
            {
                // replace last IP byte from address with 255 (broadcast)
                IPAddress validIp = null;
                bool isValidIP = IPAddress.TryParse(firstActiveIPAddr.ToString(), out validIp);
                if (isValidIP)
                {
                    byte[] ipAddressBytes = validIp.GetAddressBytes();
                    ipAddressBytes[ipAddressBytes.Length - 1] = lastIpByte;
                    return new IPAddress(ipAddressBytes);
                }
            }

            return null;
        }

        /// <summary>
        /// Check if the specified ip is a local host ip
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <returns></returns>
        private bool IsHostAddress(string ipAddress)
        {
            var hostName = Dns.GetHostName();
            foreach (var address in Dns.GetHostEntry(hostName).AddressList)
            {
                if (address.MapToIPv4().ToString().Equals(ipAddress))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get list of active IPv4 addresses.
        /// </summary>
        public static IPAddress[] GetLocalIpAddresses()
        {
            var addresses = new List<IPAddress>();
            foreach (var netI in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netI.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                   (netI.NetworkInterfaceType != NetworkInterfaceType.Ethernet ||
                    netI.OperationalStatus != OperationalStatus.Up))
                {
                    continue;
                }
                foreach (var uniIpAddrInfo in netI.GetIPProperties().UnicastAddresses.Where(x => netI.GetIPProperties().GatewayAddresses.Count > 0))
                {
                    if ((uniIpAddrInfo.Address.AddressFamily == AddressFamily.InterNetwork ||
                        uniIpAddrInfo.Address.AddressFamily == AddressFamily.InterNetworkV6) &&
                        uniIpAddrInfo.AddressPreferredLifetime != uint.MaxValue)
                    {
                        addresses.Add(uniIpAddrInfo.Address);
                    }
                }
            }
            return addresses.ToArray();
        }

        /// <summary>
        /// Get first active nic on local computer
        /// </summary>
        /// <returns></returns>
        private static IPAddress GetFirstActiveNic()
        {
            try
            {   // get host IP addresses
                IPAddress[] hostIPs = Dns.GetHostAddresses(Dns.GetHostName());
                // get local IP addresses
                IPAddress[] localIPs = GetLocalIpAddresses();

                // test if any host IP equals to any local IP or to localhost
                foreach (IPAddress hostIP in hostIPs)
                {
                    // is loopback type?
                    if (IPAddress.IsLoopback(hostIP))
                    {
                        continue;
                    }
                    // ip address available
                    foreach (IPAddress localIP in localIPs)
                    {
                        if (localIP.AddressFamily == AddressFamily.InterNetwork &&
                            hostIP.Equals(localIP))
                        {
                            return localIP;
                        }
                    }
                }
            }
            catch
            {
            }
            Assert.Inconclusive("First active NIC was not found.");

            return null;
        }
        #endregion
    }
}
