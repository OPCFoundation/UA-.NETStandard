// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
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
