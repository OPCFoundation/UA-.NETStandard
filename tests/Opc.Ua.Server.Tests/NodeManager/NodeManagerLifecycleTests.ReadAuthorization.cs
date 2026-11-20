// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A migrated monitored item is authorized against the replacement source after same-channel reactivation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityR44ShadowReloadReenableRechecksAcceptedIdentityOnExactSourceAsync(
            bool authorized)
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(1), null).ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var owner = (AsyncCustomNodeManager)original.NodeManager;
            var nodeId = new NodeId(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
            var handle = (NodeHandle)(await owner.GetManagerHandleAsync(nodeId).ConfigureAwait(false))!;
            var originalSource = (BaseVariableState)handle.Node;
            originalSource.Value = 42;
            originalSource.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndMonitoredItemAsync(
                services, nodeId, 1).ConfigureAwait(false);
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            (MonitoredItemNotification initial, acknowledgements) = await PublishForDataChangeAsync(
                services, subscriptionId, acknowledgements).ConfigureAwait(false);
            Assert.That(initial.ClientHandle, Is.EqualTo(1));
            Assert.That(initial.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(initial.Value.WrappedValue.GetInt32(), Is.EqualTo(42));

            SetMonitoringModeResponse disabled = await services.SetMonitoringModeAsync(
                m_requestHeader, subscriptionId, MonitoringMode.Disabled, [itemId]).ConfigureAwait(false);
            Assert.That(disabled.Results[0], Is.EqualTo(StatusCodes.Good));
            NodeManagerRegistration replacement = await m_server.NodeManagerLifecycle.ShadowReloadRuntimeNodeSetAsync(
                original, CreateGenerationOptions(2))
                .ConfigureAwait(false);
            Assert.That(replacement.NodeManager, Is.Not.SameAs(owner));
            var replacementOwner = (AsyncCustomNodeManager)replacement.NodeManager;
            var replacementHandle = (NodeHandle)(await replacementOwner.GetManagerHandleAsync(nodeId)
                .ConfigureAwait(false))!;
            var replacementSource = (BaseVariableState)replacementHandle.Node;
            replacementSource.Value = 42;
            replacementSource.RolePermissions =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Anonymous,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read)
                }
            ];

            var session = (Opc.Ua.Server.Session)server.SessionManager.GetSession(m_requestHeader.AuthenticationToken)!;
            string channelId = session.SecureChannelId;
            var effectiveIdentity = new Mock<IUserIdentity>();
            effectiveIdentity.SetupGet(identity => identity.GrantedRoleIds).Returns(
                authorized ? [ObjectIds.WellKnownRole_Anonymous] : [ObjectIds.WellKnownRole_AuthenticatedUser]);
            effectiveIdentity.SetupGet(identity => identity.TokenType).Returns(UserTokenType.Anonymous);
            using (var activation = new OperationContext(
                m_requestHeader, m_secureChannelContext, RequestType.ActivateSession, RequestLifetime.None, session))
            {
                session.Activate(
                    activation, session.IdentityToken, session.Identity, effectiveIdentity.Object,
                    default, Nonce.CreateNonce(SecurityPolicies.None));
            }
            Assert.That(session.SecureChannelId, Is.EqualTo(channelId));
            replacementSource.Value = authorized ? 42 : 99;

            SetMonitoringModeResponse enabled = await services.SetMonitoringModeAsync(
                m_requestHeader, subscriptionId, MonitoringMode.Reporting, [itemId]).ConfigureAwait(false);
            Assert.That(enabled.Results[0], Is.EqualTo(StatusCodes.Good));
            (MonitoredItemNotification refreshed, _) = await PublishForDataChangeAsync(
                services, subscriptionId, acknowledgements).ConfigureAwait(false);
            Assert.That(refreshed.ClientHandle, Is.EqualTo(1));
            if (authorized)
            {
                Assert.That(refreshed.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(refreshed.Value.WrappedValue.GetInt32(), Is.EqualTo(42));
            }
            else
            {
                Assert.That(refreshed.Value.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(refreshed.Value.WrappedValue.IsNull, Is.True);
            }
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }
    }
}
