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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Udp.Dtls;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Validates <see cref="UdpPubSubTransportFactory"/> connection
    /// dispatching, direction inference, address-type rejection, and
    /// network-interface property routing.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class UdpPubSubTransportFactoryTests
    {
        private static UdpPubSubTransportFactory NewFactory(UdpTransportOptions? options = null)
        {
            options ??= new UdpTransportOptions { MulticastLoopback = true };
            return new UdpPubSubTransportFactory(Options.Create(options));
        }

        private static PubSubConnectionDataType NewConnection(
            string url,
            string? networkInterface = null)
        {
            return new PubSubConnectionDataType
            {
                Name = "Test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url,
                    NetworkInterface = networkInterface ?? string.Empty
                })
            };
        }

        [Test]
        public void Create_ValidUnicastConnection_ReturnsUdpTransport()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://127.0.0.1:5000");

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
            Assert.That(transport.TransportProfileUri, Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        public void Create_MulticastUrl_ReturnsUdpTransport()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6000");

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
            var udp = (UdpDatagramTransport)transport;
            Assert.That(udp.Endpoint.AddressType, Is.EqualTo(UdpAddressType.Multicast));
        }

        [Test]
        public void Create_WriterGroupsPresent_PicksSendDirection()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6010");
            connection.WriterGroups = new ArrayOf<WriterGroupDataType>(
                new[] { new WriterGroupDataType { Name = "WG", WriterGroupId = 1 } });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.Send));
        }

        [Test]
        public void Create_ReaderGroupsPresent_PicksReceiveDirection()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6020");
            connection.ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                new[] { new ReaderGroupDataType { Name = "RG" } });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.Receive));
        }

        [Test]
        public void Create_BothGroupsPresent_PicksSendReceiveDirection()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6030");
            connection.WriterGroups = new ArrayOf<WriterGroupDataType>(
                new[] { new WriterGroupDataType { Name = "WG", WriterGroupId = 1 } });
            connection.ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                new[] { new ReaderGroupDataType { Name = "RG" } });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.SendReceive));
        }

        [Test]
        public void Create_NoGroups_FallsBackToSendReceive()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6040");

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport.Direction, Is.EqualTo(PubSubTransportDirection.SendReceive));
        }

        [Test]
        public void Create_NetworkInterfacePropertyOverride_ResolvedSilently()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6050");
            connection.ConnectionProperties = new ArrayOf<KeyValuePair>(new[]
            {
                new KeyValuePair
                {
                    Key = QualifiedName.From(UdpPubSubTransportFactory.NetworkInterfacePropertyKey),
                    Value = "totally-unknown-nic"
                }
            });

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
        }

        [Test]
        public void Create_NullConnection_Throws()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            Assert.That(
                () => factory.Create(null!, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Create_NullTelemetry_Throws()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6060");
            Assert.That(
                () => factory.Create(connection, null!, TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Create_NullTimeProvider_Throws()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            PubSubConnectionDataType connection = NewConnection("opc.udp://239.0.0.1:6070");
            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Create_AddressMissing_Throws()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "NoAddress",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport
            };
            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Create_AddressNotNetworkAddressUrlDataType_Throws()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "WrongAddress",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressDataType())
            };
            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Create_AddressWithEmptyUrl_Throws()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            var connection = new PubSubConnectionDataType
            {
                Name = "EmptyUrl",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType { Url = string.Empty })
            };
            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void TransportProfileUri_MatchesSpec()
        {
            UdpPubSubTransportFactory factory = NewFactory();
            Assert.That(
                factory.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        public void Constructor_NullOptions_Throws()
        {
            Assert.That(
                () => new UdpPubSubTransportFactory(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_OptionsWithNullValue_FallsBackToDefaults()
        {
            var nullOptions = new OptionsWrapper<UdpTransportOptions>(null!);
            var factory = new UdpPubSubTransportFactory(nullOptions);
            PubSubConnectionDataType connection = NewConnection("opc.udp://127.0.0.1:7100");
            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
        }

        private static UdpPubSubTransportFactory NewDtlsFactory(DtlsTransportOptions dtlsOptions)
        {
            var udpOptions = new UdpTransportOptions { MulticastLoopback = true };
            var registry = new DtlsProfileRegistry();
            var contextFactory = new DefaultDtlsContextFactory(Options.Create(dtlsOptions), registry);
            return new UdpPubSubTransportFactory(
                Options.Create(udpOptions),
                diagnostics: null,
                dtlsOptions: Options.Create(dtlsOptions),
                dtlsProfileRegistry: registry,
                dtlsContextFactory: contextFactory);
        }

        [Test]
        [TestSpec("7.3.2.4")]
        public async Task Create_DtlsProfileDisabled_SelectsAnotherSupportedProfileAsync()
        {
            var registry = new DtlsProfileRegistry();
            const string endpointDefault = "ECC_nistP256_AesGcm";
            if (!registry.TryResolve(endpointDefault, out _))
            {
                Assert.Ignore("Endpoint default DTLS profile is not supported by this platform BCL.");
                return;
            }

            var dtlsOptions = new DtlsTransportOptions();
            dtlsOptions.DisabledProfiles.Add(endpointDefault);
            UdpPubSubTransportFactory factory = NewDtlsFactory(dtlsOptions);
            PubSubConnectionDataType connection = NewConnection("opc.dtls://127.0.0.1:4843");

            await using var transport = (DtlsDatagramTransport)factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.Multiple(() =>
            {
                Assert.That(transport.Profile.Name, Is.Not.EqualTo(endpointDefault));
                Assert.That(
                    registry.SupportedProfiles.Select(profile => profile.Name),
                    Does.Contain(transport.Profile.Name));
            });
        }

        [Test]
        [TestSpec("7.3.2.4")]
        public async Task Create_DtlsPreferredProfileSelectedAtRuntimeAsync()
        {
            var registry = new DtlsProfileRegistry();
            const string endpointDefault = "ECC_nistP256_AesGcm";
            const string preferred = "ECC_nistP384_AesGcm";
            if (!registry.TryResolve(endpointDefault, out _) || !registry.TryResolve(preferred, out _))
            {
                Assert.Ignore("Required DTLS profiles are not supported by this platform BCL.");
                return;
            }

            var dtlsOptions = new DtlsTransportOptions { PreferredProfileName = preferred };
            dtlsOptions.DisabledProfiles.Add(endpointDefault);
            UdpPubSubTransportFactory factory = NewDtlsFactory(dtlsOptions);
            PubSubConnectionDataType connection = NewConnection("opc.dtls://127.0.0.1:4843");

            await using var transport = (DtlsDatagramTransport)factory.Create(
                connection, NUnitTelemetryContext.Create(), TimeProvider.System);

            Assert.That(transport.Profile.Name, Is.EqualTo(preferred));
        }

        [Test]
        [TestSpec("7.3.2.4")]
        public void Create_AllDtlsProfilesDisabled_FailsClosed()
        {
            var registry = new DtlsProfileRegistry();
            if (registry.SupportedProfiles.Count == 0)
            {
                Assert.Ignore("No DTLS profiles are supported by this platform BCL.");
                return;
            }

            var dtlsOptions = new DtlsTransportOptions();
            foreach (DtlsProfile profile in registry.SupportedProfiles)
            {
                dtlsOptions.DisabledProfiles.Add(profile.Name);
            }

            UdpPubSubTransportFactory factory = NewDtlsFactory(dtlsOptions);
            PubSubConnectionDataType connection = NewConnection("opc.dtls://127.0.0.1:4843");

            Assert.That(
                () => factory.Create(connection, NUnitTelemetryContext.Create(), TimeProvider.System),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
