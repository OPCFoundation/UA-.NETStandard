/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Net.NetworkInformation;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Validates the <c>opc.eth://</c> URL parser produced by
    /// <see cref="EthEndpointParser"/> for the OPC UA Part 14 Ethernet
    /// mapping addressing model.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.3", Summary = "Ethernet transport addressing")]
    public sealed class EthEndpointParserTests
    {
        [Test]
        public void ParseDashMacUnicast()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse("opc.eth://00-11-22-33-44-55");

            Assert.Multiple(() =>
            {
                Assert.That(
                    endpoint.Address.GetAddressBytes(),
                    Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }));
                Assert.That(endpoint.AddressType, Is.EqualTo(EthAddressType.Unicast));
                Assert.That(endpoint.VlanId, Is.Null);
                Assert.That(endpoint.Priority, Is.Null);
                Assert.That(endpoint.IsValid, Is.True);
                Assert.That(endpoint.OriginalUrl, Is.EqualTo("opc.eth://00-11-22-33-44-55"));
            });
        }

        [Test]
        public void ParseColonMacEqualsDashMac()
        {
            EthEndpoint colon = EthEndpointParser.Parse("opc.eth://00:11:22:33:44:55");
            EthEndpoint hex = EthEndpointParser.Parse("opc.eth://001122334455");

            Assert.Multiple(() =>
            {
                Assert.That(
                    colon.Address.GetAddressBytes(),
                    Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }));
                Assert.That(
                    hex.Address.GetAddressBytes(),
                    Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }));
            });
        }

        [Test]
        public void ParseMulticastAddressSetsMulticast()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse("opc.eth://01-00-5E-00-00-01");
            Assert.That(endpoint.AddressType, Is.EqualTo(EthAddressType.Multicast));
        }

        [Test]
        public void ParseBroadcastAddressSetsBroadcast()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse("opc.eth://FF-FF-FF-FF-FF-FF");
            Assert.That(endpoint.AddressType, Is.EqualTo(EthAddressType.Broadcast));
        }

        [Test]
        public void ParseQueryVlanAndPriority()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse(
                "opc.eth://00-11-22-33-44-55?vid=5&pcp=6");

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.VlanId, Is.EqualTo((ushort)5));
                Assert.That(endpoint.Priority, Is.EqualTo((byte)6));
            });
        }

        [Test]
        public void ParseLegacyVlanSuffix()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse("opc.eth://00-11-22-33-44-55:5.6");

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.VlanId, Is.EqualTo((ushort)5));
                Assert.That(endpoint.Priority, Is.EqualTo((byte)6));
            });
        }

        [Test]
        public void ParseLegacyVlanSuffixOnColonMac()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse("opc.eth://00:11:22:33:44:55:5.6");

            Assert.Multiple(() =>
            {
                Assert.That(
                    endpoint.Address.GetAddressBytes(),
                    Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }));
                Assert.That(endpoint.VlanId, Is.EqualTo((ushort)5));
                Assert.That(endpoint.Priority, Is.EqualTo((byte)6));
            });
        }

        [Test]
        public void ParseVlanWithoutPriority()
        {
            EthEndpoint endpoint = EthEndpointParser.Parse("opc.eth://00-11-22-33-44-55?vid=10");

            Assert.Multiple(() =>
            {
                Assert.That(endpoint.VlanId, Is.EqualTo((ushort)10));
                Assert.That(endpoint.Priority, Is.Null);
            });
        }

        [Test]
        public void ParseNullThrowsArgumentNull()
        {
            Assert.That(() => EthEndpointParser.Parse(null!), Throws.ArgumentNullException);
        }

        [Test]
        [TestCase("")]
        [TestCase("opc.udp://00-11-22-33-44-55")]
        [TestCase("opc.eth://")]
        [TestCase("opc.eth://zz-11-22-33-44-55")]
        [TestCase("opc.eth://00-11-22-33-44-55?vid=4096")]
        [TestCase("opc.eth://00-11-22-33-44-55?pcp=8")]
        [TestCase("opc.eth://00-11-22-33-44-55?bad=1")]
        public void ParseInvalidUrlThrowsFormat(string url)
        {
            Assert.That(() => EthEndpointParser.Parse(url), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ClassifyAddressMatchesIgBit()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    EthEndpointParser.ClassifyAddress(
                        new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 })),
                    Is.EqualTo(EthAddressType.Unicast));
                Assert.That(
                    EthEndpointParser.ClassifyAddress(
                        new PhysicalAddress(new byte[] { 0x01, 0x00, 0x5E, 0x00, 0x00, 0x01 })),
                    Is.EqualTo(EthAddressType.Multicast));
                Assert.That(
                    EthEndpointParser.ClassifyAddress(
                        new PhysicalAddress(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })),
                    Is.EqualTo(EthAddressType.Broadcast));
            });
        }

        [Test]
        public void ClassifyAddressNullThrows()
        {
            Assert.That(() => EthEndpointParser.ClassifyAddress((PhysicalAddress)null!), Throws.ArgumentNullException);
        }
    }
}
