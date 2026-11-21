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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
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
        [Category("SecurityRegression")]
        public async Task SecurityF15DirectConnectionRefusesMissingLeafSecurityContextAsync()
        {
            PubSubConnectionDataType configuration = NewConfig();
            configuration.ReaderGroups =
            [
                new ReaderGroupDataType
                {
                    SecurityMode = MessageSecurityMode.None,
                    DataSetReaders =
                    [
                        new DataSetReaderDataType
                        {
                            SecurityMode = MessageSecurityMode.SignAndEncrypt,
                            SecurityGroupId = "protected-leaf"
                        }
                    ]
                }
            ];
            PubSubConfigurationException? rejection = null;
            try
            {
                await using PubSubConnection connection = NewConnectionWithConfig(configuration);
            }
            catch (PubSubConfigurationException exception)
            {
                rejection = exception;
            }

            Assert.That(rejection, Is.Not.Null);
            Assert.That(rejection!.Issues[0].Code, Is.EqualTo("PSC1401"));
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
            PubSubConnectionDataType cfg = NewConfig("cfg-test", UdpProfile);
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
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await conn.EnableAsync(cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task DisableAsync_WithAlreadyCancelledToken_ThrowsOperationCancelled()
        {
            await using PubSubConnection conn = NewConnection();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await conn.DisableAsync(cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task TryRouteInboundMetaDataJsonMetaDataUpdatesConnectionRegistryAndReturnsTrue()
        {
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry);

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
        public async Task InboundMetaDataIsRegisteredOnlyForConfiguredReaderIdentities()
        {
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry,
                new DataSetReaderDataType
                {
                    Name = "expected",
                    PublisherId = new Variant((ushort)42),
                    DataSetWriterId = 17
                });

            bool otherPublisher = conn.TryRouteInboundMetaData(NewInboundMeta(PublisherId.FromUInt16(43), 17));
            bool otherWriter = conn.TryRouteInboundMetaData(NewInboundMeta(PublisherId.FromUInt16(42), 18));
            bool expected = conn.TryRouteInboundMetaData(NewInboundMeta(PublisherId.FromString("42"), 17));

            Assert.That(otherPublisher && otherWriter && expected, Is.True,
                "Metadata frames are always consumed, registered or not.");
            Assert.That(registry.Keys, Has.Count.EqualTo(1));
            Assert.That(registry.Keys[0].DataSetWriterId, Is.EqualTo((ushort)17));
        }

        [Test]
        public async Task InboundMetaDataIdentitiesAreBoundedPerConnection()
        {
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry);

            for (int i = 0; i <= PubSubConnection.MaxInboundMetaDataIdentities; i++)
            {
                _ = conn.TryRouteInboundMetaData(
                    NewInboundMeta(PublisherId.FromString("P" + i), 1));
            }
            // A known identity is still updated once the budget is exhausted.
            _ = conn.TryRouteInboundMetaData(NewInboundMeta(PublisherId.FromString("P0"), 1, major: 8));

            Assert.That(registry.Keys, Has.Count.EqualTo(PubSubConnection.MaxInboundMetaDataIdentities));
            var first = new DataSetMetaDataKey(PublisherId.FromString("P0"), 0, 1, Uuid.Empty, 8);
            Assert.That(registry.TryGet(in first, out _), Is.EqualTo(MetaDataMatchResult.Match));
        }

        [Test]
        public async Task RejectedInboundMetaDataDoesNotUseUpTheIdentityBudget()
        {
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry,
                maxUnauthenticatedCost: 1000);
            var inbound = (DataSetMetaDataRegistry)registry;

            // The budget the host configured applies to the connection registry.
            Assert.That(inbound.MaxUnauthenticatedCost, Is.EqualTo(1000));

            // More over-budget announcements than there are identity slots.
            int rejected = PubSubConnection.MaxInboundMetaDataIdentities + 10;
            for (int i = 0; i < rejected; i++)
            {
                _ = conn.TryRouteInboundMetaData(
                    NewInboundMeta(PublisherId.FromString("P" + i), 1),
                    authenticated: false,
                    frameLength: 5000);
            }

            Assert.That(registry.Keys, Has.Count.Zero);
            Assert.That(inbound.RejectedUnauthenticatedCount, Is.EqualTo(rejected));

            // A publisher whose announcement fits is still admitted.
            _ = conn.TryRouteInboundMetaData(
                NewInboundMeta(PublisherId.FromString("real"), 1),
                authenticated: false,
                frameLength: 100);
            Assert.That(registry.Keys, Has.Count.EqualTo(1));
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task IdleInboundMetaDataGivesUpItsIdentitySlot()
        {
            var clock = new FakeTimeProvider();
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry,
                maxUnauthenticatedCost: 200_000,
                timeProvider: clock);

            // A sender takes every identity slot and then goes silent.
            for (int i = 0; i < PubSubConnection.MaxInboundMetaDataIdentities; i++)
            {
                _ = conn.TryRouteInboundMetaData(NewInboundMeta(PublisherId.FromString("P" + i), 1));
            }
            var late = new DataSetMetaDataKey(PublisherId.FromString("late"), 0, 1, Uuid.Empty, 7);
            _ = conn.TryRouteInboundMetaData(NewInboundMeta(late.PublisherId, 1));
            Assert.That(registry.TryGet(in late, out _), Is.EqualTo(MetaDataMatchResult.NotFound));

            clock.Advance(DataSetMetaDataRegistry.DefaultUnauthenticatedIdleTimeout + TimeSpan.FromMinutes(1));

            // An announcement that is refused for its cost must not cost an
            // idle publisher its metadata.
            var big = new DataSetMetaDataKey(PublisherId.FromString("big"), 0, 1, Uuid.Empty, 7);
            _ = conn.TryRouteInboundMetaData(
                NewInboundMeta(big.PublisherId, 1),
                authenticated: false,
                frameLength: 300_000);
            Assert.That(registry.TryGet(in big, out _), Is.EqualTo(MetaDataMatchResult.NotFound));
            Assert.That(registry.Keys, Has.Count.EqualTo(PubSubConnection.MaxInboundMetaDataIdentities));

            // After the idle timeout a publisher that arrives later is admitted.
            _ = conn.TryRouteInboundMetaData(NewInboundMeta(late.PublisherId, 1));
            Assert.That(registry.TryGet(in late, out _), Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(registry.Keys, Has.Count.EqualTo(PubSubConnection.MaxInboundMetaDataIdentities));

            var later = new DataSetMetaDataKey(PublisherId.FromString("later"), 0, 1, Uuid.Empty, 7);
            _ = conn.TryRouteInboundMetaData(NewInboundMeta(later.PublisherId, 1));
            Assert.That(registry.TryGet(in later, out _), Is.EqualTo(MetaDataMatchResult.Match));
            Assert.That(registry.Keys, Has.Count.EqualTo(PubSubConnection.MaxInboundMetaDataIdentities));
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task EvictedInboundMetaDataIsRequestedAgainWhenItsPublisherResumes()
        {
            var clock = new FakeTimeProvider();
            var factory = new StubTransportFactory();
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry,
                maxUnauthenticatedCost: 4096,
                timeProvider: clock,
                transportFactory: factory);
            await conn.EnableAsync().ConfigureAwait(false);
            List<byte[]> sent = factory.Transport!.Sent;

            var quiet = new DataSetMetaDataKey(PublisherId.FromString("quiet"), 0, 5, Uuid.Empty, 7);
            _ = conn.TryRouteInboundMetaData(
                NewInboundMeta(quiet.PublisherId, 5), authenticated: false, frameLength: 3000);

            // A lookup that misses for an identity that never had metadata requests nothing.
            var unknown = new DataSetMetaDataKey(PublisherId.FromString("unknown"), 0, 9, Uuid.Empty, 7);
            Assert.That(registry.TryGet(in unknown, out _), Is.EqualTo(MetaDataMatchResult.NotFound));
            await conn.TryRecoverEvictedMetaDataAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sent, Is.Empty);

            // The publisher goes quiet and another one takes its budget.
            clock.Advance(TimeSpan.FromMinutes(6));
            _ = conn.TryRouteInboundMetaData(
                NewInboundMeta(PublisherId.FromString("other"), 1), authenticated: false, frameLength: 3000);

            // When it resumes, the decoder finds no metadata for its messages
            // and the connection asks for it again.
            Assert.That(registry.TryGet(in quiet, out _), Is.EqualTo(MetaDataMatchResult.NotFound));
            await conn.TryRecoverEvictedMetaDataAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sent, Has.Count.EqualTo(1));
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
            var request = UadpDecoder.Decode(sent[0], context) as UadpDiscoveryRequestMessage;
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.DiscoveryType, Is.EqualTo(UadpDiscoveryType.DataSetMetaData));
            Assert.That(request.DataSetWriterIds.ToArray(), Is.EqualTo(new ushort[] { 5 }));

            // Further misses do not send more than one request per interval.
            Assert.That(registry.TryGet(in quiet, out _), Is.EqualTo(MetaDataMatchResult.NotFound));
            await conn.TryRecoverEvictedMetaDataAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sent, Has.Count.EqualTo(1));
            clock.Advance(PubSubConnection.MetaDataRecoveryInterval);
            await conn.TryRecoverEvictedMetaDataAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sent, Has.Count.EqualTo(2));

            // Once the publisher answered, its metadata is back and nothing is requested.
            clock.Advance(TimeSpan.FromMinutes(6));
            _ = conn.TryRouteInboundMetaData(
                NewInboundMeta(quiet.PublisherId, 5), authenticated: false, frameLength: 3000);
            Assert.That(registry.TryGet(in quiet, out _), Is.EqualTo(MetaDataMatchResult.Match));
            await conn.TryRecoverEvictedMetaDataAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(sent, Has.Count.EqualTo(2));
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task MetaDataEvictedASecondTimeIsStillRequestedAfterOlderRecordsArePruned()
        {
            var clock = new FakeTimeProvider();
            var factory = new StubTransportFactory();
            await using PubSubConnection conn = NewConnectionWithOwnRegistry(
                out IDataSetMetaDataRegistry registry,
                maxUnauthenticatedCost: 4096,
                timeProvider: clock,
                transportFactory: factory);
            await conn.EnableAsync().ConfigureAwait(false);

            // The budget holds one announcement, so each one evicts the one before it.
            void Announce(string publisher, ushort writerId)
            {
                clock.Advance(TimeSpan.FromMinutes(6));
                _ = conn.TryRouteInboundMetaData(
                    NewInboundMeta(PublisherId.FromString(publisher), writerId),
                    authenticated: false,
                    frameLength: 3000);
            }

            // Evicted, restored and evicted again: two eviction records for one identity.
            Announce("quiet", 5);
            Announce("a", 1);
            Announce("quiet", 5);
            Announce("b", 1);

            // Enough further evictions to prune the first record, not the second.
            for (int i = 0; i < PubSubConnection.MaxInboundMetaDataIdentities - 2; i++)
            {
                Announce("o" + i, 1);
            }

            var quiet = new DataSetMetaDataKey(PublisherId.FromString("quiet"), 0, 5, Uuid.Empty, 7);
            Assert.That(registry.TryGet(in quiet, out _), Is.EqualTo(MetaDataMatchResult.NotFound));
            await conn.TryRecoverEvictedMetaDataAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(factory.Transport!.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task UnauthenticatedMetaDataLimitsApplyWithAnyRegistryImplementation()
        {
            // A host that injects its own registry implementation still sets the limits.
            await using var conn = new PubSubConnection(
                NewConfig(),
                new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder>(),
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                Array.Empty<ReaderGroup>(),
                new Moq.Mock<IDataSetMetaDataRegistry>().Object,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                securityWrapper: null,
                UadpSecurityWrapOptions.SignAndEncrypt,
                maxNetworkMessageSize: 0,
                requiredSecurityMode: MessageSecurityMode.None,
                scheduler: null,
                activationCoordinator: null,
                writerCheckpointStore: null,
                unauthenticatedMetaDataLimits: new UnauthenticatedMetaDataLimits
                {
                    MaxCost = 1000,
                    IdleTimeout = TimeSpan.Zero
                });

            var inbound = (DataSetMetaDataRegistry)conn.InboundMetaDataRegistry;
            Assert.That(inbound.MaxUnauthenticatedCost, Is.EqualTo(1000));
            Assert.That(inbound.UnauthenticatedIdleTimeout, Is.EqualTo(TimeSpan.Zero));
        }

        private static JsonMetaDataMessage NewInboundMeta(
            PublisherId publisherId,
            ushort writerId,
            uint major = 7)
        {
            return new JsonMetaDataMessage
            {
                PublisherId = publisherId,
                DataSetWriterId = writerId,
                MetaDataPayload = new DataSetMetaDataType
                {
                    Name = "Inbound",
                    ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = major }
                }
            };
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
            out IDataSetMetaDataRegistry registry,
            DataSetReaderDataType? readerConfiguration = null,
            long maxUnauthenticatedCost = DataSetMetaDataRegistry.DefaultMaxUnauthenticatedCost,
            TimeProvider? timeProvider = null,
            StubTransportFactory? transportFactory = null)
        {
            var hostRegistry = new DataSetMetaDataRegistry { MaxUnauthenticatedCost = maxUnauthenticatedCost };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var reader = new DataSetReader(
                readerConfiguration ?? new DataSetReaderDataType { Name = "any-publisher" },
                new Moq.Mock<ISubscribedDataSetSink>().Object,
                telemetry,
                TimeProvider.System);
            var connection = new PubSubConnection(
                NewConfig(),
                transportFactory ?? new StubTransportFactory(),
                new Dictionary<string, INetworkMessageEncoder> { [UdpProfile] = new UadpEncoder() },
                new Dictionary<string, INetworkMessageDecoder>(),
                Array.Empty<WriterGroup>(),
                new[] { new ReaderGroup(new ReaderGroupDataType { Name = "rg" }, new[] { reader }, telemetry) },
                hostRegistry,
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                NUnitTelemetryContext.Create(),
                timeProvider ?? TimeProvider.System);
            registry = connection.InboundMetaDataRegistry;
            return connection;
        }

#pragma warning disable IDE0051, RCS1213 // Test scaffold kept for constructor tests with explicit encoder/decoder maps.
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
#pragma warning restore IDE0051, RCS1213

        private sealed class StubTransportFactory : IPubSubTransportFactory
        {
            public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

            public IPubSubTransport Create(
                PubSubConnectionDataType connection,
                ITelemetryContext telemetry,
                TimeProvider timeProvider)
            {
                Transport = new StubTransport();
                return Transport;
            }

            public StubTransport? Transport { get; private set; }
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
                CancellationToken cancellationToken = default)
            {
                Sent.Add(payload.ToArray());
                return default;
            }

            public List<byte[]> Sent { get; } = [];

            public IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
                CancellationToken cancellationToken = default)
            {
                return TestAsyncEnumerable.Empty<PubSubTransportFrame>();
            }

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
