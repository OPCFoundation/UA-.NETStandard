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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <inheritdoc/>
    public partial class DiagnosticsNodeManager : AsyncCustomNodeManager, IDiagnosticsNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DiagnosticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiagnosticsNodeManager>(),
                  timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DiagnosticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : this(server, configuration, logger, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the node manager with an explicit
        /// <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used by the diagnostics-scan
        /// and sampling timers and for the scan-throttle wall-clock checks.
        /// When <c>null</c>, the time provider exposed by the server
        /// (via <see cref="ITimeProviderProvider"/>) is used, falling back
        /// to <see cref="TimeProvider.System"/>.
        /// </param>
        public DiagnosticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            TimeProvider? timeProvider)
            : base(server, configuration, logger)
        {
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            AliasRoot = "Core";

            string[] namespaceUris =
            [
                Ua.Namespaces.OpcUa,
                Ua.Namespaces.OpcUa + "Diagnostics"
            ];
            SetNamespaces(namespaceUris);

            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);

            // counter identifiers in the diagnostics namespace rather than
            // the first one, which is the OPC UA namespace this manager only
            // reads. Session and subscription diagnostics come and go under
            // repeating browse names, so browse paths would collide.
            NodeIdFactory = NodeIdFactory
                .WithMode(NodeIdAssignmentMode.Counter)
                .WithDefaultNamespaceIndex(m_namespaceIndex);
            m_sessions = [];
            m_subscriptions = [];
            DiagnosticsEnabled = true;
            m_doScanBusy = false;
            m_sampledItems = [];
            m_minimumSamplingInterval = 100;
            m_durableSubscriptionsEnabled = configuration.ServerConfiguration?
                .DurableSubscriptionsEnabled ??
                false;
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_diagnosticsLock)
                {
                    m_diagnosticsDisposed = true;
                    m_diagnosticsScanTimer?.Dispose();
                    m_diagnosticsScanTimer = null;
                }
                m_samplingTimer?.Dispose();
                m_samplingTimer = null;

                // OPC UA Part 17 — unsubscribe from the alias-name
                // registry so the registry does not hold a stale handler
                // reference into a disposed manager.
                UnwireStandardAliasMethods();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Tracks a diagnostics operation and acquires address-space access unless the node manager is stopping.
        /// </summary>
        private async ValueTask<NodeManagerOperation> EnterDiagnosticsOperationAsync(CancellationToken ct)
        {
            NodeManagerOperation operation = BeginNodeManagerOperation();
            bool acquired = false;
            try
            {
                await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
                acquired = true;
                ThrowIfNodeManagerStopping();
                return operation;
            }
            catch
            {
                if (acquired)
                {
                    m_modifyAddressSpaceSemaphoreSlim.Release();
                }
                operation.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            await m_diagnosticsTransitionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await m_modifyAddressSpaceSemaphoreSlim.WaitAsync().ConfigureAwait(false);
                try
                {
                    m_historyCapabilities = null;
                }
                finally
                {
                    m_modifyAddressSpaceSemaphoreSlim.Release();
                    m_modifyAddressSpaceSemaphoreSlim.Dispose();
                }
            }
            finally
            {
                m_diagnosticsTransitionSemaphore.Release();
                m_diagnosticsTransitionSemaphore.Dispose();
            }
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            // The nodes are loaded by the DiagnosticsNodeManager from the
            // output by the Source Generator. These nodes are added to the CoreNodeManager
            // via the ImportNodes() method.
            await Server.CoreNodeManager.ImportNodesAsync(
                SystemContext,
                PredefinedNodes.Values,
                true,
                cancellationToken).ConfigureAwait(false);

            // OPC UA Part 17 — wire the standard well-known Aliases /
            // TagVariables / Topics methods through the server-wide
            // IAliasNameStoreRegistry. See DiagnosticsNodeManager.AliasNames.cs.
            WireStandardAliasMethods();
        }

        /// <summary>
        /// Called when a client sets a subscription as durable.
        /// </summary>
        protected ServiceResult OnSetSubscriptionDurable(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId,
            uint lifetimeInHours,
            ref uint revisedLifetimeInHours)
        {
            return Server.SubscriptionManager.SetSubscriptionDurable(
                context,
                subscriptionId,
                lifetimeInHours,
                out revisedLifetimeInHours);
        }

        /// <summary>
        /// Called when a client gets the monitored items of a subscription.
        /// </summary>
        protected ServiceResult OnGetMonitoredItems(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 1)
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!inputArguments[0].TryGetValue(out uint subscriptionId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!Server.SubscriptionManager.TryGetSubscription(subscriptionId, out ISubscription? subscription))
            {
                return StatusCodes.BadSubscriptionIdInvalid;
            }

            if (context is ISessionSystemContext session &&
                subscription.SessionId != null! &&
                !subscription.SessionId.Equals(session.SessionId))
            {
                // user tries to access subscription of different session
                return StatusCodes.BadUserAccessDenied;
            }

            subscription.GetMonitoredItems(
                out ArrayOf<uint> serverHandles,
                out ArrayOf<uint> clientHandles);

            outputArguments[0] = serverHandles;
            outputArguments[1] = clientHandles;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when a client initiates resending of all data monitored items in a Subscription.
        /// </summary>
        protected ServiceResult OnResendData(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            if (inputArguments.Count != 1)
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!inputArguments[0].TryGetValue(out uint subscriptionId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!Server.SubscriptionManager.TryGetSubscription(subscriptionId, out ISubscription? subscription))
            {
                return StatusCodes.BadSubscriptionIdInvalid;
            }

            if (context is not ServerSystemContext session ||
                (subscription.SessionId != null! && !subscription.SessionId.Equals(session.SessionId)))
            {
                // user tries to access subscription of different session
                return StatusCodes.BadUserAccessDenied;
            }

            subscription.ResendData(session.OperationContext!);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when a client locks the server.
        /// </summary>
        public ServiceResult OnLockServer(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            var systemContext = context as ServerSystemContext;

            if (m_serverLockHolder != null! && !m_serverLockHolder.IsNull && !m_serverLockHolder.Equals(systemContext?.SessionId))
            {
                return StatusCodes.BadSessionIdInvalid;
            }

            m_serverLockHolder = systemContext?.SessionId ?? NodeId.Null;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when a client locks the server.
        /// </summary>
        protected ServiceResult OnUnlockServer(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            var systemContext = context as ServerSystemContext;

            if (m_serverLockHolder != null! && !m_serverLockHolder.IsNull && !m_serverLockHolder.Equals(systemContext?.SessionId))
            {
                return StatusCodes.BadSessionIdInvalid;
            }

            m_serverLockHolder = default;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            NodeStateCollection nodes = new NodeStateCollection().AddOpcUa(context);

            AddSdkImplementedOptionalChildren(context, nodes);

            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <summary>
        /// Programmatically adds Optional children of the well-known
        /// singletons that this SDK implements. Hook for subclasses that
        /// override <see cref="LoadPredefinedNodesAsync"/> — call after the
        /// base collection is built to preserve SDK-visible behaviour.
        /// </summary>
        protected virtual void AddSdkImplementedOptionalChildren(
            ISystemContext context,
            NodeStateCollection nodes)
        {
            foreach (NodeState node in nodes)
            {
                switch (node)
                {
                    case ServerObjectState serverObject:
                        AddServerSdkOptionalChildren(context, serverObject);
                        break;
                    case HistoryServerCapabilitiesState historyCaps:
                        AddHistoryCapabilitiesSdkOptionalChildren(context, historyCaps);
                        break;
                    case RoleState roleState:
                        AddWellKnownRoleSdkOptionalChildren(context, roleState);
                        break;
                    case NamespaceMetadataState metadataState:
                        AddOpcUaNamespaceMetadataSdkOptionalChildren(context, metadataState);
                        break;
                    case AliasNameCategoryState aliasCategory:
                        AddAliasNameCategorySdkOptionalChildren(context, aliasCategory);
                        break;
                }
            }
        }

        private void AddServerSdkOptionalChildren(
            ISystemContext context,
            ServerObjectState serverObject)
        {
            serverObject
                .AddGetMonitoredItems(context)
                .AddResendData(context)
                .AddSetSubscriptionDurable(context,
                    m_durableSubscriptionsEnabled,
                    _ => { })
                .AddNamespaces(context)
                .AddUrisVersion(context)
                .AddEstimatedReturnTime(context)
                .AddRequestServerStateChange(context)
                .AddLocalTime(context);

            if (serverObject.ServerCapabilities != null)
            {
                AddServerCapabilitiesSdkOptionalChildren(
                    context, serverObject.ServerCapabilities);
            }
            // The base ServerRedundancyType only declares the optional
            // RedundantServerArray. The mode-specific subtype
            // (TransparentRedundancyType / NonTransparentRedundancyType) and
            // its generated children (CurrentServerId / ServerUriArray) are
            // materialised from the configured RedundancySupport mode at
            // server startup by Opc.Ua.Redundancy.Server, which promotes this
            // node to the correct subtype while preserving the well-known
            // RedundantServerArray NodeId assigned here.
            serverObject.ServerRedundancy?.AddRedundantServerArray(context);
        }

        private static void AddServerCapabilitiesSdkOptionalChildren(
            ISystemContext context,
            ServerCapabilitiesState serverCapabilities)
        {
            serverCapabilities
                .AddMaxArrayLength(context)
                .AddMaxStringLength(context)
                .AddMaxByteStringLength(context)
                .AddMaxSessions(context)
                .AddMaxSubscriptions(context)
                .AddMaxMonitoredItems(context)
                .AddMaxSubscriptionsPerSession(context)
                .AddMaxMonitoredItemsPerSubscription(context)
                .AddMaxSelectClauseParameters(context)
                .AddMaxWhereClauseParameters(context)
                .AddMaxMonitoredItemsQueueSize(context)
                .AddConformanceUnits(context)
                .AddRoleSet(context)
                .AddOperationLimits(context)
                .OperationLimits!
                    .AddMaxNodesPerRead(context)
                    .AddMaxNodesPerHistoryReadData(context)
                    .AddMaxNodesPerHistoryReadEvents(context)
                    .AddMaxNodesPerWrite(context)
                    .AddMaxNodesPerHistoryUpdateData(context)
                    .AddMaxNodesPerHistoryUpdateEvents(context)
                    .AddMaxNodesPerMethodCall(context)
                    .AddMaxNodesPerBrowse(context)
                    .AddMaxNodesPerRegisterNodes(context)
                    .AddMaxNodesPerTranslateBrowsePathsToNodeIds(context)
                    .AddMaxNodesPerNodeManagement(context)
                    .AddMaxMonitoredItemsPerCall(context);
        }

        private static void AddHistoryCapabilitiesSdkOptionalChildren(
            ISystemContext context,
            HistoryServerCapabilitiesState historyCaps)
        {
            historyCaps.AddServerTimestampSupported(context);
        }

        private static void AddOpcUaNamespaceMetadataSdkOptionalChildren(
            ISystemContext context,
            NamespaceMetadataState metadataState)
        {
            if (metadataState.NodeId.IdType != IdType.Numeric ||
                metadataState.NodeId.NamespaceIndex != 0 ||
                !metadataState.NodeId.TryGetValue(out uint numericId) ||
                numericId != Objects.OPCUANamespaceMetadata)
            {
                return;
            }
            metadataState
                .AddDefaultRolePermissions(context)
                .AddDefaultUserRolePermissions(context)
                .AddDefaultAccessRestrictions(context);
        }

        private static void AddAliasNameCategorySdkOptionalChildren(
            ISystemContext context,
            AliasNameCategoryState category)
        {
            if (category.NodeId.IdType != IdType.Numeric ||
                category.NodeId.NamespaceIndex != 0 ||
                !category.NodeId.TryGetValue(out uint numericId) ||
                numericId != Objects.Aliases)
            {
                return;
            }
            category.AddLastChange(context);
        }

        /// <summary>
        /// Programmatically re-adds the Optional RoleType children for the
        /// six modifiable well-known roles (Observer, Operator, Engineer,
        /// Supervisor, ConfigureAdmin, SecurityAdmin)
        /// so <see cref="RoleStateBinding"/> finds them and
        /// can wire OnCallAsync delegates and OnWriteValue handlers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The three immutable roles (Anonymous, AuthenticatedUser,
        /// TrustedApplication) do not have well-known instance NodeIds for
        /// the Optional methods/properties.
        /// </para>
        /// </remarks>
        private static void AddWellKnownRoleSdkOptionalChildren(
            ISystemContext context,
            RoleState roleState)
        {
            if (roleState.NodeId.IdType != IdType.Numeric ||
                roleState.NodeId.NamespaceIndex != 0 ||
                !roleState.NodeId.TryGetValue(out uint numericId))
            {
                return;
            }
            switch (numericId)
            {
                case Objects.WellKnownRole_Observer:
                case Objects.WellKnownRole_Operator:
                case Objects.WellKnownRole_Engineer:
                case Objects.WellKnownRole_Supervisor:
                case Objects.WellKnownRole_ConfigureAdmin:
                case Objects.WellKnownRole_SecurityAdmin:
                    AddWellKnownRoleChildren(context, roleState);
                    break;
            }
        }

        private static void AddWellKnownRoleChildren(
            ISystemContext context,
            RoleState role)
        {
            role
                .AddApplications(context)
                .AddApplicationsExclude(context)
                .AddEndpoints(context)
                .AddEndpointsExclude(context)
                .AddCustomConfiguration(context)
                .AddAddIdentity(context)
                .AddRemoveIdentity(context)
                .AddAddApplication(context)
                .AddRemoveApplication(context)
                .AddAddEndpoint(context)
                .AddRemoveEndpoint(context);
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            if (predefinedNode is not BaseObjectState passiveNode)
            {
                if (predefinedNode is not MethodState passiveMethod)
                {
                    return predefinedNode;
                }

                if (passiveMethod.NodeId == MethodIds.ConditionType_ConditionRefresh)
                {
                    var activeNode = (ConditionRefreshMethodState)passiveMethod;

                    activeNode.OnCall = OnConditionRefresh;
                }
                else if (passiveMethod.NodeId == MethodIds.ConditionType_ConditionRefresh2)
                {
                    var activeNode = (ConditionRefresh2MethodState)passiveMethod;

                    activeNode.OnCall = OnConditionRefresh2;
                }
                else if (passiveMethod.NodeId == MethodIds.Server_SetSubscriptionDurable)
                {
                    var activeNode = (SetSubscriptionDurableMethodState)passiveMethod;
                    if (m_durableSubscriptionsEnabled)
                    {
                        activeNode.OnCall = OnSetSubscriptionDurable;
                    }
                }
                else if (passiveMethod.NodeId == MethodIds.Server_GetMonitoredItems)
                {
                    var activeNode = (GetMonitoredItemsMethodState)passiveMethod;
                    activeNode.OnCallMethod = OnGetMonitoredItems;
                }
                else if (passiveMethod.NodeId == MethodIds.Server_ResendData)
                {
                    var activeNode = (ResendDataMethodState)passiveMethod;
                    activeNode.OnCallMethod = OnResendData;
                }

                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || !typeId.TryGetValue(out uint numericId))
            {
                return predefinedNode;
            }

            switch (numericId)
            {
                case ObjectTypes.ServerType:
                    // add the server object as the root notifier.
                    await AddRootNotifierAsync(passiveNode, cancellationToken).ConfigureAwait(false);
                    return passiveNode;
            }

            return predefinedNode;
        }

        /// <summary>
        /// Handles a request to refresh conditions for a subscription.
        /// </summary>
        protected ServiceResult OnConditionRefresh(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId)
        {
            ServerSystemContext systemContext = context as ServerSystemContext ?? SystemContext;

            Server.ConditionRefresh(systemContext.OperationContext!, subscriptionId);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles a request to refresh conditions for a subscription and specific monitored item.
        /// </summary>
        protected ServiceResult OnConditionRefresh2(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId,
            uint monitoredItemId)
        {
            ServerSystemContext systemContext = context as ServerSystemContext ?? SystemContext;

            Server.ConditionRefresh2(
                systemContext.OperationContext!,
                subscriptionId,
                monitoredItemId);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns true of the node is a diagnostics node.
        /// </summary>
        private static bool IsDiagnosticsNode(NodeState node)
        {
            if (node == null)
            {
                return false;
            }

            if (!IsDiagnosticsStructureNode(node))
            {
                if (node is not BaseInstanceState instance)
                {
                    return false;
                }

                return IsDiagnosticsStructureNode(instance.Parent!);
            }

            return true;
        }

        /// <summary>
        /// Returns true of the node is a diagnostics node.
        /// </summary>
        private static bool IsDiagnosticsStructureNode(NodeState node)
        {
            if (node is not BaseInstanceState instance)
            {
                return false;
            }

            NodeId typeId = instance.TypeDefinitionId;

            if (typeId.IsNull ||
                typeId.NamespaceIndex != 0 ||
                !typeId.TryGetValue(out uint numericId))
            {
                return false;
            }

            switch (numericId)
            {
                case VariableTypes.ServerDiagnosticsSummaryType:
                case ObjectTypes.SessionDiagnosticsObjectType:
                case VariableTypes.SessionDiagnosticsVariableType:
                case VariableTypes.SessionDiagnosticsArrayType:
                case VariableTypes.SessionSecurityDiagnosticsType:
                case VariableTypes.SessionSecurityDiagnosticsArrayType:
                case VariableTypes.SubscriptionDiagnosticsType:
                case VariableTypes.SubscriptionDiagnosticsArrayType:
                case VariableTypes.SamplingIntervalDiagnosticsArrayType:
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public void ForceDiagnosticsScan()
        {
            m_forceDiagnosticsScan = true;
        }

        /// <inheritdoc/>
        public bool DiagnosticsEnabled { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Implements the ServerDiagnostics.EnabledFlag semantics of OPC UA Part 5 §6.3.3.
        /// Disabling stops the collection: the static diagnostic Variables return
        /// Bad_NotReadable and the dynamic Session and Subscription diagnostic Nodes are
        /// removed from the AddressSpace. The Server keeps track of the live Sessions and
        /// Subscriptions, so enabling the collection again restores their diagnostic Nodes
        /// with the same NodeIds. Subscriptions created while the collection is disabled
        /// get no diagnostic Nodes. The server-wide counters are cumulative and are not
        /// reset by either transition.
        /// </remarks>
        public async ValueTask SetDiagnosticsEnabledAsync(
            ServerSystemContext context,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = BeginNodeManagerOperation();

            // Transitions are serialized for their whole duration, including the removal of the
            // dynamic nodes, so that an enable cannot restore nodes that a still running disable
            // deletes afterwards. Once started, a transition is not cancelled half way.
            await m_diagnosticsTransitionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetDiagnosticsEnabledCoreAsync(context, enabled).ConfigureAwait(false);
            }
            finally
            {
                m_diagnosticsTransitionSemaphore.Release();
            }
        }

        private async ValueTask SetDiagnosticsEnabledCoreAsync(ServerSystemContext context, bool enabled)
        {
            var nodesToDelete = new List<NodeState>();

            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                if (enabled == DiagnosticsEnabled)
                {
                    return;
                }

                ServerDiagnosticsState diagnosticsNode = FindPredefinedNode<ServerDiagnosticsState>(
                    ObjectIds.Server_ServerDiagnostics);

                SessionDiagnosticsData[] sessions;
                SubscriptionDiagnosticsData[] subscriptions;
                lock (m_diagnosticsCollectionLock)
                {
                    sessions = [.. m_sessions];
                    subscriptions = [.. m_subscriptions];
                }

                if (!enabled)
                {
                    // stop scans; a scan that is already running holds the diagnostics lock, so
                    // the flag and the Bad_NotReadable states below are applied after it.
                    lock (m_diagnosticsLock)
                    {
                        DiagnosticsEnabled = false;
                        UpdateDiagnosticsScanTimer();

                        if (m_serverDiagnostics != null)
                        {
                            m_serverDiagnostics.Value = null!;
                            m_serverDiagnostics.Error = StatusCodes.BadNotReadable;
                            m_serverDiagnostics.Timestamp = DateTime.UtcNow;
                            m_serverDiagnostics.ChangesComplete(SystemContext);
                        }

                        SetDiagnosticsArrayStatus(diagnosticsNode, StatusCodes.BadNotReadable);
                    }

                    // remove the dynamic nodes but keep the registrations so that the
                    // nodes can be restored when the collection is enabled again.
                    SessionsDiagnosticsSummaryState? sessionsSummary = FindPredefinedNode<SessionsDiagnosticsSummaryState>(
                        ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary);
                    for (int ii = 0; ii < sessions.Length; ii++)
                    {
                        SessionDiagnosticsObjectState sessionNode = sessions[ii].Summary;
                        sessionsSummary?.RemoveReference(ReferenceTypeIds.HasComponent, false, sessionNode.NodeId);
                        nodesToDelete.Add(sessionNode);
                    }

                    SubscriptionDiagnosticsArrayState? subscriptionArray = diagnosticsNode?.SubscriptionDiagnosticsArray;
                    for (int ii = 0; ii < subscriptions.Length; ii++)
                    {
                        SubscriptionDiagnosticsState subscriptionNode = subscriptions[ii].Value.Variable;
                        subscriptionArray?.RemoveReference(ReferenceTypeIds.HasComponent, false, subscriptionNode.NodeId);
                        nodesToDelete.Add(subscriptionNode);
                    }
                }
                else
                {
                    // restore the dynamic nodes of the sessions and subscriptions that are
                    // still alive before scans resume. Sessions first, the subscriptions link to them.
                    for (int ii = 0; ii < sessions.Length; ii++)
                    {
                        SessionDiagnosticsData session = await RestoreSessionDiagnosticsAsync(
                            sessions[ii],
                            CancellationToken.None).ConfigureAwait(false);
                        lock (m_diagnosticsCollectionLock)
                        {
                            m_sessions[ii] = session;
                        }
                    }

                    for (int ii = 0; ii < subscriptions.Length; ii++)
                    {
                        SubscriptionDiagnosticsData subscription = await RestoreSubscriptionDiagnosticsAsync(
                            subscriptions[ii],
                            CancellationToken.None).ConfigureAwait(false);
                        lock (m_diagnosticsCollectionLock)
                        {
                            m_subscriptions[ii] = subscription;
                        }
                    }

                    lock (m_diagnosticsLock)
                    {
                        DiagnosticsEnabled = true;

                        // reset all diagnostics nodes.
                        if (m_serverDiagnostics != null)
                        {
                            m_serverDiagnostics.Value = null!;
                            m_serverDiagnostics.Error = StatusCodes.BadWaitingForInitialData;
                            m_serverDiagnostics.Timestamp = DateTime.UtcNow;
                        }

                        SetDiagnosticsArrayStatus(diagnosticsNode, StatusCodes.Good);
                        DoScan(true);
                        UpdateDiagnosticsScanTimer();
                    }
                }
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            // deleted outside the address space lock like DeleteSessionDiagnosticsAsync does,
            // but still inside the transition.
            for (int ii = 0; ii < nodesToDelete.Count; ii++)
            {
                await DeleteNodeAsync(context, nodesToDelete[ii].NodeId, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sets the status of the static diagnostic arrays and clears their values.
        /// </summary>
        /// <remarks>
        /// The session and subscription arrays are refreshed by the next scan. The
        /// SamplingIntervalDiagnosticsArray is not collected, so it reports an empty array.
        /// </remarks>
        private void SetDiagnosticsArrayStatus(ServerDiagnosticsState? diagnosticsNode, StatusCode statusCode)
        {
            if (diagnosticsNode == null)
            {
                return;
            }

            bool good = StatusCode.IsGood(statusCode);
            DateTimeUtc now = DateTimeUtc.Now;

            if (diagnosticsNode.SamplingIntervalDiagnosticsArray is { } samplingIntervals)
            {
                samplingIntervals.Value = good ? [] : default;
                samplingIntervals.StatusCode = statusCode;
                samplingIntervals.Timestamp = now;
                samplingIntervals.ClearChangeMasks(SystemContext, false);
            }

            if (diagnosticsNode.SubscriptionDiagnosticsArray is { } subscriptions)
            {
                subscriptions.Value = good ? [] : default;
                subscriptions.StatusCode = statusCode;
                subscriptions.Timestamp = now;
                subscriptions.ClearChangeMasks(SystemContext, false);
            }

            if (diagnosticsNode.SessionsDiagnosticsSummary?.SessionDiagnosticsArray is { } sessions)
            {
                sessions.Value = good ? [] : default;
                sessions.StatusCode = statusCode;
                sessions.Timestamp = now;
                sessions.ClearChangeMasks(SystemContext, false);
            }

            if (diagnosticsNode.SessionsDiagnosticsSummary?.SessionSecurityDiagnosticsArray is { } sessionSecurity)
            {
                sessionSecurity.Value = good ? [] : default;
                sessionSecurity.StatusCode = statusCode;
                sessionSecurity.Timestamp = now;
                sessionSecurity.ClearChangeMasks(SystemContext, false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CreateServerDiagnosticsAsync(
            ServerSystemContext systemContext,
            ServerDiagnosticsSummaryDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            CancellationToken cancellationToken)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                // get the node.
                ServerDiagnosticsSummaryState diagnosticsNode = FindPredefinedNode<ServerDiagnosticsSummaryState>(
                    VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary);

                // wrap diagnostics in a thread safe object.
                var diagnosticsValue = new ServerDiagnosticsSummaryValue(
                    diagnosticsNode,
                    diagnostics,
                    m_diagnosticsLock)
                {
                    // must ensure the first update gets sent.
                    Value = null!,
                    Error = StatusCodes.BadWaitingForInitialData,
                    CopyPolicy = VariableCopyPolicy.Never,
                    OnBeforeRead = OnBeforeReadDiagnostics
                };
                // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                diagnosticsNode.OnReadUserRolePermissions = OnReadUserRolePermissions;

                m_serverDiagnostics = diagnosticsValue;
                m_serverDiagnosticsCallback = updateCallback;

                // set up handler for session diagnostics array.
                SessionDiagnosticsArrayState array1 = FindPredefinedNode<SessionDiagnosticsArrayState>(
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray);

                if (array1 != null)
                {
                    array1.OnSimpleReadValue = OnReadDiagnosticsArray;
                    // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                    array1.OnReadUserRolePermissions = OnReadUserRolePermissions;
                }

                // set up handler for session security diagnostics array.
                SessionSecurityDiagnosticsArrayState array2 = FindPredefinedNode<SessionSecurityDiagnosticsArrayState>(
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray);

                if (array2 != null)
                {
                    array2.OnSimpleReadValue = OnReadDiagnosticsArray;
                    // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                    array2.OnReadUserRolePermissions = OnReadUserRolePermissions;
                }

                // set up handler for subscription security diagnostics array.
                SubscriptionDiagnosticsArrayState array3 = FindPredefinedNode<SubscriptionDiagnosticsArrayState>(
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

                if (array3 != null)
                {
                    array3.OnSimpleReadValue = OnReadDiagnosticsArray;
                    // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                    array3.OnReadUserRolePermissions = OnReadUserRolePermissions;
                }

                // send initial update.
                DoScan(true);
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> CreateSessionDiagnosticsAsync(
            ServerSystemContext systemContext,
            SessionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            SessionSecurityDiagnosticsDataType securityDiagnostics,
            NodeValueSimpleEventHandler updateSecurityCallback,
            CancellationToken cancellationToken = default)
        {
            NodeId nodeId = default;

            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            SessionDiagnosticsObjectState? tempSessionNode = null;
            try
            {
                tempSessionNode = new SessionDiagnosticsObjectState(null);
                SessionDiagnosticsObjectState sessionNode = tempSessionNode;
                QualifiedName browseName = QualifiedName.From(diagnostics.SessionName!);

                if (DiagnosticsEnabled)
                {
                    // create a new instance and assign ids.
                    nodeId = await CreateNodeAsync(
                        SystemContext,
                        default,
                        ReferenceTypeIds.HasComponent,
                        browseName,
                        sessionNode,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // the session nodes are not part of the address space while the
                    // collection is disabled; assign the ids so that the node can be
                    // added when the collection is enabled.
                    sessionNode.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                    sessionNode.Create(SystemContext, default, browseName, default, true);
                    SystemContext.AssignInstanceNodeId(sessionNode);
                    nodeId = sessionNode.NodeId;
                }
                tempSessionNode = null; // ownership transferred to the session registration

                diagnostics.SessionId = nodeId;
                securityDiagnostics.SessionId = nodeId;

                SessionDiagnosticsData sessionData = CreateSessionDiagnosticsData(
                    sessionNode,
                    diagnostics,
                    updateCallback,
                    securityDiagnostics,
                    updateSecurityCallback);

                lock (m_diagnosticsCollectionLock)
                {
                    m_sessions.Add(sessionData);
                }

                if (!DiagnosticsEnabled)
                {
                    return nodeId;
                }

                LinkSessionDiagnostics(sessionNode);

                // Mark the diagnostics arrays dirty instead of rebuilding them
                // synchronously here. Rebuilding on every CreateSession is O(N) in
                // the session count, so establishing N sessions is O(N^2) - a
                // dominant connect-time cost at scale. The read paths
                // (OnBeforeReadDiagnostics / OnReadDiagnosticsArray) and the
                // periodic scan timer honor this flag and run a single fresh scan
                // when the arrays are actually read or monitored, so correctness
                // is preserved without the per-create quadratic work.
                m_forceDiagnosticsScan = true;
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            return nodeId;
        }

        /// <summary>
        /// Wraps the diagnostics of a session in the thread safe values of its node.
        /// </summary>
        private SessionDiagnosticsData CreateSessionDiagnosticsData(
            SessionDiagnosticsObjectState sessionNode,
            SessionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            SessionSecurityDiagnosticsDataType securityDiagnostics,
            NodeValueSimpleEventHandler updateSecurityCallback)
        {
            // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
            sessionNode.OnReadUserRolePermissions = OnReadUserRolePermissions;

            // initialize diagnostics node.
            var diagnosticsNode =
                sessionNode.CreateChild(SystemContext, QualifiedName.From(BrowseNames.SessionDiagnostics)) as
                SessionDiagnosticsVariableState;

            // wrap diagnostics in a thread safe object.
            var diagnosticsValue = new SessionDiagnosticsVariableValue(
                diagnosticsNode!,
                diagnostics,
                m_diagnosticsLock)
            {
                // must ensure the first update gets sent.
                Value = null!,
                Error = StatusCodes.BadWaitingForInitialData,
                CopyPolicy = VariableCopyPolicy.Never,
                OnBeforeRead = OnBeforeReadDiagnostics
            };
            NodeId ownerSessionId = diagnostics.SessionId;
            diagnosticsNode!.OnReadUserRolePermissions =
                (ISystemContext context, NodeState node, ref ArrayOf<RolePermissionType> value) =>
                    OnReadUserRolePermissions(context, node, ownerSessionId, ref value);

            // initialize security diagnostics node.
            var securityDiagnosticsNode =
                sessionNode.CreateChild(
                    SystemContext,
                    QualifiedName.From(BrowseNames.SessionSecurityDiagnostics)) as
                SessionSecurityDiagnosticsState;

            // wrap diagnostics in a thread safe object.
            var securityDiagnosticsValue = new SessionSecurityDiagnosticsValue(
                securityDiagnosticsNode!,
                securityDiagnostics,
                m_diagnosticsLock)
            {
                // must ensure the first update gets sent.
                Value = null!,
                Error = StatusCodes.BadWaitingForInitialData,
                CopyPolicy = VariableCopyPolicy.Never,
                OnBeforeRead = OnBeforeReadDiagnostics
            };
            securityDiagnosticsNode!.OnReadUserRolePermissions =
                (ISystemContext context, NodeState node, ref ArrayOf<RolePermissionType> value) =>
                    OnReadUserRolePermissions(context, node, ownerSessionId, ref value);

            return new SessionDiagnosticsData(
                sessionNode,
                diagnosticsValue,
                updateCallback,
                securityDiagnosticsValue,
                updateSecurityCallback,
                diagnostics,
                securityDiagnostics);
        }

        /// <summary>
        /// Adds the references between a session node and the SessionsDiagnosticsSummary.
        /// </summary>
        private void LinkSessionDiagnostics(SessionDiagnosticsObjectState sessionNode)
        {
            // add reference to session summary object.
            sessionNode.AddReference(
                ReferenceTypeIds.HasComponent,
                true,
                ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary);

            // add reference from session summary object.
            SessionsDiagnosticsSummaryState summary = FindPredefinedNode<SessionsDiagnosticsSummaryState>(
                ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary);

            summary?.AddReference(ReferenceTypeIds.HasComponent, false, sessionNode.NodeId);
        }

        /// <summary>
        /// Adds the node of a registered session to the address space again after the
        /// diagnostics collection was enabled.
        /// </summary>
        /// <remarks>
        /// Disabling the collection deletes the node and its children, so a new node is
        /// created with the NodeId the session already uses as its identifier.
        /// </remarks>
        private async ValueTask<SessionDiagnosticsData> RestoreSessionDiagnosticsAsync(
            SessionDiagnosticsData sessionData,
            CancellationToken cancellationToken)
        {
            NodeId nodeId = sessionData.Summary.NodeId;
            var sessionNode = new SessionDiagnosticsObjectState(null)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            sessionNode.Create(SystemContext, default, sessionData.Summary.BrowseName, default, true);
            sessionNode.NodeId = nodeId;

            await AddPredefinedNodeAsync(SystemContext, sessionNode, cancellationToken).ConfigureAwait(false);
            LinkSessionDiagnostics(sessionNode);

            return CreateSessionDiagnosticsData(
                sessionNode,
                sessionData.Diagnostics,
                sessionData.UpdateCallback,
                sessionData.SecurityDiagnostics,
                sessionData.SecurityUpdateCallback);
        }

        /// <inheritdoc/>
        public async ValueTask DeleteSessionDiagnosticsAsync(
            ServerSystemContext systemContext,
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                lock (m_diagnosticsCollectionLock)
                {
                    for (int ii = 0; ii < m_sessions.Count; ii++)
                    {
                        SessionDiagnosticsObjectState summary = m_sessions[ii].Summary;
                        if (summary.NodeId == nodeId)
                        {
                            m_sessions.RemoveAt(ii);
                            m_forceDiagnosticsScan = true;
                            break;
                        }
                    }
                }

                // release the server lock if it is being held.
                if (m_serverLockHolder == nodeId)
                {
                    m_serverLockHolder = default;
                }
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            await DeleteNodeAsync(SystemContext, nodeId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<NodeId> CreateSubscriptionDiagnosticsAsync(
            ServerSystemContext systemContext,
            SubscriptionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            CancellationToken cancellationToken = default)
        {
            NodeId nodeId = default;

            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            SubscriptionDiagnosticsState? tempDiagnosticsNode = null;
            try
            {
                // check if diagnostics have been enabled.
                if (!DiagnosticsEnabled)
                {
                    return default;
                }

                tempDiagnosticsNode = new SubscriptionDiagnosticsState(null);
                SubscriptionDiagnosticsState diagnosticsNode = tempDiagnosticsNode;

                // create a new instance and assign ids.
                nodeId = await CreateNodeAsync(
                    SystemContext,
                    default,
                    ReferenceTypeIds.HasComponent,
                    QualifiedName.From(
                        diagnostics.SubscriptionId.ToString(CultureInfo.InvariantCulture)),
                    diagnosticsNode,
                    cancellationToken).ConfigureAwait(false);
                tempDiagnosticsNode = null; // ownership transferred to address space

                SubscriptionDiagnosticsData subscriptionData = CreateSubscriptionDiagnosticsData(
                    diagnosticsNode, diagnostics, updateCallback);
                lock (m_diagnosticsCollectionLock)
                {
                    m_subscriptions.Add(subscriptionData);
                }

                LinkSubscriptionDiagnostics(diagnosticsNode, diagnostics);

                // Mark the diagnostics arrays dirty rather than rebuilding them
                // synchronously on every CreateSubscription (O(N) per call, hence
                // O(N^2) over N subscriptions). The read paths and periodic scan
                // timer run a single fresh scan on demand, honoring this flag.
                m_forceDiagnosticsScan = true;
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            return nodeId;
        }

        /// <summary>
        /// Wraps the diagnostics of a subscription in the thread safe value of its node.
        /// </summary>
        private SubscriptionDiagnosticsData CreateSubscriptionDiagnosticsData(
            SubscriptionDiagnosticsState diagnosticsNode,
            SubscriptionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback)
        {
            // wrap diagnostics in a thread safe object.
            var diagnosticsValue = new SubscriptionDiagnosticsValue(
                diagnosticsNode,
                diagnostics,
                m_diagnosticsLock)
            {
                CopyPolicy = VariableCopyPolicy.Never,
                OnBeforeRead = OnBeforeReadDiagnostics,

                // must ensure the first update gets sent.
                Value = null!,
                Error = StatusCodes.BadWaitingForInitialData
            };
            NodeId ownerSessionId = diagnostics.SessionId;
            diagnosticsNode.OnReadUserRolePermissions =
                (ISystemContext context, NodeState node, ref ArrayOf<RolePermissionType> value) =>
                    OnReadUserRolePermissions(context, node, ownerSessionId, ref value);

            return new SubscriptionDiagnosticsData(diagnosticsValue, updateCallback, diagnostics);
        }

        /// <summary>
        /// Adds the references between a subscription node, the SubscriptionDiagnosticsArray
        /// and the node of the session that owns the subscription.
        /// </summary>
        private void LinkSubscriptionDiagnostics(
            SubscriptionDiagnosticsState diagnosticsNode,
            SubscriptionDiagnosticsDataType diagnostics)
        {
            // add reference to subscription array.
            diagnosticsNode.AddReference(
                ReferenceTypeIds.HasComponent,
                true,
                VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

            // add reference from subscription array.
            SubscriptionDiagnosticsArrayState? array = FindPredefinedNode<SubscriptionDiagnosticsArrayState>(
                VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

            array?.AddReference(ReferenceTypeIds.HasComponent, false, diagnosticsNode.NodeId);

            if (!diagnostics.SessionId.IsNull)
            {
                // add reference to session subscription array.
                diagnosticsNode.AddReference(
                    ReferenceTypeIds.HasComponent,
                    true,
                    diagnostics.SessionId);
            }

            // add reference from session subscription array.
            SessionDiagnosticsObjectState sessionNode = FindPredefinedNode<SessionDiagnosticsObjectState>(
                diagnostics.SessionId);

            if (sessionNode != null)
            {
                // add reference from subscription array.
                array = (SubscriptionDiagnosticsArrayState?)
                    sessionNode.CreateChild(
                        SystemContext,
                        QualifiedName.From(BrowseNames.SubscriptionDiagnosticsArray))!;

                array?.AddReference(
                    ReferenceTypeIds.HasComponent,
                    false,
                    diagnosticsNode.NodeId);
            }
        }

        /// <summary>
        /// Adds the node of a registered subscription to the address space again after
        /// the diagnostics collection was enabled.
        /// </summary>
        private async ValueTask<SubscriptionDiagnosticsData> RestoreSubscriptionDiagnosticsAsync(
            SubscriptionDiagnosticsData subscriptionData,
            CancellationToken cancellationToken)
        {
            SubscriptionDiagnosticsState previousNode = subscriptionData.Value.Variable;
            var diagnosticsNode = new SubscriptionDiagnosticsState(null)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            diagnosticsNode.Create(SystemContext, default, previousNode.BrowseName, default, true);
            diagnosticsNode.NodeId = previousNode.NodeId;

            await AddPredefinedNodeAsync(SystemContext, diagnosticsNode, cancellationToken).ConfigureAwait(false);
            LinkSubscriptionDiagnostics(diagnosticsNode, subscriptionData.Diagnostics);

            return CreateSubscriptionDiagnosticsData(
                diagnosticsNode,
                subscriptionData.Diagnostics,
                subscriptionData.UpdateCallback);
        }

        /// <inheritdoc/>
        public async ValueTask DeleteSubscriptionDiagnosticsAsync(
            ServerSystemContext systemContext,
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                lock (m_diagnosticsCollectionLock)
                {
                    for (int ii = 0; ii < m_subscriptions.Count; ii++)
                    {
                        SubscriptionDiagnosticsData diagnostics = m_subscriptions[ii];
                        if (diagnostics.Value.Variable.NodeId == nodeId)
                        {
                            m_subscriptions.RemoveAt(ii);
                            m_forceDiagnosticsScan = true;
                            break;
                        }
                    }
                }
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            await DeleteNodeAsync(SystemContext, nodeId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<HistoryServerCapabilitiesState> GetDefaultHistoryCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                // search the Node in PredefinedNodes.
                HistoryServerCapabilitiesState historyServerCapabilitiesNode
                    = m_historyCapabilities ??
                        FindPredefinedNode<HistoryServerCapabilitiesState>(
                            ObjectIds.HistoryServerCapabilities);

                if (historyServerCapabilitiesNode == null)
                {
                    // create new node if not found.
                    historyServerCapabilitiesNode = new HistoryServerCapabilitiesState(null);

                    _ = await CreateNodeAsync(
                        SystemContext,
                        default,
                        ReferenceTypeIds.HasComponent,
                        QualifiedName.From(BrowseNames.HistoryServerCapabilities),
                        historyServerCapabilitiesNode,
                        cancellationToken).ConfigureAwait(false);

                    historyServerCapabilitiesNode.MaxReturnDataValues!.Value = 0;
                    historyServerCapabilitiesNode.MaxReturnEventValues!.Value = 0;

                    ServerCapabilitiesState parent = FindPredefinedNode<ServerCapabilitiesState>(
                        ObjectIds.Server_ServerCapabilities);

                    if (parent != null)
                    {
                        parent.AddReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            historyServerCapabilitiesNode.NodeId);
                        historyServerCapabilitiesNode.AddReference(
                            ReferenceTypeIds.HasComponent,
                            true,
                            parent.NodeId);
                    }

                    await AddPredefinedNodeAsync(SystemContext, historyServerCapabilitiesNode, cancellationToken).ConfigureAwait(false);
                }

                // Overlay the registered-historian rollup onto the
                // capabilities node so the values reflect what the
                // installed providers actually advertise. Runs whether
                // the node was found in the predefined nodeset or
                // freshly created above.
                Historian.HistorianNodeCapabilities? rolled = await RollUpHistorianCapabilitiesAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (rolled != null)
                {
                    historyServerCapabilitiesNode.AccessHistoryDataCapability!.Value =
                        rolled.ReadRawData ||
                        rolled.ReadModifiedData ||
                        rolled.ReadAtTime ||
                        rolled.ReadProcessedData ||
                        rolled.ReadStructuredData ||
                        rolled.ReadModifiedStructuredData ||
                        rolled.ReadAtTimeStructuredData;
                    historyServerCapabilitiesNode.AccessHistoryEventsCapability!.Value =
                        rolled.ReadEventHistory;
                    historyServerCapabilitiesNode.MaxReturnDataValues!.Value =
                        rolled.MaxReturnDataValues;
                    historyServerCapabilitiesNode.MaxReturnEventValues!.Value =
                        rolled.MaxReturnEventValues;
                    historyServerCapabilitiesNode.ReplaceDataCapability!.Value = rolled.ReplaceData;
                    historyServerCapabilitiesNode.UpdateDataCapability!.Value = rolled.UpdateData;
                    historyServerCapabilitiesNode.InsertAnnotationCapability!.Value = rolled.InsertAnnotation;
                    historyServerCapabilitiesNode.InsertDataCapability!.Value = rolled.InsertData;
                    historyServerCapabilitiesNode.DeleteRawCapability!.Value = rolled.DeleteRaw;
                    historyServerCapabilitiesNode.DeleteAtTimeCapability!.Value = rolled.DeleteAtTime;
                    historyServerCapabilitiesNode.InsertEventCapability!.Value = rolled.InsertEvent;
                    historyServerCapabilitiesNode.ReplaceEventCapability!.Value = rolled.ReplaceEvent;
                    historyServerCapabilitiesNode.UpdateEventCapability!.Value = rolled.UpdateEvent;
                    historyServerCapabilitiesNode.DeleteEventCapability!.Value = rolled.DeleteEvent;
                    historyServerCapabilitiesNode.ServerTimestampSupported!.Value = rolled.ServerTimestampSupported;
                }

                m_historyCapabilities = historyServerCapabilitiesNode;
                return m_historyCapabilities;
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask UpdateServerEventNotifierAsync(CancellationToken cancellationToken = default)
        {
            // Refresh the server-wide capability object first.
            _ = await GetDefaultHistoryCapabilitiesAsync(cancellationToken)
                .ConfigureAwait(false);
            Historian.HistorianNodeCapabilities? serverHistory = null;
            if (Server is Historian.IHistorianRegistryProvider registry)
            {
                Historian.IHistorianProvider? provider =
                    registry.HistorianRegistry.Resolve(ObjectIds.Server);
                if (provider != null &&
                    await provider.IsHistorizingAsync(
                        ObjectIds.Server,
                        cancellationToken).ConfigureAwait(false))
                {
                    serverHistory = await provider.GetCapabilitiesAsync(
                        ObjectIds.Server,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                // Find the Server object
                ServerObjectState serverObject = FindPredefinedNode<ServerObjectState>(
                    ObjectIds.Server);

                if (serverObject != null)
                {
                    // Update EventNotifier based on history capabilities
                    byte eventNotifier = serverObject.EventNotifier;

                    // EventNotifier history bits describe historical events,
                    // not historical variable data.
                    if (serverHistory?.ReadEventHistory == true)
                    {
                        eventNotifier |= EventNotifiers.HistoryRead;
                    }
                    else
                    {
                        eventNotifier = (byte)(eventNotifier & ~EventNotifiers.HistoryRead);
                    }

                    // Set HistoryWrite bit if history update capabilities are enabled
                    if (serverHistory?.SupportsAnyEventUpdate == true)
                    {
                        eventNotifier |= EventNotifiers.HistoryWrite;
                    }
                    else
                    {
                        eventNotifier = (byte)(eventNotifier & ~EventNotifiers.HistoryWrite);
                    }

                    serverObject.EventNotifier = eventNotifier;
                }
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask AddAggregateFunctionAsync(
            NodeId aggregateId,
            string aggregateName,
            bool isHistorical,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var state = new FolderState(null)
                {
                    SymbolicName = aggregateName,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = ObjectTypeIds.AggregateFunctionType,
                    NodeId = aggregateId,
                    BrowseName = new QualifiedName(aggregateName, aggregateId.NamespaceIndex)
                };
                state.DisplayName = LocalizedText.From(state.BrowseName.Name!);
                state.WriteMask = AttributeWriteMask.None;
                state.UserWriteMask = AttributeWriteMask.None;
                state.EventNotifier = EventNotifiers.None;

                NodeState folder = FindPredefinedNode<BaseObjectState>(
                    ObjectIds.Server_ServerCapabilities_AggregateFunctions);

                if (folder != null)
                {
                    folder.AddReference(ReferenceTypeIds.Organizes, false, state.NodeId);
                    state.AddReference(ReferenceTypeIds.Organizes, true, folder.NodeId);
                }

                if (isHistorical)
                {
                    folder = FindPredefinedNode<BaseObjectState>(
                        ObjectIds.HistoryServerCapabilities_AggregateFunctions);

                    if (folder != null)
                    {
                        folder.AddReference(ReferenceTypeIds.Organizes, false, state.NodeId);
                        state.AddReference(ReferenceTypeIds.Organizes, true, folder.NodeId);
                    }
                }

                await AddPredefinedNodeAsync(SystemContext, state, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask AddModellingRuleAsync(
            NodeId modellingRuleId,
            string modellingRuleName,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var state = new FolderState(null)
                {
                    SymbolicName = modellingRuleName,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = ObjectTypeIds.ModellingRuleType,
                    NodeId = modellingRuleId,
                    BrowseName = new QualifiedName(modellingRuleName, modellingRuleId.NamespaceIndex)
                };
                state.DisplayName = LocalizedText.From(state.BrowseName.Name!);
                state.WriteMask = AttributeWriteMask.None;
                state.UserWriteMask = AttributeWriteMask.None;
                state.EventNotifier = EventNotifiers.None;

                NodeState folder = FindPredefinedNode<BaseObjectState>(
                    ObjectIds.Server_ServerCapabilities_ModellingRules);

                if (folder != null)
                {
                    folder.AddReference(ReferenceTypeIds.Organizes, false, state.NodeId);
                    state.AddReference(ReferenceTypeIds.Organizes, true, folder.NodeId);
                }

                await AddPredefinedNodeAsync(SystemContext, state, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask PublishConformanceUnitsAsync(
            ArrayOf<QualifiedName> conformanceUnits,
            ArrayOf<string> serverProfiles,
            CancellationToken cancellationToken = default)
        {
            using NodeManagerOperation nodeOperation = await EnterDiagnosticsOperationAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                BaseVariableState? conformanceUnitsNode = FindPredefinedNode<BaseVariableState>(
                    VariableIds.Server_ServerCapabilities_ConformanceUnits);

                if (conformanceUnitsNode != null)
                {
                    conformanceUnitsNode.Value = Variant.From(conformanceUnits);
                    conformanceUnitsNode.ClearChangeMasks(SystemContext, false);
                }

                if (serverProfiles.Count == 0)
                {
                    return;
                }

                BaseVariableState? profileArrayNode = FindPredefinedNode<BaseVariableState>(
                    VariableIds.Server_ServerCapabilities_ServerProfileArray);

                if (profileArrayNode == null)
                {
                    return;
                }

                // Preserve profiles already declared (e.g. from configuration) and
                // append the contributed ones that are not already present.
                var merged = new List<string>();
                if (profileArrayNode.Value.TryGetValue(out ArrayOf<string> existing))
                {
                    foreach (string profile in existing)
                    {
                        if (!string.IsNullOrEmpty(profile))
                        {
                            merged.Add(profile);
                        }
                    }
                }
                foreach (string profile in serverProfiles)
                {
                    if (!string.IsNullOrEmpty(profile) && !merged.Contains(profile))
                    {
                        merged.Add(profile);
                    }
                }

                profileArrayNode.Value = Variant.From(merged.ToArrayOf());
                profileArrayNode.ClearChangeMasks(SystemContext, false);
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Updates the server diagnostics summary structure.
        /// </summary>
        private bool UpdateServerDiagnosticsSummary()
        {
            // get the latest snapshot.
            Variant value = default;

            ServiceResult result = m_serverDiagnosticsCallback!(
                SystemContext,
                m_serverDiagnostics!.Variable,
                ref value);

            ServerDiagnosticsSummaryDataType newValue = value.GetStructure<ServerDiagnosticsSummaryDataType>();

            // check for changes.
            if (Utils.IsEqual(newValue, m_serverDiagnostics.Value))
            {
                return false;
            }

            m_serverDiagnostics.Error = null;

            // check for bad value.
            if (ServiceResult.IsNotBad(result) && newValue == null)
            {
                result = StatusCodes.BadOutOfService;
            }

            // check for bad result.
            if (ServiceResult.IsBad(result))
            {
                m_serverDiagnostics.Error = result;
                newValue = null!;
            }

            // update the value.
            m_serverDiagnostics.Value = newValue!;
            m_serverDiagnostics.Timestamp = DateTime.UtcNow;

            // notify any monitored items.
            m_serverDiagnostics.ChangesComplete(SystemContext);

            return true;
        }

        /// <summary>
        /// Updates the session diagnostics summary structure.
        /// </summary>
        private bool UpdateSessionDiagnostics(
            ISystemContext context,
            SessionDiagnosticsData diagnostics,
            SessionDiagnosticsDataType[] sessionArray,
            int index)
        {
            // get the latest snapshot.
            Variant value = default;

            ServiceResult result = diagnostics.UpdateCallback(
                SystemContext,
                diagnostics.Value.Variable,
                ref value);

            SessionDiagnosticsDataType newValue = value.GetStructure<SessionDiagnosticsDataType>();

            sessionArray[index] = newValue;

            if ((context != null) && (sessionArray?[index] != null))
            {
                FilterOutUnAuthorized(sessionArray, newValue.SessionId, context, index);
            }

            // check for changes.
            if (Utils.IsEqual(newValue, diagnostics.Value.Value))
            {
                return false;
            }

            diagnostics.Value.Error = null;

            // check for bad value.
            if (ServiceResult.IsNotBad(result) && newValue == null)
            {
                result = StatusCodes.BadOutOfService;
            }

            // check for bad result.
            if (ServiceResult.IsBad(result))
            {
                diagnostics.Value.Error = result;
                newValue = null!;
            }

            // update the value.
            diagnostics.Value.Value = newValue!;
            diagnostics.Value.Timestamp = DateTime.UtcNow;

            // notify any monitored items.
            diagnostics.Value.ChangesComplete(SystemContext);

            return true;
        }

        /// <summary>
        /// Updates the session diagnostics summary structure.
        /// </summary>
        private bool UpdateSessionSecurityDiagnostics(
            ISystemContext context,
            SessionDiagnosticsData diagnostics,
            SessionSecurityDiagnosticsDataType[] sessionArray,
            int index)
        {
            // get the latest snapshot.
            Variant value = default;

            ServiceResult result = diagnostics.SecurityUpdateCallback(
                SystemContext,
                diagnostics.SecurityValue.Variable,
                ref value);

            SessionSecurityDiagnosticsDataType newValue = value.GetStructure<SessionSecurityDiagnosticsDataType>();

            sessionArray[index] = newValue;

            if ((context != null) && (sessionArray?[index] != null))
            {
                FilterOutUnAuthorized(sessionArray, newValue.SessionId, context, index);
            }

            // check for changes.
            if (Utils.IsEqual(newValue, diagnostics.SecurityValue.Value))
            {
                return false;
            }

            diagnostics.SecurityValue.Error = null;

            // check for bad value.
            if (ServiceResult.IsNotBad(result) && newValue == null)
            {
                result = StatusCodes.BadOutOfService;
            }

            // check for bad result.
            if (ServiceResult.IsBad(result))
            {
                diagnostics.SecurityValue.Error = result;
                newValue = null!;
            }

            // update the value.
            diagnostics.SecurityValue.Value = newValue!;
            diagnostics.SecurityValue.Timestamp = DateTime.UtcNow;

            // notify any monitored items.
            diagnostics.SecurityValue.ChangesComplete(SystemContext);

            return true;
        }

        /// <summary>
        /// Updates the subscription diagnostics summary structure.
        /// </summary>
        private bool UpdateSubscriptionDiagnostics(
            ISystemContext context,
            SubscriptionDiagnosticsData diagnostics,
            SubscriptionDiagnosticsDataType[] subscriptionArray,
            int index)
        {
            // get the latest snapshot.
            Variant value = default;

            ServiceResult result = diagnostics.UpdateCallback(
                SystemContext,
                diagnostics.Value.Variable,
                ref value);

            SubscriptionDiagnosticsDataType newValue = value.GetStructure<SubscriptionDiagnosticsDataType>();

            subscriptionArray[index] = newValue;

            if ((context != null) && (subscriptionArray?[index] != null))
            {
                FilterOutUnAuthorized(subscriptionArray, newValue.SessionId, context, index);
            }

            // check for changes.
            if (Utils.IsEqual(newValue, diagnostics.Value.Value))
            {
                return false;
            }

            diagnostics.Value.Error = null;

            // check for bad value.
            if (ServiceResult.IsNotBad(result) && newValue == null)
            {
                result = StatusCodes.BadOutOfService;
            }

            // check for bad result.
            if (ServiceResult.IsBad(result))
            {
                diagnostics.Value.Error = result;
                newValue = null!;
            }

            // update the value.
            diagnostics.Value.Value = newValue!;
            diagnostics.Value.Timestamp = DateTime.UtcNow;

            // notify any monitored items.
            diagnostics.Value.ChangesComplete(SystemContext);

            return true;
        }

        /// <summary>
        /// Filter out the members which correspond to users that are not allowed to see their contents
        /// Current user is allowed to read its data, together with users which have permissions
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static void FilterOutUnAuthorized<T>(
            IList<T> list,
            NodeId sessionId,
            ISystemContext context,
            int index)
        {
            NodeId curSession = (context as ISessionSystemContext)?.SessionId ?? default;
            if ((sessionId != curSession) &&
                !HasApplicationSecureAdminAccess(context))
            {
                list[index] = default!;
            }
        }

        /// <summary>
        /// Set custom role permissions for desired node
        /// </summary>
        protected ServiceResult OnReadUserRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            return OnReadUserRolePermissions(context, node, node.NodeId, ref value);
        }

        private ServiceResult OnReadUserRolePermissions(
            ISystemContext context,
            NodeState node,
            NodeId ownerSessionId,
            ref ArrayOf<RolePermissionType> value)
        {
            bool adminUser;

            if ((node.NodeId == VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary) ||
                (node.NodeId == VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray))
            {
                adminUser = HasApplicationSecureAdminAccess(context);
            }
            else
            {
                // allow Session to see own session diagnostics
                NodeId curSession = (context as ISessionSystemContext)?.SessionId ?? default;
                adminUser = ownerSessionId == curSession || node.NodeId == curSession ||
                    HasApplicationSecureAdminAccess(context);
            }

            if (adminUser)
            {
                IEnumerable<RolePermissionType> rolePermissionTypes =
                    from roleId in s_kWellKnownRoles
                    select new RolePermissionType
                    {
                        RoleId = roleId,
                        Permissions = (uint)(
                            PermissionType.Browse |
                            PermissionType.Read |
                            PermissionType.ReadRolePermissions)
                    };

                value = [.. rolePermissionTypes];
            }
            else
            {
                IEnumerable<RolePermissionType> rolePermissionTypes =
                    from roleId in s_kWellKnownRoles
                    select new RolePermissionType
                    {
                        RoleId = roleId,
                        Permissions = (uint)PermissionType.None
                    };

                value = [.. rolePermissionTypes];
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Does a scan before the diagnostics are read.
        /// </summary>
        protected void OnBeforeReadDiagnostics(
            ISystemContext context,
            BaseVariableValue variable,
            NodeState component)
        {
            lock (m_diagnosticsLock)
            {
                if (!DiagnosticsEnabled)
                {
                    return;
                }

                if (!m_forceDiagnosticsScan &&
                    m_timeProvider.GetElapsedTime(m_lastDiagnosticsScanTimestamp) <
                        TimeSpan.FromSeconds(1))
                {
                    return;
                }

                DoScan(true);
            }
        }

        /// <summary>
        /// Does a scan before the diagnostics are read.
        /// </summary>
        protected ServiceResult OnReadDiagnosticsArray(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            lock (m_diagnosticsLock)
            {
                if (!DiagnosticsEnabled)
                {
                    // Part 5 §6.3.3: static diagnostic nodes are not readable while the
                    // collection is disabled.
                    return StatusCodes.BadNotReadable;
                }

                if (!m_forceDiagnosticsScan &&
                    m_timeProvider.GetElapsedTime(m_lastDiagnosticsScanTimestamp) <
                        TimeSpan.FromSeconds(1))
                {
                    // diagnostic nodes already scanned.
                    return ServiceResult.Good;
                }

                if (node.NodeId ==
                    VariableIds
                        .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray)
                {
                    // read session diagnostics.
                    SessionDiagnosticsData[] sessions;
                    lock (m_diagnosticsCollectionLock)
                    {
                        sessions = [.. m_sessions];
                    }
                    var sessionArray = new SessionDiagnosticsDataType[sessions.Length];

                    for (int ii = 0; ii < sessions.Length; ii++)
                    {
                        SessionDiagnosticsData diagnostics = sessions[ii];
                        UpdateSessionDiagnostics(context, diagnostics, sessionArray, ii);
                    }

                    value = Variant.FromStructure(sessionArray.Where(s => s != null).ToArrayOf());
                }
                else if (node.NodeId ==
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray)
                {
                    // read session security diagnostics.
                    SessionDiagnosticsData[] sessions;
                    lock (m_diagnosticsCollectionLock)
                    {
                        sessions = [.. m_sessions];
                    }
                    var sessionSecurityArray = new SessionSecurityDiagnosticsDataType[sessions.Length];

                    for (int ii = 0; ii < sessions.Length; ii++)
                    {
                        UpdateSessionSecurityDiagnostics(
                            context,
                            sessions[ii],
                            sessionSecurityArray,
                            ii);
                    }
                    value = Variant.FromStructure(sessionSecurityArray.Where(s => s != null).ToArrayOf());
                }
                else if (node.NodeId == VariableIds
                    .Server_ServerDiagnostics_SubscriptionDiagnosticsArray)
                {
                    // read subscription diagnostics.
                    SubscriptionDiagnosticsData[] subscriptions;
                    lock (m_diagnosticsCollectionLock)
                    {
                        subscriptions = [.. m_subscriptions];
                    }
                    var subscriptionArray = new SubscriptionDiagnosticsDataType[subscriptions.Length];

                    for (int ii = 0; ii < subscriptions.Length; ii++)
                    {
                        UpdateSubscriptionDiagnostics(
                            context,
                            subscriptions[ii],
                            subscriptionArray,
                            ii);
                    }
                    value = Variant.FromStructure(subscriptionArray.Where(s => s != null).ToArrayOf());
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <exception cref="ServiceResultException"/>
        private static bool HasApplicationSecureAdminAccess(ISystemContext context)
        {
            if (context is SessionSystemContext { OperationContext: OperationContext operationContext } session)
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode !=
                    MessageSecurityMode.SignAndEncrypt)
                {
                    return false;
                }

                return session?.UserIdentity?.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin) == true;
            }
            return false;
        }

        /// <summary>
        /// Reports notifications for any monitored diagnostic nodes.
        /// </summary>
        private void DoScan(object? alwaysUpdateArrays)
        {
            try
            {
                lock (m_diagnosticsLock)
                {
                    if (!DiagnosticsEnabled || m_doScanBusy)
                    {
                        return;
                    }

                    try
                    {
                        m_doScanBusy = true;
                        SessionDiagnosticsData[] sessions;
                        SubscriptionDiagnosticsData[] subscriptions;
                        lock (m_diagnosticsCollectionLock)
                        {
                            m_forceDiagnosticsScan = false;
                            sessions = [.. m_sessions];
                            subscriptions = [.. m_subscriptions];
                        }

                        m_lastDiagnosticsScanTimestamp = m_timeProvider.GetTimestamp();

                        // update server diagnostics.
                        UpdateServerDiagnosticsSummary();

                        // update session diagnostics.
                        bool sessionsChanged = alwaysUpdateArrays != null;
                        var sessionArray = new SessionDiagnosticsDataType[sessions.Length];

                        for (int ii = 0; ii < sessions.Length; ii++)
                        {
                            SessionDiagnosticsData diagnostics = sessions[ii];

                            if (UpdateSessionDiagnostics(null!, diagnostics, sessionArray, ii))
                            {
                                sessionsChanged = true;
                            }
                        }

                        // check of the session diagnostics array node needs to be updated.
                        SessionDiagnosticsArrayState sessionsNode = FindPredefinedNode<SessionDiagnosticsArrayState>(
                            VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray);

                        if (sessionsNode != null &&
                            (
                                sessionsNode.Value.IsNull ||
                                StatusCode.IsBad(sessionsNode.StatusCode) ||
                                sessionsChanged))
                        {
                            sessionsNode.Value = sessionArray;
                            sessionsNode.ClearChangeMasks(SystemContext, false);
                        }

                        bool sessionsSecurityChanged = alwaysUpdateArrays != null;
                        var sessionSecurityArray = new SessionSecurityDiagnosticsDataType[sessions.Length];

                        for (int ii = 0; ii < sessions.Length; ii++)
                        {
                            SessionDiagnosticsData diagnostics = sessions[ii];

                            if (UpdateSessionSecurityDiagnostics(
                                null!,
                                diagnostics,
                                sessionSecurityArray,
                                ii))
                            {
                                sessionsSecurityChanged = true;
                            }
                        }

                        // check of the array node needs to be updated.
                        SessionSecurityDiagnosticsArrayState sessionsSecurityNode
                            = FindPredefinedNode<SessionSecurityDiagnosticsArrayState>(
                            VariableIds
                                .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray);

                        if (sessionsSecurityNode != null &&
                            (
                                sessionsSecurityNode.Value.IsNull ||
                                StatusCode.IsBad(sessionsSecurityNode.StatusCode) ||
                                sessionsSecurityChanged)
                            )
                        {
                            sessionsSecurityNode.Value = sessionSecurityArray;
                            sessionsSecurityNode.ClearChangeMasks(SystemContext, false);
                        }

                        bool subscriptionsChanged = alwaysUpdateArrays != null;
                        var subscriptionArray = new SubscriptionDiagnosticsDataType[subscriptions.Length];

                        for (int ii = 0; ii < subscriptions.Length; ii++)
                        {
                            SubscriptionDiagnosticsData diagnostics = subscriptions[ii];

                            if (UpdateSubscriptionDiagnostics(
                                null!,
                                diagnostics,
                                subscriptionArray,
                                ii))
                            {
                                subscriptionsChanged = true;
                            }
                        }

                        // check of the subscription node needs to be updated.
                        SubscriptionDiagnosticsArrayState subscriptionsNode
                            = FindPredefinedNode<SubscriptionDiagnosticsArrayState>(
                            VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

                        if (subscriptionsNode != null &&
                            (
                                subscriptionsNode.Value.IsNull ||
                                StatusCode.IsBad(subscriptionsNode.StatusCode) ||
                                subscriptionsChanged))
                        {
                            subscriptionsNode.Value = subscriptionArray;
                            subscriptionsNode.ClearChangeMasks(SystemContext, false);
                        }

                        for (int ii = 0; ii < sessions.Length; ii++)
                        {
                            SessionDiagnosticsData diagnostics = sessions[ii];
                            var subscriptionDiagnosticsArray
                                = new List<SubscriptionDiagnosticsDataType>();

                            NodeId sessionId = diagnostics.Summary.NodeId;

                            for (int jj = 0; jj < subscriptions.Length; jj++)
                            {
                                SubscriptionDiagnosticsData subscriptionDiagnostics
                                    = subscriptions[jj];

                                if (subscriptionDiagnostics.Value.Value == null)
                                {
                                    continue;
                                }

                                if (subscriptionDiagnostics.Value.Value.SessionId != sessionId)
                                {
                                    continue;
                                }

                                subscriptionDiagnosticsArray.Add(
                                    subscriptionDiagnostics.Value.Value);
                            }

                            // update session subscription array.
                            subscriptionsNode = (SubscriptionDiagnosticsArrayState?)
                                diagnostics.Summary.CreateChild(
                                    SystemContext,
                                    QualifiedName.From(BrowseNames.SubscriptionDiagnosticsArray))!;

                            if (subscriptionsNode != null &&
                                (
                                    subscriptionsNode.Value.IsNull ||
                                    StatusCode.IsBad(subscriptionsNode.StatusCode) ||
                                    subscriptionsChanged))
                            {
                                subscriptionsNode.Value = [.. subscriptionDiagnosticsArray];
                                subscriptionsNode.ClearChangeMasks(SystemContext, false);
                            }
                        }
                    }
                    finally
                    {
                        m_doScanBusy = false;
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.UnexpectedErrorDuringDiagnosticsScan(e);
            }
        }

        /// <summary>
        /// Validates the view description passed to a browse request (throws on error).
        /// </summary>
        protected override void ValidateViewDescription(
            ServerSystemContext context,
            ViewDescription view)
        {
            // always accept all views so the root nodes appear in the view.
        }

        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            // check if the variable needs to be sampled.
            if (monitoredItem.AttributeId == Attributes.Value &&
                handle.Node is BaseVariableState variable &&
                variable.MinimumSamplingInterval > 0)
            {
                CreateSampledItem(monitoredItem.SamplingInterval, monitoredItem);
            }

            // check if diagnostics collection needs to be turned one.
            if (IsDiagnosticsNode(handle.Node))
            {
                monitoredItem.AlwaysReportUpdates = IsDiagnosticsStructureNode(handle.Node);

                if (UpdateDiagnosticsMonitoring(MonitoringMode.Disabled, monitoredItem.MonitoringMode))
                {
                    DoScan(true);
                }
            }
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected override ValueTask OnMonitoredItemDeletedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            // check if diagnostics collection needs to be turned off.
            if (IsDiagnosticsNode(handle.Node) &&
                monitoredItem.MonitoringMode != MonitoringMode.Disabled &&
                UpdateDiagnosticsMonitoring(monitoredItem.MonitoringMode, MonitoringMode.Disabled))
            {
                DoScan(true);
            }

            // check if sampling needs to be turned off.
            if (monitoredItem.AttributeId == Attributes.Value &&
                handle.Node is BaseVariableState variable &&
                variable.MinimumSamplingInterval > 0)
            {
                DeleteSampledItem(monitoredItem);
            }

            return new ValueTask();
        }

        /// <summary>
        /// Called after changing the MonitoringMode for a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="previousMode">The previous monitoring mode.</param>
        /// <param name="monitoringMode">The current monitoring mode.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        protected override ValueTask OnMonitoringModeChangedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken = default)
        {
            if (IsDiagnosticsNode(handle.Node) &&
                UpdateDiagnosticsMonitoring(previousMode, monitoringMode) &&
                previousMode == MonitoringMode.Disabled)
            {
                DoScan(true);
            }
            return default;
        }

        /// <summary>
        /// Updates the active diagnostics-monitor count and reports whether periodic scanning remains enabled.
        /// </summary>
        private bool UpdateDiagnosticsMonitoring(MonitoringMode previousMode, MonitoringMode monitoringMode)
        {
            lock (m_diagnosticsLock)
            {
                if (m_diagnosticsDisposed)
                {
                    return false;
                }
                if (previousMode != MonitoringMode.Disabled)
                {
                    m_diagnosticsMonitoringCount--;
                }
                if (monitoringMode != MonitoringMode.Disabled)
                {
                    m_diagnosticsMonitoringCount++;
                }
                UpdateDiagnosticsScanTimer();
                return m_diagnosticsScanTimer != null;
            }
        }

        /// <summary>
        /// Runs the scan timer only while diagnostics are enabled, monitored and not disposed.
        /// </summary>
        private void UpdateDiagnosticsScanTimer()
        {
            if (!m_diagnosticsDisposed && DiagnosticsEnabled && m_diagnosticsMonitoringCount > 0)
            {
                m_diagnosticsScanTimer ??= m_timeProvider.CreateTimer(
                    DoScan,
                    null,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(1));
            }
            else
            {
                m_diagnosticsScanTimer?.Dispose();
                m_diagnosticsScanTimer = null;
            }
        }

        /// <summary>
        /// Stores the callback information for a session diagnostics structures.
        /// </summary>
        private class SessionDiagnosticsData
        {
            public SessionDiagnosticsData(
                SessionDiagnosticsObjectState summary,
                SessionDiagnosticsVariableValue value,
                NodeValueSimpleEventHandler updateCallback,
                SessionSecurityDiagnosticsValue securityValue,
                NodeValueSimpleEventHandler securityUpdateCallback,
                SessionDiagnosticsDataType diagnostics,
                SessionSecurityDiagnosticsDataType securityDiagnostics)
            {
                Summary = summary;
                Value = value;
                UpdateCallback = updateCallback;
                SecurityValue = securityValue;
                SecurityUpdateCallback = securityUpdateCallback;
                Diagnostics = diagnostics;
                SecurityDiagnostics = securityDiagnostics;
            }

            public SessionDiagnosticsObjectState Summary;
            public SessionDiagnosticsVariableValue Value;
            public NodeValueSimpleEventHandler UpdateCallback;
            public SessionSecurityDiagnosticsValue SecurityValue;
            public NodeValueSimpleEventHandler SecurityUpdateCallback;
            public SessionDiagnosticsDataType Diagnostics;
            public SessionSecurityDiagnosticsDataType SecurityDiagnostics;
        }

        /// <summary>
        /// Stores the callback information for a subscription diagnostics structure.
        /// </summary>
        private class SubscriptionDiagnosticsData
        {
            public SubscriptionDiagnosticsData(
                SubscriptionDiagnosticsValue value,
                NodeValueSimpleEventHandler updateCallback,
                SubscriptionDiagnosticsDataType diagnostics)
            {
                Value = value;
                UpdateCallback = updateCallback;
                Diagnostics = diagnostics;
            }

            public SubscriptionDiagnosticsValue Value;
            public NodeValueSimpleEventHandler UpdateCallback;
            public SubscriptionDiagnosticsDataType Diagnostics;
        }

        /// <summary>
        /// Creates a new sampled item.
        /// </summary>
        private void CreateSampledItem(
            double samplingInterval,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            m_sampledItems.TryAdd(monitoredItem.Id, monitoredItem);

            m_samplingTimer ??= m_timeProvider.CreateTimer(
                DoSample,
                null,
                TimeSpan.FromMilliseconds(m_minimumSamplingInterval),
                TimeSpan.FromMilliseconds(m_minimumSamplingInterval));
        }

        /// <summary>
        /// Deletes a sampled item.
        /// </summary>
        private void DeleteSampledItem(ISampledDataChangeMonitoredItem monitoredItem)
        {
            m_sampledItems.TryRemove(monitoredItem.Id, out _);

            if (m_sampledItems.IsEmpty && m_samplingTimer != null)
            {
                m_samplingTimer.Dispose();
                m_samplingTimer = null;
            }
        }

        /// <summary>
        /// Polls each monitored item which requires sample.
        /// </summary>
        private void DoSample(object? state)
        {
            try
            {
                lock (m_diagnosticsLock)
                {
                    foreach (KeyValuePair<uint, ISampledDataChangeMonitoredItem> kvp in m_sampledItems)
                    {
                        ISampledDataChangeMonitoredItem monitoredItem = kvp.Value;

                        // get the handle.
                        if (monitoredItem.ManagerHandle is not NodeHandle handle)
                        {
                            continue;
                        }

                        // check if it is time to sample.
                        if (monitoredItem.TimeToNextSample > m_minimumSamplingInterval)
                        {
                            continue;
                        }

                        // read the value.
                        var value = new DataValue();

                        ServiceResult error = handle.Node.ReadAttribute(
                            SystemContext,
                            monitoredItem.AttributeId,
                            monitoredItem.IndexRange,
                            monitoredItem.DataEncoding,
                            ref value);

                        if (ServiceResult.IsBad(error))
                        {
                            value = DataValue.FromStatusCode(error.StatusCode);
                        }

                        value = value.WithServerTimestamp(DateTime.UtcNow);

                        // queue the value.
                        monitoredItem.QueueValue(value, error);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.UnexpectedErrorDuringDiagnosticsScan(e);
            }
        }

        private readonly SemaphoreSlim m_modifyAddressSpaceSemaphoreSlim = new(1, 1);
        private readonly SemaphoreSlim m_diagnosticsTransitionSemaphore = new(1, 1);
        private readonly Lock m_diagnosticsLock = new();

        /// <summary>
        /// Protects membership snapshots of session and subscription diagnostics.
        /// </summary>
        private readonly Lock m_diagnosticsCollectionLock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly ushort m_namespaceIndex;
        private ITimer? m_diagnosticsScanTimer;
        private int m_diagnosticsMonitoringCount;

        /// <summary>
        /// Prevents diagnostics monitoring from restarting after disposal.
        /// </summary>
        private bool m_diagnosticsDisposed;
        private bool m_doScanBusy;
        private readonly bool m_durableSubscriptionsEnabled;
        private long m_lastDiagnosticsScanTimestamp;

        /// <summary>
        /// Requests a fresh diagnostics scan after collection membership changes.
        /// </summary>
        private volatile bool m_forceDiagnosticsScan = true;
        private ServerDiagnosticsSummaryValue? m_serverDiagnostics;
        private NodeValueSimpleEventHandler? m_serverDiagnosticsCallback;
        private readonly List<SessionDiagnosticsData> m_sessions;
        private readonly List<SubscriptionDiagnosticsData> m_subscriptions;
        private NodeId m_serverLockHolder;
        private ITimer? m_samplingTimer;
        private readonly ConcurrentDictionary<uint, ISampledDataChangeMonitoredItem> m_sampledItems;
        private readonly double m_minimumSamplingInterval;
        private HistoryServerCapabilitiesState? m_historyCapabilities;

        /// <summary>
        /// Aggregates the per-node capabilities advertised by every
        /// registered historian provider into a single union view used
        /// to populate the server-wide <c>HistoryServerCapabilities</c>
        /// flags.
        /// </summary>
        private async ValueTask<Historian.HistorianNodeCapabilities?> RollUpHistorianCapabilitiesAsync(
            CancellationToken cancellationToken)
        {
            if (Server is not Historian.IHistorianRegistryProvider registry)
            {
                return null;
            }

            ArrayOf<Historian.IHistorianProvider> providers =
                registry.HistorianRegistry.Providers;
            if (providers.Count == 0)
            {
                return null;
            }

            var rolled = new Historian.HistorianNodeCapabilities
            {
                ReadRawData = false,
                ReadModifiedData = false,
                ReadAtTime = false,
                ReadProcessedData = false
            };
            bool readRawData = false;
            bool readModifiedData = false;
            bool readAtTime = false;
            bool readProcessedData = false;
            bool insertData = false;
            bool replaceData = false;
            bool updateData = false;
            bool deleteRaw = false;
            bool deleteAtTime = false;
            bool insertAnnotation = false;
            bool readEventHistory = false;
            bool insertEvent = false;
            bool replaceEvent = false;
            bool updateEvent = false;
            bool deleteEvent = false;
            bool readStructuredData = false;
            bool readModifiedStructuredData = false;
            bool readAtTimeStructuredData = false;
            bool insertStructuredData = false;
            bool replaceStructuredData = false;
            bool updateStructuredData = false;
            bool deleteStructuredData = false;
            uint maxReturnDataValues = 0;
            uint maxReturnEventValues = 0;
            bool serverTimestampSupported = false;
            bool portableResumeTokens = false;

            for (int providerIndex = 0;
                providerIndex < providers.Count;
                providerIndex++)
            {
                Historian.IHistorianProvider provider =
                    providers[providerIndex];
                Historian.HistorianNodeCapabilities caps;
                try
                {
                    caps = await provider.GetCapabilitiesAsync(NodeId.Null, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is NotSupportedException or
                        InvalidOperationException)
                {
                    m_logger.HistorianCapabilityRollupFailed(
                        provider.GetType().FullName ??
                        provider.GetType().Name,
                        exception);
                    continue;
                }

                bool hasData = provider is Historian.IHistorianDataProvider;
                bool hasModified =
                    provider is Historian.IHistorianModifiedProvider;
                bool hasAtTime =
                    provider is Historian.IHistorianAtTimeProvider ||
                    hasData;
                bool hasProcessed =
                    provider is Historian.IHistorianProcessedProvider ||
                    hasData;
                bool hasAnnotations =
                    provider is Historian.IHistorianAnnotationProvider;
                bool hasEvents =
                    provider is Historian.IHistorianEventProvider;
                bool hasStructured =
                    provider is Historian.IHistorianStructuredDataProvider;

                readRawData |= hasData && caps.ReadRawData;
                readModifiedData |= hasModified && caps.ReadModifiedData;
                readAtTime |= hasAtTime && caps.ReadAtTime;
                readProcessedData |= hasProcessed && caps.ReadProcessedData;
                insertData |= hasData && caps.InsertData;
                replaceData |= hasData && caps.ReplaceData;
                updateData |= hasData && caps.UpdateData;
                deleteRaw |= hasData && caps.DeleteRaw;
                deleteAtTime |= hasData && caps.DeleteAtTime;
                insertAnnotation |= hasAnnotations && caps.InsertAnnotation;
                readEventHistory |= hasEvents && caps.ReadEventHistory;
                insertEvent |= hasEvents && caps.InsertEvent;
                replaceEvent |= hasEvents && caps.ReplaceEvent;
                updateEvent |= hasEvents && caps.UpdateEvent;
                deleteEvent |= hasEvents && caps.DeleteEvent;
                readStructuredData |=
                    hasStructured && hasData && caps.ReadStructuredData;
                readModifiedStructuredData |=
                    hasStructured &&
                    hasModified &&
                    caps.ReadModifiedStructuredData;
                readAtTimeStructuredData |=
                    hasStructured &&
                    hasAtTime &&
                    caps.ReadAtTimeStructuredData;
                insertStructuredData |=
                    hasStructured && caps.InsertStructuredData;
                replaceStructuredData |=
                    hasStructured && caps.ReplaceStructuredData;
                updateStructuredData |=
                    hasStructured && caps.UpdateStructuredData;
                deleteStructuredData |=
                    hasStructured && caps.DeleteStructuredData;
                if ((hasData &&
                    (caps.ReadRawData ||
                        caps.ReadModifiedData ||
                        caps.ReadAtTime ||
                        caps.ReadProcessedData)) ||
                    (hasStructured &&
                        (caps.ReadStructuredData ||
                            caps.ReadModifiedStructuredData ||
                            caps.ReadAtTimeStructuredData)))
                {
                    maxReturnDataValues = MergeHistorianLimit(
                        maxReturnDataValues,
                        caps.MaxReturnDataValues);
                }
                if (hasEvents && caps.ReadEventHistory)
                {
                    maxReturnEventValues = MergeHistorianLimit(
                        maxReturnEventValues,
                        caps.MaxReturnEventValues);
                }
                serverTimestampSupported |=
                    (hasData || hasStructured) &&
                    caps.ServerTimestampSupported;
                portableResumeTokens |=
                    provider is Historian.IHistorianProviderIdentity &&
                    caps.PortableResumeTokens;
            }

            return rolled with
            {
                ReadRawData = readRawData,
                ReadModifiedData = readModifiedData,
                ReadAtTime = readAtTime,
                ReadProcessedData = readProcessedData,
                InsertData = insertData,
                ReplaceData = replaceData,
                UpdateData = updateData,
                DeleteRaw = deleteRaw,
                DeleteAtTime = deleteAtTime,
                InsertAnnotation = insertAnnotation,
                ReadEventHistory = readEventHistory,
                InsertEvent = insertEvent,
                ReplaceEvent = replaceEvent,
                UpdateEvent = updateEvent,
                DeleteEvent = deleteEvent,
                ReadStructuredData = readStructuredData,
                ReadModifiedStructuredData = readModifiedStructuredData,
                ReadAtTimeStructuredData = readAtTimeStructuredData,
                InsertStructuredData = insertStructuredData,
                ReplaceStructuredData = replaceStructuredData,
                UpdateStructuredData = updateStructuredData,
                DeleteStructuredData = deleteStructuredData,
                MaxReturnDataValues = maxReturnDataValues,
                MaxReturnEventValues = maxReturnEventValues,
                ServerTimestampSupported = serverTimestampSupported,
                PortableResumeTokens = portableResumeTokens
            };
        }

        private static uint MergeHistorianLimit(uint current, uint candidate)
        {
            if (candidate == 0)
            {
                return current;
            }
            return current == 0 ? candidate : Math.Min(current, candidate);
        }

        private static readonly NodeId[] s_kWellKnownRoles =
        [
            ObjectIds.WellKnownRole_Anonymous,
            ObjectIds.WellKnownRole_AuthenticatedUser,
            ObjectIds.WellKnownRole_TrustedApplication,
            ObjectIds.WellKnownRole_ConfigureAdmin,
            ObjectIds.WellKnownRole_Engineer,
            ObjectIds.WellKnownRole_Observer,
            ObjectIds.WellKnownRole_Operator,
            ObjectIds.WellKnownRole_SecurityAdmin,
            ObjectIds.WellKnownRole_Supervisor
        ];
    }

    /// <summary>
    /// Source-generated log messages for DiagnosticsNodeManager.
    /// </summary>
    internal static partial class DiagnosticsNodeManagerLog
    {
        /// <summary>
        /// Logs an unexpected exception while scanning server diagnostics.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.DiagnosticsNodeManager + 0, Level = LogLevel.Error,
            Message = "Unexpected error during diagnostics scan.")]
        public static partial void UnexpectedErrorDuringDiagnosticsScan(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs a provider skipped while aggregating historian capabilities.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.DiagnosticsNodeManager + 1,
            Level = LogLevel.Warning,
            Message = "Historian capability rollup skipped provider {ProviderType}.")]
        public static partial void HistorianCapabilityRollupFailed(
            this ILogger logger,
            string providerType,
            Exception exception);
    }
}
