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

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS

using System;
using System.Globalization;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end smoke tests for
    /// <see cref="KestrelTcpTransportListener"/>. Verifies the listener
    /// can be opened on a real port, accepts inbound TCP connections,
    /// and shuts down cleanly. The full UA-SC handshake / Session
    /// integration is covered by the existing
    /// <c>Opc.Ua.Sessions.Tests</c> matrix (TCP-only); this fixture
    /// only proves the new Kestrel-hosted listener is wire-compatible
    /// at the transport layer.
    /// </summary>
    [TestFixture]
    [Category("KestrelTcp")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class KestrelTcpIntegrationTests
    {
        [Test]
        public void KestrelTcpFactoryReportsOpcTcpScheme()
        {
            var factory = new KestrelTcpTransportListenerFactory();
            Assert.That(factory.UriScheme, Is.EqualTo(Utils.UriSchemeOpcTcp));
        }

        /// <summary>
        /// The Kestrel-TCP factory must inherit from
        /// <see cref="TcpServiceHost"/> so it reuses the same
        /// <see cref="ITransportListenerFactory.CreateServiceHost"/>
        /// EndpointDescription emission logic the raw-socket factory
        /// uses. Without this both the Reference server and any
        /// discovery client wired up against the Kestrel binding would
        /// see an empty endpoint list.
        /// </summary>
        [Test]
        public void KestrelTcpFactoryInheritsFromTcpServiceHost()
        {
            var factory = new KestrelTcpTransportListenerFactory();
            Assert.That(factory, Is.InstanceOf<TcpServiceHost>());
        }

        /// <summary>
        /// <see cref="KestrelTcpTransportListener"/> implements the
        /// optional cert-rotation capability so
        /// <c>ConfigurationNodeManager.ApplyChanges</c> can drive
        /// SecureChannel cleanup when the application cert changes
        /// (matches the raw-socket TcpTransportListener contract).
        /// </summary>
        [Test]
        public async Task KestrelTcpListenerImplementsCertificateRotationCapabilityAsync()
        {
            await using var listener = new KestrelTcpTransportListener(NUnitTelemetryContext.Create());
            Assert.That(listener, Is.InstanceOf<ITransportListenerCertificateRotation>());
        }

        /// <summary>
        /// Calling <see cref="ITransportListenerCertificateRotation.CloseChannelsForCertificate"/>
        /// on an unopened Kestrel listener must be safe (no exception),
        /// return an empty list, and not throw for the
        /// no-active-channels case.
        /// </summary>
        [Test]
        public async Task KestrelTcpCloseChannelsForCertificateOnUnopenedListenerIsSafeAsync()
        {
            await using var listener = new KestrelTcpTransportListener(NUnitTelemetryContext.Create());
            using Certificate oldCertificate = CertificateBuilder.Create("CN=Old").CreateForRSA();

            System.Collections.Generic.IReadOnlyList<string> closed = await ((ITransportListenerCertificateRotation)listener).CloseChannelsForCertificateAsync(oldCertificate).ConfigureAwait(false);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
        }

        /// <summary>
        /// Null cert must throw <see cref="ArgumentNullException"/>
        /// instead of corrupting the channel map.
        /// </summary>
        [Test]
        public async Task KestrelTcpCloseChannelsForCertificateRejectsNullAsync()
        {
            await using var listener = new KestrelTcpTransportListener(NUnitTelemetryContext.Create());

            Assert.That(
                async () => await ((ITransportListenerCertificateRotation)listener).CloseChannelsForCertificateAsync(null!).ConfigureAwait(false),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public async Task KestrelTcpListenerOpensAndAcceptsTcpConnectionsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            int port = ServerFixtureUtils.GetNextFreeIPPort();
            var baseAddress = new Uri(
                $"opc.tcp://localhost:{port.ToString(CultureInfo.InvariantCulture)}/KestrelTcpSmokeTest");

            await using var listener = new KestrelTcpTransportListener(telemetry);
            await listener.OpenAsync(baseAddress, BuildMinimalSettings(telemetry), callback: null!).ConfigureAwait(false);

            // Smoke check: a raw TCP connect to the bound port succeeds.
            // The connection will not complete a UA-SC handshake (we did
            // not pass a callback), but the OS-level TCP accept proves
            // the Kestrel listener is bound and ready to serve traffic.
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            Assert.That(client.Connected, Is.True);
            client.Close();

            await listener.CloseAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task KestrelTcpListenerStartStopDoesNotLeakPortAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            int port = ServerFixtureUtils.GetNextFreeIPPort();
            var baseAddress = new Uri($"opc.tcp://localhost:{port}/Reuse");

            for (int i = 0; i < 3; i++)
            {
                await using var listener = new KestrelTcpTransportListener(telemetry);
                await listener.OpenAsync(baseAddress, BuildMinimalSettings(telemetry), callback: null!).ConfigureAwait(false);
                await listener.CloseAsync().ConfigureAwait(false);
            }
        }

        private static TransportListenerSettings BuildMinimalSettings(ITelemetryContext telemetry = null!)
        {
            telemetry ??= NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            return new TransportListenerSettings
            {
                Descriptions = new System.Collections.Generic.List<EndpointDescription>(),
                Configuration = EndpointConfiguration.Create(),
                NamespaceUris = context.NamespaceUris,
                Factory = context.Factory,
                CertificateValidator = null,
                ServerCertificates = null,
                MaxChannelCount = 4
            };
        }
    }
}

#endif // NET8_0_OR_GREATER
