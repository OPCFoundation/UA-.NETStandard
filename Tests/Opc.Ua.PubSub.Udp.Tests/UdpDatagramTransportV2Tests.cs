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
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Runtime consumption coverage for the
    /// <c>DatagramConnectionTransport2DataType</c> v2 fields
    /// (<c>DiscoveryAnnounceRate</c>, <c>DiscoveryMaxMessageSize</c>,
    /// <c>QosCategory</c>) defined by
    /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/6.4.1.2.7">
    /// Part 14 §6.4.1.2.7</see>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("6.4.1.2.7")]
    public sealed class UdpDatagramTransportV2Tests
    {
        private static UdpDatagramTransport NewTransport(
            DatagramConnectionTransport2DataType? v2)
        {
            var connection = new PubSubConnectionDataType
            {
                Name = "UdpV2Test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:4840"
                }),
                TransportSettings = v2 is null
                    ? ExtensionObject.Null
                    : new ExtensionObject(v2)
            };
            var factory = new UdpPubSubTransportFactory(
                Options.Create(new UdpTransportOptions { MulticastLoopback = true }));
            return (UdpDatagramTransport)factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        [Test]
        [TestSpec("7.3.2.1")]
        public async Task V2Settings_NoExtensionObject_DefaultsDiscoveryMaxMessageSizeTo4096()
        {
            await using UdpDatagramTransport transport = NewTransport(v2: null);

            Assert.That(transport.DiscoveryAnnounceRate, Is.Zero);
            Assert.That(transport.DiscoveryMaxMessageSize, Is.EqualTo(4096u));
            Assert.That(transport.QosCategory, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task V2Settings_DiscoveryAnnounceRate_HonouredFromConfig()
        {
            var v2 = new DatagramConnectionTransport2DataType
            {
                DiscoveryAnnounceRate = 2500,
                DiscoveryMaxMessageSize = 8192,
                QosCategory = "Reliable"
            };
            await using UdpDatagramTransport transport = NewTransport(v2);

            Assert.That(transport.DiscoveryAnnounceRate, Is.EqualTo(2500u));
            Assert.That(transport.DiscoveryMaxMessageSize, Is.EqualTo(8192u));
            Assert.That(transport.QosCategory, Is.EqualTo("Reliable"));
        }

        [Test]
        public async Task Send_DiscoveryExceedsMaxSize_Throws()
        {
            var v2 = new DatagramConnectionTransport2DataType
            {
                DiscoveryMaxMessageSize = 100
            };
            await using UdpDatagramTransport transport = NewTransport(v2);

            // Under cap → no throw.
            transport.EnforceDiscoveryLimit(new byte[100]);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => transport.EnforceDiscoveryLimit(new byte[101]))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        [TestSpec("7.3.2.1")]
        public async Task Send_DiscoveryLimit_DefaultCapWhenZero()
        {
            await using UdpDatagramTransport transport = NewTransport(
                new DatagramConnectionTransport2DataType());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => transport.EnforceDiscoveryLimit(new byte[4097]))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void QosCategoryReliable_SetsTosToAf21()
        {
            // AF21 = DSCP 18 = 0b010010, encoded TOS byte = DSCP << 2 = 0x48.
            Assert.That(UdpDatagramTransport.MapQosCategoryToTos("Reliable"),
                Is.EqualTo(0x48));
        }

        [Test]
        public void QosCategoryBestEffort_SetsTosToZero()
        {
            // BestEffort = CS0 = DSCP 0.
            Assert.That(UdpDatagramTransport.MapQosCategoryToTos("BestEffort"),
                Is.Zero);
        }

        [Test]
        public void QosCategoryUnknown_FallsBackToZero()
        {
            Assert.That(UdpDatagramTransport.MapQosCategoryToTos("CustomBucket"),
                Is.Zero);
            Assert.That(UdpDatagramTransport.MapQosCategoryToTos(string.Empty),
                Is.Zero);
        }
    }
}
