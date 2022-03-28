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
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;

namespace Opc.Ua.PubSub.Tests.Transport
{
    public partial class UdpClientCreatorTests
    {
        private string m_publisherConfigurationFileName = Path.Combine("Configuration", "PublisherConfiguration.xml");
        private string m_urlScheme = string.Format("{0}://", Utils.UriSchemeOpcUdp);

        // generic well known address
        private string m_urlHostName = "192.168.0.1";
        private const int kDiscoveryPortNo = 4840;

        private string m_defaultUrl;

        [OneTimeSetUp()]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void MyTestInitialize()
        {
            var localhost = UdpPubSubConnectionTests.GetFirstNic();
            if (localhost != null && localhost.Address != null)
            {
                m_urlHostName = localhost.Address.ToString();
            }
            m_defaultUrl = string.Concat(m_urlScheme, m_urlHostName, ":", kDiscoveryPortNo);
        }

        [Test(Description = "Validate url value")]
        public void ValidateUdpClientCreatorGetEndPoint()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(m_defaultUrl);
            Assert.IsNotNull(ipEndPoint, "GetEndPoint failed: ipEndPoint is null");

            Assert.AreEqual(ipEndPoint.Address.ToString(), m_urlHostName, "The url hostname: {0} is not equal to specified hostname: {1}", ipEndPoint.Address.ToString(), m_urlHostName);
            Assert.AreEqual(ipEndPoint.Port, kDiscoveryPortNo, "The url port: {0} is not equal to specified port: {1}", ipEndPoint.Port, kDiscoveryPortNo);
        }

        [Test(Description = "Invalidate url Scheme value")]
        public void InvalidateUdpClientCreatorUrlScheme()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(Utils.UriSchemeOpcUdp, ":", m_urlHostName, ":", kDiscoveryPortNo));
            Assert.IsNull(ipEndPoint, "Url scheme is not corect!");
        }

        [Test(Description = "Invalidate url Hostname value")]
        public void InvalidateUdpClientCreatorUrlHostName()
        {
            string urlHostNameChanged = "192.168.0.280";
            string localhostIP = ReplaceLastIpByte(m_urlHostName, "280");
            if (localhostIP != null)
            {
                urlHostNameChanged = localhostIP;
            }
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(m_urlScheme, urlHostNameChanged, ":", kDiscoveryPortNo));
            Assert.IsNull(ipEndPoint, "Url hostname is not corect!");
        }

        [Test(Description = "Invalidate url Port number value")]
        public void InvalidateUdpClientCreatorUrlPort()
        {
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(m_urlScheme, m_urlHostName, ":", "0"));
            Assert.IsNull(ipEndPoint, "Url port number is wrong");
        }

        [Test(Description = "Validate url hostname as ip address value")]
        public void ValidateUdpClientCreatorUrlIPAddress()
        {
            string urlHostNameChanged = "192.168.0.200";
            string localhostIP = ReplaceLastIpByte(m_urlHostName, "200");
            if (localhostIP != null)
            {
                urlHostNameChanged = localhostIP;
            }
            var address = string.Concat(m_urlScheme, urlHostNameChanged, ":", kDiscoveryPortNo);
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(address);
            Assert.IsNotNull(ipEndPoint, $"Url hostname({address}) is not correct!");
        }

        [Test(Description = "Validate url hostname as computer bane value (DNS might be necessary)")]
        public void ValidateUdpClientCreatorUrlHostname()
        {
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Skip UdpClientCreatorUrl test on mac OS.");
            }

            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(string.Concat(m_urlScheme, Environment.MachineName, ":", kDiscoveryPortNo));
            Assert.IsNotNull(ipEndPoint, "Url hostname is not corect!");
        }

        [Test(Description = "Validate GetUdpClients value")]
#if !CUSTOM_TESTS
        [Ignore("This test should be executed locally")]
#endif
        public void ValidateUdpClientCreatorGetUdpClients()
        {
            // Create a publisher application
            string configurationFile = Utils.GetAbsoluteFilePath(m_publisherConfigurationFileName, true, true, false);
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(configurationFile);
            Assert.IsNotNull(publisherApplication, "m_publisherApplication should not be null");

            // Get the publisher configuration
            PubSubConfigurationDataType publisherConfiguration = publisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Check publisher connections
            Assert.IsNotNull(publisherConfiguration.Connections, "publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(publisherConfiguration.Connections, "publisherConfiguration.Connections should not be empty");

            PubSubConnectionDataType publisherConnection1 = publisherConfiguration.Connections.First();
            Assert.IsNotNull(publisherConnection1, "publisherConnection1 should not be null");

            NetworkAddressUrlDataType networkAddressUrlState1 = ExtensionObject.ToEncodeable(publisherConnection1.Address)
                as NetworkAddressUrlDataType;
            Assert.IsNotNull(networkAddressUrlState1, "networkAddressUrlState1 is null");

            IPEndPoint configuredEndPoint1 = UdpClientCreator.GetEndPoint(networkAddressUrlState1.Url);
            Assert.IsNotNull(configuredEndPoint1, "configuredEndPoint1 is null");

            List<UdpClient> udpClients1 = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState1.NetworkInterface, configuredEndPoint1);
            Assert.IsNotNull(udpClients1, "udpClients1 is null");
            Assert.IsNotEmpty(udpClients1, "udpClients1 is empty");

            UdpClient udpClient1 = udpClients1[0];
            Assert.IsTrue(udpClient1 is UdpClientMulticast, "udpClient1 was configured as UdpClientMulticast");
            Assert.IsNotNull(udpClient1.Client, "udpClient1 client socket should not be null");
            Assert.IsNotNull(udpClient1.Client.LocalEndPoint, "udpClient1 IP address is empty");

            PubSubConnectionDataType publisherConnection2 = publisherConfiguration.Connections[1];
            Assert.IsNotNull(publisherConnection2, "publisherConnection2 should not be null");

            NetworkAddressUrlDataType networkAddressUrlState2 = ExtensionObject.ToEncodeable(publisherConnection2.Address)
                as NetworkAddressUrlDataType;
            Assert.IsNotNull(networkAddressUrlState2, "networkAddressUrlState2 is null");

            IPEndPoint configuredEndPoint2 = UdpClientCreator.GetEndPoint(networkAddressUrlState2.Url);
            Assert.IsNotNull(configuredEndPoint2, "configuredEndPoint2 is null");

            List<UdpClient> udpClients2 = UdpClientCreator.GetUdpClients(UsedInContext.Publisher, networkAddressUrlState2.NetworkInterface, configuredEndPoint2);
            Assert.IsNotNull(udpClients2, "udpClients2 is null");
            Assert.IsNotEmpty(udpClients2, "udpClients2 is empty");

            UdpClient udpClient2 = udpClients2[0];
            Assert.IsTrue(udpClient2 is UdpClientBroadcast, "udpClient2 was configured as UdpClientBroadcast");
            Assert.IsNotNull(udpClient2.Client, "udpClient1 client socket should not be null");
            Assert.IsNotNull(udpClient2.Client.LocalEndPoint, "udpClient2 IP address is empty");

            IPEndPoint udpClientEndPoint1 = udpClient1.Client.LocalEndPoint as IPEndPoint;
            Assert.IsNotNull(udpClientEndPoint1, "udpClientEndPoint1 could not be cast to IPEndPoint");

            IPEndPoint udpClientEndPoint2 = udpClient2.Client.LocalEndPoint as IPEndPoint;
            Assert.IsNotNull(udpClientEndPoint2, "udpClientEndPoint2 could not be cast to IPEndPoint");

            Assert.AreEqual(udpClientEndPoint1.Address.ToString(), udpClientEndPoint2.Address.ToString(), "udpClientEndPoint1 IP address: {0} should match udpClientEndPoint2 IP Address {1}", udpClientEndPoint1.Address.ToString(), udpClientEndPoint2.Address.ToString());
            Assert.AreNotEqual(udpClientEndPoint1.Port, udpClientEndPoint2.Port, "udpClientEndPoint1 port number: {0} should not match udpClientEndPoint1 port number: {1}", udpClientEndPoint1.Port, udpClientEndPoint2.Port);
        }

        #region Private methods
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
                    for (int pos = 0; pos < ipAddressBytes.Length - 1; pos++)
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
