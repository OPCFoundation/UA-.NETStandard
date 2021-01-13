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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using Opc.Ua.PubSub.Uadp;

namespace Opc.Ua.PubSub.Tests
{
    public partial class UdpClientCreatorTests
    {
        private const string PublisherConfigurationFileName = "PublisherConfiguration.xml";

        private const string UrlScheme = "opc.udp://";
        // generic well known address
        private string UrlHostName = "192.168.0.1"; 
        private const int UrlPortNo = 4840;

        private string m_defaultUrl;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            var localhost = UadpPubSubConnectionTests.GetFirstNic();
            if(localhost != null && localhost.Address != null)
            {
                UrlHostName = localhost.Address.ToString();
            }
            m_defaultUrl = string.Concat(UrlScheme, UrlHostName, ":", UrlPortNo);
        }

        [Test(Description = "Validate url value")]
        public void ValidateUdpClientCreatorGetEndPoint()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(m_defaultUrl);
            Assert.IsNotNull(ipEndPoint, "GetEndPoint failed: ipEndPoint is null");

            Assert.AreEqual(ipEndPoint.Address.ToString(), UrlHostName, "The url hostname: {0} is not equal to specified hostname: {1}", ipEndPoint.Address.ToString(), UrlHostName);
            Assert.AreEqual(ipEndPoint.Port, UrlPortNo, "The url port: {0} is not equal to specified port: {1}", ipEndPoint.Port, UrlPortNo);
        }

        [Test(Description = "Invalidate url Scheme value")]
        public void InvalidateUdpClientCreatorUrlScheme()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat("opc.udp:", UrlHostName, ":", UrlPortNo));
            Assert.IsNull(ipEndPoint, "Url scheme is good!?");
        }

        [Test(Description = "Invalidate url Hostname value")]
        public void InvalidateUdpClientCreatorUrlHostName()
        {
            string urlHostNameChanged = "192.168.0.280";
            string localhostIP = ReplaceLastIpByte(UrlHostName, "280");
            if (localhostIP != null)
            {
                urlHostNameChanged = localhostIP;
            }
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(UrlScheme, urlHostNameChanged, ":", UrlPortNo));
            Assert.IsNull(ipEndPoint, "Url hostname is good!?");
        }

        [Test(Description = "Invalidate url Port number value")]
        public void InvalidateUdpClientCreatorUrlPort()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(UrlScheme, UrlHostName, ":", "0"));
            Assert.IsNull(ipEndPoint, "Url port number is wrong");
        }

        [Test(Description = "Validate url hostname as ip address value")]
        public void ValidateUdpClientCreatorUrlIPAddress()
        {
            string urlHostNameChanged = "192.168.0.200";
            string localhostIP = ReplaceLastIpByte(UrlHostName, "200");
            if (localhostIP != null )
            {
                urlHostNameChanged = localhostIP;
            }
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(UrlScheme, urlHostNameChanged, ":", UrlPortNo));
            Assert.IsNotNull(ipEndPoint, "Url hostname(address) is good!?");
        }

        [Test(Description = "Validate url hostname as computer bane value (DNS might be necessary)")]
        public void ValidateUdpClientCreatorUrlHostname()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(UrlScheme, Environment.MachineName, ":", UrlPortNo));
            Assert.IsNotNull(ipEndPoint, "Url hostname is good!?");
        }

        [Test(Description = "Validate GetUdpClients value")]
        public void ValidateUdpClientCreatorGetUdpClients()
        {
            // Create a publisher application
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(PublisherConfigurationFileName);
            Assert.IsNotNull(publisherApplication, "m_publisherApplication should not be null");

            // Get the publisher configuration
            PubSubConfigurationDataType publisherConfiguration = publisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(publisherConfiguration, "m_publisherConfiguration should not be null");

            // Check publisher connections
            Assert.IsNotNull(publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be empty");

            PubSubConnectionDataType publisherConnection1 = publisherConfiguration.Connections[0];
            Assert.IsNotNull(publisherConnection1, "publisherConnection1 should not be null");

            NetworkAddressUrlDataType networkAddressUrlState1 = ExtensionObject.ToEncodeable(publisherConnection1.Address)
                as NetworkAddressUrlDataType;
            Assert.IsNotNull(networkAddressUrlState1, "networkAddressUrlState1 is null");

            IPEndPoint configuredEndPoint1 = UdpClientCreator.GetEndPoint(networkAddressUrlState1.Url);
            Assert.IsNotNull(configuredEndPoint1, "configuredEndPoint1 is null");

            List<UdpClient> udpClients1 =  UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState1, configuredEndPoint1);
            Assert.IsNotNull(udpClients1, "udpClients1 is null");
            Assert.IsNotEmpty(udpClients1, "udpClients1 is empty");

            UdpClient udpClient1 = udpClients1[0];
            Assert.IsTrue(udpClient1 is UdpClientMulticast, "udpClient1 was configured as UdpClientMulticast");
            Assert.IsNotNull(udpClient1.Client.LocalEndPoint, "udpClient1 IP address is empty");

            PubSubConnectionDataType publisherConnection2 = publisherConfiguration.Connections[1];
            Assert.IsNotNull(publisherConnection2, "publisherConnection2 should not be null");

            NetworkAddressUrlDataType networkAddressUrlState2 = ExtensionObject.ToEncodeable(publisherConnection2.Address)
                as NetworkAddressUrlDataType;
            Assert.IsNotNull(networkAddressUrlState2, "networkAddressUrlState2 is null");

            IPEndPoint configuredEndPoint2 = UdpClientCreator.GetEndPoint(networkAddressUrlState2.Url);
            Assert.IsNotNull(configuredEndPoint2, "configuredEndPoint2 is null");

            List<UdpClient> udpClients2 = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState2, configuredEndPoint2);
            Assert.IsNotNull(udpClients2, "udpClients2 is null");
            Assert.IsNotEmpty(udpClients2, "udpClients2 is empty");

            UdpClient udpClient2 = udpClients2[0];
            Assert.IsTrue(udpClient2 is UdpClientBroadcast, "udpClient2 was configured as UdpClientBroadcast");
            Assert.IsNotNull(udpClient2.Client.LocalEndPoint, "udpClient2 IP address is empty");

            IPEndPoint udpClientEndPoint1 = udpClient1.Client.LocalEndPoint as IPEndPoint;
            Assert.IsNotNull(udpClientEndPoint1, "udpClientEndPoint1 could not be cast to IPEndPoint");

            IPEndPoint udpClientEndPoint2 = udpClient2.Client.LocalEndPoint as IPEndPoint;
            Assert.IsNotNull(udpClientEndPoint2, "udpClientEndPoint2 could not be cast to IPEndPoint");

            Assert.AreEqual(udpClientEndPoint1.Address.ToString(), udpClientEndPoint2.Address.ToString(), "udpClientEndPoint1 IP address: {0} should match udpClientEndPoint2 IP Address {1}", udpClientEndPoint1.Address.ToString(), udpClientEndPoint2.Address.ToString());
            Assert.AreNotEqual(udpClientEndPoint1.Port, udpClientEndPoint2.Port, "udpClientEndPoint1 port number: {0} should not match udpClientEndPoint1 port number: {1}", udpClientEndPoint1.Port, udpClientEndPoint2.Port);
        }

        #region Helper methods
        private string ReplaceLastIpByte(string ipAddress, string lastIpByte)
        {
            string newIPAddress = null;
            try
            {
                IPAddress validIp = null;
                bool isValidIP = IPAddress.TryParse(ipAddress, out validIp);
                if (isValidIP)
                {
                    byte[] ipAddressBytes = validIp.GetAddressBytes();
                    for (int pos = 0; pos < ipAddressBytes.Count() - 1; pos++)
                    {
                        newIPAddress += string.Format("{0}.", ipAddressBytes[pos]);
                    }
                    newIPAddress += lastIpByte;
                    return newIPAddress;
                }
            }
            catch
            {

            }
            return newIPAddress;
        }
        #endregion
    }
}
