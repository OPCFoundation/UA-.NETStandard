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

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub;
using Opc.Ua.PubSub.Uadp;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for UadpPubSubConnection class")]
    public partial class UadpPubSubConnectionTests
    {
        private const int EstimatedPublishingTime = 6000;

        private const string PublisherConfigurationFileName = "PublisherConfiguration.xml";
        private const string SubscriberConfigurationFileName = "SubscriberConfiguration.xml";

        private PubSubConfigurationDataType m_publisherConfiguration;
        private UaPubSubApplication m_uaPublisherApplication;
        private UadpPubSubConnection m_uadpPublisherConnection;

        private ManualResetEvent m_shutdownEvent;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            // Create a publisher application
            m_uaPublisherApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            Assert.IsNotNull(m_uaPublisherApplication, "m_publisherApplication should not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_uaPublisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_publisherConfiguration, "m_publisherConfiguration should not be null");

            // Get publisher connection
            Assert.IsNotNull(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be empty");
            m_uadpPublisherConnection = m_uaPublisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(m_uadpPublisherConnection, "m_uadpPublisherConnection should not be null");
        }

        [Test(Description = "Validate TransportProtocol value")]
        public void ValidateUadpPubSubConnectionTransportProtocol()
        {
            //Assert
            Assert.IsNotNull(m_uadpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            Assert.IsTrue(m_uadpPublisherConnection.TransportProtocol == TransportProtocol.UADP,
                "The UADP connection has wrong TransportProtocol {0}", m_uadpPublisherConnection.TransportProtocol);
        }

        [Test(Description = "Validate PubSubConnectionConfiguration value")]
        public void ValidateUadpPubSubConnectionPubSubConnectionConfiguration()
        {
            //Assert
            Assert.IsNotNull(m_uadpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            PubSubConnectionDataType connectionConfiguration = m_uadpPublisherConnection.PubSubConnectionConfiguration;
            PubSubConnectionDataType originalConnectionConfiguration = m_publisherConfiguration.Connections[0];
            Assert.IsNotNull(connectionConfiguration, "The UADP connection configuration from UADP connection object is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.Name, connectionConfiguration.Name, "The connection configuration Name is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.PublisherId, connectionConfiguration.PublisherId, "The connection configuration PublisherId is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.Address, connectionConfiguration.Address, "The connection configuration Address is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.Enabled, connectionConfiguration.Enabled, "The connection configuration Enabled is invalid.");
            Assert.AreEqual(originalConnectionConfiguration.TransportProfileUri, connectionConfiguration.TransportProfileUri, "The connection configuration TransportProfileUri is invalid.");

        }

        [Test(Description = "Validate Application value")]
        public void ValidateUadpPubSubConnectionApplication()
        {
            //Assert
            Assert.IsNotNull(m_uadpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            Assert.AreEqual(m_uadpPublisherConnection.Application, m_uaPublisherApplication, "The UADP connection Application reference is invalid.");
        }

        [Test(Description = "Validate Publishers value")]
        public void ValidateUadpPubSubConnectionPublishers()
        {
            //Assert
            Assert.IsNotNull(m_uadpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            Assert.IsNotNull(m_uadpPublisherConnection.Publishers, "The UADP connection Publishers is invalid.");
            Assert.AreEqual(1, m_uadpPublisherConnection.Publishers.Count, "The UADP connection Publishers.Count is invalid.");
            int index = 0;
            foreach (IUaPublisher publisher in m_uadpPublisherConnection.Publishers)
            {
                Assert.IsTrue(publisher != null, "connection.Publishers[{0}] is null", index);
                Assert.IsTrue(publisher.PubSubConnection == m_uadpPublisherConnection, "connection.Publishers[{0}].PubSubConnection is not set correctly", index);
                Assert.IsTrue(publisher.WriterGroupConfiguration.WriterGroupId == m_publisherConfiguration.Connections[0].WriterGroups[index].WriterGroupId, "connection.Publishers[{0}].WriterGroupConfiguration is not set correctly", index);
                index++;
            }
        }

        [Test(Description = "Validate CreateNetworkMessage")]
        public void ValidateUadpPubSubConnectionCreateNetworkMessage()
        {
            Assert.IsNotNull(m_uadpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            
            //Arrange
            WriterGroupDataType writerGroup0 = m_uadpPublisherConnection.PubSubConnectionConfiguration.WriterGroups[0];
            UadpWriterGroupMessageDataType messageSettings = ExtensionObject.ToEncodeable(writerGroup0.MessageSettings)
                as UadpWriterGroupMessageDataType;

            //Act  
            m_uadpPublisherConnection.ResetSequenceNumber();
            UadpNetworkMessage networkMessage0 = m_uadpPublisherConnection.CreateNetworkMessage(writerGroup0) as UadpNetworkMessage;

            //Assert
            Assert.IsNotNull(networkMessage0, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            Assert.AreEqual(networkMessage0.DataSetClassId, Guid.Empty, "UadpNetworkMessage.DataSetClassId is invalid.");
            Assert.AreEqual(networkMessage0.WriterGroupId, writerGroup0.WriterGroupId, "UadpNetworkMessage.WriterGroupId is invalid.");
            Assert.AreEqual(networkMessage0.UADPVersion, 1, "UadpNetworkMessage.UADPVersion is invalid.");
            Assert.AreEqual(networkMessage0.SequenceNumber, 1, "UadpNetworkMessage.SequenceNumber is not 1.");
            Assert.AreEqual(networkMessage0.GroupVersion, messageSettings.GroupVersion, "UadpNetworkMessage.GroupVersion is not valid.");
            Assert.AreEqual(networkMessage0.PublisherId, m_uadpPublisherConnection.PubSubConnectionConfiguration.PublisherId.Value,
                "UadpNetworkMessage.PublisherId is not valid.");
            Assert.IsNotNull(networkMessage0.UadpDataSetMessages, "UadpNetworkMessage.UadpDataSetMessages is null.");
            Assert.AreEqual(networkMessage0.UadpDataSetMessages.Count, 3, "UadpNetworkMessage.UadpDataSetMessages.Count is not 3.");
            //validate flags
            Assert.AreEqual((uint)networkMessage0.NetworkMessageContentMask, messageSettings.NetworkMessageContentMask,
                "UadpNetworkMessage.messageSettings.NetworkMessageContentMask is not valid.");

        }

        [Test(Description = "Validate CreateNetworkMessage SequenceNumber increment")]
        public void ValidateUadpPubSubConnectionCreateNetworkMessageSequenceNumber()
        {
            Assert.IsNotNull(m_uadpPublisherConnection, "The UADP connection from standard configuration is invalid.");
            //Arrange
            WriterGroupDataType writerGroup0 = m_uadpPublisherConnection.PubSubConnectionConfiguration.WriterGroups[0];

            //Act  
            m_uadpPublisherConnection.ResetSequenceNumber();
            for (int i = 0; i < 10; i++)
            {
                //Create network message
                UadpNetworkMessage networkMessage = m_uadpPublisherConnection.CreateNetworkMessage(writerGroup0) as UadpNetworkMessage;

                //Assert
                Assert.IsNotNull(networkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");
                Assert.AreEqual(networkMessage.SequenceNumber, i + 1, "UadpNetworkMessage.SequenceNumber for message {0} is not {0}.", i + 1);

                //validate dataset message sequence number
                Assert.IsNotNull(networkMessage.UadpDataSetMessages, "CreateNetworkMessage did not return an UadpNetworkMessage.UadpDataSetMessages.");
                Assert.IsTrue(networkMessage.UadpDataSetMessages.Count == 3, "CreateNetworkMessage did not return 3 UadpNetworkMessage.UadpDataSetMessages.");
                Assert.AreEqual(networkMessage.UadpDataSetMessages[0].SequenceNumber, i * 3 + 1, "UadpNetworkMessage.UadpDataSetMessages[0].SequenceNumber for message {0} is not {1}.", i + 1, i * 3 + 1);
                Assert.AreEqual(networkMessage.UadpDataSetMessages[1].SequenceNumber, i * 3 + 2, "UadpNetworkMessage.UadpDataSetMessages[1].SequenceNumber for message {0} is not {1}.", i + 1, i * 3 + 2);
                Assert.AreEqual(networkMessage.UadpDataSetMessages[2].SequenceNumber, i * 3 + 3, "UadpNetworkMessage.UadpDataSetMessages[2].SequenceNumber for message {0} is not {1}.", i + 1, i * 3 + 3);
            }
        }

        #region Helper methods
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
            var netInterface = interfaces.FirstOrDefault(nic => nic.GetIPProperties().UnicastAddresses.Any(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork));

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
                    ipAddressBytes[ipAddressBytes.Count() - 1] = lastIpByte;
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
        /// Get first active nic on local computer
        /// </summary>
        /// <returns></returns>
        private static IPAddress GetFirstActiveNic()
        {
            IPAddress firstActiveIPAddr = null;
            string localComputerName = Dns.GetHostName();
            try
            { // get host IP addresses
                IPAddress[] hostIPs = Dns.GetHostAddresses(localComputerName);
                // get local IP addresses
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

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
                        if (hostIP.Equals(localIP))
                        {
                            firstActiveIPAddr = localIP;
                        }
                    }
                }
            }
            catch
            {
            }

            return firstActiveIPAddr;
        }
        #endregion
    }
}
