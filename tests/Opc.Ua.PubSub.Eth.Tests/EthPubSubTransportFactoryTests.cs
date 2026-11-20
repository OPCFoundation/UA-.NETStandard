// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Validates <see cref="EthPubSubTransportFactory"/> creation and
    /// input validation.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.3", Summary = "Ethernet transport factory")]
    public sealed class EthPubSubTransportFactoryTests
    {
        private static EthPubSubTransportFactory NewFactory()
        {
            return new EthPubSubTransportFactory(
                Options.Create(new EthTransportOptions()),
                new InMemoryEthernetFrameChannelFactory());
        }

        [Test]
        public void TransportProfileUriIsEthernetUadp()
        {
            Assert.That(NewFactory().TransportProfileUri, Is.EqualTo(EthProfiles.PubSubEthUadpTransport));
        }

        [Test]
        public async Task CreateReturnsEthernetTransport()
        {
            EthPubSubTransportFactory factory = NewFactory();
            await using IPubSubTransport transport = factory.Create(
                EthTestHelpers.NewConnection("opc.eth://01-00-5E-00-00-01"),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(
                    transport.TransportProfileUri,
                    Is.EqualTo(EthProfiles.PubSubEthUadpTransport));
                Assert.That(transport, Is.InstanceOf<EthernetDatagramTransport>());
            });
        }

        [Test]
        public void CreateNullConnectionThrows()
        {
            EthPubSubTransportFactory factory = NewFactory();
            Assert.That(
                () => factory.Create(null!, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.ArgumentNullException);
        }

        [Test]
        public void CreateAddressNotNetworkAddressUrlThrows()
        {
            EthPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "Bad",
                TransportProfileUri = EthProfiles.PubSubEthUadpTransport,
                Address = new ExtensionObject(new NetworkAddressDataType())
            };

            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void CreateEmptyUrlThrows()
        {
            EthPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "Empty",
                TransportProfileUri = EthProfiles.PubSubEthUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = string.Empty })
            };

            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void ConstructorNullChannelFactoryThrows()
        {
            Assert.That(
                () => new EthPubSubTransportFactory(
                    Options.Create(new EthTransportOptions()), null!),
                Throws.ArgumentNullException);
        }
    }
}
