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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.DataChannels.ClientServer.Tests
{
    /// <summary>
    /// Tests the explicitly enabled native lane composition boundary.
    /// </summary>
    [TestFixture]
    public sealed class DataChannelClientServerCompositionTests
    {
        /// <summary>
        /// A lane cannot request a UACP buffer below the protocol minimum.
        /// </summary>
        [Test]
        public void LaneOptionsRejectInsufficientChunkSize()
        {
            var options = new DataChannelClientServerOptions { MaxChunkSize = 8191 };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DataChannelClientServerClient(new TestTelemetry(), options));
        }

        /// <summary>
        /// Enforces independent chunk, encoded-window, lane and operation boundaries.
        /// </summary>
        [TestCase(8192, 8205U, 1, 1, true)]
        [TestCase(8192, 8204U, 1, 1, false)]
        [TestCase(8191, 8205U, 1, 1, false)]
        [TestCase(16777216, 16777229U, 128, 1, true)]
        [TestCase(16777217, 16777230U, 128, 1, false)]
        [TestCase(8192, 8205U, 0, 1, false)]
        [TestCase(8192, 8205U, -1, 1, false)]
        [TestCase(8192, 8205U, 129, 1, false)]
        [TestCase(8192, 8205U, 1, 0, false)]
        [TestCase(8192, 8205U, 1, -1, false)]
        public void LaneOptionsPreserveExactBounds(int chunk, uint receive, int lanes, int operations, bool accepted)
        {
            var options = new DataChannelClientServerOptions
            {
                MaxChunkSize = chunk,
                MaxReceiveBytes = receive,
                MaxLanes = lanes,
                MaxPendingRequests = operations
            };
            void create() => _ = new DataChannelClientServerClient(new TestTelemetry(), options);
            if (accepted)
            {
                Assert.That(create, Throws.Nothing);
            }
            else
            {
                Assert.That(create, Throws.TypeOf<ArgumentOutOfRangeException>());
            }
        }

        /// <summary>
        /// Registration never creates a flow, replaces resource authorization or modifies another application's bindings.
        /// </summary>
        [Test]
        public async Task DirectAndDiCompositionPreserveExplicitOptIn()
        {
            var telemetry = new TestTelemetry();
            var source = new Mock<IDataChannelContextFactory>(MockBehavior.Strict);
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(telemetry);
            services.AddSingleton(source.Object);
            IOpcUaBuilder builder = services.AddOpcUa();
            await using var quic = new DataChannelClientServerListener(DataChannelCarrierKind.Quic, telemetry);
            await using var web = new DataChannelClientServerListener(DataChannelCarrierKind.WebTransport, telemetry);
            builder.AddDataChannelClientServer()
                .AddDataChannelClientServerListener(quic)
                .AddDataChannelClientServerListener(web);
            using ServiceProvider provider = services.BuildServiceProvider();
            ITransportBindingRegistry registry = provider.GetRequiredService<ITransportBindingRegistry>();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IDataChannelClientServerClient>(),
                    Is.TypeOf<DataChannelClientServerClient>());
                Assert.That(provider.GetRequiredService<IDataChannelContextFactory>(), Is.SameAs(source.Object));
                Assert.That(registry.GetListenerFactory("opc.quic"),
                    Is.TypeOf<DataChannelClientServerListenerFactory>());
                Assert.That(registry.GetListenerFactory("opc.wt"),
                    Is.TypeOf<DataChannelClientServerListenerFactory>());
                Assert.That(registry.GetListenerFactory("opc.tcp"), Is.TypeOf<TcpTransportListenerFactory>());
                Assert.That(new DefaultTransportBindingRegistry().HasListenerFactory("opc.wt"), Is.False);
            });
            source.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A UASC carrier cannot be used to recursively compose a native-only lane.
        /// </summary>
        [TestCase(DataChannelCarrierKind.DedicatedUasc)]
        [TestCase(DataChannelCarrierKind.SharedUasc)]
        [TestCase((DataChannelCarrierKind)1000)]
        public void ListenerRejectsNonNativeCarriers(DataChannelCarrierKind kind)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DataChannelClientServerListener(kind, new TestTelemetry()));
        }

        /// <summary>
        /// Ordinary host registration cannot accidentally open the same virtual listener twice.
        /// </summary>
        [Test]
        public async Task ListenerFactoryTransfersExactlyOneListener()
        {
            var telemetry = new TestTelemetry();
            await using var listener = new DataChannelClientServerListener(DataChannelCarrierKind.Quic, telemetry);
            var factory = new DataChannelClientServerListenerFactory(listener);
            Assert.That(factory.Create(telemetry), Is.SameAs(listener));
            Assert.Throws<InvalidOperationException>(() => factory.Create(telemetry));
        }

        /// <summary>
        /// Invalid composition fails before constructing listeners, resolving policies or acquiring native resources.
        /// </summary>
        [Test]
        public async Task PublicCompositionRejectsMissingDependencies()
        {
            var telemetry = new TestTelemetry();
            await using var listener = new DataChannelClientServerListener(DataChannelCarrierKind.Quic, telemetry);
            Assert.Throws<ArgumentNullException>(() => new DataChannelClientServerClient(null!));
            Assert.Throws<ArgumentNullException>(() =>
                new DataChannelClientServerListener(DataChannelCarrierKind.Quic, null!));
            Assert.Throws<ArgumentNullException>(() => new DataChannelClientServerListenerFactory(null!));
            Assert.Throws<ArgumentNullException>(() =>
                DataChannelClientServerBuilderExtensions.AddDataChannelClientServer(null!));
            Assert.Throws<ArgumentNullException>(() =>
                DataChannelClientServerBuilderExtensions.AddDataChannelClientServerListener(null!, listener));
            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.Throws<ArgumentNullException>(() => builder.AddDataChannelClientServerListener(null!));
            var factory = new DataChannelClientServerListenerFactory(listener);
            Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
            Assert.That(factory.Create(telemetry), Is.SameAs(listener), "Rejected creation must not consume ownership.");
        }

        /// <summary>
        /// Virtual listener startup succeeds while another owner already reserves the corresponding TCP port.
        /// No request callback or native context is started by registering the ordinary Service dispatcher.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic)]
        [TestCase(DataChannelCarrierKind.WebTransport)]
        public async Task VirtualListenerDoesNotBindTcpPort(DataChannelCarrierKind kind)
        {
            var telemetry = new TestTelemetry();
            using var reservedTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            reservedTcp.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var address = (IPEndPoint)reservedTcp.LocalEndPoint!;
            await using var listener = new DataChannelClientServerListener(kind, telemetry);
            var endpoint = new Uri($"{listener.UriScheme}://127.0.0.1:{address.Port}/ua");
            var callback = new Mock<ITransportListenerCallback>(MockBehavior.Strict);
            var settings = new TransportListenerSettings
            {
                Descriptions =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = endpoint.AbsoluteUri,
                        TransportProfileUri = Profiles.UaTcpTransport,
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ],
                Configuration = EndpointConfiguration.Create(),
                Factory = ServiceMessageContext.Create(telemetry).Factory,
                NamespaceUris = new NamespaceTable(),
                CertificateValidator = Mock.Of<ICertificateValidatorEx>(),
                ServerCertificates = Mock.Of<ICertificateRegistry>()
            };
            await listener.OpenAsync(endpoint, settings, callback.Object).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(listener.ListenerId, Is.Not.Empty);
                Assert.That(listener.EndpointUrl, Is.EqualTo(endpoint));
                Assert.That(reservedTcp.LocalEndPoint, Is.EqualTo(address));
            });
            callback.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A lane cannot select another flow profile, resource or context factory during inner HEL/OPN setup.
        /// </summary>
        [TestCase(DataChannelCarrierKind.Quic, "profile")]
        [TestCase(DataChannelCarrierKind.Quic, "resource")]
        [TestCase(DataChannelCarrierKind.Quic, "factory")]
        [TestCase(DataChannelCarrierKind.WebTransport, "profile")]
        [TestCase(DataChannelCarrierKind.WebTransport, "resource")]
        [TestCase(DataChannelCarrierKind.WebTransport, "factory")]
        public async Task InnerListenerRejectsDataChannelComposition(DataChannelCarrierKind kind, string nested)
        {
            var telemetry = new TestTelemetry();
            await using var listener = new DataChannelClientServerListener(kind, telemetry);
            string resource = nested == "resource" ? "/flow/v1/shared" : "/ua";
            var endpoint = new Uri($"{listener.UriScheme}://localhost:4840{resource}");
            var factory = new Mock<IDataChannelContextFactory>(MockBehavior.Strict);
            var callback = new Mock<ITransportListenerCallback>(MockBehavior.Strict);
            var settings = new TransportListenerSettings
            {
                Descriptions =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = endpoint.AbsoluteUri,
                        TransportProfileUri = nested == "profile"
                            ? DataChannelProfiles.SharedUasc : Profiles.UaTcpTransport,
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ],
                Configuration = EndpointConfiguration.Create(),
                Factory = ServiceMessageContext.Create(telemetry).Factory,
                NamespaceUris = new NamespaceTable(),
                CertificateValidator = Mock.Of<ICertificateValidatorEx>(),
                ServerCertificates = Mock.Of<ICertificateRegistry>(),
                DataChannels = nested == "factory" ? factory.Object : null
            };

            if (nested == "factory")
            {
                Assert.ThrowsAsync<ArgumentException>(async () =>
                    await listener.OpenAsync(endpoint, settings, callback.Object).ConfigureAwait(false));
            }
            else
            {
                ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await listener.OpenAsync(endpoint, settings, callback.Object).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
            factory.VerifyNoOtherCalls();
            callback.VerifyNoOtherCalls();
        }

        private sealed class TestTelemetry() : TelemetryContextBase(NullLoggerFactory.Instance);
    }
}
