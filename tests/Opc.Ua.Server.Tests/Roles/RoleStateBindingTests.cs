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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        private ObservedLogger? m_observedLogger;
        private static readonly TimeSpan s_workerTimeout = TimeSpan.FromSeconds(10);

        [SetUp]
        public async Task SetUp()
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

            m_roleState.CustomConfiguration = PropertyState<bool>
                .With<VariantBuilder>(m_roleState);
            m_roleState.CustomConfiguration.BrowseName = new QualifiedName(BrowseNames.CustomConfiguration);
            m_roleState.CustomConfiguration.DisplayName = new LocalizedText(BrowseNames.CustomConfiguration);
            m_roleState.CustomConfiguration.DataType = DataTypeIds.Boolean;
            m_roleState.CustomConfiguration.ValueRank = ValueRanks.Scalar;

            // Applications and Endpoints typed properties are not attached by
            // the source-gen factory either — attach them via the typed
            // PropertyState&lt;ArrayOf&lt;T&gt;&gt; builder so SyncPropertiesFromManager
            // can write back to them when the manager state changes.
            m_roleState.Applications = PropertyState<ArrayOf<string>>
                .With<VariantBuilder>(m_roleState);
            m_roleState.Applications.BrowseName = new QualifiedName(BrowseNames.Applications);
            m_roleState.Applications.DisplayName = new LocalizedText(BrowseNames.Applications);
            m_roleState.Applications.DataType = DataTypeIds.String;
            m_roleState.Applications.ValueRank = ValueRanks.OneDimension;

            m_roleState.Endpoints = PropertyState<ArrayOf<EndpointType>>
                .With<StructureBuilder<EndpointType>>(m_roleState);
            m_roleState.Endpoints.BrowseName = new QualifiedName(BrowseNames.Endpoints);
            m_roleState.Endpoints.DisplayName = new LocalizedText(BrowseNames.Endpoints);
            m_roleState.Endpoints.DataType = DataTypeIds.EndpointType;
            m_roleState.Endpoints.ValueRank = ValueRanks.OneDimension;

            m_roleSet.AddChild(m_roleState);
            m_nodeManager.PredefinedNodes[m_roleState.NodeId] = m_roleState;

            m_roleManager = new RoleManager();
            m_auditServer = new Mock<IAuditEventServer>();
            m_auditServer.Setup(a => a.Auditing).Returns(true);

            m_binding = await RoleStateBinding
                .BindAsync(m_nodeManager, m_roleManager, m_auditServer.Object)
                .ConfigureAwait(false);
            Assume.That(m_binding, Is.Not.Null, "RoleStateBinding.BindAsync should locate the RoleSet.");
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_binding != null)
            {
                await m_binding.DisposeAsync().ConfigureAwait(false);
            }
            m_observedLogger?.Dispose();
            m_observedLogger = null;
            m_roleManager.Dispose();
            m_nodeManager.Dispose();
        }

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
            Assert.That(result!.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
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
            Assert.That(result!.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
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
            Assert.That(ServiceResult.IsGood(result), Is.True);

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
            Assert.That(result!.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task AddEndpointHandlerAuthorisedCallerAcceptsSecurityModeOnlyRule()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            var endpoint = new EndpointType
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };

            ServiceResult? result = await InvokeAddEndpointAsync(ctx, endpoint)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            RoleEntry? entry = m_roleManager.GetRole(ObjectIds.WellKnownRole_Observer);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Endpoints, Has.Count.EqualTo(1));
            Assert.That(
                entry.Endpoints[0].SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(string.IsNullOrEmpty(entry.Endpoints[0].EndpointUrl), Is.True);

            Assume.That(m_roleState.Endpoints, Is.Not.Null);
            ArrayOf<EndpointType> syncedEndpoints = m_roleState.Endpoints!.Value;
            Assert.That(syncedEndpoints, Has.Count.EqualTo(1));
            Assert.That(
                syncedEndpoints[0].SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(string.IsNullOrEmpty(syncedEndpoints[0].EndpointUrl), Is.True);

            m_auditServer.Verify(a => a.ReportAuditEvent(
                It.IsAny<ISystemContext>(),
                It.IsAny<AuditEventState>()),
                Times.Once);
        }

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

        [Test]
        public void ApplicationsExcludeWrite_AnonymousCaller_ReturnsBadUserAccessDenied()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true);
            ServiceResult result = InvokeBoolWrite(ctx, m_roleState.ApplicationsExclude!, true);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
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
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

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

        [Test]
        public void RoleConfigurationChanged_SyncsIdentitiesOnRoleState()
        {
            Assume.That(m_roleState.Identities, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(
                m_roleManager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "alice"
                    })), Is.True);

            ArrayOf<IdentityMappingRuleType> synced = m_roleState.Identities!.Value;
            bool hasAlice = false;
            foreach (IdentityMappingRuleType rule in synced)
            {
                if (rule.CriteriaType == IdentityCriteriaType.UserName &&
                    string.Equals(rule.Criteria, "alice", StringComparison.Ordinal))
                {
                    hasAlice = true;
                    break;
                }
            }
            Assert.That(hasAlice, Is.True,
                "Identities sync must reflect AddIdentity mutations.");
        }

        [Test]
        public void RoleConfigurationChanged_SyncsApplicationsOnRoleState()
        {
            Assume.That(m_roleState.Applications, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(
                m_roleManager.AddApplication(ObjectIds.WellKnownRole_Observer, "urn:test:app")), Is.True);

            ArrayOf<string> apps = m_roleState.Applications!.Value;
            bool found = false;
            foreach (string app in apps)
            {
                if (string.Equals(app, "urn:test:app", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "Applications sync must reflect AddApplication mutations.");
        }

        [Test]
        public void RoleConfigurationChanged_SyncsEndpointsOnRoleState()
        {
            Assume.That(m_roleState.Endpoints, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(
                m_roleManager.AddEndpoint(ObjectIds.WellKnownRole_Observer,
                    new EndpointType { EndpointUrl = "opc.tcp://srv:4840" })), Is.True);

            ArrayOf<EndpointType> endpoints = m_roleState.Endpoints!.Value;
            bool found = false;
            foreach (EndpointType ep in endpoints)
            {
                if (string.Equals(ep.EndpointUrl, "opc.tcp://srv:4840", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True,
                "Endpoints sync must reflect AddEndpoint mutations.");
        }

        [Test]
        public void RoleConfigurationChanged_SyncsEndpointsExcludeOnRoleState()
        {
            Assume.That(m_roleState.EndpointsExclude, Is.Not.Null);
            // Observer starts with EndpointsExclude=false (it's a well-known
            // role with no endpoints configured). Flip to true and verify
            // the sync propagates.
            Assert.That(ServiceResult.IsGood(
                m_roleManager.SetEndpointsExclude(ObjectIds.WellKnownRole_Observer, true)),
                Is.True);
            Assert.That(m_roleState.EndpointsExclude!.Value, Is.True);

            Assert.That(ServiceResult.IsGood(
                m_roleManager.SetEndpointsExclude(ObjectIds.WellKnownRole_Observer, false)),
                Is.True);
            Assert.That(m_roleState.EndpointsExclude.Value, Is.False,
                "EndpointsExclude sync must mirror SetEndpointsExclude.");
        }

        [Test]
        public void RoleConfigurationChanged_SyncsCustomConfigurationOnRoleState()
        {
            Assume.That(m_roleState.CustomConfiguration, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(
                m_roleManager.SetCustomConfiguration(ObjectIds.WellKnownRole_Observer, true)),
                Is.True);
            Assert.That(m_roleState.CustomConfiguration!.Value, Is.True);

            Assert.That(ServiceResult.IsGood(
                m_roleManager.SetCustomConfiguration(ObjectIds.WellKnownRole_Observer, false)),
                Is.True);
            Assert.That(m_roleState.CustomConfiguration.Value, Is.False,
                "CustomConfiguration sync must mirror SetCustomConfiguration.");
        }

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
            AssertGeneratedRoleProperty(role.ApplicationsExclude!);
            AssertGeneratedRoleProperty(role.EndpointsExclude!);
            AssertGeneratedRoleProperty(role.CustomConfiguration!);
        }

        [Test]
        public async Task AddRoleHandlerRetainsMandatoryIdentitiesWithUniqueNodeIds()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult first = await InvokeAddRoleAsync(
                ctx, "CustomReporter", "http://test.org/role-binding/").ConfigureAwait(false);
            AddRoleMethodStateResult second = await InvokeAddRoleAsync(
                ctx, "CustomAuditor", "http://test.org/role-binding/").ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(first.ServiceResult), Is.True);
            Assert.That(ServiceResult.IsGood(second.ServiceResult), Is.True);

            var firstRole = (RoleState)m_nodeManager.PredefinedNodes[first.RoleNodeId];
            var secondRole = (RoleState)m_nodeManager.PredefinedNodes[second.RoleNodeId];

            Assert.That(firstRole.Identities, Is.Not.Null);
            Assert.That(secondRole.Identities, Is.Not.Null);
            Assert.That(firstRole.NodeId, Is.Not.EqualTo(secondRole.NodeId));

            BaseInstanceState[] firstChildren =
            [
                firstRole.Identities!,
                firstRole.AddIdentity!,
                firstRole.RemoveIdentity!,
                firstRole.AddApplication!,
                firstRole.RemoveApplication!,
                firstRole.AddEndpoint!,
                firstRole.RemoveEndpoint!,
                firstRole.ApplicationsExclude!,
                firstRole.EndpointsExclude!,
                firstRole.CustomConfiguration!
            ];
            BaseInstanceState[] secondChildren =
            [
                secondRole.Identities!,
                secondRole.AddIdentity!,
                secondRole.RemoveIdentity!,
                secondRole.AddApplication!,
                secondRole.RemoveApplication!,
                secondRole.AddEndpoint!,
                secondRole.RemoveEndpoint!,
                secondRole.ApplicationsExclude!,
                secondRole.EndpointsExclude!,
                secondRole.CustomConfiguration!
            ];

            for (int ii = 0; ii < firstChildren.Length; ii++)
            {
                Assert.That(firstChildren[ii].NodeId.IsNull, Is.False);
                Assert.That(secondChildren[ii].NodeId.IsNull, Is.False);
                Assert.That(firstChildren[ii].BrowseName, Is.EqualTo(secondChildren[ii].BrowseName));
                Assert.That(
                    firstChildren[ii].NodeId,
                    Is.Not.EqualTo(secondChildren[ii].NodeId),
                    $"{firstChildren[ii].BrowseName} must have a per-role NodeId.");
            }
        }

        [Test]
        public void GeneratedRoleFactoryRebasesWithDefaultNodeIdFactory()
        {
            ushort namespaceIndex = m_nodeManager.NamespaceIndex;
            RoleState first = m_nodeManager.SystemContext.CreateInstanceOfRoleType(
                m_roleSet,
                new QualifiedName("GeneratedRoleA", namespaceIndex));
            RoleState second = m_nodeManager.SystemContext.CreateInstanceOfRoleType(
                m_roleSet,
                new QualifiedName("GeneratedRoleB", namespaceIndex));

            Assert.That(first.NodeId.IsNull, Is.False);
            Assert.That(second.NodeId.IsNull, Is.False);
            Assert.That(first.NodeId, Is.Not.EqualTo(second.NodeId));
            Assert.That(first.Identities, Is.Not.Null);
            Assert.That(second.Identities, Is.Not.Null);
            Assert.That(first.Identities!.NodeId.IsNull, Is.False);
            Assert.That(second.Identities!.NodeId.IsNull, Is.False);
            Assert.That(first.Identities.NodeId, Is.Not.EqualTo(second.Identities.NodeId));

            first.AddApplicationsExclude(m_nodeManager.SystemContext);
            second.AddApplicationsExclude(m_nodeManager.SystemContext);

            Assert.That(first.ApplicationsExclude, Is.Not.Null);
            Assert.That(second.ApplicationsExclude, Is.Not.Null);
            Assert.That(first.ApplicationsExclude!.NodeId.IsNull, Is.False);
            Assert.That(second.ApplicationsExclude!.NodeId.IsNull, Is.False);
            Assert.That(
                first.ApplicationsExclude.NodeId,
                Is.Not.EqualTo(second.ApplicationsExclude.NodeId));
        }

        [Test]
        public async Task AddRoleHandlerKeepsRootAndChildNodeIdsDisjoint()
        {
            var nodeIdFactory = new SequentialNodeIdFactory(m_nodeManager.NamespaceIndex);
            m_nodeManager.SystemContext.NodeIdFactory = nodeIdFactory;
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            var nodeIds = new HashSet<NodeId>();

            for (int ii = 0; ii < 6; ii++)
            {
                AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                    ctx,
                    $"CustomRole{ii}",
                    "http://test.org/role-binding/").ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

                var role = (RoleState)m_nodeManager.PredefinedNodes[result.RoleNodeId];
                Assert.That(nodeIds.Add(role.NodeId), Is.True);

                BaseInstanceState[] children =
                [
                    role.Identities!,
                    role.AddIdentity!,
                    role.RemoveIdentity!,
                    role.AddApplication!,
                    role.RemoveApplication!,
                    role.AddEndpoint!,
                    role.RemoveEndpoint!,
                    role.ApplicationsExclude!,
                    role.EndpointsExclude!,
                    role.CustomConfiguration!
                ];
                foreach (BaseInstanceState child in children)
                {
                    Assert.That(
                        nodeIds.Add(child.NodeId),
                        Is.True,
                        $"{child.BrowseName} must not collide with a role root or sibling.");
                }
            }

            Assert.That(nodeIdFactory.AllocationCount, Is.GreaterThan(0));
        }

        [Test]
        public async Task AddRoleHandlerUsesProvidedNodeIdFactory()
        {
            var nodeIdFactory = new PrefixedNodeIdFactory();
            m_nodeManager.SystemContext.NodeIdFactory = nodeIdFactory;
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);

            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx,
                "FactoryRole",
                "http://test.org/role-binding/").ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            var role = (RoleState)m_nodeManager.PredefinedNodes[result.RoleNodeId];
            Assert.That(role.Identities, Is.Not.Null);
            Assert.That(
                role.Identities!.NodeId.IdentifierAsString,
                Does.StartWith("provided:"));
            Assert.That(nodeIdFactory.AllocationCount, Is.GreaterThan(0));
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

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_nodeManager.PredefinedNodes, Has.Count.EqualTo(countBefore),
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
        public async Task ReaddedWellKnownRoleSurvivesItsEarlierRemovalAsync()
        {
            await m_binding!.DisposeAsync().ConfigureAwait(false);
            NodeId readdedId = NodeId.Null;
            ServiceResult? readded = null;
            void ReaddBeforeRemovalIsDispatched(object? sender, RoleConfigurationChangedEventArgs change)
            {
                if (change.Kind == RoleConfigurationChangeKind.RoleRemoved &&
                    change.RoleId == ObjectIds.WellKnownRole_Engineer)
                {
                    readded = m_roleManager.AddRole(
                        BrowseNames.WellKnownRole_Engineer, "http://opcfoundation.org/UA/", m_namespaceTable,
                        m_nodeManager.NamespaceIndex, out readdedId);
                }
            }
            m_roleManager.RoleConfigurationChanged += ReaddBeforeRemovalIsDispatched;
            try
            {
                RoleState engineer = m_serverSystemContext.CreateInstanceOfRoleType(
                    m_roleSet, new QualifiedName(BrowseNames.WellKnownRole_Engineer));
                engineer.NodeId = ObjectIds.WellKnownRole_Engineer;
                engineer.AddIdentity = m_serverSystemContext.CreateInstanceOfAddIdentityMethodType(engineer);
                m_roleSet.AddChild(engineer);
                m_nodeManager.PredefinedNodes[engineer.NodeId] = engineer;
                m_binding = await RoleStateBinding.BindAsync(m_nodeManager, m_roleManager, m_auditServer.Object)
                    .ConfigureAwait(false);
                ISystemContext context = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);

                ServiceResult removed = await InvokeRemoveRoleAsync(context, engineer.NodeId).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(removed), Is.True);
                Assert.That(readded, Is.Not.Null);
                Assert.That(ServiceResult.IsGood(readded), Is.True);
                Assert.That(readdedId, Is.EqualTo(ObjectIds.WellKnownRole_Engineer));
                Assert.That(m_roleManager.GetRole(readdedId), Is.Not.Null);
                Assert.That(m_nodeManager.FindPredefinedNode<RoleState>(readdedId), Is.SameAs(engineer));
                AddIdentityMethodState method = engineer.AddIdentity!;
                AddIdentityMethodStateResult identity = await method.OnCallAsync!(
                    context, method, engineer.NodeId,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "replacement-engineer"
                    }, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(identity.ServiceResult), Is.True);
                Assert.That(m_roleManager.GetRole(readdedId)!.Identities,
                    Has.Some.Matches<IdentityMappingRuleType>(rule => rule.Criteria == "replacement-engineer"));
            }
            finally
            {
                m_roleManager.RoleConfigurationChanged -= ReaddBeforeRemovalIsDispatched;
            }
        }

        [Test]
        public async Task RemovedRoleCannotDeleteADifferentNodeReusingItsIdAsync()
        {
            ISystemContext context = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult added = await InvokeAddRoleAsync(
                context, "ReplacedRole", "http://test.org/role-binding/").ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(added.ServiceResult), Is.True);
            var foreign = new BaseObjectState(null)
            {
                NodeId = added.RoleNodeId,
                BrowseName = new QualifiedName("ForeignReplacement", m_nodeManager.NamespaceIndex)
            };
            void ReplaceRemovedRole(object? sender, RoleConfigurationChangedEventArgs change)
            {
                if (change.Kind == RoleConfigurationChangeKind.RoleRemoved && change.RoleId == added.RoleNodeId)
                {
                    m_nodeManager.PredefinedNodes[change.RoleId] = foreign;
                }
            }
            await m_binding!.DisposeAsync().ConfigureAwait(false);
            m_roleManager.RoleConfigurationChanged += ReplaceRemovedRole;
            try
            {
                m_binding = await RoleStateBinding.BindAsync(m_nodeManager, m_roleManager, m_auditServer.Object)
                    .ConfigureAwait(false);
                ServiceResult removed = await InvokeRemoveRoleAsync(context, added.RoleNodeId).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(removed), Is.True);
                Assert.That(m_nodeManager.FindPredefinedNode<NodeState>(added.RoleNodeId), Is.SameAs(foreign));
            }
            finally
            {
                m_roleManager.RoleConfigurationChanged -= ReplaceRemovedRole;
            }
        }

        [Test]
        public async Task RemovalOfAnUnmaterializedRoleCannotDeleteAForeignNodeAsync()
        {
            var occupied = new NodeId(4243u, m_nodeManager.NamespaceIndex);
            var foreign = new BaseObjectState(null)
            {
                NodeId = occupied,
                BrowseName = new QualifiedName("ForeignMachine", m_nodeManager.NamespaceIndex)
            };
            m_nodeManager.PredefinedNodes[occupied] = foreign;
            using var manager = new FixedNodeIdRoleManager(occupied);
            await using RoleStateBinding? binding = await RoleStateBinding.BindAsync(
                m_nodeManager, manager, m_auditServer.Object).ConfigureAwait(false);
            Assert.That(binding, Is.Not.Null);
            ISystemContext context = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);

            AddRoleMethodStateResult failed = await InvokeAddRoleAsync(context, "Collision", string.Empty)
                .ConfigureAwait(false);
            Assert.That(failed.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(manager.Roles, Is.Empty);
            Assert.That(ServiceResult.IsGood(manager.AddRole(
                "Unmaterialized", string.Empty, m_namespaceTable, m_nodeManager.NamespaceIndex, out NodeId roleId)),
                Is.True);
            ServiceResult removed = await InvokeRemoveRoleAsync(context, roleId).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(removed), Is.True);
            Assert.That(manager.Roles, Is.Empty);
            Assert.That(m_nodeManager.FindPredefinedNode<NodeState>(occupied), Is.SameAs(foreign));
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

            Assert.That(removeResult.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(addResult.RoleNodeId), Is.True,
                "Auth-rejected RemoveRole must leave the address space untouched.");
        }

        [Test]
        public async Task AddRoleHandler_TypedRoleStateUpgradedByBinding_CanCallAddIdentity()
        {
            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            // Not one of the nine well-known names — those are already in the
            // manager's browse-name index, so AddRole would reject them.
            AddRoleMethodStateResult addResult = await InvokeAddRoleAsync(
                ctx, "SiteEngineer", "http://test.org/role-binding/").ConfigureAwait(false);
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

        [Test]
        public async Task AddRoleHandler_ForeignNamespaceUri_DoesNotReplaceAnExistingNode()
        {
            // A model owned by another NodeManager, numbered from i=1 as every
            // model compiler does.
            m_namespaceTable.Append("http://example.org/MyModel");
            ushort modelNamespace = (ushort)m_namespaceTable.GetIndex("http://example.org/MyModel");
            var machineId = new NodeId(1u, modelNamespace);
            var machine = new BaseObjectState(null)
            {
                NodeId = machineId,
                BrowseName = new QualifiedName("Machine", modelNamespace),
                DisplayName = new LocalizedText("Machine")
            };
            m_nodeManager.PredefinedNodes[machineId] = machine;

            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx, "Maintenance", "http://example.org/MyModel").ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.RoleNodeId, Is.Not.EqualTo(machineId),
                "AddRole must never hand out a NodeId that already names a node.");
            Assert.That(m_nodeManager.PredefinedNodes[machineId], Is.SameAs(machine),
                "The model's own node must survive AddRole untouched.");

            var role = (RoleState)m_nodeManager.PredefinedNodes[result.RoleNodeId];
            Assert.That(role.BrowseName.Name, Is.EqualTo("Maintenance"));
            Assert.That(role.BrowseName.NamespaceIndex, Is.EqualTo(modelNamespace),
                "Part 18 §4.2.2: NamespaceUri qualifies the BrowseName of the new role.");
            Assert.That(role.NodeId.NamespaceIndex, Is.EqualTo(m_nodeManager.NamespaceIndex),
                "The role NodeId belongs to the namespace the server picked.");
        }

        [Test]
        public async Task AddRoleHandler_ManagerReturnsOccupiedNodeId_RefusesAndRollsBack()
        {
            // A custom IRoleManager that hands out a NodeId already in use. The
            // binding must refuse rather than silently replace the node, and it
            // must not leave the role behind in the manager.
            var occupied = new NodeId(4242u, m_nodeManager.NamespaceIndex);
            m_nodeManager.PredefinedNodes[occupied] = new BaseObjectState(null)
            {
                NodeId = occupied,
                BrowseName = new QualifiedName("Existing", m_nodeManager.NamespaceIndex)
            };

            using var stub = new FixedNodeIdRoleManager(occupied);
            await using RoleStateBinding? binding = await RoleStateBinding
                .BindAsync(m_nodeManager, stub, m_auditServer.Object)
                .ConfigureAwait(false);
            Assume.That(binding, Is.Not.Null);

            AddRoleMethodState method = m_roleSet.AddRole!;
            AddRoleMethodStateResult result = await method.OnCallAsync!(
                BuildAdminContext(MessageSecurityMode.SignAndEncrypt),
                method, m_roleSet.NodeId, "Colliding", string.Empty,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(result.RoleNodeId.IsNull, Is.True);
            Assert.That(m_nodeManager.PredefinedNodes[occupied], Is.Not.InstanceOf<RoleState>(),
                "The existing node must not be replaced by the new role.");
            Assert.That(stub.Roles, Is.Empty,
                "A role the client cannot browse must be rolled back out of the manager.");
        }

        [Test]
        public async Task Bind_MaterializesRolesConfiguredBeforeTheAddressSpaceExisted()
        {
            // A role created straight on the manager - what
            // StandardServer.CreateRoleManager, ConfigureRoles and a
            // pre-populated custom IRoleManager all end up doing.
            using var roleManager = new RoleManager();
            Assume.That(ServiceResult.IsGood(roleManager.AddRole(
                "Maintenance", "http://test.org/role-binding/", m_namespaceTable,
                m_nodeManager.NamespaceIndex, out NodeId configuredRoleId)), Is.True);
            Assume.That(m_nodeManager.PredefinedNodes.ContainsKey(configuredRoleId), Is.False);

            await using RoleStateBinding? binding = await RoleStateBinding
                .BindAsync(m_nodeManager, roleManager, m_auditServer.Object)
                .ConfigureAwait(false);
            Assume.That(binding, Is.Not.Null);

            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(configuredRoleId), Is.True,
                "A role configured before start-up must get a node under the RoleSet.");
            var role = (RoleState)m_nodeManager.PredefinedNodes[configuredRoleId];
            Assert.That(role.BrowseName.Name, Is.EqualTo("Maintenance"));
            Assert.That(role.AddIdentity, Is.Not.Null);
            Assert.That(role.AddIdentity!.OnCallAsync, Is.Not.Null,
                "The materialized role must be wired to the manager like any other.");

            // And the RoleSet must actually reference it, so a client browsing
            // Server/ServerCapabilities/RoleSet sees the role.
            var references = new List<IReference>();
            m_roleSet.GetReferences(m_nodeManager.SystemContext, references);
            Assert.That(
                references.Any(r => !r.IsInverse &&
                    r.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                    ExpandedNodeId.ToNodeId(r.TargetId, m_namespaceTable) == configuredRoleId),
                Is.True,
                "The RoleSet must expose the configured role as a component.");
        }

        [Test]
        public async Task Bind_AppliesRoleConfigurationStagedByConfigureRoles()
        {
            var options = new RoleConfigurationOptions();
            var definition = new RoleDefinitionOptions
            {
                Name = "Maintenance",
                NamespaceUri = "http://test.org/role-binding/"
            };
            definition.Identities.Add(new RoleIdentityMappingOptions
            {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "dave"
            });
            options.Roles.Add(definition);

            using var roleManager = new RoleManager { PendingConfiguration = options };
            Assume.That(roleManager.RoleIds.Any(
                id => roleManager.GetRole(id)?.BrowseName == "Maintenance"), Is.False,
                "Configured roles are staged, not applied, before the address space exists.");

            await using RoleStateBinding? binding = await RoleStateBinding
                .BindAsync(m_nodeManager, roleManager, m_auditServer.Object)
                .ConfigureAwait(false);
            Assume.That(binding, Is.Not.Null);

            NodeId configuredRoleId = roleManager.RoleIds.Single(
                id => roleManager.GetRole(id)?.BrowseName == "Maintenance");
            Assert.That(configuredRoleId.NamespaceIndex,
                Is.EqualTo(m_nodeManager.NamespaceIndex),
                "The role NodeId must land in a namespace the RoleSet's NodeManager owns.");
            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(configuredRoleId), Is.True,
                "A role from ConfigureRoles must be browsable under the RoleSet.");

            RoleEntry? entry = roleManager.GetRole(configuredRoleId);
            Assert.That(entry!.Identities, Has.Count.EqualTo(1));
            Assert.That(entry.Identities[0].Criteria, Is.EqualTo("dave"));
        }

        [Test]
        public async Task RoleAddedOnTheManagerAfterBind_MaterializesTheRole()
        {
            // A custom IRoleManager can add roles at runtime without going
            // through the AddRole Method; the RoleAdded event has to bring them
            // into the address space.
            Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                "LateRole", "http://test.org/role-binding/", m_namespaceTable,
                m_nodeManager.NamespaceIndex, out NodeId lateRoleId)), Is.True);

            await WaitForNodeAsync(lateRoleId).ConfigureAwait(false);

            Assert.That(m_nodeManager.PredefinedNodes[lateRoleId], Is.InstanceOf<RoleState>(),
                "A role added directly on the manager must appear under the RoleSet.");
        }

        [Test]
        public async Task Bind_MaterializationThrows_StillCompletesTheBindingAsync()
        {
            // One role the server cannot represent must not stop the server from
            // starting: the sweep logs it and carries on.
            using var roleManager = new RoleManager();
            Assume.That(ServiceResult.IsGood(roleManager.AddRole(
                "Unmaterializable", "http://test.org/role-binding/", m_namespaceTable,
                m_nodeManager.NamespaceIndex, out NodeId roleId)), Is.True);

            m_nodeManager.SystemContext.NodeIdFactory = new ThrowingNodeIdFactory();

            await using RoleStateBinding? binding = await RoleStateBinding
                .BindAsync(m_nodeManager, roleManager, m_auditServer.Object)
                .ConfigureAwait(false);

            Assert.That(binding, Is.Not.Null,
                "A role that cannot be materialized must not abort the binding.");
            Assert.That(m_nodeManager.PredefinedNodes.ContainsKey(roleId), Is.False,
                "The failed role must not leave a half-built node behind.");
            Assert.That(m_roleSet.AddRole!.OnCallAsync, Is.Not.Null,
                "The RoleSet methods must still be wired up.");
        }

        [Test]
        public async Task AddRoleHandler_AfterDispose_ReturnsBadInvalidStateAndRollsBackAsync()
        {
            m_binding!.Dispose();

            ISystemContext ctx = BuildAdminContext(MessageSecurityMode.SignAndEncrypt);
            AddRoleMethodStateResult result = await InvokeAddRoleAsync(
                ctx, "AfterShutdown", "http://test.org/role-binding/").ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState),
                "A torn-down binding must not report success for a role it never created.");
            Assert.That(result.RoleNodeId.IsNull, Is.True);
            Assert.That(
                m_roleManager.RoleIds.Any(id =>
                    string.Equals(m_roleManager.GetRole(id)?.BrowseName, "AfterShutdown",
                        StringComparison.Ordinal)),
                Is.False,
                "A role that could not be materialized must not linger in the manager.");
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            Assert.DoesNotThrow(() =>
            {
                m_binding!.Dispose();
                m_binding.Dispose();
            });
        }

        [Test]
        public async Task RoleEventStormUsesOneWorkerAndOnePendingSignalAsync()
        {
            ObservedLogger logger = await RebindWithLoggerAsync().ConfigureAwait(false);
            (TaskCompletionSource<bool> release, _) = await BlockRoleRemovalAsync().ConfigureAwait(false);
            try
            {
                BackgroundTaskScope scope = GetBindingField<BackgroundTaskScope>("m_backgroundWork");
                SemaphoreSlim signal = GetBindingField<SemaphoreSlim>("m_reconcileSignal");
                Task worker = GetBindingField<Task>("m_reconcileTask");
                Task materialized = ObserveRoleLog(logger, ServerEventIds.RoleStateBinding + 2);
                int materializations = 0;
                m_nodeManager.AddBehaviourCallback = node =>
                {
                    if (node is RoleState)
                    {
                        Interlocked.Increment(ref materializations);
                    }
                    return node;
                };

                var removedIds = new List<NodeId>();
                for (int ii = 0; ii < 512; ii++)
                {
                    Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                        "StormRole", "http://test.org/role-binding/", m_namespaceTable,
                        m_nodeManager.NamespaceIndex, out NodeId roleId)), Is.True);
                    removedIds.Add(roleId);
                    Assert.That(ServiceResult.IsGood(m_roleManager.RemoveRole(roleId)), Is.True);
                }
                Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                    "StormRole", "http://test.org/role-binding/", m_namespaceTable,
                    m_nodeManager.NamespaceIndex, out NodeId latestId)), Is.True);

                Assert.That(scope.PendingCount, Is.EqualTo(1), "Events must not schedule more scope operations.");
                Assert.That(signal.CurrentCount, Is.EqualTo(1), "Only one follow-up pass may be pending.");
                Assert.That(GetBindingField<Task>("m_reconcileTask"), Is.SameAs(worker));
                Assert.That(materializations, Is.Zero);

                release.TrySetResult(true);
                await materialized.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                Assert.That(materializations, Is.EqualTo(1), "Only the desired final role should be materialized.");
                Assert.That(removedIds.All(id => m_nodeManager.FindPredefinedNode<NodeState>(id) == null), Is.True);
                RoleState? latest = m_nodeManager.FindPredefinedNode<RoleState>(latestId);
                Assert.That(latest, Is.Not.Null);
                Assert.That(latest!.BrowseName.Name, Is.EqualTo("StormRole"));
                var children = new List<BaseInstanceState>();
                m_roleSet.GetChildren(m_nodeManager.SystemContext, children);
                Assert.That(children.Where(child => child.BrowseName.Name == "StormRole"),
                    Is.EqualTo([latest]));
                Assert.That(scope.PendingCount, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task AddRemoveReaddKeepsLatestRoleNodeAndPermissionsAsync()
        {
            await m_binding!.DisposeAsync().ConfigureAwait(false);
            RoleState engineer = m_serverSystemContext.CreateInstanceOfRoleType(
                m_roleSet, new QualifiedName(BrowseNames.WellKnownRole_Engineer));
            engineer.NodeId = ObjectIds.WellKnownRole_Engineer;
            engineer.AddIdentity = m_serverSystemContext.CreateInstanceOfAddIdentityMethodType(engineer);
            engineer.AddApplicationsExclude(m_serverSystemContext);
            m_roleSet.AddChild(engineer);
            m_nodeManager.PredefinedNodes[engineer.NodeId] = engineer;
            ObservedLogger logger = await RebindWithLoggerAsync().ConfigureAwait(false);
            (TaskCompletionSource<bool> release, _) = await BlockRoleRemovalAsync().ConfigureAwait(false);
            try
            {
                for (int ii = 0; ii < 2; ii++)
                {
                    Assert.That(ServiceResult.IsGood(m_roleManager.RemoveRole(engineer.NodeId)), Is.True);
                    Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                        BrowseNames.WellKnownRole_Engineer, "http://opcfoundation.org/UA/", m_namespaceTable,
                        m_nodeManager.NamespaceIndex, out NodeId roleId)), Is.True);
                    Assert.That(roleId, Is.EqualTo(engineer.NodeId));
                    Assert.That(ServiceResult.IsGood(m_roleManager.AddIdentity(
                        roleId, new IdentityMappingRuleType
                        {
                            CriteriaType = IdentityCriteriaType.UserName,
                            Criteria = ii == 0 ? "stale-engineer" : "latest-engineer"
                        })), Is.True);
                }
                Assert.That(ServiceResult.IsGood(
                    m_roleManager.SetApplicationsExclude(engineer.NodeId, true)), Is.True);
                Task reconciled = ObserveRoleLog(logger, ServerEventIds.RoleStateBinding + 2);
                Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                    "ReconciliationFence", "http://test.org/role-binding/", m_namespaceTable,
                    m_nodeManager.NamespaceIndex, out NodeId fenceId)), Is.True);

                release.TrySetResult(true);
                await reconciled.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                Assert.That(m_nodeManager.FindPredefinedNode<RoleState>(fenceId), Is.Not.Null);
                Assert.That(m_nodeManager.FindPredefinedNode<RoleState>(engineer.NodeId), Is.SameAs(engineer));
                Assert.That(engineer.Identities!.Value, Has.Count.EqualTo(1));
                Assert.That(engineer.Identities.Value[0].Criteria, Is.EqualTo("latest-engineer"));
                Assert.That(engineer.ApplicationsExclude!.Value, Is.True);
                AssertGeneratedRoleProperty(engineer.ApplicationsExclude);
                Assert.That(m_roleManager.ResolveGrantedRoles(
                    CreateUserNameIdentity("latest-engineer"), null, null), Does.Contain(engineer.NodeId));
                Assert.That(m_roleManager.ResolveGrantedRoles(
                    CreateUserNameIdentity("stale-engineer"), null, null), Does.Not.Contain(engineer.NodeId));

                AddIdentityMethodState method = engineer.AddIdentity!;
                var rule = new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "after-readd"
                };
                AddIdentityMethodStateResult denied = await method.OnCallAsync!(
                    BuildContext(MessageSecurityMode.SignAndEncrypt, anonymous: true),
                    method, engineer.NodeId, rule, CancellationToken.None).ConfigureAwait(false);
                Assert.That(denied.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(m_roleManager.GetRole(engineer.NodeId)!.Identities, Has.Count.EqualTo(1));
                AddIdentityMethodStateResult allowed = await method.OnCallAsync!(
                    BuildAdminContext(MessageSecurityMode.SignAndEncrypt),
                    method, engineer.NodeId, rule, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(allowed.ServiceResult), Is.True);
                Assert.That(engineer.Identities.Value, Has.Count.EqualTo(2));
                Assert.That(engineer.Identities.Value[0].Criteria, Is.EqualTo("latest-engineer"));
                Assert.That(engineer.Identities.Value[1].Criteria, Is.EqualTo("after-readd"));
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task MaterializationFailureIsLoggedAndLaterChangesAreReconciledAsync()
        {
            ObservedLogger logger = await RebindWithLoggerAsync().ConfigureAwait(false);
            INodeIdFactory? originalFactory = m_nodeManager.SystemContext.NodeIdFactory;
            m_nodeManager.SystemContext.NodeIdFactory = new ThrowingNodeIdFactory();
            Task failed = ObserveRoleLog(
                logger, ServerEventIds.RoleStateBinding + 5,
                failure: exception => exception is InvalidOperationException);
            try
            {
                Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                    "FailedRole", "http://test.org/role-binding/", m_namespaceTable,
                    m_nodeManager.NamespaceIndex, out NodeId failedId)), Is.True);
                await failed.WaitAsync(s_workerTimeout).ConfigureAwait(false);
                Assert.That(m_nodeManager.FindPredefinedNode<NodeState>(failedId), Is.Null);
                RecordedLogRecord failure = logger.Records.ToList().Single(
                    record => record.EventId.Id == ServerEventIds.RoleStateBinding + 5 &&
                        record.Properties["RoleId"] is NodeId roleId &&
                        roleId == failedId);
                Assert.That(failure.LogLevel, Is.EqualTo(LogLevel.Warning));
                Assert.That(failure.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure.Exception!.Message, Is.EqualTo("NodeId allocation failed."));
                Assert.That(failure.Properties["RoleId"], Is.EqualTo(failedId));

                m_nodeManager.SystemContext.NodeIdFactory = originalFactory;
                Task recovered = ObserveRoleLog(logger, ServerEventIds.RoleStateBinding + 2, count: 2);
                Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                    "LaterRole", "http://test.org/role-binding/", m_namespaceTable,
                    m_nodeManager.NamespaceIndex, out NodeId laterId)), Is.True);
                await recovered.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                Assert.That(m_nodeManager.FindPredefinedNode<RoleState>(failedId)!.AddIdentity!.OnCallAsync,
                    Is.Not.Null);
                Assert.That(m_nodeManager.FindPredefinedNode<RoleState>(laterId)!.AddIdentity!.OnCallAsync,
                    Is.Not.Null);
                Assert.That(GetBindingField<BackgroundTaskScope>("m_backgroundWork").PendingCount, Is.EqualTo(1));
                Assert.That(GetBindingField<Task>("m_reconcileTask").IsCompleted, Is.False);
            }
            finally
            {
                m_nodeManager.SystemContext.NodeIdFactory = originalFactory;
            }
        }

        [Test]
        public async Task DisposeDuringInFlightMutationRetainsGatesUntilAsyncDrainAsync()
        {
            ObservedLogger logger = await RebindWithLoggerAsync().ConfigureAwait(false);
            (TaskCompletionSource<bool> release, CancellationToken mutationToken) =
                await BlockRoleRemovalAsync().ConfigureAwait(false);
            var added = new TaskCompletionSource<NodeId>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRoleAdded(object? sender, RoleConfigurationChangedEventArgs change)
            {
                if (change.Kind == RoleConfigurationChangeKind.RoleAdded)
                {
                    added.TrySetResult(change.RoleId);
                }
            }
            m_roleManager.RoleConfigurationChanged += OnRoleAdded;
            Task<AddRoleMethodStateResult>? queuedAdd = null;
            try
            {
                SemaphoreSlim gate = GetBindingField<SemaphoreSlim>("m_roleSetLock");
                SemaphoreSlim signal = GetBindingField<SemaphoreSlim>("m_reconcileSignal");
                BackgroundTaskScope scope = GetBindingField<BackgroundTaskScope>("m_backgroundWork");
                Task worker = GetBindingField<Task>("m_reconcileTask");
                queuedAdd = InvokeAddRoleAsync(
                    BuildAdminContext(MessageSecurityMode.SignAndEncrypt),
                    "QueuedDuringShutdown", "http://test.org/role-binding/").AsTask();
                NodeId queuedId = await added.Task.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                m_binding!.Dispose();
                Assert.That(mutationToken.IsCancellationRequested, Is.True);
                Task drain = m_binding.DisposeAsync().AsTask();
                Assert.That(drain.IsCompleted, Is.False, "Async disposal must join the mutation still in flight.");
                Assert.That(worker.IsCompleted, Is.False);
                Assert.That(scope.PendingCount, Is.EqualTo(1));
                Assert.That(await gate.WaitAsync(0).ConfigureAwait(false), Is.False,
                    "The held gate must remain usable, not disposed by synchronous shutdown.");

                AddRoleMethodStateResult cancelled = await queuedAdd.WaitAsync(s_workerTimeout).ConfigureAwait(false);
                Assert.That(cancelled.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(m_roleManager.GetRole(queuedId), Is.Null);
                Assert.That(m_nodeManager.FindPredefinedNode<NodeState>(queuedId), Is.Null);
                Assert.That(ServiceResult.IsGood(m_roleManager.AddRole(
                    "AfterDisposal", "http://test.org/role-binding/", m_namespaceTable,
                    m_nodeManager.NamespaceIndex, out NodeId afterDisposalId)), Is.True);

                release.TrySetResult(true);
                await drain.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                Assert.That(worker.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(scope.PendingCount, Is.Zero);
                Assert.That(m_nodeManager.FindPredefinedNode<NodeState>(afterDisposalId), Is.Null);
                Assert.Throws<ObjectDisposedException>(() => gate.Release());
                Assert.Throws<ObjectDisposedException>(() => signal.Release());
                Assert.That(logger.Records.ToList().Exists(record => record.Exception is ObjectDisposedException),
                    Is.False);
                await m_binding.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                m_roleManager.RoleConfigurationChanged -= OnRoleAdded;
                if (queuedAdd != null)
                {
                    await queuedAdd.WaitAsync(s_workerTimeout).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task ScopeDrainTimeoutDoesNotReleaseInFlightRoleGateAsync()
        {
            ObservedLogger logger = await RebindWithLoggerAsync().ConfigureAwait(false);
            (TaskCompletionSource<bool> release, _) = await BlockRoleRemovalAsync().ConfigureAwait(false);
            try
            {
                BackgroundTaskScope scope = GetBindingField<BackgroundTaskScope>("m_backgroundWork");
                SemaphoreSlim gate = GetBindingField<SemaphoreSlim>("m_roleSetLock");
                Task worker = GetBindingField<Task>("m_reconcileTask");
                m_binding!.Dispose();

                await scope.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);

                RecordedLogRecord timeout = logger.Records.ToList().Single(
                    record => record.EventId.Name == "BackgroundTaskDrainTimedOut");
                Assert.That(timeout.LogLevel, Is.EqualTo(LogLevel.Warning));
                Assert.That(timeout.Properties["Pending"], Is.EqualTo(1));
                Assert.That(scope.PendingCount, Is.EqualTo(1));
                Assert.That(worker.IsCompleted, Is.False);
                Assert.That(await gate.WaitAsync(0).ConfigureAwait(false), Is.False);
                Task drain = m_binding.DisposeAsync().AsTask();
                Assert.That(drain.IsCompleted, Is.False);

                release.TrySetResult(true);
                await drain.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                Assert.That(worker.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(scope.PendingCount, Is.Zero);
                Assert.Throws<ObjectDisposedException>(() => gate.Release());
                Assert.That(logger.Records.ToList().Exists(record => record.Exception is ObjectDisposedException),
                    Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [Test]
        public async Task ServerDisposalDrainsRoleWorkerBeforeDisposingNodeManagerAsync()
        {
            await RebindWithLoggerAsync().ConfigureAwait(false);
            m_configuration.ApplicationUri = "urn:opcfoundation:tests:role-binding";
            await using var server = new ServerInternalData(
                new ServerProperties(), m_configuration, ServiceMessageContext.Create(m_telemetry));
            var nodeManager = new Mock<IMasterNodeManager>();
            Mock<IAsyncDisposable> lifetime = nodeManager.As<IAsyncDisposable>();
            lifetime.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            server.SetNodeManager(nodeManager.Object);
            FieldInfo? bindingField = typeof(ServerInternalData).GetField(
                "m_roleStateBinding", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(bindingField, Is.Not.Null);
            bindingField!.SetValue(server, m_binding);
            (TaskCompletionSource<bool> release, _) = await BlockRoleRemovalAsync().ConfigureAwait(false);
            try
            {
                Task disposal = server.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                lifetime.Verify(value => value.DisposeAsync(), Times.Never);

                release.TrySetResult(true);
                await disposal.WaitAsync(s_workerTimeout).ConfigureAwait(false);

                lifetime.Verify(value => value.DisposeAsync(), Times.Once);
                Assert.That(GetBindingField<Task>("m_reconcileTask").Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(bindingField.GetValue(server), Is.Null);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        private async Task<ObservedLogger> RebindWithLoggerAsync()
        {
            await m_binding!.DisposeAsync().ConfigureAwait(false);
            m_observedLogger?.Dispose();
            m_observedLogger = new ObservedLogger();
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(m_observedLogger);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
            m_mockServer.Setup(server => server.Telemetry).Returns(telemetry.Object);
            m_binding = await RoleStateBinding.BindAsync(m_nodeManager, m_roleManager, m_auditServer.Object)
                .ConfigureAwait(false);
            Assert.That(m_binding, Is.Not.Null);
            return m_observedLogger;
        }

        private async Task<(TaskCompletionSource<bool> Release, CancellationToken CancellationToken)>
            BlockRoleRemovalAsync()
        {
            AddRoleMethodStateResult blocker = await InvokeAddRoleAsync(
                BuildAdminContext(MessageSecurityMode.SignAndEncrypt),
                "BlockedRemoval", "http://test.org/role-binding/").ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(blocker.ServiceResult), Is.True);
            var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_nodeManager.NodeRemovedCallback = async (node, cancellationToken) =>
            {
                if (node.NodeId == blocker.RoleNodeId)
                {
                    entered.TrySetResult(cancellationToken);
                    await release.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            };
            try
            {
                Assert.That(ServiceResult.IsGood(m_roleManager.RemoveRole(blocker.RoleNodeId)), Is.True);
                CancellationToken token = await entered.Task.WaitAsync(s_workerTimeout).ConfigureAwait(false);
                return (release, token);
            }
            catch
            {
                release.TrySetResult(true);
                throw;
            }
        }

        private T GetBindingField<T>(string name)
            where T : class
        {
            FieldInfo? field = typeof(RoleStateBinding).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            Assert.That(field!.GetValue(m_binding), Is.InstanceOf<T>());
            return (T)field.GetValue(m_binding)!;
        }

        private static Task<bool> ObserveRoleLog(
            ObservedLogger logger,
            int eventId,
            int count = 1,
            Predicate<Exception?>? failure = null)
        {
            var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int remaining = count;
            logger.OnLog = (id, exception) =>
            {
                if (id.Id == eventId &&
                    (failure == null || failure(exception)) &&
                    Interlocked.Decrement(ref remaining) == 0)
                {
                    observed.TrySetResult(true);
                }
            };
            return observed.Task;
        }

        private static IUserIdentity CreateUserNameIdentity(string userName)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(value => value.DisplayName).Returns(userName);
            return identity.Object;
        }

        private async Task WaitForNodeAsync(NodeId nodeId)
        {
            for (int ii = 0; ii < 200 && !m_nodeManager.PredefinedNodes.ContainsKey(nodeId); ii++)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assume.That(m_nodeManager.PredefinedNodes.ContainsKey(nodeId), Is.True,
                $"Timed out waiting for {nodeId} to be materialized.");
        }

        private static void AssertGeneratedRoleProperty(PropertyState<bool> property)
        {
            Assert.That(property.TypeDefinitionId, Is.EqualTo(VariableTypeIds.PropertyType));
            Assert.That(property.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
            Assert.That(
                property.AccessRestrictions.GetValueOrDefault()
                    .HasFlag(AccessRestrictionType.EncryptionRequired),
                Is.True);

            bool hasSecurityAdmin = false;
            if (!property.RolePermissions.IsNull)
            {
                foreach (RolePermissionType rolePermission in property.RolePermissions)
                {
                    if (rolePermission.RoleId == ObjectIds.WellKnownRole_SecurityAdmin)
                    {
                        hasSecurityAdmin = true;
                        break;
                    }
                }
            }
            Assert.That(hasSecurityAdmin, Is.True);
        }

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
                : [];
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

        private async ValueTask<ServiceResult?> InvokeAddEndpointAsync(
            ISystemContext context, EndpointType endpoint)
        {
            AddEndpointMethodState? method = m_roleState.AddEndpoint;
            Assume.That(method, Is.Not.Null, "AddEndpoint method state should be attached.");
            Assume.That(method!.OnCallAsync, Is.Not.Null,
                "Binding should have wired the typed OnCallAsync delegate.");

            AddEndpointMethodStateResult result = await method.OnCallAsync!(
                context,
                method,
                m_roleState.NodeId,
                endpoint,
                CancellationToken.None).ConfigureAwait(false);
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

        private sealed class ObservedLogger : ILogger, IDisposable
        {
            public ObservedLogger()
            {
                m_logger = m_provider.CreateLogger(nameof(RoleStateBinding));
            }

            public Action<EventId, Exception?>? OnLog { get; set; }

            public ArrayOf<RecordedLogRecord> Records => ArrayOf.Wrapped(m_provider.Records.ToArray());

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return m_logger.BeginScope(state);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return m_logger.IsEnabled(logLevel);
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                m_logger.Log(logLevel, eventId, state, exception, formatter);
                OnLog?.Invoke(eventId, exception);
            }

            public void Dispose()
            {
                m_provider.Dispose();
            }

            private readonly RecordingLoggerProvider m_provider = new();
            private readonly ILogger m_logger;
        }

        /// <summary>
        /// Minimal <see cref="IRoleManager"/> whose <c>AddRole</c> always hands
        /// back the same NodeId — the shape of a manager that does not check
        /// whether the identifier it allocates is free.
        /// </summary>
        private sealed class FixedNodeIdRoleManager : IRoleManager, IDisposable
        {
            public FixedNodeIdRoleManager(NodeId fixedNodeId)
            {
                m_fixedNodeId = fixedNodeId;
            }

            public event EventHandler<RoleConfigurationChangedEventArgs>? RoleConfigurationChanged;

            public IReadOnlyList<NodeId> Roles => [.. m_roles.Keys];

            public IReadOnlyList<NodeId> RoleIds => Roles;

            public RoleEntry? GetRole(NodeId roleId)
            {
                return m_roles.TryGetValue(roleId, out RoleEntry? entry) ? entry : null;
            }

            public ServiceResult AddRole(
                string roleName,
                string? namespaceUri,
                NamespaceTable namespaces,
                ushort defaultNamespaceIndex,
                out NodeId newRoleId)
            {
                newRoleId = m_fixedNodeId;
                m_roles[m_fixedNodeId] = new RoleEntry(
                    m_fixedNodeId, roleName, namespaceUri,
                    isReserved: false, isWellKnown: false, [], [], true, [], true, false);
                RoleConfigurationChanged?.Invoke(this,
                    new RoleConfigurationChangedEventArgs(
                        m_fixedNodeId, RoleConfigurationChangeKind.RoleAdded));
                return ServiceResult.Good;
            }

            public ServiceResult RemoveRole(NodeId roleId)
            {
                if (!m_roles.TryRemove(roleId, out _))
                {
                    return new ServiceResult(StatusCodes.BadNodeIdUnknown);
                }
                RoleConfigurationChanged?.Invoke(this,
                    new RoleConfigurationChangedEventArgs(
                        roleId, RoleConfigurationChangeKind.RoleRemoved));
                return ServiceResult.Good;
            }

            public ServiceResult AddIdentity(NodeId roleId, IdentityMappingRuleType rule)
            {
                return ServiceResult.Good;
            }

            public ServiceResult RemoveIdentity(NodeId roleId, IdentityMappingRuleType rule)
            {
                return ServiceResult.Good;
            }

            public ServiceResult AddApplication(NodeId roleId, string applicationUri)
            {
                return ServiceResult.Good;
            }

            public ServiceResult RemoveApplication(NodeId roleId, string applicationUri)
            {
                return ServiceResult.Good;
            }

            public ServiceResult AddEndpoint(NodeId roleId, EndpointType endpoint)
            {
                return ServiceResult.Good;
            }

            public ServiceResult RemoveEndpoint(NodeId roleId, EndpointType endpoint)
            {
                return ServiceResult.Good;
            }

            public ServiceResult SetApplicationsExclude(NodeId roleId, bool value)
            {
                return ServiceResult.Good;
            }

            public ServiceResult SetEndpointsExclude(NodeId roleId, bool value)
            {
                return ServiceResult.Good;
            }

            public ServiceResult SetCustomConfiguration(NodeId roleId, bool value)
            {
                return ServiceResult.Good;
            }

            public IList<NodeId> ResolveGrantedRoles(
                IUserIdentity identity,
                Security.Certificates.Certificate? clientCertificate,
                EndpointDescription? endpoint)
            {
                return [];
            }

            public void Dispose()
            {
                RoleConfigurationChanged = null;
            }

            private readonly NodeId m_fixedNodeId;
            private readonly ConcurrentDictionary<NodeId, RoleEntry> m_roles = new();
        }

        /// <summary>
        /// Fails every allocation, so materializing a role throws part-way
        /// through building its subtree.
        /// </summary>
        private sealed class ThrowingNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                throw new InvalidOperationException("NodeId allocation failed.");
            }
        }

        private sealed class SequentialNodeIdFactory : INodeIdFactory
        {
            public SequentialNodeIdFactory(ushort namespaceIndex)
            {
                m_namespaceIndex = namespaceIndex;
            }

            public uint AllocationCount { get; private set; }

            public NodeId New(ISystemContext context, NodeState node)
            {
                if (!node.NodeId.IsNull)
                {
                    return node.NodeId;
                }

                AllocationCount++;
                return new NodeId(m_nextId++, m_namespaceIndex);
            }

            private readonly ushort m_namespaceIndex;
            private uint m_nextId = 1;
        }

        private sealed class PrefixedNodeIdFactory : INodeIdFactory
        {
            public uint AllocationCount { get; private set; }

            public NodeId New(ISystemContext context, NodeState node)
            {
                if (node is BaseInstanceState instance &&
                    instance.Parent != null)
                {
                    AllocationCount++;
                    return new NodeId(
                        $"provided:{instance.Parent.NodeId.IdentifierAsString}:" +
                        instance.SymbolicName,
                        instance.Parent.NodeId.NamespaceIndex);
                }

                return node.NodeId;
            }
        }
    }
}
