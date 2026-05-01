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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("DiagnosticsNodeManager")]
    [Parallelizable]
    public class DiagnosticsNodeManagerTests
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<ICoreNodeManager> m_coreNodeManagerMock;
        private Mock<ISubscriptionManager> m_subscriptionManagerMock;

        private void SetupServerMock()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var namespaces = new NamespaceTable();
            namespaces.Append(Ua.Namespaces.OpcUa);
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaces);
            var messageContext = new ServiceMessageContext(telemetry, EncodeableFactory.Create())
            {
                NamespaceUris = namespaces,
                ServerUris = serverUris
            };

            m_coreNodeManagerMock = new Mock<ICoreNodeManager>();
            m_coreNodeManagerMock.Setup(m => m.ImportNodesAsync(
                It.IsAny<ISystemContext>(),
                It.IsAny<IList<NodeState>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            m_subscriptionManagerMock = new Mock<ISubscriptionManager>();

            var masterNodeManagerMock = new Mock<IMasterNodeManager>();
            masterNodeManagerMock.Setup(m => m.RemoveReferencesAsync(It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaces);
            m_serverMock.Setup(s => s.ServerUris).Returns(serverUris);
            m_serverMock.Setup(s => s.TypeTree).Returns(typeTree);
            m_serverMock.Setup(s => s.MessageContext).Returns(messageContext);
            m_serverMock.Setup(s => s.CoreNodeManager).Returns(m_coreNodeManagerMock.Object);
            m_serverMock.Setup(s => s.NodeManager).Returns(masterNodeManagerMock.Object);
            m_serverMock.Setup(s => s.SubscriptionManager).Returns(m_subscriptionManagerMock.Object);
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);

            var defSysContext = new ServerSystemContext(m_serverMock.Object);
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(defSysContext);
        }

        [Test]
        public async Task CreateAddressSpace_HooksUpGetMonitoredItemsAndResendDataAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            GetMonitoredItemsMethodState getMonitoredItems = manager.FindPredefinedNode<GetMonitoredItemsMethodState>(MethodIds.Server_GetMonitoredItems);
            Assert.That(getMonitoredItems, Is.Not.Null, "GetMonitoredItems should exist.");
            Assert.That(getMonitoredItems.OnCallMethod, Is.Not.Null, "GetMonitoredItems OnCallMethod should be wired.");

            PropertyState getMonitoredItemsOutputArgs = manager.FindPredefinedNode<PropertyState>(VariableIds.Server_GetMonitoredItems_OutputArguments);
            Assert.That(getMonitoredItemsOutputArgs, Is.Not.Null, "GetMonitoredItems output arguments should exist.");
            Assert.That(getMonitoredItemsOutputArgs.Value.IsNull, Is.False, "Output arguments value should be initialized.");

            ResendDataMethodState resendData = manager.FindPredefinedNode<ResendDataMethodState>(MethodIds.Server_ResendData);
            Assert.That(resendData, Is.Not.Null, "ResendData should exist.");
            Assert.That(resendData.OnCallMethod, Is.Not.Null, "ResendData OnCallMethod should be wired.");
        }

        [Test]
        public async Task CreateAddressSpace_DurableSubscriptionsEnabled_SetsOnCallAsync()
        {
            var config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    DurableSubscriptionsEnabled = true
                }
            };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            SetSubscriptionDurableMethodState setSubscriptionDurable =
                manager.FindPredefinedNode<SetSubscriptionDurableMethodState>(MethodIds.Server_SetSubscriptionDurable);

            Assert.That(setSubscriptionDurable, Is.Not.Null, "SetSubscriptionDurable should exist.");
            Assert.That(setSubscriptionDurable.OnCall, Is.Not.Null, "SetSubscriptionDurable OnCall method should be wired.");
        }

        [Test]
        public async Task CreateAddressSpace_DurableSubscriptionsDisabled_DeletesNodeAsync()
        {
            var config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    DurableSubscriptionsEnabled = false
                }
            };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            SetSubscriptionDurableMethodState setSubscriptionDurable =
                manager.FindPredefinedNode<SetSubscriptionDurableMethodState>(MethodIds.Server_SetSubscriptionDurable);
            Assert.That(setSubscriptionDurable, Is.Null, "SetSubscriptionDurable should not exist.");
        }

        [Test]
        public async Task SetSubscriptionDurable_CallsSubscriptionManagerAsync()
        {
            var config = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    DurableSubscriptionsEnabled = true
                }
            };
            SetupServerMock();

            uint mockRevisedLifetime = 50;
            m_subscriptionManagerMock.Setup(m => m.SetSubscriptionDurable(
                It.IsAny<ISystemContext>(),
                1234u,
                100u,
                out mockRevisedLifetime))
                .Returns(StatusCodes.Good);

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            SetSubscriptionDurableMethodState setSubscriptionDurable =
                manager.FindPredefinedNode<SetSubscriptionDurableMethodState>(MethodIds.Server_SetSubscriptionDurable);

            uint actualRevisedLifetime = 0;
            ServiceResult result = setSubscriptionDurable.OnCall(
                manager.SystemContext,
                setSubscriptionDurable,
ObjectIds.Server,
                1234,
                100,
                ref actualRevisedLifetime);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(actualRevisedLifetime, Is.EqualTo(50));
            m_subscriptionManagerMock.Verify(m => m.SetSubscriptionDurable(It.IsAny<ISystemContext>(), 1234, 100, out It.Ref<uint>.IsAny), Times.Once);
        }

        [Test]
        public async Task GetMonitoredItems_ValidatesSessionAndReturnsItemsAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1234);
            subMock.Setup(s => s.SessionId).Returns(new NodeId(1, 1));
            ArrayOf<uint> serverHandles = [1, 2];
            ArrayOf<uint> clientHandles = [3, 4];
            subMock.Setup(s => s.GetMonitoredItems(out serverHandles, out clientHandles));

            ISubscription outSub = subMock.Object;
            m_subscriptionManagerMock.Setup(m => m.TryGetSubscription(It.IsAny<uint>(), out outSub)).Returns(true);

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            GetMonitoredItemsMethodState getMonitoredItems = manager.FindPredefinedNode<GetMonitoredItemsMethodState>(MethodIds.Server_GetMonitoredItems);

            var sysContextMock = new Mock<ISessionSystemContext>();
            sysContextMock.Setup(c => c.SessionId).Returns(new NodeId(1, 1));

            ArrayOf<Variant> inputs = [new Variant(1234u)];
            var outputs = new List<Variant> { Variant.Null, Variant.Null };

            ServiceResult result = getMonitoredItems.OnCallMethod(
                sysContextMock.Object,
                getMonitoredItems,
                inputs,
                outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(outputs[0].GetUInt32Array(), Is.EqualTo(serverHandles));
            Assert.That(outputs[1].GetUInt32Array(), Is.EqualTo(clientHandles));

            // Test access denied (different session ID)
            sysContextMock.Setup(c => c.SessionId).Returns(new NodeId(2, 1));
            result = getMonitoredItems.OnCallMethod(
                sysContextMock.Object,
                getMonitoredItems,
                inputs,
                outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task GetMonitoredItems_InvalidSubscriptionId_ReturnsBadSubscriptionIdInvalidAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            ISubscription outSub = null;
            m_subscriptionManagerMock.Setup(m => m.TryGetSubscription(It.IsAny<uint>(), out outSub)).Returns(false);

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            GetMonitoredItemsMethodState getMonitoredItems = manager.FindPredefinedNode<GetMonitoredItemsMethodState>(MethodIds.Server_GetMonitoredItems);

            var sysContextMock = new Mock<ISessionSystemContext>();
            ArrayOf<Variant> inputs = [new Variant(1234u)];
            var outputs = new List<Variant> { Variant.Null, Variant.Null };

            ServiceResult result = getMonitoredItems.OnCallMethod(
                sysContextMock.Object,
                getMonitoredItems,
                inputs,
                outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task ResendData_ValidatesAccessAndCallsResendDataAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            var subMock = new Mock<ISubscription>();
            subMock.Setup(s => s.Id).Returns(1234);
            subMock.Setup(s => s.SessionId).Returns(new NodeId(1, 1));
            subMock.Setup(s => s.ResendData(It.IsAny<OperationContext>()));

            ISubscription outSub = subMock.Object;
            m_subscriptionManagerMock.Setup(m => m.TryGetSubscription(It.IsAny<uint>(), out outSub)).Returns(true);

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            ResendDataMethodState resendData = manager.FindPredefinedNode<ResendDataMethodState>(MethodIds.Server_ResendData);

            var reqHeader = new RequestHeader();
            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(1, 1));
            using var userIdentity = new UserIdentity();
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(userIdentity);

            var opContext = new OperationContext(reqHeader, null, RequestType.Read, RequestLifetime.None, sessionMock.Object);
            var sysContext = new ServerSystemContext(m_serverMock.Object, opContext);

            ArrayOf<Variant> inputs = [new Variant(1234u)];
            var outputs = new List<Variant>();

            ServiceResult result = resendData.OnCallMethod(
                sysContext,
                resendData,
                inputs,
                outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            subMock.Verify(s => s.ResendData(opContext), Times.Once);

            // Test bad access denied
            sessionMock.Setup(s => s.Id).Returns(new NodeId(2, 1));
            result = resendData.OnCallMethod(
                sysContext,
                resendData,
                inputs,
                outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task ResendData_InvalidSubscriptionId_ReturnsBadSubscriptionIdInvalidAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            ISubscription outSub = null;
            m_subscriptionManagerMock.Setup(m => m.TryGetSubscription(It.IsAny<uint>(), out outSub)).Returns(false);

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            ResendDataMethodState resendData = manager.FindPredefinedNode<ResendDataMethodState>(MethodIds.Server_ResendData);

            var reqHeader = new RequestHeader();
            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(1, 1));
            using var userIdentity = new UserIdentity();
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(userIdentity);

            var opContext = new OperationContext(reqHeader, null, RequestType.Read, RequestLifetime.None, sessionMock.Object);
            var sysContext = new ServerSystemContext(m_serverMock.Object, opContext);

            ArrayOf<Variant> inputs = [new Variant(1234u)];
            var outputs = new List<Variant>();

            ServiceResult result = resendData.OnCallMethod(
                sysContext,
                resendData,
                inputs,
                outputs);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task NodesAreCreatedCorrectlyAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            ServerObjectState serverObj = manager.FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            Assert.That(serverObj, Is.Not.Null, "Server object should be present.");

            ServerDiagnosticsState serverDiagnostics = manager.FindPredefinedNode<ServerDiagnosticsState>(ObjectIds.Server_ServerDiagnostics);
            Assert.That(serverDiagnostics, Is.Not.Null, "ServerDiagnostics should be present.");

            HistoryServerCapabilitiesState historyCapabilities =
                manager.FindPredefinedNode<HistoryServerCapabilitiesState>(ObjectIds.HistoryServerCapabilities);
            Assert.That(historyCapabilities, Is.Not.Null, "HistoryServerCapabilities should be present.");
        }

        [Test]
        public async Task SetDiagnosticsEnabledAsync_SameState_DoesNothingAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            // By default DiagnosticsEnabled is true
            Assert.That(manager.DiagnosticsEnabled, Is.True);

            await manager.SetDiagnosticsEnabledAsync(manager.SystemContext, true).ConfigureAwait(false);

            // Should remain true without side effects
            Assert.That(manager.DiagnosticsEnabled, Is.True);
        }

        [Test]
        public async Task SetDiagnosticsEnabledAsync_WhenDisabled_DeletesNodesAndStopsTimerAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            // Setup a mock update callback
            static ServiceResult UpdateCallback(ISystemContext ctx, NodeState node, ref Variant value) => ServiceResult.Good;

            // Add a mock session to verify deletion path
            var sessionDiag = new SessionDiagnosticsDataType { SessionName = "TestSession" };
            var sessionSecDiag = new SessionSecurityDiagnosticsDataType();

            NodeId sessionId = await manager.CreateSessionDiagnosticsAsync(
                manager.SystemContext,
                sessionDiag,
                UpdateCallback,
                sessionSecDiag,
                UpdateCallback).ConfigureAwait(false);

            Assert.That(sessionId.IsNull, Is.False);

            // Add a mock subscription to verify deletion path
            var subDiag = new SubscriptionDiagnosticsDataType { SubscriptionId = 1, SessionId = sessionId };
            NodeId subId = await manager.CreateSubscriptionDiagnosticsAsync(
                manager.SystemContext,
                subDiag,
                UpdateCallback).ConfigureAwait(false);

            Assert.That(subId.IsNull, Is.False);

            // Check node exists before deleting
            SessionDiagnosticsObjectState sessionNodeBefore = manager.FindPredefinedNode<SessionDiagnosticsObjectState>(sessionId);
            Assert.That(sessionNodeBefore, Is.Not.Null);

            SubscriptionDiagnosticsState subNodeBefore = manager.FindPredefinedNode<SubscriptionDiagnosticsState>(subId);
            Assert.That(subNodeBefore, Is.Not.Null);

            // Act: Disable diagnostics
            await manager.SetDiagnosticsEnabledAsync(manager.SystemContext, false).ConfigureAwait(false);

            Assert.That(manager.DiagnosticsEnabled, Is.False);

            // Verifying the nodes have been removed from the PredefinedNodes is a strong indicator DeleteNodeAsync was called
            SessionDiagnosticsObjectState sessionNodeAfter = manager.FindPredefinedNode<SessionDiagnosticsObjectState>(sessionId);
            Assert.That(sessionNodeAfter, Is.Null, "Session node should be deleted.");

            // Also check subscription diagnostics array was updated / removed
            SubscriptionDiagnosticsState subNodeAfter = manager.FindPredefinedNode<SubscriptionDiagnosticsState>(subId);
            Assert.That(subNodeAfter, Is.Null, "Subscription node should be deleted.");
        }

        [Test]
        public async Task SetDiagnosticsEnabledAsync_WhenEnabled_ClearsArraysAndStartsScanAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            static ServiceResult UpdateCallback(ISystemContext ctx, NodeState node, ref Variant value) => ServiceResult.Good;
            await manager.CreateServerDiagnosticsAsync(manager.SystemContext, new ServerDiagnosticsSummaryDataType(), UpdateCallback, CancellationToken.None)
                .ConfigureAwait(false);

            // start disabled
            await manager.SetDiagnosticsEnabledAsync(manager.SystemContext, false).ConfigureAwait(false);
            Assert.That(manager.DiagnosticsEnabled, Is.False);

            // Act: Enable diagnostics
            await manager.SetDiagnosticsEnabledAsync(manager.SystemContext, true).ConfigureAwait(false);

            Assert.That(manager.DiagnosticsEnabled, Is.True);

            // Verify arrays. Following SetDiagnosticsEnabledAsync(true), DoScan(true) is called.
            // Empty sessions and subscriptions will result in empty arrays and 'Good' StatusCodes.
            ServerDiagnosticsState serverDiagnostics = manager.FindPredefinedNode<ServerDiagnosticsState>(ObjectIds.Server_ServerDiagnostics);
            Assert.That(serverDiagnostics, Is.Not.Null);

            if (serverDiagnostics.SamplingIntervalDiagnosticsArray != null)
            {
                // This array is not evaluated heavily on normal DoScan, so it stays BadWaitingForInitialData
                Assert.That(serverDiagnostics.SamplingIntervalDiagnosticsArray.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            }

            // The rest are populated with empty arrays due to no sessions in test
            if (serverDiagnostics.SubscriptionDiagnosticsArray != null)
            {
                Assert.That(serverDiagnostics.SubscriptionDiagnosticsArray.Value, Is.Empty);
            }
            if (serverDiagnostics.SessionsDiagnosticsSummary?.SessionDiagnosticsArray != null)
            {
                Assert.That(serverDiagnostics.SessionsDiagnosticsSummary.SessionDiagnosticsArray.Value, Is.Empty);
            }
            if (serverDiagnostics.SessionsDiagnosticsSummary?.SessionSecurityDiagnosticsArray != null)
            {
                Assert.That(serverDiagnostics.SessionsDiagnosticsSummary.SessionSecurityDiagnosticsArray.Value, Is.Empty);
            }

            ServerDiagnosticsSummaryState serverDiagSummary =
                manager.FindPredefinedNode<ServerDiagnosticsSummaryState>(VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary);
            Assert.That(serverDiagSummary, Is.Not.Null);
            Assert.That(serverDiagSummary.StatusCode.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task CreateServerDiagnosticsAsync_WiresUpHandlers_AndPerformsActionsAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            static ServiceResult UpdateCallback(ISystemContext ctx, NodeState node, ref Variant value) => ServiceResult.Good;
            await manager.CreateServerDiagnosticsAsync(
                manager.SystemContext,
                new ServerDiagnosticsSummaryDataType(),
                UpdateCallback,
                CancellationToken.None).ConfigureAwait(false);

            // 1. Verify ServerDiagnosticsSummaryState
            ServerDiagnosticsSummaryState summaryNode = manager.FindPredefinedNode<ServerDiagnosticsSummaryState>(
VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary);
            Assert.That(summaryNode, Is.Not.Null);
            Assert.That(summaryNode.OnReadUserRolePermissions, Is.Not.Null,
                "OnReadUserRolePermissions should be wired on ServerDiagnosticsSummary.");

            // Test OnReadUserRolePermissions (Non-admin context -> PermissionType.None)
            ArrayOf<RolePermissionType> permissions = default;
            ServiceResult result = summaryNode.OnReadUserRolePermissions(manager.SystemContext, summaryNode, ref permissions);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(permissions.IsNull, Is.False);
            Assert.That(permissions.Count, Is.GreaterThan(0));
            Assert.That(permissions[0].Permissions, Is.EqualTo((uint)PermissionType.None));

            // 2. Verify SessionDiagnosticsArrayState
            SessionDiagnosticsArrayState sessionArrayNode = manager.FindPredefinedNode<SessionDiagnosticsArrayState>(
VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray);
            Assert.That(sessionArrayNode, Is.Not.Null);
            Assert.That(sessionArrayNode.OnSimpleReadValue, Is.Not.Null,
                "OnSimpleReadValue should be wired on SessionDiagnosticsArray.");
            Assert.That(sessionArrayNode.OnReadUserRolePermissions, Is.Not.Null,
                "OnReadUserRolePermissions should be wired on SessionDiagnosticsArray.");

            manager.ForceDiagnosticsScan();
            Variant arrayValue = default;
            result = sessionArrayNode.OnSimpleReadValue(manager.SystemContext, sessionArrayNode, ref arrayValue);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.IsNull, Is.False); // Depends on Variant.FromStructure

            // 3. Verify SessionSecurityDiagnosticsArrayState
            SessionSecurityDiagnosticsArrayState sessionSecurityArrayNode = manager.FindPredefinedNode<SessionSecurityDiagnosticsArrayState>(
VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray);
            Assert.That(sessionSecurityArrayNode, Is.Not.Null);
            Assert.That(sessionSecurityArrayNode.OnSimpleReadValue, Is.Not.Null,
                "OnSimpleReadValue should be wired on SessionSecurityDiagnosticsArray.");
            Assert.That(sessionSecurityArrayNode.OnReadUserRolePermissions, Is.Not.Null,
                "OnReadUserRolePermissions should be wired on SessionSecurityDiagnosticsArray.");

            manager.ForceDiagnosticsScan();
            arrayValue = default;
            result = sessionSecurityArrayNode.OnSimpleReadValue(
                manager.SystemContext,
                sessionSecurityArrayNode,
                ref arrayValue);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.IsNull, Is.False);

            // 4. Verify SubscriptionDiagnosticsArrayState
            SubscriptionDiagnosticsArrayState subArrayNode = manager.FindPredefinedNode<SubscriptionDiagnosticsArrayState>(
VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);
            Assert.That(subArrayNode, Is.Not.Null);
            Assert.That(subArrayNode.OnSimpleReadValue, Is.Not.Null,
                "OnSimpleReadValue should be wired on SubscriptionDiagnosticsArray.");
            Assert.That(subArrayNode.OnReadUserRolePermissions, Is.Not.Null,
                "OnReadUserRolePermissions should be wired on SubscriptionDiagnosticsArray.");

            manager.ForceDiagnosticsScan();
            arrayValue = default;
            result = subArrayNode.OnSimpleReadValue(manager.SystemContext, subArrayNode, ref arrayValue);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.IsNull, Is.False);
        }

        [Test]
        public async Task ReadUserRolePermissions_AdminAndSessionHandlingAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            static ServiceResult UpdateCallback(ISystemContext ctx, NodeState node, ref Variant value) => ServiceResult.Good;

            // 1. Create server and session diagnostics
            await manager.CreateServerDiagnosticsAsync(
                manager.SystemContext,
                new ServerDiagnosticsSummaryDataType(),
                UpdateCallback,
                CancellationToken.None).ConfigureAwait(false);

            var initialSessionId = new NodeId(1234, 1);
            var sessionDiag = new SessionDiagnosticsDataType { SessionName = "TestSession", SessionId = initialSessionId };
            var sessionSecDiag = new SessionSecurityDiagnosticsDataType { SessionId = initialSessionId };

            NodeId sessionId = await manager.CreateSessionDiagnosticsAsync(
                manager.SystemContext,
                sessionDiag,
                UpdateCallback,
                sessionSecDiag,
                UpdateCallback).ConfigureAwait(false);

            // Get nodes for permissions testing
            ServerDiagnosticsSummaryState summaryNode =
                manager.FindPredefinedNode<ServerDiagnosticsSummaryState>(VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary);
            SessionDiagnosticsObjectState sessionObjectNode = manager.FindPredefinedNode<SessionDiagnosticsObjectState>(sessionId);

            Assert.That(summaryNode, Is.Not.Null);
            Assert.That(sessionObjectNode, Is.Not.Null);
            Assert.That(summaryNode.OnReadUserRolePermissions, Is.Not.Null);
            Assert.That(sessionObjectNode.OnReadUserRolePermissions, Is.Not.Null);

            // 2. Test Context: Admin
            using var identity = new UserIdentity("admin", []);
            Role[] roles = [Role.SecurityAdmin];
            var namespaces = new NamespaceTable();
            namespaces.Append(Ua.Namespaces.OpcUa);
            var roleIdentity = new RoleBasedIdentity(identity, roles, namespaces);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext(
                "1",
                endpoint,
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);
            var reqHeader = new RequestHeader();

            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(9999, 1)); // Different from sessionId to test admin overrides
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(roleIdentity);

            var opContextAdmin = new OperationContext(reqHeader, channelContext, RequestType.Read, RequestLifetime.None, sessionMock.Object);
            var adminContext = new ServerSystemContext(m_serverMock.Object, opContextAdmin);

            ArrayOf<RolePermissionType> permissionsAdmin = default;
            summaryNode.OnReadUserRolePermissions(adminContext, summaryNode, ref permissionsAdmin);
            Assert.That(permissionsAdmin.IsNull, Is.False);
            Assert.That(permissionsAdmin.Count, Is.GreaterThan(0));
            Assert.That(permissionsAdmin[0].Permissions, Is.Not.EqualTo((uint)PermissionType.None),
                "Admin should have permissions on summary");

            permissionsAdmin = default;
            sessionObjectNode.OnReadUserRolePermissions(adminContext, sessionObjectNode, ref permissionsAdmin);
            Assert.That(permissionsAdmin.IsNull, Is.False);
            Assert.That(permissionsAdmin[0].Permissions, Is.Not.EqualTo((uint)PermissionType.None),
                "Admin should have permissions on any session");

            // 3. Test Context: Session Owner (Non-Admin)
            using var normalIdentity = new UserIdentity("owner", []);
            var normalRoleIdentity = new RoleBasedIdentity(normalIdentity, [Role.AuthenticatedUser], namespaces);

            var sessionOwnerMock = new Mock<ISession>();
            sessionOwnerMock.Setup(s => s.Id).Returns(sessionId); // Same as sessionId
            sessionOwnerMock.Setup(s => s.EffectiveIdentity).Returns(normalRoleIdentity);

            var opContextOwner = new OperationContext(
                reqHeader, channelContext, RequestType.Read, RequestLifetime.None, sessionOwnerMock.Object);
            var ownerContext = new ServerSystemContext(m_serverMock.Object, opContextOwner);

            ArrayOf<RolePermissionType> permissionsOwner = default;
            summaryNode.OnReadUserRolePermissions(ownerContext, summaryNode, ref permissionsOwner);
            Assert.That(permissionsOwner.IsNull, Is.False);
            Assert.That(permissionsOwner[0].Permissions, Is.EqualTo((uint)PermissionType.None),
                "Non-admin owner should NOT have overall summary permissions");

            permissionsOwner = default;
            sessionObjectNode.OnReadUserRolePermissions(ownerContext, sessionObjectNode, ref permissionsOwner);
            Assert.That(permissionsOwner.IsNull, Is.False);
            Assert.That(permissionsOwner[0].Permissions, Is.Not.EqualTo((uint)PermissionType.None),
                "Owner should have permissions on their own session");

            // 4. Test Context: Other Session (Non-Admin)
            var sessionOtherMock = new Mock<ISession>();
            sessionOtherMock.Setup(s => s.Id).Returns(new NodeId(9999, 1)); // Different sessionId
            sessionOtherMock.Setup(s => s.EffectiveIdentity).Returns(normalRoleIdentity);

            var opContextOther = new OperationContext(
                reqHeader, channelContext, RequestType.Read, RequestLifetime.None, sessionOtherMock.Object);
            var otherContext = new ServerSystemContext(m_serverMock.Object, opContextOther);

            ArrayOf<RolePermissionType> permissionsOther = default;
            sessionObjectNode.OnReadUserRolePermissions(otherContext, sessionObjectNode, ref permissionsOther);
            Assert.That(permissionsOther.IsNull, Is.False);
            Assert.That(permissionsOther[0].Permissions, Is.EqualTo((uint)PermissionType.None),
                "Other non-admin session should NOT have permissions on this session");
        }

        [Test]
        public async Task OnReadDiagnosticsArray_ExpectedBehaviorForDifferentTypesAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            var sessionDiag = new SessionDiagnosticsDataType { SessionName = "TestSession", SessionId = new NodeId(1, 1) };
            var sessionSecDiag = new SessionSecurityDiagnosticsDataType { SessionId = new NodeId(1, 1) };
            var subDiag = new SubscriptionDiagnosticsDataType { SubscriptionId = 100, SessionId = new NodeId(1, 1) };

            var sessionDiag2 = new SessionDiagnosticsDataType { SessionName = "TestSession2", SessionId = new NodeId(2, 1) };
            var sessionSecDiag2 = new SessionSecurityDiagnosticsDataType { SessionId = new NodeId(2, 1) };
            var subDiag2 = new SubscriptionDiagnosticsDataType { SubscriptionId = 200, SessionId = new NodeId(2, 1) };

            ServiceResult UpdateCallback(ISystemContext ctx, NodeState node, ref Variant value)
            {
                var instanceState = node as BaseInstanceState;
                if (node.BrowseName.Name == BrowseNames.SessionDiagnostics)
                {
                    value = instanceState?.Parent?.BrowseName.Name == "TestSession" ? Variant.FromStructure(sessionDiag) : Variant.FromStructure(sessionDiag2);
                }
                else if (node.BrowseName.Name == BrowseNames.SessionSecurityDiagnostics)
                {
                    value = instanceState?.Parent?.BrowseName.Name == "TestSession"
                        ? Variant.FromStructure(sessionSecDiag)
                        : Variant.FromStructure(sessionSecDiag2);
                }
                else if (node.BrowseName.Name == "100")
                {
                    value = Variant.FromStructure(subDiag); // Subscription node is named after SubscriptionId
                }
                else if (node.BrowseName.Name == "200")
                {
                    value = Variant.FromStructure(subDiag2);
                }
                else
                {
                    value = Variant.FromStructure(new ServerDiagnosticsSummaryDataType());
                }
                return ServiceResult.Good;
            }

            await manager.CreateServerDiagnosticsAsync(manager.SystemContext, new ServerDiagnosticsSummaryDataType(), UpdateCallback, CancellationToken.None)
                .ConfigureAwait(false);

            NodeId sessionId = await manager.CreateSessionDiagnosticsAsync(
                manager.SystemContext,
                sessionDiag,
                UpdateCallback,
                sessionSecDiag,
                UpdateCallback).ConfigureAwait(false);

            subDiag.SessionId = sessionId;

            NodeId subId = await manager.CreateSubscriptionDiagnosticsAsync(
                manager.SystemContext,
                subDiag,
                UpdateCallback).ConfigureAwait(false);

            subDiag2.SessionId = await manager.CreateSessionDiagnosticsAsync(
                manager.SystemContext,
                sessionDiag2,
                UpdateCallback,
                sessionSecDiag2,
                UpdateCallback).ConfigureAwait(false);

            NodeId subId2 = await manager.CreateSubscriptionDiagnosticsAsync(
                manager.SystemContext,
                subDiag2,
                UpdateCallback).ConfigureAwait(false);

            manager.ForceDiagnosticsScan();

            using var adminIdentity = new UserIdentity("admin", []);
            var namespaces = new NamespaceTable();
            namespaces.Append(Ua.Namespaces.OpcUa);
            var roleIdentity = new RoleBasedIdentity(adminIdentity, [Role.SecurityAdmin], namespaces);
            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(9999, 1));
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(roleIdentity);
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var secureChannelContext = new SecureChannelContext(
                "1",
                endpoint,
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);
            var opContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext,
                RequestType.Read,
                RequestLifetime.None,
                sessionMock.Object);
            var adminContext = new ServerSystemContext(m_serverMock.Object, opContext);

            SessionDiagnosticsArrayState sessionArrayNode = manager.FindPredefinedNode<SessionDiagnosticsArrayState>(
VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray);
            Variant arrayValue = default;
            ServiceResult result = sessionArrayNode.OnSimpleReadValue(adminContext, sessionArrayNode, ref arrayValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.TryGetValue(out ArrayOf<ExtensionObject> sessionArray), Is.True);
            Assert.That(sessionArray.Count, Is.EqualTo(2));
            sessionArray[0].TryGetValue(out SessionDiagnosticsDataType sessionDiagObj);
            Assert.That(sessionDiagObj.SessionName, Is.EqualTo("TestSession"));

            SessionSecurityDiagnosticsArrayState sessionSecurityArrayNode = manager.FindPredefinedNode<SessionSecurityDiagnosticsArrayState>(
VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray);
            arrayValue = default;
            result = sessionSecurityArrayNode.OnSimpleReadValue(adminContext, sessionSecurityArrayNode, ref arrayValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.TryGetValue(out ArrayOf<ExtensionObject> sessionSecurityArray), Is.True);
            Assert.That(sessionSecurityArray.Count, Is.EqualTo(2));
            sessionSecurityArray[0].TryGetValue(out SessionSecurityDiagnosticsDataType sessionSecDiagObj);
            Assert.That(sessionSecDiagObj.SessionId, Is.EqualTo(sessionId));

            SubscriptionDiagnosticsArrayState subArrayNode = manager.FindPredefinedNode<SubscriptionDiagnosticsArrayState>(
VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

            arrayValue = default;
            result = subArrayNode.OnSimpleReadValue(adminContext, subArrayNode, ref arrayValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.TryGetValue(out ArrayOf<ExtensionObject> subArray), Is.True);
            Assert.That(subArray.Count, Is.EqualTo(2));
            subArray[0].TryGetValue(out SubscriptionDiagnosticsDataType subDiagObj);
            Assert.That(subDiagObj.SubscriptionId, Is.EqualTo(100));

            // Test unauthorized non-admin user accessing their own session
            using var normalIdentity = new UserIdentity("user", []);
            var normalRoleIdentity = new RoleBasedIdentity(normalIdentity, [Role.AuthenticatedUser], namespaces);
            var normalSessionMock = new Mock<ISession>();
            normalSessionMock.Setup(s => s.Id).Returns(sessionId); // set to first session
            normalSessionMock.Setup(s => s.EffectiveIdentity).Returns(normalRoleIdentity);
            var normalOpContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext,
                RequestType.Read,
                RequestLifetime.None,
                normalSessionMock.Object);
            var normalContext = new ServerSystemContext(m_serverMock.Object, normalOpContext);

            manager.ForceDiagnosticsScan();

            arrayValue = default;
            result = sessionArrayNode.OnSimpleReadValue(normalContext, sessionArrayNode, ref arrayValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.TryGetValue(out ArrayOf<ExtensionObject> normalSessionArray), Is.True);
            Assert.That(normalSessionArray.Count, Is.EqualTo(1));
            normalSessionArray[0].TryGetValue(out SessionDiagnosticsDataType normalSessionDiagObj);
            Assert.That(normalSessionDiagObj.SessionName, Is.EqualTo("TestSession"));

            manager.ForceDiagnosticsScan();

            arrayValue = default;
            result = sessionSecurityArrayNode.OnSimpleReadValue(normalContext, sessionSecurityArrayNode, ref arrayValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.TryGetValue(out ArrayOf<ExtensionObject> normalSessionSecArray), Is.True);
            Assert.That(normalSessionSecArray.Count, Is.EqualTo(1));
            normalSessionSecArray[0].TryGetValue(out SessionSecurityDiagnosticsDataType normalSessionSecDiagObj);
            Assert.That(normalSessionSecDiagObj.SessionId, Is.EqualTo(sessionId));

            manager.ForceDiagnosticsScan();

            arrayValue = default;
            result = subArrayNode.OnSimpleReadValue(normalContext, subArrayNode, ref arrayValue);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arrayValue.TryGetValue(out ArrayOf<ExtensionObject> normalSubArray), Is.True);
            Assert.That(normalSubArray.Count, Is.EqualTo(1));
            normalSubArray[0].TryGetValue(out SubscriptionDiagnosticsDataType normalSubDiagObj);
            Assert.That(normalSubDiagObj.SessionId, Is.EqualTo(sessionId));

            // Test if recently scanned just returns Good
            result = sessionArrayNode.OnSimpleReadValue(adminContext, sessionArrayNode, ref arrayValue);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));

            // Test behavior when diagnostics are disabled
            await manager.SetDiagnosticsEnabledAsync(manager.SystemContext, false).ConfigureAwait(false);
            result = sessionArrayNode.OnSimpleReadValue(adminContext, sessionArrayNode, ref arrayValue);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfService));
        }

        [Test]
        public async Task MonitoredItemLifecycle_ChangesDiagnosticsMonitoring_And_SamplingAsync()
        {
            var config = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
            SetupServerMock();

            // Required for CreateMonitoredItemsAsync
            using var queueFactory = new MonitoredItemQueueFactory(m_serverMock.Object.Telemetry);
            m_serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(queueFactory);

            using var manager = new DiagnosticsNodeManager(m_serverMock.Object, config, NullLogger.Instance);
            var externalRefs = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalRefs).ConfigureAwait(false);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var secureChannelContext = new SecureChannelContext(
                "1",
                endpoint,
                RequestEncoding.Binary,
                clientChannelCertificate: null,
                serverChannelCertificate: null,
                channelThumbprint: null);

            using var mockIdentity = new UserIdentity("admin", []);
            var mockRoleIdentity = new RoleBasedIdentity(mockIdentity, [Role.SecurityAdmin], m_serverMock.Object.NamespaceUris);

            var sessionMock = new Mock<ISession>();
            sessionMock.Setup(s => s.Id).Returns(new NodeId(1, 1));
            sessionMock.Setup(s => s.EffectiveIdentity).Returns(mockRoleIdentity);

            var opContext = new OperationContext(new RequestHeader(), secureChannelContext, RequestType.Read, RequestLifetime.None, sessionMock.Object);
            var sysContext = new ServerSystemContext(m_serverMock.Object, opContext);

            static ServiceResult UpdateCallback(ISystemContext ctx, NodeState node, ref Variant value)
            {
                value = Variant.FromStructure(new ServerDiagnosticsSummaryDataType { CurrentSessionCount = 42 });
                return ServiceResult.Good;
            }

            await manager.CreateServerDiagnosticsAsync(
                manager.SystemContext,
                new ServerDiagnosticsSummaryDataType(),
                UpdateCallback,
                CancellationToken.None).ConfigureAwait(false);

            ServerDiagnosticsSummaryState summaryNode =
                manager.FindPredefinedNode<ServerDiagnosticsSummaryState>(VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary);
            Assert.That(summaryNode, Is.Not.Null);

            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = summaryNode.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters { ClientHandle = 1, SamplingInterval = 10, QueueSize = 10 }
            };

            var itemsToCreate = new List<MonitoredItemCreateRequest> { itemToCreate };
            var createErrors = new List<ServiceResult> { null };
            var filterErrors = new List<MonitoringFilterResult> { null };
            var monitoredItems = new List<IMonitoredItem> { null };

            await manager.CreateMonitoredItemsAsync(
                opContext,
                1,
                100,
TimestampsToReturn.Both,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                false,
                new MonitoredItemIdFactory()).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(createErrors[0]), Is.True);
            var monitoredItem = monitoredItems[0] as IDataChangeMonitoredItem;
            Assert.That(monitoredItem, Is.Not.Null);

            // Push an update
            manager.ForceDiagnosticsScan();

            // Wait for internal timer to call DoSample
            bool valueQueued = false;
            for (int i = 0; i < 10; i++)
            {
                var notifications = new Queue<MonitoredItemNotification>();
                var diagnostics = new Queue<DiagnosticInfo>();
                monitoredItem.Publish(
                    opContext,
                    notifications,
                    diagnostics,
                    1,
                    NullLogger.Instance);

                if (notifications.Count > 0)
                {
                    valueQueued = true;
                    break;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            Assert.That(valueQueued, Is.True, "MonitoredItem did not receive updated value from sampling timer.");

            // Delete nodes
            var processedItems = new List<bool> { false };
            var deleteErrors = new List<ServiceResult> { null };

            await manager.DeleteMonitoredItemsAsync(
                opContext,
                [monitoredItem],
                processedItems,
                deleteErrors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(deleteErrors[0]), Is.True);
        }
    }
}
