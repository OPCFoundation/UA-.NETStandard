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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transport
{
    public class UdpClientCreatorTests
    {
        private readonly string m_publisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private readonly string m_urlScheme = Utils.Format("{0}://", Utils.UriSchemeOpcUdp);

        /// <summary>
        /// generic well known address
        /// </summary>
        private string m_urlHostName = "192.168.0.1";
        private const int kDiscoveryPortNo = 4840;

        private string m_defaultUrl;

        [OneTimeSetUp]
#if !CUSTOM_TESTS
        [Ignore("A network interface controller is necessary in order to run correctly.")]
#endif
        public void MyTestInitialize()
        {
            System.Net.NetworkInformation.UnicastIPAddressInformation localhost =
                UdpPubSubConnectionTests.GetFirstNic();
            if (localhost != null && localhost.Address != null)
            {
                m_urlHostName = localhost.Address.ToString();
            }
            m_defaultUrl = $"{m_urlScheme}{m_urlHostName}:{kDiscoveryPortNo}";
        }

        [Test(Description = "Validate url value")]
        public void ValidateUdpClientCreatorGetEndPoint()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(m_defaultUrl, logger);
            Assert.That(ipEndPoint, Is.Not.Null, "GetEndPoint failed: ipEndPoint is null");

            Assert.That(
                m_urlHostName,
                Is.EqualTo(ipEndPoint.Address.ToString()),
                $"The url hostname: {ipEndPoint.Address} is not equal to specified hostname: {m_urlHostName}");
            Assert.That(
                ipEndPoint.Port,
                Is.EqualTo(kDiscoveryPortNo),
                $"The url port: {ipEndPoint.Port} is not equal to specified port: {kDiscoveryPortNo}");
        }

        [Test(Description = "Invalidate url Scheme value")]
        public void InvalidateUdpClientCreatorUrlScheme()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(
                $"{Utils.UriSchemeOpcUdp}:{m_urlHostName}:{kDiscoveryPortNo}",
                logger);
            Assert.That(ipEndPoint, Is.Null, "Url scheme is not corect!");
        }

        [Test(Description = "Invalidate url Hostname value")]
        public void InvalidateUdpClientCreatorUrlHostName()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();
            string urlHostNameChanged = "192.168.0.280";
            string localhostIP = ReplaceLastIpByte(m_urlHostName, "280");
            if (localhostIP != null)
            {
                urlHostNameChanged = localhostIP;
            }
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(
                $"{m_urlScheme}{urlHostNameChanged}:{kDiscoveryPortNo}",
                logger);
            Assert.That(ipEndPoint, Is.Null, "Url hostname is not corect!");
        }

        [Test(Description = "Invalidate url Port number value")]
        public void InvalidateUdpClientCreatorUrlPort()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(
                $"{m_urlScheme}{m_urlHostName}: 0",
                logger);
            Assert.That(ipEndPoint, Is.Null, "Url port number is wrong");
        }

        [Test(Description = "Validate url hostname as ip address value")]
        public void ValidateUdpClientCreatorUrlIPAddress()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();
            string urlHostNameChanged = "192.168.0.200";
            string localhostIP = ReplaceLastIpByte(m_urlHostName, "200");
            if (localhostIP != null)
            {
                urlHostNameChanged = localhostIP;
            }
            string address = $"{m_urlScheme}{urlHostNameChanged}:{kDiscoveryPortNo}";
            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(address, logger);
            Assert.That(ipEndPoint, Is.Not.Null, $"Url hostname({address}) is not correct!");
        }

        [Test(
            Description = "Validate url hostname as computer bane value (DNS might be necessary)")]
        public void ValidateUdpClientCreatorUrlHostname()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();
            // this test fails on macOS, ignore
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Skip UdpClientCreatorUrl test on mac OS.");
            }

            IPEndPoint ipEndPoint = UdpClientCreator.GetEndPoint(
                $"{m_urlScheme}{Environment.MachineName}:{kDiscoveryPortNo}",
                logger);
            Assert.That(ipEndPoint, Is.Not.Null, "Url hostname is not corect!");
        }

        [Test(Description = "Validate GetUdpClients value")]
#if !CUSTOM_TESTS
        [Ignore("This test should be executed locally")]
#endif
        public void ValidateUdpClientCreatorGetUdpClients()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger<UdpClientCreatorTests> logger = telemetry.CreateLogger<UdpClientCreatorTests>();

            // Create a publisher application
            string configurationFile = Utils.GetAbsoluteFilePath(
                m_publisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            using var publisherApplication = UaPubSubApplication.Create(configurationFile, telemetry);
            Assert.That(publisherApplication, Is.Not.Null, "m_publisherApplication should not be null");

            // Get the publisher configuration
            PubSubConfigurationDataType publisherConfiguration = publisherApplication
                .UaPubSubConfigurator
                .PubSubConfiguration;
            Assert.That(publisherConfiguration, Is.Not.Null, "publisherConfiguration should not be null");

            // Check publisher connections
            Assert.That(
                publisherConfiguration.Connections.IsEmpty,
                Is.False,
                "publisherConfiguration.Connections should not be empty");

            PubSubConnectionDataType publisherConnection1 = publisherConfiguration.Connections[0];
            Assert.That(publisherConnection1, Is.Not.Null, "publisherConnection1 should not be null");

            var networkAddressUrlState1 =
                ExtensionObject.ToEncodeable(
                    publisherConnection1.Address) as NetworkAddressUrlDataType;
            Assert.That(networkAddressUrlState1, Is.Not.Null, "networkAddressUrlState1 is null");

            IPEndPoint configuredEndPoint1 = UdpClientCreator.GetEndPoint(
                networkAddressUrlState1.Url,
                logger);
            Assert.That(configuredEndPoint1, Is.Not.Null, "configuredEndPoint1 is null");

            List<UdpClient> udpClients1 = UdpClientCreator.GetUdpClients(
                UsedInContext.Publisher,
                networkAddressUrlState1.NetworkInterface,
                configuredEndPoint1,
                telemetry,
                logger);
            Assert.That(udpClients1, Is.Not.Null, "udpClients1 is null");
            Assert.IsNotEmpty(udpClients1, "udpClients1 is empty");

            UdpClient udpClient1 = udpClients1[0];
            Assert.That(
                udpClient1,
Is.InstanceOf<UdpClientMulticast>());
            Assert.That(udpClient1.Client, Is.Not.Null, "udpClient1 client socket should not be null");
            Assert.That(udpClient1.Client.LocalEndPoint, Is.Not.Null, "udpClient1 IP address is empty");

            PubSubConnectionDataType publisherConnection2 = publisherConfiguration.Connections[1];
            Assert.That(publisherConnection2, Is.Not.Null, "publisherConnection2 should not be null");

            var networkAddressUrlState2 =
                ExtensionObject.ToEncodeable(
                    publisherConnection2.Address) as NetworkAddressUrlDataType;
            Assert.That(networkAddressUrlState2, Is.Not.Null, "networkAddressUrlState2 is null");

            IPEndPoint configuredEndPoint2 = UdpClientCreator.GetEndPoint(
                networkAddressUrlState2.Url,
                logger);
            Assert.That(configuredEndPoint2, Is.Not.Null, "configuredEndPoint2 is null");

            List<UdpClient> udpClients2 = UdpClientCreator.GetUdpClients(
                UsedInContext.Publisher,
                networkAddressUrlState2.NetworkInterface,
                configuredEndPoint2,
                telemetry,
                logger);
            Assert.That(udpClients2, Is.Not.Null, "udpClients2 is null");
            Assert.IsNotEmpty(udpClients2, "udpClients2 is empty");

            UdpClient udpClient2 = udpClients2[0];
            Assert.That(
                udpClient2,
Is.InstanceOf<UdpClientBroadcast>());
            Assert.That(udpClient2.Client, Is.Not.Null, "udpClient1 client socket should not be null");
            Assert.That(udpClient2.Client.LocalEndPoint, Is.Not.Null, "udpClient2 IP address is empty");

            var udpClientEndPoint1 = udpClient1.Client.LocalEndPoint as IPEndPoint;
            Assert.That(
                udpClientEndPoint1,
                Is.Not.Null,
                "udpClientEndPoint1 could not be cast to IPEndPoint");

            var udpClientEndPoint2 = udpClient2.Client.LocalEndPoint as IPEndPoint;
            Assert.That(
                udpClientEndPoint2,
                Is.Not.Null,
                "udpClientEndPoint2 could not be cast to IPEndPoint");

            Assert.That(
                udpClientEndPoint2.Address.ToString(),
                Is.EqualTo(udpClientEndPoint1.Address.ToString()),
                $"udpClientEndPoint1 IP address: {udpClientEndPoint1.Address} should match udpClientEndPoint2 IP Address {udpClientEndPoint2.Address}");
            Assert.That(
                udpClientEndPoint2.Port,
                Is.Not.EqualTo(udpClientEndPoint1.Port),
                $"udpClientEndPoint1 port number: {udpClientEndPoint1.Port} should not match udpClientEndPoint1 port number: {udpClientEndPoint2.Port}");
        }

        private static string ReplaceLastIpByte(string ipAddress, string lastIpByte)
        {
            string newIPAddress = null;
            try
            {
                bool isValidIP = IPAddress.TryParse(ipAddress, out IPAddress validIp);
                if (isValidIP)
                {
                    byte[] ipAddressBytes = validIp.GetAddressBytes();
                    for (int pos = 0; pos < ipAddressBytes.Length - 1; pos++)
                    {
                        newIPAddress += Utils.Format("{0}.", ipAddressBytes[pos]);
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
    }
}
