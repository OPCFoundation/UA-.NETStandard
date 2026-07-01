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
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Verifies the UDP factory accepts connections whose
    /// <c>TransportSettings</c> is a v2-only
    /// <see cref="DatagramConnectionTransport2DataType"/> body (Part 14
    /// §6.4.1.2.7) without throwing — informative diagnostics about
    /// v2-only fields belong to the configuration validator
    /// and must not block transport construction.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("6.4.1.2.7")]
    public sealed class Datagrams2DataTypeTests
    {
        private static UdpPubSubTransportFactory NewFactory()
        {
            return new UdpPubSubTransportFactory(
                Options.Create(new UdpTransportOptions { MulticastLoopback = true }));
        }

        [Test]
        public void Factory_AcceptsConnectionWithDatagramConnectionTransport2DataType()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "UdpWithV2",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:4840"
                }),
                TransportSettings = new ExtensionObject(new DatagramConnectionTransport2DataType
                {
                    DiscoveryAnnounceRate = 5,
                    QosCategory = "default"
                })
            };

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
            Assert.That(
                transport.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        public void Factory_AcceptsConnectionWithLegacyDatagramTransportDataType()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "UdpWithV1",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:4841"
                }),
                TransportSettings = new ExtensionObject(new DatagramConnectionTransportDataType
                {
                    DiscoveryAddress = new ExtensionObject(new NetworkAddressUrlDataType
                    {
                        Url = "opc.udp://224.0.0.6:4840"
                    })
                })
            };

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
        }
    }
}
