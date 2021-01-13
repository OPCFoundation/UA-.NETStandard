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
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Uadp;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Opc.Ua.PubSub.Tests
{
    
    [TestFixture(Description = "Tests for UadpPubSubConnection class - Publisher ")]
    public partial class UadpPubSubConnectionTests
    {
        [Test(Description = "Validate unicast PublishNetworkMessage")]
        public void ValidateUadpPubSubConnectionNetworkMessagePublishUnicast()
        {
            //Arrange 
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //create publisher configuration object with modified port
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            IPAddress unicastIPAddress = localhost.Address;
            Assert.IsNotNull(unicastIPAddress, "unicastIPAddress is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://" + unicastIPAddress.ToString() + ":4840";
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);

            //create publisher UaPubSubApplication with changed configuration settings
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UadpPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            // will signal that the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            //setup uadp client for receiving from multicast (simulate a subscriber unicast)
            UdpClient udpUnicastClient = new UdpClientUnicast(localhost.Address, 4840);
            Assert.IsNotNull(udpUnicastClient, "udpUnicastClient is null");
            udpUnicastClient.BeginReceive(new AsyncCallback(OnReceive), udpUnicastClient);

            // prepare a network message
            WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration.WriterGroups[0];
            UaNetworkMessage networkMessage = publisherConnection.CreateNetworkMessage(writerGroup0);

            //Act  
            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(networkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            publisherConnection.Stop();
            udpUnicastClient.Close();
            udpUnicastClient.Dispose();
        }

        [Test(Description = "Validate broadcast PublishNetworkMessage")]
        public void ValidateUadpPubSubConnectionNetworkMessagePublishBroadcast() 
        {
            //Arrange 
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //create publisher configuration object with modified port
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            IPAddress broadcastIPAddress = GetFirstNicLastIPByteChanged(255);
            Assert.IsNotNull(broadcastIPAddress, "broadcastIPAddress is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://" + broadcastIPAddress.ToString() + ":4840";
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);

            //create publisher UaPubSubApplication with changed configuration settings
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UadpPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            // will signal that the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            //setup uadp client for receiving from broadcast (simulate a subscriber broadcast)
            UdpClient udpBroadcastClient = new UdpClientBroadcast(localhost.Address, 4840, UsedInContext.Subscriber);
            udpBroadcastClient.BeginReceive(new AsyncCallback(OnReceive), udpBroadcastClient);

            // prepare a network message
            WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration.WriterGroups[0];
            UaNetworkMessage networkMessage = publisherConnection.CreateNetworkMessage(writerGroup0);

            //Act  
            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(networkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            publisherConnection.Stop();
            udpBroadcastClient.Close();
            udpBroadcastClient.Dispose();
        }

        [Test(Description = "Validate multicast PublishNetworkMessage")]
        public void ValidateUadpPubSubConnectionNetworkMessagePublishMulticast()
        {
            //Arrange 
            var localhost = GetFirstNic();
            Assert.IsNotNull(localhost, "localhost is null");
            Assert.IsNotNull(localhost.Address, "localhost.Address is null");

            //create publisher configuration object with modified port
            PubSubConfigurationDataType publisherConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration is null");

            IPAddress multicastIPAddress = new IPAddress(new byte[4] { 239, 0, 0, 1 });
            Assert.IsNotNull(multicastIPAddress, "multicastIPAddress is null");

            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://" + multicastIPAddress.ToString() + ":4840";
            publisherConfiguration.Connections[0].Address = new ExtensionObject(publisherAddress);

            //create publisher UaPubSubApplication with changed configuration settings
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            Assert.IsNotNull(publisherApplication, "publisherApplication is null");

            UadpPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0] as UadpPubSubConnection;
            Assert.IsNotNull(publisherConnection, "publisherConnection is null");

            // will signal that the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            //setup uadp client for receiving from multicast (simulate a subscriber multicast)
            UdpClient udpMulticastClient = new UdpClientMulticast(localhost.Address, multicastIPAddress, 4840);
            udpMulticastClient.BeginReceive(new AsyncCallback(OnReceive), udpMulticastClient);

            // prepare a network message
            WriterGroupDataType writerGroup0 = publisherConnection.PubSubConnectionConfiguration.WriterGroups[0];
            UaNetworkMessage networkMessage = publisherConnection.CreateNetworkMessage(writerGroup0);

            //Act  
            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(networkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            publisherConnection.Stop();
            udpMulticastClient.Close();
            udpMulticastClient.Dispose();
        }

        /// <summary>
        /// Handle Receive event for an UADP channel
        /// </summary>
        /// <param name="result"></param>
        private void OnReceive(IAsyncResult result)
        {
            // this is what had been passed into BeginReceive as the second parameter:
            UdpClient socket = result.AsyncState as UdpClient;
            // points towards whoever had sent the message:
            IPEndPoint source = new IPEndPoint(0, 0);
            // get the actual message and fill out the source:
            byte[] message = socket.EndReceive(result, ref source);
            if (IsHostAddress(source.Address.ToString()))
            {
                //signal that uadp message was received from local ip
                m_shutdownEvent.Set();
                return;
            }
            // schedule the next receive operation once reading is done:
            socket.BeginReceive(new AsyncCallback(OnReceive), socket);
        }

    }
}
