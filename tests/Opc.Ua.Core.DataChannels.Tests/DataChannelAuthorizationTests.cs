/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

using Opc.Ua.Server;

namespace Opc.Ua.Core.DataChannels.Tests
{
    [TestFixture]
    [Category("DataChannels")]
    public class DataChannelAuthorizationTests
    {
        [SetUp]
        public void SetUp()
        {
            m_fixture = new AuthorizationFixture();
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("authorization", 65536, m_telemetry);
            m_transport = new LoopbackTransport(m_bufferManager, TimeProvider.System);
            m_manager = new DataChannelManager(m_transport, true, m_telemetry);
            m_sources = new DataChannelSourceRegistry();
            m_sources.Register(new TestSource(SourceNodeId, SourceCapabilities()));
            m_handler = CreateHandler(m_fixture.CreateAuthorizer());
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
        public void OpenDataChannelAsync_WhenSourceHasNoOwningNodeManager_ThrowsBadUserAccessDenied()
        {
            m_fixture.WithoutOwningNodeManager();

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
        public void OpenDataChannelAsync_WhenEncryptionIsRequiredOnSignOnlyChannel_ThrowsBadUserAccessDenied()
        {
            m_fixture.Metadata = Metadata(
                accessRestrictions: AccessRestrictionType.EncryptionRequired);

            AssertServiceStatus(
                StatusCodes.BadUserAccessDenied,
                () => m_handler.OpenDataChannelAsync(
                    Context(securityMode: MessageSecurityMode.Sign),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None));
        }

        [Test]
        public void OpenDataChannelAsync_WhenReadPermissionIsDenied_ThrowsBadUserAccessDenied()
        {
            m_fixture.Metadata = Metadata(rolePermissions: [Role(PermissionType.Browse)]);

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
        public async Task RecheckAuthorizationAsync_WhenReadPermissionIsRevoked_AbortsOpenChannel()
        {
            m_fixture.Metadata = Metadata(rolePermissions: [Role(PermissionType.Read)]);
            OpenDataChannelResponse response = await m_handler
                .OpenDataChannelAsync(
                    Context(),
                    SourceNodeId,
                    0,
                    Parameters(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            m_handler.OnResponseSent(response.ChannelId);
            Assert.That(m_manager.TryGetChannel(response.ChannelId, out _), Is.True);

            m_fixture.Metadata = Metadata(rolePermissions: [Role(PermissionType.Browse)]);

            int revoked = await m_handler
                .RecheckAuthorizationAsync(
                    sessionId => Context(sessionId: sessionId),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(revoked, Is.EqualTo(1));
                Assert.That(m_manager.TryGetChannel(response.ChannelId, out _), Is.False);
                Assert.That(m_manager.WasEverAllocated(response.ChannelId), Is.True);
            });
        }

        private DataChannelServiceHandler CreateHandler(IDataChannelAuthorizer authorizer)
        {
            return new DataChannelServiceHandler(
                m_manager,
                m_sources,
                ServerCapabilities(),
                authorizer,
                auditor: null,
                streamAllocator: null,
                timeProvider: TimeProvider.System);
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
            MessageSecurityMode securityMode = MessageSecurityMode.SignAndEncrypt)
        {
            return new DataChannelRequestContext
            {
                SessionId = sessionId ?? SessionNodeId,
                IsSessionActivated = true,
                SecurityMode = securityMode,
                TransportProfileUri = Profiles.UaTcpTransport,
                TransportIsReliable = true,
                TransportMaxFrameSize = 65_536
            };
        }

        /// <summary>
        /// Part 4 errata §7.2, DCS-023: a channel that carries payload towards
        /// the source is a write, so read permission alone must not grant it.
        /// A user permitted to watch a drive but not to command it could
        /// otherwise open the SinkToSource channel that drive advertises and
        /// send it firmware, setpoints or console input.
        /// </summary>
        [TestCase(DataChannelDirection.SinkToSource)]
        [TestCase(DataChannelDirection.Bidirectional)]
        public void OpenDataChannelAsyncWhenOnlyReadIsPermittedRefusesAnInboundDirection(
            DataChannelDirection direction)
        {
            m_fixture.Metadata = Metadata(rolePermissions: [Role(PermissionType.Read)]);

            AssertServiceStatus(
                StatusCodes.BadUserAccessDenied,
                () => m_handler.OpenDataChannelAsync(
                    Context(),
                    SourceNodeId,
                    0,
                    Parameters(direction),
                    CancellationToken.None));
        }

        /// <summary>
        /// The mirror of the case above: Write permission is what an inbound
        /// direction needs, and it is enough for one.
        /// </summary>
        [Test]
        public async Task OpenDataChannelAsyncWhenWriteIsPermittedGrantsAnInboundDirectionAsync()
        {
            m_fixture.Metadata = Metadata(rolePermissions: [Role(PermissionType.Write)]);

            OpenDataChannelResponse response = await m_handler
                .OpenDataChannelAsync(
                    Context(),
                    SourceNodeId,
                    0,
                    Parameters(DataChannelDirection.SinkToSource),
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response.ChannelId, Is.Not.Zero);
        }

        private static DataChannelParametersDataType Parameters()
        {
            return Parameters(DataChannelDirection.SourceToSink);
        }

        private static DataChannelParametersDataType Parameters(DataChannelDirection direction)
        {
            return new DataChannelParametersDataType
            {
                Direction = direction,
                DeliveryMode = DataChannelDeliveryMode.ReliableOrdered,
                ContentType = "application/octet-stream",
                MaxFrameSize = 4096,
                InitialCredit = 8192,
                Priority = 1
            };
        }

        private static DataChannelSourceCapabilities SourceCapabilities()
        {
            return new DataChannelSourceCapabilities
            {
                Direction = DataChannelDirection.Bidirectional,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                ContentType = "application/octet-stream",
                MaxFrameSize = 65_536,
                Priority = 3
            };
        }

        private static DataChannelServerCapabilities ServerCapabilities()
        {
            return new DataChannelServerCapabilities
            {
                MaxFrameSize = 65_536,
                MaxCreditPerChannel = 1024 * 1024,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                SupportedTransportProfileUris = [Profiles.UaTcpTransport]
            };
        }

        private static NodeMetadata Metadata(
            AccessRestrictionType accessRestrictions = AccessRestrictionType.None,
            ArrayOf<RolePermissionType>? rolePermissions = null)
        {
            return new NodeMetadata(new object(), SourceNodeId)
            {
                NodeClass = NodeClass.Object,
                BrowseName = new QualifiedName("DataChannelSource"),
                DisplayName = new LocalizedText("DataChannelSource"),
                AccessRestrictions = accessRestrictions,
                RolePermissions = rolePermissions ?? [],
                DefaultRolePermissions = [],
                UserRolePermissions = [],
                DefaultUserRolePermissions = []
            };
        }

        private static RolePermissionType Role(PermissionType permissions)
        {
            return new RolePermissionType
            {
                RoleId = ObjectIds.WellKnownRole_Anonymous,
                Permissions = (uint)permissions
            };
        }

        private sealed class AuthorizationFixture
        {
            public AuthorizationFixture()
            {
                Session.SetupGet(session => session.Id).Returns(SessionNodeId);
                Session.SetupGet(session => session.Activated).Returns(true);
                Session.SetupGet(session => session.EffectiveIdentity).Returns(new UserIdentity());
                Session.SetupGet(session => session.PreferredLocales).Returns(Array.Empty<string>());
                Session.SetupGet(session => session.SecureChannelId).Returns("secure-channel");

                Sessions.Setup(manager => manager.GetSessions()).Returns([Session.Object]);
                Master
                    .Setup(manager => manager.GetManagerHandleAsync(
                        It.IsAny<NodeId>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((NodeId _, CancellationToken _) =>
                        new ValueTask<(object?, IAsyncNodeManager?)>((NodeHandle, NodeManager.Object)));
                NodeManager
                    .Setup(manager => manager.GetPermissionMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<Dictionary<NodeId, Variant[]>>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<NodeMetadata?>(Metadata));
                NodeManager
                    .Setup(manager => manager.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<NodeMetadata>(Metadata!));
                NodeManager
                    .Setup(manager => manager.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(StatusCodes.Good));
                Server.SetupGet(server => server.SessionManager).Returns(Sessions.Object);
                Server.SetupGet(server => server.NodeManager).Returns(Master.Object);

                Metadata = DataChannelAuthorizationTests.Metadata();
            }

            public NodeMetadata? Metadata { get; set; }

            private object NodeHandle { get; } = new();

            private Mock<IAsyncNodeManager> NodeManager { get; } = new(MockBehavior.Strict);

            private Mock<IMasterNodeManager> Master { get; } = new(MockBehavior.Strict);

            private Mock<ISessionManager> Sessions { get; } = new(MockBehavior.Strict);

            private Mock<ISession> Session { get; } = new(MockBehavior.Strict);

            private Mock<IServerInternal> Server { get; } = new(MockBehavior.Strict);

            public void WithoutOwningNodeManager()
            {
                Master
                    .Setup(manager => manager.GetManagerHandleAsync(
                        It.IsAny<NodeId>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((NodeId _, CancellationToken _) =>
                        new ValueTask<(object?, IAsyncNodeManager?)>((null, null)));
            }

            public IDataChannelAuthorizer CreateAuthorizer()
            {
                Type? type = typeof(StandardServer).GetNestedType(
                    "ReadEquivalentDataChannelAuthorizer",
                    BindingFlags.NonPublic);
                Assert.That(type, Is.Not.Null);

                object? instance = Activator.CreateInstance(type!, Server.Object);
                Assert.That(instance, Is.InstanceOf<IDataChannelAuthorizer>());
                return (IDataChannelAuthorizer)instance!;
            }
        }

        private sealed class TestSource(NodeId nodeId, DataChannelSourceCapabilities capabilities)
            : IDataChannelSource
        {
            public NodeId NodeId { get; } = nodeId;

            public DataChannelSourceCapabilities Capabilities { get; } = capabilities;

            public int ActiveChannelCount => 0;

            public void OnChannelOpened(DataChannel channel)
            {
            }

            public void OnChannelClosed(DataChannel channel, StatusCode reason)
            {
            }
        }

        private static readonly NodeId SessionNodeId = new(1000u);
        private static readonly NodeId SourceNodeId = new(1001u);
        private AuthorizationFixture m_fixture = null!;
        private BufferManager m_bufferManager = null!;
        private LoopbackTransport m_transport = null!;
        private DataChannelManager m_manager = null!;
        private DataChannelSourceRegistry m_sources = null!;
        private DataChannelServiceHandler m_handler = null!;
        private ITelemetryContext m_telemetry = null!;
    }
}
