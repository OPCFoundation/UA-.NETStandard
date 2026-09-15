/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    public class DataChannelServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("service", 65536, m_telemetry);
            m_transport = new LoopbackTransport(m_bufferManager, TimeProvider.System);
            m_manager = new DataChannelManager(m_transport, true, m_telemetry);
            m_sources = new DataChannelSourceRegistry();
            m_authorizer = new TestAuthorizer();
            m_auditor = new TestAuditor();
            m_source = new TestSource(SourceNodeId, SourceCapabilities());
            m_sources.Register(m_source);
            m_handler = CreateHandler();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_manager != null)
            {
                await m_manager.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task OpenDataChannelAsync_WhenRequestIsAccepted_ReturnsGoodAllocatesIdAndEchoesRevisions()
        {
            DataChannelParametersDataType requested = Parameters(
                maxFrameSize: 16_384,
                initialCredit: 32_768,
                priority: 2);
            DataChannelRequestContext context = Context(transportChannelId: 123);

            OpenDataChannelResponse response = await m_handler
                .OpenDataChannelAsync(context, SourceNodeId, 0, requested, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.ChannelId, Is.GreaterThan(0u));
                Assert.That(response.RevisedTransportChannelId, Is.Zero);
                Assert.That(response.RevisedParameters.Direction, Is.EqualTo(DataChannelDirection.SourceToSink));
                Assert.That(response.RevisedParameters.DeliveryMode, Is.EqualTo(DataChannelDeliveryMode.ReliableOrdered));
                Assert.That(response.RevisedParameters.ContentType, Is.EqualTo("application/octet-stream"));
                Assert.That(response.RevisedParameters.MaxFrameSize, Is.EqualTo(16_384u));
                Assert.That(response.RevisedParameters.InitialCredit, Is.EqualTo(32_768u));
                Assert.That(response.RevisedParameters.Priority, Is.EqualTo((byte)2));
                Assert.That(m_auditor.Records, Has.Count.EqualTo(1));
                Assert.That(m_auditor.Records[0].Status, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(m_auditor.Records[0].ChannelId, Is.EqualTo(response.ChannelId));
                Assert.That(m_source.OpenedChannels, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void OpenDataChannelAsync_WhenSessionIsNotActivated_ThrowsBadSessionNotActivated()
        {
            AssertServiceStatus(
                StatusCodes.BadSessionNotActivated,
                () => m_handler.OpenDataChannelAsync(
                    Context(isSessionActivated: false),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenSecurityModeIsNone_ThrowsBadSecurityModeInsufficient()
        {
            AssertServiceStatus(
                StatusCodes.BadSecurityModeInsufficient,
                () => m_handler.OpenDataChannelAsync(
                    Context(securityMode: MessageSecurityMode.None),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenSourceChannelLimitIsReached_ThrowsBadTooManyDataChannels()
        {
            m_source.ActiveChannelCountOverride = 1;
            m_source.CapabilitiesOverride = SourceCapabilities(maxChannels: 1);

            AssertServiceStatus(
                StatusCodes.BadTooManyDataChannels,
                () => m_handler.OpenDataChannelAsync(
                    Context(),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenNoFrameSizeCanBeNegotiated_ThrowsBadDataChannelLimitsExceeded()
        {
            m_source.CapabilitiesOverride = SourceCapabilities(maxFrameSize: 0);
            m_handler = CreateHandler(new DataChannelServerCapabilities
            {
                MaxFrameSize = 0,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaTcpTransport]
            });

            AssertServiceStatus(
                StatusCodes.BadDataChannelLimitsExceeded,
                () => m_handler.OpenDataChannelAsync(
                    Context(transportMaxFrameSize: 0),
                    SourceNodeId,
                    0,
                    Parameters(maxFrameSize: 0),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenSourceIsUnknown_ThrowsBadDataChannelNotSupported()
        {
            AssertServiceStatus(
                StatusCodes.BadDataChannelNotSupported,
                () => m_handler.OpenDataChannelAsync(
                    Context(),
                    new NodeId(999u),
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenTransportProfileIsUnsupported_ThrowsBadDataChannelTransportUnsupported()
        {
            AssertServiceStatus(
                StatusCodes.BadDataChannelTransportUnsupported,
                () => m_handler.OpenDataChannelAsync(
                    Context(transportProfileUri: "urn:unsupported-transport"),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenQuicClientInitiatedDirectionOmitsTransportChannelId_ThrowsBadDataChannelLimitsExceeded()
        {
            m_source.CapabilitiesOverride = SourceCapabilities(direction: DataChannelDirection.SinkToSource);
            m_handler = CreateHandler(ServerCapabilities(Profiles.UaQuicTransport));

            AssertServiceStatus(
                StatusCodes.BadDataChannelLimitsExceeded,
                () => m_handler.OpenDataChannelAsync(
                    Context(transportProfileUri: Profiles.UaQuicTransport, transportChannelId: 0),
                    SourceNodeId,
                    0,
                    Parameters(direction: DataChannelDirection.SinkToSource),
                    CancellationToken.None));
        }

        [Test]
        public async Task OpenDataChannelAsync_WhenQuicSourceToSink_AllocatesRevisedTransportChannelId()
        {
            m_handler = CreateHandler(
                ServerCapabilities(Profiles.UaQuicTransport),
                new TestStreamAllocator(987));

            OpenDataChannelResponse response = await m_handler
                .OpenDataChannelAsync(
                    Context(transportProfileUri: Profiles.UaQuicTransport),
                    SourceNodeId,
                    0,
                    Parameters(direction: DataChannelDirection.SourceToSink),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.RevisedTransportChannelId, Is.EqualTo(987ul));
        }

        [Test]
        public void OpenDataChannelAsync_WhenOfferDoesNotExist_ThrowsBadDataChannelOfferInvalid()
        {
            AssertServiceStatus(
                StatusCodes.BadDataChannelOfferInvalid,
                () => m_handler.OpenDataChannelAsync(
                    Context(),
                    SourceNodeId,
                    42,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenUserIsDenied_ThrowsBadUserAccessDenied()
        {
            m_authorizer.Deny(SourceNodeId);

            AssertServiceStatus(
                StatusCodes.BadUserAccessDenied,
                () => m_handler.OpenDataChannelAsync(
                    Context(),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void ModifyDataChannelAsync_WhenChannelIdIsUnknown_ThrowsBadDataChannelIdInvalid()
        {
            AssertServiceStatus(
                StatusCodes.BadDataChannelIdInvalid,
                () => m_handler.ModifyDataChannelAsync(
                    Context(),
                    12345,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public async Task CloseDataChannelAsync_WhenChannelIsAlreadyClosed_ThrowsBadDataChannelClosed()
        {
            uint channelId = await OpenAndMarkResponseSentAsync().ConfigureAwait(false);

            CloseDataChannelResponse response = await m_handler
                .CloseDataChannelAsync(Context(), channelId, StatusCodes.Good, deleteQueued: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            AssertServiceStatus(
                StatusCodes.BadDataChannelClosed,
                () => m_handler.CloseDataChannelAsync(
                    Context(),
                    channelId,
                    StatusCodes.Good,
                    deleteQueued: true,
                    CancellationToken.None));
        }

        [Test]
        public async Task OnResponseSent_MakesChannelObservableAsOpenAndGrantsConnectionCredit()
        {
            OpenDataChannelResponse response = await m_handler
                .OpenDataChannelAsync(Context(), SourceNodeId, 0, Parameters(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(m_manager.TryGetChannel(response.ChannelId, out DataChannel? channel), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(channel!.State, Is.EqualTo(DataChannelState.Opening));
                Assert.That(m_transport.CountOf(DataChannelFrameType.Credit), Is.Zero);
            });

            m_handler.OnResponseSent(response.ChannelId);
            await WaitForAsync(() => m_transport.CountOf(DataChannelFrameType.Credit) > 0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(channel!.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(
                    m_transport.CountOf(DataChannelFrameType.Credit),
                    Is.GreaterThan(0),
                    "the response boundary is what grants the peer credit to start DATA");
            });
        }

        [Test]
        public async Task ModifyDataChannelAsync_WhenRequestIsMutable_RenegotiatesAndReportsRevisedValues()
        {
            uint channelId = await OpenAndMarkResponseSentAsync().ConfigureAwait(false);

            ModifyDataChannelResponse response = await m_handler
                .ModifyDataChannelAsync(
                    Context(),
                    channelId,
                    Parameters(maxFrameSize: 1024, initialCredit: 1, priority: 7),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.RevisedParameters.MaxFrameSize, Is.EqualTo(1024u));
                Assert.That(response.RevisedParameters.InitialCredit, Is.EqualTo(1024u));
                Assert.That(response.RevisedParameters.Priority, Is.EqualTo((byte)7));
                Assert.That(m_manager.Channels[0].Settings.MaxFrameSize, Is.EqualTo(1024u));
                Assert.That(m_manager.Channels[0].Settings.InitialCredit, Is.EqualTo(1024u));
            });
        }

        [Test]
        public async Task ModifyDataChannelAsync_WhenImmutableParametersChange_ThrowsBadDataChannelLimitsExceeded()
        {
            uint channelId = await OpenAndMarkResponseSentAsync().ConfigureAwait(false);

            AssertServiceStatus(
                StatusCodes.BadDataChannelLimitsExceeded,
                () => m_handler.ModifyDataChannelAsync(
                    Context(),
                    channelId,
                    Parameters(direction: DataChannelDirection.Bidirectional),
                    CancellationToken.None));
        }

        [Test]
        public async Task AbortChannelsOfSession_AbortsOnlyChannelsAuthorizedByThatSession()
        {
            NodeId otherSession = new NodeId(2002u);
            uint first = await OpenAndMarkResponseSentAsync(Context(sessionId: SessionNodeId), SourceNodeId)
                .ConfigureAwait(false);
            uint second = await OpenAndMarkResponseSentAsync(
                    Context(sessionId: SessionNodeId),
                    new NodeId(43u))
                .ConfigureAwait(false);
            uint other = await OpenAndMarkResponseSentAsync(
                    Context(sessionId: otherSession),
                    new NodeId(44u))
                .ConfigureAwait(false);

            m_handler.AbortChannelsOfSession(SessionNodeId, StatusCodes.BadSessionClosed);

            // An aborted channel reaches a terminal state and is released,
            // so the observable outcome is that this Session's channels are
            // gone while the other Session's is untouched. Their identifiers
            // are never reissued, which is what still lets a Close on one
            // return Bad_DataChannelClosed rather than Bad_DataChannelIdInvalid.
            Assert.Multiple(() =>
            {
                Assert.That(IsPresent(first), Is.False);
                Assert.That(IsPresent(second), Is.False);
                Assert.That(m_manager.WasEverAllocated(first), Is.True);
                Assert.That(m_manager.WasEverAllocated(second), Is.True);
                Assert.That(Channel(other).State, Is.EqualTo(DataChannelState.Open));
                Assert.That(Channel(other).Status, Is.EqualTo((StatusCode)StatusCodes.Good));
            });
        }

        [Test]
        public async Task RecheckAuthorizationAsync_WhenSourcesLoseAccess_RevokesThemAndReturnsCount()
        {
            uint first = await OpenAndMarkResponseSentAsync(Context(sessionId: SessionNodeId), SourceNodeId)
                .ConfigureAwait(false);
            uint second = await OpenAndMarkResponseSentAsync(
                    Context(sessionId: SessionNodeId),
                    new NodeId(45u))
                .ConfigureAwait(false);
            uint stillAllowed = await OpenAndMarkResponseSentAsync(
                    Context(sessionId: SessionNodeId),
                    new NodeId(46u))
                .ConfigureAwait(false);

            m_authorizer.Deny(SourceNodeId);
            m_authorizer.Deny(new NodeId(45u));

            int revoked = await m_handler
                .RecheckAuthorizationAsync(
                    sessionId => Context(sessionId: sessionId),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(revoked, Is.EqualTo(2));
                Assert.That(IsPresent(first), Is.False);
                Assert.That(IsPresent(second), Is.False);
                Assert.That(m_manager.WasEverAllocated(first), Is.True);
                Assert.That(m_manager.WasEverAllocated(second), Is.True);
                Assert.That(Channel(stillAllowed).State, Is.EqualTo(DataChannelState.Open));
            });
        }

        private DataChannelServiceHandler CreateHandler(
            DataChannelServerCapabilities? capabilities = null,
            IDataChannelTransportStreamAllocator? streamAllocator = null)
        {
            return new DataChannelServiceHandler(
                m_manager,
                m_sources,
                capabilities ?? ServerCapabilities(),
                m_authorizer,
                m_auditor,
                streamAllocator,
                timeProvider: TimeProvider.System);
        }

        private async ValueTask<uint> OpenAndMarkResponseSentAsync(
            DataChannelRequestContext? context = null,
            NodeId? sourceNodeId = null)
        {
            NodeId source = sourceNodeId ?? SourceNodeId;
            if (!m_sources.TryGet(source, out _))
            {
                m_sources.Register(new TestSource(source, SourceCapabilities()));
            }

            OpenDataChannelResponse response = await m_handler
                .OpenDataChannelAsync(context ?? Context(), source, 0, Parameters(), CancellationToken.None)
                .ConfigureAwait(false);
            m_handler.OnResponseSent(response.ChannelId);
            return response.ChannelId;
        }

        private DataChannel Channel(uint channelId)
        {
            Assert.That(m_manager.TryGetChannel(channelId, out DataChannel? channel), Is.True);
            return channel!;
        }

        private bool IsPresent(uint channelId)
        {
            return m_manager.TryGetChannel(channelId, out _);
        }

        private static void AssertServiceStatus(
            StatusCode expected,
            Func<ValueTask> action)
        {
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await action().ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.StatusCode, Is.EqualTo(expected));
        }

        private static void AssertServiceStatus<T>(
            StatusCode expected,
            Func<ValueTask<T>> action)
        {
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await action().ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.StatusCode, Is.EqualTo(expected));
        }

        private static DataChannelRequestContext Context(
            NodeId? sessionId = null,
            bool isSessionActivated = true,
            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt,
            string? transportProfileUri = null,
            ulong transportChannelId = 0,
            bool transportIsReliable = true,
            uint transportMaxFrameSize = 65_536)
        {
            return new DataChannelRequestContext
            {
                SessionId = sessionId ?? SessionNodeId,
                IsSessionActivated = isSessionActivated,
                SecurityMode = securityMode,
                TransportProfileUri = transportProfileUri ?? Profiles.UaTcpTransport,
                TransportChannelId = transportChannelId,
                TransportIsReliable = transportIsReliable,
                TransportMaxFrameSize = transportMaxFrameSize
            };
        }

        private static DataChannelParametersDataType Parameters(
            DataChannelDirection direction = DataChannelDirection.SourceToSink,
            DataChannelDeliveryMode deliveryMode = DataChannelDeliveryMode.ReliableOrdered,
            uint maxFrameSize = 4096,
            uint initialCredit = 8192,
            byte priority = 1)
        {
            return new DataChannelParametersDataType
            {
                Direction = direction,
                DeliveryMode = deliveryMode,
                ContentType = "application/octet-stream",
                MaxFrameSize = maxFrameSize,
                InitialCredit = initialCredit,
                Priority = priority
            };
        }

        private static DataChannelSourceCapabilities SourceCapabilities(
            DataChannelDirection direction = DataChannelDirection.SourceToSink,
            uint maxFrameSize = 65_536,
            ushort maxChannels = 0)
        {
            return new DataChannelSourceCapabilities
            {
                Direction = direction,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                ContentType = "application/octet-stream",
                MaxFrameSize = maxFrameSize,
                Priority = 3,
                MaxChannels = maxChannels
            };
        }

        private static DataChannelServerCapabilities ServerCapabilities(
            string transportProfileUri = Profiles.UaTcpTransport)
        {
            return new DataChannelServerCapabilities
            {
                MaxFrameSize = 65_536,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [transportProfileUri]
            };
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private sealed class TestSource : IDataChannelSource
        {
            public TestSource(NodeId nodeId, DataChannelSourceCapabilities capabilities)
            {
                NodeId = nodeId;
                CapabilitiesOverride = capabilities;
            }

            public NodeId NodeId { get; }

            public DataChannelSourceCapabilities Capabilities => CapabilitiesOverride;

            public DataChannelSourceCapabilities CapabilitiesOverride { get; set; }

            public int ActiveChannelCount => ActiveChannelCountOverride ?? OpenedChannels.Count;

            public int? ActiveChannelCountOverride { get; set; }

            public List<DataChannel> OpenedChannels { get; } = [];

            public List<(DataChannel Channel, StatusCode Reason)> ClosedChannels { get; } = [];

            public void OnChannelOpened(DataChannel channel)
            {
                OpenedChannels.Add(channel);
            }

            public void OnChannelClosed(DataChannel channel, StatusCode reason)
            {
                ClosedChannels.Add((channel, reason));
            }
        }

        private sealed class TestAuthorizer : IDataChannelAuthorizer
        {
            public ValueTask<bool> IsAuthorizedAsync(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return new ValueTask<bool>(!m_denied.Contains(sourceNodeId));
            }

            public void Deny(NodeId sourceNodeId)
            {
                m_denied.Add(sourceNodeId);
            }

            private readonly HashSet<NodeId> m_denied = [];
        }

        private sealed class TestAuditor : IDataChannelAuditor
        {
            public List<AuditRecord> Records { get; } = [];

            public void OnOpenDataChannel(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelParametersDataType parameters,
                uint? channelId,
                StatusCode status)
            {
                Records.Add(new AuditRecord(sourceNodeId, channelId, status));
            }
        }

        private sealed record AuditRecord(NodeId SourceNodeId, uint? ChannelId, StatusCode Status);

        private sealed class TestStreamAllocator(ulong streamId) : IDataChannelTransportStreamAllocator
        {
            public ValueTask<ulong> AllocateServerStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return new ValueTask<ulong>(streamId);
            }

            public ValueTask BindClientStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                ulong streamId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                return default;
            }
        }

        private static readonly NodeId SessionNodeId = new(1001u);
        private static readonly NodeId SourceNodeId = new(42u);

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;
        private LoopbackTransport m_transport = null!;
        private DataChannelManager m_manager = null!;
        private DataChannelSourceRegistry m_sources = null!;
        private TestAuthorizer m_authorizer = null!;
        private TestAuditor m_auditor = null!;
        private TestSource m_source = null!;
        private DataChannelServiceHandler m_handler = null!;
    }
}
