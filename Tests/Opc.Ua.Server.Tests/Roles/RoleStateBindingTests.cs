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

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Roles
{
    /// <summary>
    /// Unit tests for <see cref="RoleStateBinding"/> exercising the typed
    /// <c>OnCallAsync</c> delegates against in-memory <see cref="RoleSetState"/>
    /// + <see cref="RoleState"/> instances constructed via the source-generated
    /// public factories (<c>CreateInstanceOfRoleSetType</c> /
    /// <c>CreateInstanceOfRoleType</c>).
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class RoleStateBindingTests
    {
        private Mock<IServerInternal> m_mockServer = null!;
        private Mock<IMasterNodeManager> m_mockMasterNodeManager = null!;
        private ApplicationConfiguration m_configuration = null!;
        private NamespaceTable m_namespaceTable = null!;
        private ServerSystemContext m_serverSystemContext = null!;
        private ITelemetryContext m_telemetry = null!;
        private TestableAsyncCustomNodeManager m_nodeManager = null!;
        private RoleSetState m_roleSet = null!;
        private RoleState m_roleState = null!;
        private RoleManager m_roleManager = null!;
        private Mock<IAuditEventServer> m_auditServer = null!;
        private RoleStateBinding? m_binding;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_mockServer = new Mock<IServerInternal>();
            m_mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append("http://test.org/role-binding/");

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            m_mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(m_namespaceTable));
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.NodeManager).Returns(m_mockMasterNodeManager.Object);
            m_mockServer.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);

            m_serverSystemContext = new ServerSystemContext(m_mockServer.Object);
            m_mockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            m_nodeManager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                NullLogger.Instance,
                "http://test.org/role-binding/");

            // Construct an in-memory RoleSetState anchored at the well-known
            // RoleSet NodeId (i=15606). The source-gen factory creates the
            // RoleSetState with AddRole + RemoveRole children already attached.
            m_roleSet = m_serverSystemContext.CreateInstanceOfRoleSetType(
                parent: null!,
                browseName: new QualifiedName(BrowseNames.RoleSet));
            m_roleSet.NodeId = ObjectIds.Server_ServerCapabilities_RoleSet;
            m_nodeManager.PredefinedNodes[m_roleSet.NodeId] = m_roleSet;

            // Attach one well-known role (Observer) as a child of the RoleSet
            // so the binding picks it up during enumeration. The source-gen
            // CreateInstanceOfRoleType factory only populates the mandatory
            // Identities child, so we attach the optional children manually
            // via the public source-gen factories.
            m_roleState = m_serverSystemContext.CreateInstanceOfRoleType(
                parent: m_roleSet,
                browseName: new QualifiedName(BrowseNames.WellKnownRole_Observer));
            m_roleState.NodeId = ObjectIds.WellKnownRole_Observer;

            m_roleState.AddIdentity = m_serverSystemContext.CreateInstanceOfAddIdentityMethodType(m_roleState);
            m_roleState.RemoveIdentity = m_serverSystemContext.CreateInstanceOfRemoveIdentityMethodType(m_roleState);
            m_roleState.AddApplication = m_serverSystemContext.CreateInstanceOfAddApplicationMethodType(m_roleState);
            m_roleState.RemoveApplication = m_serverSystemContext.CreateInstanceOfRemoveApplicationMethodType(m_roleState);
            m_roleState.AddEndpoint = m_serverSystemContext.CreateInstanceOfAddEndpointMethodType(m_roleState);
            m_roleState.RemoveEndpoint = m_serverSystemContext.CreateInstanceOfRemoveEndpointMethodType(m_roleState);

            m_roleState.ApplicationsExclude = PropertyState<bool>
                .With<VariantBuilder>(m_roleState);
            m_roleState.ApplicationsExclude.BrowseName = new QualifiedName(BrowseNames.ApplicationsExclude);
            m_roleState.ApplicationsExclude.DisplayName = new LocalizedText(BrowseNames.ApplicationsExclude);
            m_roleState.ApplicationsExclude.DataType = DataTypeIds.Boolean;
            m_roleState.ApplicationsExclude.ValueRank = ValueRanks.Scalar;

            m_roleState.EndpointsExclude = PropertyState<bool>
                .With<VariantBuilder>(m_roleState);
            m_roleState.EndpointsExclude.BrowseName = new QualifiedName(BrowseNames.EndpointsExclude);
            m_roleState.EndpointsExclude.DisplayName = new LocalizedText(BrowseNames.EndpointsExclude);
            m_roleState.EndpointsExclude.DataType = DataTypeIds.Boolean;
            m_roleState.EndpointsExclude.ValueRank = ValueRanks.Scalar;

            m_roleSet.AddChild(m_roleState);
            m_nodeManager.PredefinedNodes[m_roleState.NodeId] = m_roleState;

            m_roleManager = new RoleManager();
            m_auditServer = new Mock<IAuditEventServer>();
            m_auditServer.Setup(a => a.Auditing).Returns(true);

            m_binding = RoleStateBinding.Bind(m_nodeManager, m_roleManager, m_auditServer.Object);
            Assume.That(m_binding, Is.Not.Null, "RoleStateBinding.Bind should locate the RoleSet.");
        }

        [TearDown]
        public void TearDown()
        {
            m_binding?.Dispose();
            m_roleManager.Dispose();
            m_nodeManager.Dispose();
        }

        // ----------------------------------------------------------------
        // Auth gate enforcement (Part 18 §4.4)
        // ----------------------------------------------------------------

        [Test]
        public async Task AddIdentityHandler_AnonymousCaller_ReturnsBadUserAccessDenied()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true);
            ServiceResult? result = await InvokeAddIdentityAsync(ctx,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                }).ConfigureAwait(false);
            Assert.That((StatusCode)result!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AddIdentityHandler_UnencryptedChannel_ReturnsBadSecurityModeInsufficient()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.Sign);
            ServiceResult? result = await InvokeAddIdentityAsync(ctx,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                }).ConfigureAwait(false);
            Assert.That((StatusCode)result!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task AddIdentityHandler_AuthorisedCaller_DelegatesToRoleManager()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            ServiceResult? result = await InvokeAddIdentityAsync(ctx,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                }).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result!), Is.True);

            RoleEntry? entry = m_roleManager.GetRole(ObjectIds.WellKnownRole_Observer);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Identities, Has.Count.EqualTo(1));
            Assert.That(entry.Identities[0].Criteria, Is.EqualTo("alice"));
        }

        [Test]
        public async Task AddIdentityHandler_PropagatesManagerErrorVerbatim()
        {
            // Anonymous criteriaType with non-empty criteria is rejected by the rule validator.
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            ServiceResult? result = await InvokeAddIdentityAsync(ctx,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.Anonymous,
                    Criteria = "not-allowed"
                }).ConfigureAwait(false);
            Assert.That((StatusCode)result!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        // ----------------------------------------------------------------
        // Audit-event firing (Part 18 §4.5)
        // ----------------------------------------------------------------

        [Test]
        public async Task AddIdentityHandler_OnSuccess_FiresRoleMappingRuleChangedAuditEvent()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            await InvokeAddIdentityAsync(ctx,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                }).ConfigureAwait(false);

            m_auditServer.Verify(a => a.ReportAuditEvent(
                It.IsAny<ISystemContext>(),
                It.IsAny<AuditEventState>()),
                Times.Once,
                "RoleMappingRuleChangedAuditEvent should fire on success.");
        }

        [Test]
        public async Task AddIdentityHandler_OnAuthFailure_StillFiresAuditEventWithStatusFalse()
        {
            // Auth-failure paths should still log an attempted role mutation —
            // the binding raises the audit event with success=false.
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true);
            await InvokeAddIdentityAsync(ctx,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                }).ConfigureAwait(false);

            m_auditServer.Verify(a => a.ReportAuditEvent(
                It.IsAny<ISystemContext>(),
                It.IsAny<AuditEventState>()),
                Times.Once,
                "Failed mutator attempt should still raise the audit event.");
        }

        // ----------------------------------------------------------------
        // Exclude-flag write path
        // ----------------------------------------------------------------

        [Test]
        public void ApplicationsExcludeWrite_AnonymousCaller_ReturnsBadUserAccessDenied()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true);
            ServiceResult result = InvokeBoolWrite(ctx, m_roleState.ApplicationsExclude!, true);
            Assert.That((StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void ApplicationsExcludeWrite_AdminCaller_UpdatesRoleManager()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            ServiceResult result = InvokeBoolWrite(ctx, m_roleState.ApplicationsExclude!, false);
            Assert.That(ServiceResult.IsGood(result), Is.True);

            RoleEntry? entry = m_roleManager.GetRole(ObjectIds.WellKnownRole_Observer);
            Assert.That(entry!.ApplicationsExclude, Is.False);
        }

        [Test]
        public void ApplicationsExcludeWrite_NonBoolValue_ReturnsBadTypeMismatch()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            ServiceResult result = InvokeWrite(
                ctx, m_roleState.ApplicationsExclude!, new Variant("not-a-bool"));
            Assert.That((StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
        }

        // ----------------------------------------------------------------
        // RoleConfigurationChanged → property sync
        // ----------------------------------------------------------------

        [Test]
        public void RoleConfigurationChanged_SyncsApplicationsExcludeOnRoleState()
        {
            // Mutate via the manager; binding's event subscription pushes the
            // new value to the typed RoleState property.
            Assume.That(m_roleState.ApplicationsExclude, Is.Not.Null);
            // Start at true (the default for Observer is false, so flip to
            // true first to ensure the next setter call actually changes the
            // value and raises the event).
            Assert.That(ServiceResult.IsGood(
                m_roleManager.SetApplicationsExclude(
                    ObjectIds.WellKnownRole_Observer, true)),
                Is.True);
            Assert.That(m_roleState.ApplicationsExclude!.Value, Is.True);

            Assert.That(ServiceResult.IsGood(
                m_roleManager.SetApplicationsExclude(
                    ObjectIds.WellKnownRole_Observer, false)),
                Is.True);
            Assert.That(m_roleState.ApplicationsExclude.Value, Is.False,
                "RoleConfigurationChanged subscription should sync the role-state property.");
        }

        // ----------------------------------------------------------------
        // Dynamic AddRole / RemoveRole address-space materialization
        // ----------------------------------------------------------------

        [Test]
        public async Task AddRoleHandler_AdminCaller_MaterializesRoleStateUnderRoleSet()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx, "CustomReporter", "http://test.org/role-binding/").ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True,
                "AddRole must succeed under SecurityAdmin + SignAndEncrypt.");
            Assert.That(result.RoleNodeId.IsNull, Is.False,
                "AddRole must return a non-null allocated NodeId.");

            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(result.RoleNodeId), Is.True,
                "The new RoleState must be present in PredefinedNodes.");

            Assert.That(m_nodeManager.PredefinedNodes[result.RoleNodeId], Is.InstanceOf<RoleState>(),
                "Materialization must place a typed RoleState — not a bare BaseObjectState.");
        }

        [Test]
        public async Task AddRoleHandler_MaterializedRoleHasAllOptionalChildren()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx, "CustomReporter", "http://test.org/role-binding/").ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            var role = (RoleState)m_nodeManager.PredefinedNodes[result.RoleNodeId];

            Assert.That(role.AddIdentity, Is.Not.Null);
            Assert.That(role.RemoveIdentity, Is.Not.Null);
            Assert.That(role.AddApplication, Is.Not.Null);
            Assert.That(role.RemoveApplication, Is.Not.Null);
            Assert.That(role.AddEndpoint, Is.Not.Null);
            Assert.That(role.RemoveEndpoint, Is.Not.Null);
            Assert.That(role.ApplicationsExclude, Is.Not.Null);
            Assert.That(role.EndpointsExclude, Is.Not.Null);
            Assert.That(role.CustomConfiguration, Is.Not.Null);
        }

        [Test]
        public async Task AddRoleHandler_MaterializedRoleHasOnCallAsyncDelegatesWired()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx, "CustomReporter", "http://test.org/role-binding/").ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            var role = (RoleState)m_nodeManager.PredefinedNodes[result.RoleNodeId];

            // The binding must wire each typed method's OnCallAsync so callers
            // can invoke the methods immediately after AddRole returns.
            Assert.That(role.AddIdentity!.OnCallAsync, Is.Not.Null);
            Assert.That(role.RemoveIdentity!.OnCallAsync, Is.Not.Null);
            Assert.That(role.AddApplication!.OnCallAsync, Is.Not.Null);
            Assert.That(role.RemoveApplication!.OnCallAsync, Is.Not.Null);
            Assert.That(role.AddEndpoint!.OnCallAsync, Is.Not.Null);
            Assert.That(role.RemoveEndpoint!.OnCallAsync, Is.Not.Null);

            // And the exclude-flag writers.
            Assert.That(role.ApplicationsExclude!.OnWriteValue, Is.Not.Null);
            Assert.That(role.EndpointsExclude!.OnWriteValue, Is.Not.Null);
            Assert.That(role.CustomConfiguration!.OnWriteValue, Is.Not.Null);
        }

        [Test]
        public async Task AddRoleHandler_AnonymousCaller_ReturnsBadUserAccessDenied_AndDoesNotMaterialize()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true);
            int countBefore = m_nodeManager.PredefinedNodes.Count;

            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx, "ShouldNotMaterialize", "http://test.org/role-binding/").ConfigureAwait(false);

            Assert.That((StatusCode)result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(m_nodeManager.PredefinedNodes.Count, Is.EqualTo(countBefore),
                "Auth failures must not leave a partially-materialized node.");
        }

        [Test]
        public async Task RemoveRoleHandler_AdminCaller_DropsRoleStateFromAddressSpace()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);

            AddRoleMethodStateResult addResult = await InvokeAddRoleAsync(
                ctx, "Ephemeral", "http://test.org/role-binding/").ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(addResult.ServiceResult), Is.True);
            Assume.That(m_nodeManager.PredefinedNodes.ContainsKey(addResult.RoleNodeId), Is.True);

            ServiceResult removeResult = await InvokeRemoveRoleAsync(ctx, addResult.RoleNodeId)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(removeResult), Is.True);
            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(addResult.RoleNodeId), Is.False,
                "RemoveRole must drop the role from PredefinedNodes.");
        }

        [Test]
        public async Task RemoveRoleHandler_AnonymousCaller_ReturnsBadUserAccessDenied_AndKeepsRole()
        {
            ISystemContext adminCtx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);

            AddRoleMethodStateResult addResult = await InvokeAddRoleAsync(
                adminCtx, "Persistent", "http://test.org/role-binding/").ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(addResult.ServiceResult), Is.True);

            ISystemContext anonCtx = BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true);
            ServiceResult removeResult = await InvokeRemoveRoleAsync(anonCtx, addResult.RoleNodeId)
                .ConfigureAwait(false);

            Assert.That((StatusCode)removeResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(addResult.RoleNodeId), Is.True,
                "Auth-rejected RemoveRole must leave the address space untouched.");
        }

        [Test]
        public async Task AddRoleHandler_TypedRoleStateUpgradedByBinding_CanCallAddIdentity()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult addResult = await InvokeAddRoleAsync(
                ctx, "Engineer", "http://test.org/role-binding/").ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(addResult.ServiceResult), Is.True);

            // Invoke AddIdentity on the freshly materialized role to prove the
            // OnCallAsync delegate works end-to-end.
            var role = (RoleState)m_nodeManager.PredefinedNodes[addResult.RoleNodeId];
            AddIdentityMethodState addIdentity = role.AddIdentity!;
            AddIdentityMethodStateResult identityResult = await addIdentity.OnCallAsync!(
                ctx, addIdentity, role.NodeId,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "carol"
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(identityResult.ServiceResult), Is.True);

            RoleEntry? entry = m_roleManager.GetRole(addResult.RoleNodeId);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Identities, Has.Count.EqualTo(1));
            Assert.That(entry.Identities[0].Criteria, Is.EqualTo("carol"));
        }

        // ----------------------------------------------------------------
        // Test helpers
        // ----------------------------------------------------------------

        private SessionSystemContext BuildContext(
            MessageSecurityMode securityMode, bool anonymous)
        {
            return BuildContext(securityMode, CreateIdentity(anonymous, securityAdmin: false));
        }

        private SessionSystemContext BuildAdminContext(MessageSecurityMode securityMode)
        {
            return BuildContext(securityMode, CreateIdentity(anonymous: false, securityAdmin: true));
        }

        private SessionSystemContext BuildContext(
            MessageSecurityMode securityMode, IUserIdentity identity)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext(
                "test-channel", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(), channelContext, RequestType.Call, RequestLifetime.None,
                identity);
            return new SessionSystemContext(operationContext, m_telemetry)
            {
                NamespaceUris = m_namespaceTable,
                ServerUris = new StringTable()
            };
        }

        private static IUserIdentity CreateIdentity(bool anonymous, bool securityAdmin)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(
                anonymous ? UserTokenType.Anonymous : UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(anonymous ? "Anonymous" : "admin");
            NodeId[] roles = securityAdmin
                ? [ObjectIds.WellKnownRole_SecurityAdmin]
                : System.Array.Empty<NodeId>();
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(roles));
            return identity.Object;
        }

        private async ValueTask<ServiceResult?> InvokeAddIdentityAsync(
            ISystemContext context, IdentityMappingRuleType rule)
        {
            AddIdentityMethodState? method = m_roleState.AddIdentity;
            Assume.That(method, Is.Not.Null, "AddIdentity method state should be attached.");
            Assume.That(method!.OnCallAsync, Is.Not.Null,
                "Binding should have wired the typed OnCallAsync delegate.");

            AddIdentityMethodStateResult result = await method.OnCallAsync!(
                context, method, m_roleState.NodeId, rule, CancellationToken.None)
                .ConfigureAwait(false);
            return result.ServiceResult;
        }

        private ValueTask<AddRoleMethodStateResult> InvokeAddRoleAsync(
            ISystemContext context, string roleName, string namespaceUri)
        {
            AddRoleMethodState? method = m_roleSet.AddRole;
            Assume.That(method, Is.Not.Null, "AddRole method state should be attached.");
            Assume.That(method!.OnCallAsync, Is.Not.Null,
                "Binding should have wired the typed OnCallAsync delegate.");

            return method.OnCallAsync!(
                context, method, m_roleSet.NodeId, roleName, namespaceUri, CancellationToken.None);
        }

        private async ValueTask<ServiceResult> InvokeRemoveRoleAsync(
            ISystemContext context, NodeId roleNodeId)
        {
            RemoveRoleMethodState? method = m_roleSet.RemoveRole;
            Assume.That(method, Is.Not.Null, "RemoveRole method state should be attached.");
            Assume.That(method!.OnCallAsync, Is.Not.Null,
                "Binding should have wired the typed OnCallAsync delegate.");

            RemoveRoleMethodStateResult result = await method.OnCallAsync!(
                context, method, m_roleSet.NodeId, roleNodeId, CancellationToken.None)
                .ConfigureAwait(false);
            return result.ServiceResult;
        }

        private static ServiceResult InvokeBoolWrite(
            ISystemContext context, PropertyState<bool> property, bool value)
        {
            return InvokeWrite(context, property, new Variant(value));
        }

        private static ServiceResult InvokeWrite(
            ISystemContext context, PropertyState<bool> property, Variant value)
        {
            NodeValueEventHandler? handler = property.OnWriteValue;
            Assume.That(handler, Is.Not.Null,
                "Binding should have wired the OnWriteValue handler.");
            StatusCode statusCode = StatusCodes.Good;
            DateTimeUtc timestamp = DateTimeUtc.MinValue;
            Variant working = value;
            return handler!(
                context, property, NumericRange.Null, QualifiedName.Null,
                ref working, ref statusCode, ref timestamp);
        }
    }
}
