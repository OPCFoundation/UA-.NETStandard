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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Connections
{
    /// <summary>
    /// Covers the constructor guard-rails, property initialisation, and
    /// basic lifecycle (Enable → Disable → Dispose) of
    /// <see cref="PubSubConnection"/> using a stub transport so that no
    /// real network is required.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PubSubConnectionConstructorTests
    {
        private const string UdpProfile =
            "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

        // ------------------------------------------------------------------
        // Constructor null-guard tests
        // ------------------------------------------------------------------

        [Test]
        public void ConstructorRejectsNullConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                configuration: null!,
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void ConstructorRejectsNullTransportFactory()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                NewConfig(),
                transportFactory: null!,
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void ConstructorRejectsNullEncoders()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                encoders: null!,
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void ConstructorRejectsNullDecoders()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                decoders: null!,
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void ConstructorAcceptsDefaultWriterGroups()
        {
            PubSubConnection connection = new(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                writerGroups: default,
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(connection.WriterGroups.Count, Is.Zero);
        }

        [Test]
        public void ConstructorAcceptsDefaultReaderGroups()
        {
            PubSubConnection connection = new(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                readerGroups: default,
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.That(connection.ReaderGroups.Count, Is.Zero);
        }

        [Test]
        public void ConstructorRejectsNullMetaDataRegistry()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                metaDataRegistry: null!,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void ConstructorRejectsNullDiagnostics()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                diagnostics: null!,
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
        }

        [Test]
        public void ConstructorRejectsNullTimeProvider()
        {
            Assert.Throws<ArgumentNullException>(() => new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                timeProvider: null!));
        }

        // ------------------------------------------------------------------
        // Property initialisation
        // ------------------------------------------------------------------

        [Test]
        public async Task ConstructorInitializesName()
        {
            await using PubSubConnection conn = NewConnection(name: "MyConn");
            Assert.That(conn.Name, Is.EqualTo("MyConn"));
        }

        [Test]
        public async Task ConstructorInitializesTransportProfileUri()
        {
            await using PubSubConnection conn = NewConnection(profile: UdpProfile);
            Assert.That(conn.TransportProfileUri, Is.EqualTo(UdpProfile));
        }

        [Test]
        public async Task ConstructorInitializesPublisherIdFromConfig()
        {
            var cfg = new PubSubConnectionDataType
            {
                Name = "pub-id-conn",
                TransportProfileUri = UdpProfile,
                PublisherId = new Variant((ushort)42)
            };
            await using PubSubConnection conn = NewConnectionWithConfig(cfg);
            Assert.That(conn.PublisherId, Is.EqualTo(PublisherId.FromUInt16(42)));
        }

        [Test]
        public async Task ConstructorInitializesNullPublisherIdAsNull()
        {
            var cfg = new PubSubConnectionDataType
            {
                Name = "no-pub-id",
                TransportProfileUri = UdpProfile
            };
            await using PubSubConnection conn = NewConnectionWithConfig(cfg);
            Assert.That(conn.PublisherId, Is.EqualTo(PublisherId.Null));
        }

        [Test]
        public async Task ConstructorInitializesWriterGroupsAndReaderGroups()
        {
            await using PubSubConnection conn = NewConnection();
            Assert.That(conn.WriterGroups.Count, Is.Zero);
            Assert.That(conn.ReaderGroups.Count, Is.Zero);
        }

        [Test]
        public async Task ConstructorSetsConfigurationProperty()
        {
            var cfg = NewConfig("cfg-test", UdpProfile);
            await using PubSubConnection conn = NewConnectionWithConfig(cfg);
            Assert.That(conn.Configuration, Is.SameAs(cfg));
        }

        [Test]
        public async Task ConstructorInitializesStateNotNull()
        {
            await using PubSubConnection conn = NewConnection();
            Assert.That(conn.State, Is.Not.Null);
        }

        [Test]
        public async Task ConstructorCurrentTransportIsNull()
        {
            await using PubSubConnection conn = NewConnection();
            Assert.That(conn.CurrentTransport, Is.Null);
        }

        // ------------------------------------------------------------------
        // Lifecycle tests
        // ------------------------------------------------------------------

        [Test]
        public async Task EnableAsync_SetsStateOperational()
        {
            await using PubSubConnection conn = NewConnection();
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(conn.State.State, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task EnableAsync_CurrentTransportIsNotNullAfterEnable()
        {
            await using PubSubConnection conn = NewConnection();
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(conn.CurrentTransport, Is.Not.Null);
        }

        [Test]
        public async Task EnableAsync_IsIdempotentOnSecondCall()
        {
            await using PubSubConnection conn = NewConnection();
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(conn.State.State, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task DisableAsync_AfterEnable_SetsStateDisabled()
        {
            await using PubSubConnection conn = NewConnection();
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            await conn.DisableAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(conn.State.State, Is.EqualTo(PubSubState.Disabled));
        }

        [Test]
        public async Task DisableAsync_CurrentTransportIsNullAfterDisable()
        {
            await using PubSubConnection conn = NewConnection();
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            await conn.DisableAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(conn.CurrentTransport, Is.Null);
        }

        [Test]
        public async Task DisposeAsync_IsIdempotent()
        {
            PubSubConnection conn = NewConnection();
            await conn.DisposeAsync().ConfigureAwait(false);
            await conn.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeAsync_AfterEnable_ShutsDownCleanly()
        {
            PubSubConnection conn = NewConnection();
            await conn.EnableAsync(CancellationToken.None).ConfigureAwait(false);
            await conn.DisposeAsync().ConfigureAwait(false);
            Assert.That(conn.CurrentTransport, Is.Null);
        }

        [Test]
        public async Task EnableAsync_WithAlreadyCancelledToken_ThrowsOperationCancelled()
        {
            await using PubSubConnection conn = NewConnection();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await conn.EnableAsync(cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task DisableAsync_WithAlreadyCancelledToken_ThrowsOperationCancelled()
        {
            await using PubSubConnection conn = NewConnection();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await conn.DisableAsync(cts.Token).ConfigureAwait(false));
        }

        // ------------------------------------------------------------------
        // TryRouteInboundMetaData – instance overload delegates to static
        // ------------------------------------------------------------------

        [Test]
        public async Task TryRouteInboundMetaData_JsonMetaData_UpdatesRegistryAndReturnsTrue()
        {
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out DataSetMetaDataRegistry registry);

            var meta = new DataSetMetaDataType
            {
                Name = "RouteTest",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 7,
                    MinorVersion = 0
                }
            };
            var message = new JsonMetaDataMessage
            {
                PublisherId = PublisherId.FromUInt16(99),
                DataSetWriterId = 5,
                MetaDataPayload = meta
            };

            bool routed = conn.TryRouteInboundMetaData(message);

            Assert.That(routed, Is.True);
            var key = new DataSetMetaDataKey(PublisherId.FromUInt16(99), 0, 5, Uuid.Empty, 7);
            MetaDataMatchResult result = registry.TryGet(in key, out DataSetMetaDataType? stored);
            Assert.That(result, Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(stored, Is.SameAs(meta));
        }

        [Test]
        public async Task TryRouteInboundMetaData_NonMetaMessage_ReturnsFalse()
        {
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(out _);

            // Any message that is not a JsonMetaDataMessage or UadpDiscoveryResponseMessage
            // hits the default case and returns false.
            var dataMessage = new DummyNetworkMessage();

            bool routed = conn.TryRouteInboundMetaData(dataMessage);

            Assert.That(routed, Is.False);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static PubSubConnectionDataType NewConfig(
            string name = "test-conn",
            string profile = UdpProfile)
        {
            return new PubSubConnectionDataType
            {
                Name = name,
                TransportProfileUri = profile
            };
        }

        private static PubSubConnection NewConnection(
            string name = "test-conn",
            string profile = UdpProfile)
        {
            return NewConnectionWithConfig(NewConfig(name, profile));
        }

        private static PubSubConnection NewConnectionWithConfig(
            PubSubConnectionDataType cfg)
        {
            return new PubSubConnection(
                cfg,
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        private static PubSubConnection NewConnectionWithOwnRegistry(
            out DataSetMetaDataRegistry registry)
        {
            registry = new DataSetMetaDataRegistry();
            return new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                registry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        private static PubSubConnection NewConnectionWithDicts(
            string profile,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoders)
        {
            return new PubSubConnection(
                NewConfig(profile: profile),
                new StubTransportFactory(),
                encoders,
                decoders,
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
                => new StubTransport();
        }

        private sealed class StubTransport : IPubSubTransport
        {
            private bool m_isConnected;

            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public PubSubTransportDirection Direction => PubSubTransportDirection.SendReceive;

            public bool IsConnected => m_isConnected;

            public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged
            {
                add { }
                remove { }
            }

            public ValueTask OpenAsync(CancellationToken cancellationToken = default)
            {
                m_isConnected = true;
                return default;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            {
                m_isConnected = false;
                return default;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> payload,
                string? topic = null,
                CancellationToken cancellationToken = default) => default;

            public System.Collections.Generic.IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
                => System.Linq.AsyncEnumerable.Empty<PubSubTransportFrame>();

            public ValueTask DisposeAsync()
            {
                m_isConnected = false;
                return default;
            }
        }

        /// <summary>
        /// Concrete subclass of the abstract record used to trigger
        /// the <c>default</c> branch in <see cref="PubSubConnection.TryRouteInboundMetaData"/>.
        /// </summary>
        private sealed record DummyNetworkMessage : PubSubNetworkMessage
        {
            public override string TransportProfileUri => "dummy";
        }
    }
}
