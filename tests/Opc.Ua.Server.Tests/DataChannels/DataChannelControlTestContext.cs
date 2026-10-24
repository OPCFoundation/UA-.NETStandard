/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Opc.Ua.DataChannels;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Real common-runtime peers with a bounded deterministic carrier and a live mocked Session policy host.
    /// Network carrier conformance is deliberately not claimed by this fixture.
    /// </summary>
    internal sealed class DataChannelControlTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Builds the source, authenticated identities and Session metadata seams.
        /// </summary>
        private DataChannelControlTestContext(ArrayOf<ExpandedNodeId> contentNodeIds)
        {
            Telemetry = NUnitTelemetryContext.Create();
            Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            NamespaceUris = new NamespaceTable();
            NamespaceUris.Append("urn:control-tests:source");
            SourceNodeId = new NodeId(400u, 1);
            Certificate = CertificateBuilder.Create("CN=DataChannelControlTests")
                .AddExtension(new X509SubjectAltNameExtension("urn:control-tests:client", ["localhost"]))
                .CreateForRSA();
            ServerCertificate = CertificateBuilder.Create("CN=DataChannelControlServer")
                .AddExtension(new X509SubjectAltNameExtension("urn:control-tests:server", ["localhost"]))
                .CreateForRSA();
            PeerKey = DataChannelCertificateIdentity.GetKeyThumbprint(Certificate);
            Identity = new UserIdentity();
            EffectiveIdentity = new RoleBasedIdentity(Identity, [Role.Anonymous], NamespaceUris);
            SourcePolicy = new TestSourcePolicy();
            Source = new DataChannelSource(ResourceUri, SourcePolicy)
            {
                SourceNodeId = new ExpandedNodeId(400u, "urn:control-tests:source"),
                ContentNodeIds = contentNodeIds,
                SupportedDirections = [DataChannelDirection.OpenerToAcceptor,
                    DataChannelDirection.AcceptorToOpener, DataChannelDirection.Duplex],
                SupportedProtocols = [DataChannelProfiles.UadpPayload],
                ContentType = "application/opcua+uadp",
                MaxMessageSize = 2048,
                MaxReceiveBytes = 8192,
                MaxFlows = 4
            };
            var sources = new DataChannelSourceRegistry();
            sources.Register(Source);
            Options = new DataChannelOptions
            {
                MaxFlows = 8,
                MaxMessageSize = 4096,
                MaxReceiveBytes = 16384,
                MaxContextBytes = 65536
            };
            Provider = new ServerDataChannelProvider(
                sources, Options, new DataChannelApplicationBudget(), Clock, Telemetry);
            Session.SetupGet(value => value.Id).Returns(new NodeId(100u, 1));
            Session.SetupGet(value => value.Activated).Returns(() => Activated);
            Session.SetupGet(value => value.IsClosing).Returns(() => Closing);
            Session.SetupGet(value => value.HasExpired).Returns(() => Expired);
            Session.SetupGet(value => value.Identity).Returns(() => Identity);
            Session.SetupGet(value => value.IdentityToken).Returns(() => Identity.TokenHandler);
            Session.SetupGet(value => value.EffectiveIdentity).Returns(() => EffectiveIdentity);
            Session.SetupGet(value => value.PreferredLocales).Returns([]);
            Session.SetupGet(value => value.ClientCertificate).Returns(Certificate);
            Session.SetupGet(value => value.ClientApplicationUri).Returns("urn:control-tests:client");
            Session.SetupGet(value => value.SecureChannelId).Returns(() => ControlChannelId);
            Session.SetupGet(value => value.LastContactTickCount).Returns(() => LastContact);
            Session.SetupGet(value => value.EndpointDescription).Returns(new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaTcpTransport
            });
            Session.Setup(value => value.ReadDiagnostics(It.IsAny<Func<SessionDiagnosticsDataType, double>>()))
                .Returns((Func<SessionDiagnosticsDataType, double> read) =>
                    read(new SessionDiagnosticsDataType { ActualSessionTimeout = SessionTimeout }));
            LastContact = Clock.GetTimestampMilliseconds();
            Sessions.Setup(value => value.GetSessions()).Returns(() => [Session.Object]);
            Server.SetupGet(value => value.SessionManager).Returns(Sessions.Object);
            Server.SetupGet(value => value.NamespaceUris).Returns(NamespaceUris);
            var serverUris = new StringTable();
            serverUris.Append("urn:control-tests:server");
            Server.SetupGet(value => value.ServerUris).Returns(serverUris);
            Server.SetupGet(value => value.NodeManager).Returns(Master.Object);
            Master.Setup(value => value.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken _) => new ValueTask<(object?, IAsyncNodeManager?)>(
                    HasSource ? (Source, NodeManager.Object) : (null, null)));
            NodeManager.Setup(value => value.GetPermissionMetadataAsync(
                It.IsAny<OperationContext>(), It.IsAny<object>(), It.IsAny<BrowseResultMask>(),
                It.IsAny<Dictionary<NodeId, Variant[]>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<NodeMetadata?>(Metadata));
            NodeManager.Setup(value => value.GetNodeMetadataAsync(
                It.IsAny<OperationContext>(), It.IsAny<object>(), It.IsAny<BrowseResultMask>(),
                It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<NodeMetadata>(Metadata!));
            Metadata = new NodeMetadata(Source, SourceNodeId)
            {
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("Source"),
                AccessRestrictions = AccessRestrictionType.None,
                RolePermissions = [],
                DefaultRolePermissions = [],
                UserRolePermissions = [],
                DefaultUserRolePermissions = []
            };
            Provider.AttachServer(Server.Object);
        }

        internal const string ResourceUri = "urn:control-tests:resource";
        internal ITelemetryContext Telemetry { get; }
        internal FakeTimeProvider Clock { get; }
        internal NamespaceTable NamespaceUris { get; }
        internal NodeId SourceNodeId { get; }
        internal Certificate Certificate { get; }
        internal Certificate ServerCertificate { get; }
        internal ByteString PeerKey { get; }
        internal UserIdentity Identity { get; set; }
        internal IUserIdentity EffectiveIdentity { get; set; }
        internal bool Activated { get; set; } = true;
        internal bool Closing { get; set; }
        internal bool Expired { get; set; }
        internal bool HasSource { get; set; } = true;
        internal double SessionTimeout { get; set; } = 120000;
        internal long LastContact { get; set; }
        internal string ControlChannelId { get; set; } = "control-one";
        internal NodeMetadata? Metadata { get; set; }
        internal DataChannelSource Source { get; }
        internal TestSourcePolicy SourcePolicy { get; }
        internal DataChannelOptions Options { get; }
        internal ServerDataChannelProvider Provider { get; }
        internal Mock<ISession> Session { get; } = new();
        internal Mock<ISessionManager> Sessions { get; } = new();
        internal Mock<IServerInternal> Server { get; } = new();
        internal Mock<IMasterNodeManager> Master { get; } = new();
        internal Mock<IAsyncNodeManager> NodeManager { get; } = new();
        internal DataChannelControlTestPair Pair { get; private set; } = null!;

        /// <summary>
        /// Establishes two actual common-runtime contexts through their HELLO exchange.
        /// </summary>
        internal static async Task<DataChannelControlTestContext> CreateAsync(
            ArrayOf<ExpandedNodeId> contentNodeIds = default)
        {
            var fixture = new DataChannelControlTestContext(contentNodeIds);
            try
            {
                fixture.Pair = await DataChannelControlTestPair.CreateAsync(
                    fixture.Provider, fixture.Options, fixture.Clock, fixture.Telemetry,
                    "urn:control-tests:server", "urn:control-tests:client",
                    DataChannelCertificateIdentity.GetKeyThumbprint(fixture.ServerCertificate), fixture.PeerKey)
                    .ConfigureAwait(false);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates a complete valid flow proposal.
        /// </summary>
        internal static DataChannelParametersDataType Parameters(
            DataChannelDirection direction = DataChannelDirection.Duplex)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = ResourceUri,
                Protocol = DataChannelProfiles.UadpPayload,
                ContentType = "application/opcua+uadp",
                Direction = direction,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 1024,
                MaxReceiveBytes = 4096,
                Priority = 1
            };
        }

        /// <summary>
        /// Encodes an ordinary UADP application message with an unsigned PublisherId.
        /// </summary>
        internal static ByteString UadpPayload(ByteString body)
        {
            var networkMessage = new byte[7 + body.Length];
            networkMessage[0] = 0x91;
            networkMessage[1] = 0x82;
            networkMessage[2] = 0;
            networkMessage[3] = 42;
            body.Span.CopyTo(networkMessage.AsSpan(7));
            return new ByteString(networkMessage);
        }

        /// <summary>
        /// Creates the issuer-facing binding from the actual confirmed context.
        /// </summary>
        internal DataChannelBindingDataType Binding()
        {
            DataChannelContextIdentity identity = Pair.Server.Identity;
            return new DataChannelBindingDataType
            {
                ContextId = identity.ContextId,
                Generation = identity.Generation,
                PeerApplicationUri = identity.Peer.PeerApplicationUri,
                PeerKeyThumbprint = identity.Peer.PeerKeyThumbprint,
                Challenge = identity.LocalChallenge,
                ExpiresAt = DateTime.MaxValue
            };
        }

        /// <summary>
        /// Invokes the provider admission after source/Session setup.
        /// </summary>
        internal ValueTask<DataChannelGrantDataType> GrantAsync(uint id)
        {
            return Provider.OpenAsync(Session.Object, id, SourceNodeId, Binding(), Parameters());
        }

        /// <summary>
        /// Builds the exact runtime redemption request or a chosen negative variant.
        /// </summary>
        internal DataChannelAuthorizationRequest Redemption(
            DataChannelGrantDataType grant,
            DataChannelContextIdentity? identity = null,
            DataChannelParametersDataType? parameters = null)
        {
            return new DataChannelAuthorizationRequest(
                identity ?? Pair.Server.Identity, grant.ChannelId, false, DataChannelAuthorizationMode.Session,
                grant.GrantId, DataChannelAuthorizationPurpose.Open, 0, 0, parameters ?? Parameters());
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Pair != null)
                {
                    await Pair.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await Provider.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    Certificate.Dispose();
                    ServerCertificate.Dispose();
                }
            }
        }

        /// <summary>
        /// Controllable domain policy which is still applied to configured and Session requests separately.
        /// </summary>
        internal sealed class TestSourcePolicy : IDataChannelSourcePolicy
        {
            internal bool AllowSession { get; set; } = true;
            internal bool AllowConfigured { get; set; } = true;

            public ValueTask<DataChannelResult> AuthorizeAsync(
                DataChannelSourceAccess access,
                CancellationToken cancellationToken = default)
            {
                bool allowed = access.Session == null ? AllowConfigured : AllowSession;
                return new ValueTask<DataChannelResult>(
                    allowed ? DataChannelResult.Good : DataChannelResult.NotAuthorized);
            }
        }
    }

    /// <summary>
    /// A bounded two-peer runtime carrier for control-plane regression tests.
    /// </summary>
    internal sealed class DataChannelControlTestPair : IAsyncDisposable
    {
        private DataChannelControlTestPair(
            DataChannelContext client,
            DataChannelContext server,
            TestCarrier clientCarrier,
            TestCarrier serverCarrier)
        {
            Client = client;
            Server = server;
            m_clientCarrier = clientCarrier;
            m_serverCarrier = serverCarrier;
        }

        internal DataChannelContext Client { get; }
        internal DataChannelContext Server { get; }

        internal static async Task<DataChannelControlTestPair> CreateAsync(
            ServerDataChannelProvider provider,
            DataChannelOptions options,
            TimeProvider clock,
            ITelemetryContext telemetry,
            string serverUri,
            string clientUri,
            ByteString serverKey,
            ByteString clientKey,
            IDataChannelAuthorizer? clientAuthorizer = null)
        {
            var clientCarrier = new TestCarrier(new DataChannelPeerIdentity(
                clientUri, serverUri, clientKey, serverKey, DataChannelProtectionMode.Confidential, true));
            var serverCarrier = new TestCarrier(new DataChannelPeerIdentity(
                serverUri, clientUri, serverKey, clientKey, DataChannelProtectionMode.Confidential, false));
            clientCarrier.Peer = serverCarrier;
            serverCarrier.Peer = clientCarrier;
            var client = new DataChannelContext(
                clientCarrier, clientAuthorizer ?? new LocalPolicy(), options,
                new DataChannelApplicationBudget(), clock, telemetry);
            DataChannelContext server = provider.Create(serverCarrier);
            clientCarrier.Start(client);
            serverCarrier.Start(server);
            var pair = new DataChannelControlTestPair(client, server, clientCarrier, serverCarrier);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await Task.WhenAll(
                    client.StartAsync(deadline.Token).AsTask(), server.StartAsync(deadline.Token).AsTask())
                    .ConfigureAwait(false);
                return pair;
            }
            catch
            {
                await pair.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(
                Client.DisposeAsync().AsTask(),
                Server.DisposeAsync().AsTask(),
                m_clientCarrier.DisposeAsync().AsTask(),
                m_serverCarrier.DisposeAsync().AsTask()).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicit local application policy used where the test targets the issuer's checks.
        /// </summary>
        internal sealed class LocalPolicy : IDataChannelAuthorizer
        {
            internal bool Allowed { get; set; } = true;

            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<DataChannelAuthorization>(Allowed &&
                    request.Parameters.ResourceUri == DataChannelControlTestContext.ResourceUri
                        ? new DataChannelAuthorization(DataChannelResult.Good, request.Parameters)
                        : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
        }

        /// <summary>
        /// Sends complete owned authenticated plaintext containers to a serialized receive pump.
        /// </summary>
        private sealed class TestCarrier(DataChannelPeerIdentity identity) : IDataChannelCarrier, IAsyncDisposable
        {
            public DataChannelCarrierKind Kind => DataChannelCarrierKind.SharedUasc;
            public DataChannelPeerIdentity Identity { get; } = identity;
            public int MaxRecordSize => 65536;
            public int MaxDatagramSize => 0;
            public ulong ManagementStreamId => 0;
            internal TestCarrier Peer { get; set; } = null!;

            internal void Start(DataChannelContext context)
            {
                m_receiver = ReceiveAsync(context);
            }

            public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask<bool> BindStreamAsync(
                ulong streamId, uint channelId, bool locallyOpened, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(false);
            }

            public async ValueTask<DataChannelResult> SendAsync(
                DataChannelWrite write, CancellationToken cancellationToken = default)
            {
                ByteString payload = write.Payload.Copy();
                DataChannelUascMessageKind kind = write.IsData
                    ? DataChannelUascMessageKind.Flo
                    : DataChannelUascMessageKind.Flc;
                if (!await Peer.m_incoming.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) ||
                    !write.TryCommit())
                {
                    return DataChannelResult.Closed;
                }
                return Peer.m_incoming.Writer.TryWrite((kind, write.RequestId, payload))
                    ? DataChannelResult.Good : DataChannelResult.WouldBlock;
            }

            public ValueTask CloseFlowAsync(
                ulong streamId, DataChannelResult result, CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask CloseAsync(DataChannelResult result, CancellationToken cancellationToken = default)
            {
                m_incoming.Writer.TryComplete();
                return default;
            }

            public async ValueTask DisposeAsync()
            {
                m_incoming.Writer.TryComplete();
                await m_receiver.ConfigureAwait(false);
            }

            private async Task ReceiveAsync(DataChannelContext context)
            {
                while (await m_incoming.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (m_incoming.Reader.TryRead(
                        out (DataChannelUascMessageKind Kind, uint RequestId, ByteString Payload) value))
                    {
                        await context.ReceiveUascAsync(
                            value.Kind, (byte)'F', value.RequestId, value.Payload).ConfigureAwait(false);
                    }
                }
            }

            private readonly Channel<(DataChannelUascMessageKind Kind, uint RequestId, ByteString Payload)> m_incoming =
                Channel.CreateBounded<(DataChannelUascMessageKind, uint, ByteString)>(new BoundedChannelOptions(256)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
            private Task m_receiver = Task.CompletedTask;
        }

        private readonly TestCarrier m_clientCarrier;
        private readonly TestCarrier m_serverCarrier;
    }
}
