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
    public class DiagnosticsNodeManager : AsyncCustomNodeManager, IDiagnosticsNodeManager
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
                  server.Telemetry.CreateLogger<DiagnosticsNodeManager>())
        {
        }

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DiagnosticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : base(server, configuration, logger)
        {
            AliasRoot = "Core";

            string[] namespaceUris =
            [
                Ua.Namespaces.OpcUa,
                Ua.Namespaces.OpcUa + "Diagnostics"
            ];
            SetNamespaces(namespaceUris);

            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);
            m_lastUsedId = (uint)DateTime.UtcNow.Ticks & 0x7FFFFFFF;
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
                m_modifyAddressSpaceSemaphoreSlim.Wait(10);
                try
                {
                    m_diagnosticsScanTimer?.Dispose();
                    m_diagnosticsScanTimer = null;

                    m_samplingTimer?.Dispose();
                    m_samplingTimer = null;
                }
                finally
                {
                    m_modifyAddressSpaceSemaphoreSlim.Release();
                }

                m_modifyAddressSpaceSemaphoreSlim.Dispose();

                m_historyCapabilities?.Dispose();
                m_historyCapabilities = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            uint id = Utils.IncrementIdentifier(ref m_lastUsedId);
            return new NodeId(id, m_namespaceIndex);
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

            // sampling interval diagnostics not supported by the server.
            ServerDiagnosticsState serverDiagnosticsNode = FindPredefinedNode<ServerDiagnosticsState>(
                ObjectIds.Server_ServerDiagnostics);

            if (serverDiagnosticsNode != null)
            {
                NodeState samplingDiagnosticsArrayNode = serverDiagnosticsNode.FindChild(
                    SystemContext,
                    QualifiedName.From(BrowseNames.SamplingIntervalDiagnosticsArray));

                if (samplingDiagnosticsArrayNode != null)
                {
                    await DeleteNodeAsync(
                        SystemContext,
                        VariableIds.Server_ServerDiagnostics_SamplingIntervalDiagnosticsArray,
                        cancellationToken).ConfigureAwait(false);
                    serverDiagnosticsNode.SamplingIntervalDiagnosticsArray = null;
                }
            }

            // The nodes are now loaded by the DiagnosticsNodeManager from the file
            // output by the ModelDesigner V2. These nodes are added to the CoreNodeManager
            // via the ImportNodes() method when the DiagnosticsNodeManager starts.
            await Server.CoreNodeManager.ImportNodesAsync(
                SystemContext,
                PredefinedNodes.Values,
                true,
                cancellationToken).ConfigureAwait(false);

            // hook up the server GetMonitoredItems method.
            GetMonitoredItemsMethodState getMonitoredItems = FindPredefinedNode<GetMonitoredItemsMethodState>(
                MethodIds.Server_GetMonitoredItems);

            getMonitoredItems?.OnCallMethod = OnGetMonitoredItems;

            // set ArrayDimensions for GetMonitoredItems.OutputArguments.Value.
            PropertyState getMonitoredItemsOutputArguments = FindPredefinedNode<PropertyState>(
                VariableIds.Server_GetMonitoredItems_OutputArguments);

            if (getMonitoredItemsOutputArguments != null &&
                getMonitoredItemsOutputArguments.Value.TryGetStructure(out ArrayOf<Argument> outputArgumentsValue))
            {
                getMonitoredItemsOutputArguments.ClearChangeMasks(SystemContext, false);
            }

            if (m_durableSubscriptionsEnabled)
            {
                // hook up the server SetSubscriptionDurable method.
                SetSubscriptionDurableMethodState setSubscriptionDurable
                    = FindPredefinedNode<SetSubscriptionDurableMethodState>(
                    MethodIds.Server_SetSubscriptionDurable);

                setSubscriptionDurable?.OnCall = OnSetSubscriptionDurable;
            }
            else
            {
                // Subscription Durable mode not supported by the server.
                ServerObjectState serverObject = FindPredefinedNode<ServerObjectState>(
                    ObjectIds.Server);

                if (serverObject != null)
                {
                    NodeState setSubscriptionDurableNode = serverObject.FindChild(
                        SystemContext,
                        QualifiedName.From(BrowseNames.SetSubscriptionDurable));

                    if (setSubscriptionDurableNode != null)
                    {
                        await DeleteNodeAsync(SystemContext, MethodIds.Server_SetSubscriptionDurable, cancellationToken)
                            .ConfigureAwait(false);
                        serverObject.SetSubscriptionDurable = null;
                    }
                }
            }
            // hookup server ResendData method.

            ResendDataMethodState resendData = FindPredefinedNode<ResendDataMethodState>(
                MethodIds.Server_ResendData);

            resendData?.OnCallMethod = OnResendData;
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

            if (!inputArguments[0].TryGet(out uint subscriptionId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!Server.SubscriptionManager.TryGetSubscription(subscriptionId, out ISubscription subscription))
            {
                return StatusCodes.BadSubscriptionIdInvalid;
            }

            if (context is ISessionSystemContext session &&
                subscription.SessionId != session.SessionId)
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

            if (!inputArguments[0].TryGet(out uint subscriptionId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!Server.SubscriptionManager.TryGetSubscription(subscriptionId, out ISubscription subscription))
            {
                return StatusCodes.BadSubscriptionIdInvalid;
            }

            if (context is not ServerSystemContext session ||
                subscription.SessionId != session.SessionId)
            {
                // user tries to access subscription of different session
                return StatusCodes.BadUserAccessDenied;
            }

            subscription.ResendData(session.OperationContext);

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

            if (!m_serverLockHolder.IsNull && m_serverLockHolder != systemContext.SessionId)
            {
                return StatusCodes.BadSessionIdInvalid;
            }

            m_serverLockHolder = systemContext.SessionId;

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

            if (!m_serverLockHolder.IsNull && m_serverLockHolder != systemContext.SessionId)
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
            return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUa(context));
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
                if (predefinedNode is BaseVariableState passiveVariable)
                {
                    if (passiveVariable.NodeId == VariableIds.ServerStatusType_BuildInfo)
                    {
                        if (passiveVariable is BuildInfoVariableState)
                        {
                            return predefinedNode;
                        }

                        var activeNode = new BuildInfoVariableState(passiveVariable.Parent);
                        activeNode.Create(context, passiveVariable);

                        // replace the node in the parent.
                        passiveVariable.Parent?.ReplaceChild(context, activeNode);

                        return activeNode;
                    }
                    return predefinedNode;
                }

                if (predefinedNode is not MethodState passiveMethod)
                {
                    return predefinedNode;
                }

                if (passiveMethod.NodeId == MethodIds.ConditionType_ConditionRefresh)
                {
                    var activeNode = new ConditionRefreshMethodState(passiveMethod.Parent);
                    activeNode.Create(context, passiveMethod);

                    // replace the node in the parent.
                    passiveMethod.Parent?.ReplaceChild(context, activeNode);

                    activeNode.OnCall = OnConditionRefresh;

                    return activeNode;
                }
                else if (passiveMethod.NodeId == MethodIds.ConditionType_ConditionRefresh2)
                {
                    var activeNode = new ConditionRefresh2MethodState(passiveMethod.Parent);
                    activeNode.Create(context, passiveMethod);

                    // replace the node in the parent.
                    passiveMethod.Parent?.ReplaceChild(context, activeNode);

                    activeNode.OnCall = OnConditionRefresh2;

                    return activeNode;
                }

                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || !typeId.TryGetIdentifier(out uint numericId))
            {
                return predefinedNode;
            }

            switch (numericId)
            {
                case ObjectTypes.ServerType:
                {
                    if (passiveNode is ServerObjectState)
                    {
                        // add the server object as the root notifier.
                        await AddRootNotifierAsync(passiveNode, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    var activeNode = new ServerObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    // add the server object as the root notifier.
                    await AddRootNotifierAsync(activeNode, cancellationToken).ConfigureAwait(false);

                    // replace the node in the parent.
                    passiveNode.Parent?.ReplaceChild(context, activeNode);

                    return activeNode;
                }
                case ObjectTypes.HistoryServerCapabilitiesType:
                {
                    if (passiveNode is HistoryServerCapabilitiesState)
                    {
                        break;
                    }

                    var activeNode = new HistoryServerCapabilitiesState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    // replace the node in the parent.
                    passiveNode.Parent?.ReplaceChild(context, activeNode);

                    return activeNode;
                }
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

            Server.ConditionRefresh(systemContext.OperationContext, subscriptionId);

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
                systemContext.OperationContext,
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

                return IsDiagnosticsStructureNode(instance.Parent);
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
                typeId.TryGetIdentifier(out uint numericId))
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
            m_lastDiagnosticsScanTime = DateTime.MinValue;
        }

        /// <inheritdoc/>
        public bool DiagnosticsEnabled { get; private set; }

        /// <inheritdoc/>
        public async ValueTask SetDiagnosticsEnabledAsync(
            ServerSystemContext context,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            var nodesToDelete = new List<NodeState>();

            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (enabled == DiagnosticsEnabled)
                {
                    return;
                }

                DiagnosticsEnabled = enabled;

                if (!enabled)
                {
                    // stop scans.
                    m_diagnosticsScanTimer?.Dispose();
                    m_diagnosticsScanTimer = null;

                    if (m_sessions != null)
                    {
                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            nodesToDelete.Add(m_sessions[ii].Summary);
                        }

                        m_sessions.Clear();
                    }

                    if (m_subscriptions != null)
                    {
                        for (int ii = 0; ii < m_subscriptions.Count; ii++)
                        {
                            nodesToDelete.Add(m_subscriptions[ii].Value.Variable);
                        }

                        m_subscriptions.Clear();
                    }
                }
                else
                {
                    // reset all diagnostics nodes.
                    if (m_serverDiagnostics != null)
                    {
                        m_serverDiagnostics.Value = null;
                        m_serverDiagnostics.Error = StatusCodes.BadWaitingForInitialData;
                        m_serverDiagnostics.Timestamp = DateTime.UtcNow;
                    }

                    // get the node.
                    ServerDiagnosticsState diagnosticsNode = FindPredefinedNode<ServerDiagnosticsState>(
                        ObjectIds.Server_ServerDiagnostics);

                    // clear arrays.
                    if (diagnosticsNode != null)
                    {
                        if (diagnosticsNode.SamplingIntervalDiagnosticsArray != null)
                        {
                            diagnosticsNode.SamplingIntervalDiagnosticsArray.Value = default;
                            diagnosticsNode.SamplingIntervalDiagnosticsArray.StatusCode =
                                StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SamplingIntervalDiagnosticsArray.Timestamp = DateTime
                                .UtcNow;
                        }

                        if (diagnosticsNode.SubscriptionDiagnosticsArray != null)
                        {
                            diagnosticsNode.SubscriptionDiagnosticsArray.Value = default;
                            diagnosticsNode.SubscriptionDiagnosticsArray.StatusCode =
                                StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SubscriptionDiagnosticsArray.Timestamp = DateTime
                                .UtcNow;
                        }

                        if (diagnosticsNode.SessionsDiagnosticsSummary != null)
                        {
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionDiagnosticsArray.Value
                                = default;
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionDiagnosticsArray
                                .StatusCode =
                                StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionDiagnosticsArray
                                .Timestamp =
                                DateTime.UtcNow;
                        }

                        if (diagnosticsNode.SessionsDiagnosticsSummary != null)
                        {
                            diagnosticsNode.SessionsDiagnosticsSummary
                                .SessionSecurityDiagnosticsArray
                                .Value = default;
                            diagnosticsNode.SessionsDiagnosticsSummary
                                .SessionSecurityDiagnosticsArray
                                .StatusCode =
                                StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SessionsDiagnosticsSummary
                                .SessionSecurityDiagnosticsArray
                                .Timestamp =
                                DateTime.UtcNow;
                        }
                    }

                    DoScan(true);
                }
            }
            finally
            {
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            for (int ii = 0; ii < nodesToDelete.Count; ii++)
            {
                await DeleteNodeAsync(context, nodesToDelete[ii].NodeId, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask CreateServerDiagnosticsAsync(
            ServerSystemContext systemContext,
            ServerDiagnosticsSummaryDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            CancellationToken cancellationToken)
        {
            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    Value = null,
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

            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            SessionDiagnosticsObjectState tempSessionNode = null;
            try
            {
                tempSessionNode = new SessionDiagnosticsObjectState(null);
                SessionDiagnosticsObjectState sessionNode = tempSessionNode;

                // create a new instance and assign ids.
                nodeId = await CreateNodeAsync(
                    SystemContext,
                    default,
                    ReferenceTypeIds.HasComponent,
                    QualifiedName.From(diagnostics.SessionName),
                    sessionNode,
                    cancellationToken).ConfigureAwait(false);
                tempSessionNode = null; // ownership transferred to address space

                diagnostics.SessionId = nodeId;
                securityDiagnostics.SessionId = nodeId;

                // check if diagnostics have been enabled.
                if (!DiagnosticsEnabled)
                {
                    return nodeId;
                }

                // add reference to session summary object.
                sessionNode.AddReference(
                    ReferenceTypeIds.HasComponent,
                    true,
                    ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary);

                // add reference from session summary object.
                SessionsDiagnosticsSummaryState summary = FindPredefinedNode<SessionsDiagnosticsSummaryState>(
                    ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary);

                summary?.AddReference(ReferenceTypeIds.HasComponent, false, sessionNode.NodeId);

                // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                sessionNode.OnReadUserRolePermissions = OnReadUserRolePermissions;

                // initialize diagnostics node.
                var diagnosticsNode =
                    sessionNode.CreateChild(SystemContext, QualifiedName.From(BrowseNames.SessionDiagnostics)) as
                    SessionDiagnosticsVariableState;

                // wrap diagnostics in a thread safe object.
                var diagnosticsValue = new SessionDiagnosticsVariableValue(
                    diagnosticsNode,
                    diagnostics,
                    m_diagnosticsLock)
                {
                    // must ensure the first update gets sent.
                    Value = null,
                    Error = StatusCodes.BadWaitingForInitialData,
                    CopyPolicy = VariableCopyPolicy.Never,
                    OnBeforeRead = OnBeforeReadDiagnostics
                };

                // initialize security diagnostics node.
                var securityDiagnosticsNode =
                    sessionNode.CreateChild(
                        SystemContext,
                        QualifiedName.From(BrowseNames.SessionSecurityDiagnostics)) as
                    SessionSecurityDiagnosticsState;

                // wrap diagnostics in a thread safe object.
                var securityDiagnosticsValue = new SessionSecurityDiagnosticsValue(
                    securityDiagnosticsNode,
                    securityDiagnostics,
                    m_diagnosticsLock)
                {
                    // must ensure the first update gets sent.
                    Value = null,
                    Error = StatusCodes.BadWaitingForInitialData,
                    CopyPolicy = VariableCopyPolicy.Never,
                    OnBeforeRead = OnBeforeReadDiagnostics
                };

                // save the session.
                var sessionData = new SessionDiagnosticsData(
                    sessionNode,
                    diagnosticsValue,
                    updateCallback,
                    securityDiagnosticsValue,
                    updateSecurityCallback);

                m_sessions.Add(sessionData);

                // send initial update.
                DoScan(true);
            }
            finally
            {
                tempSessionNode?.Dispose();
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            return nodeId;
        }

        /// <inheritdoc/>
        public async ValueTask DeleteSessionDiagnosticsAsync(
            ServerSystemContext systemContext,
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (int ii = 0; ii < m_sessions.Count; ii++)
                {
                    SessionDiagnosticsObjectState summary = m_sessions[ii].Summary;

                    if (summary.NodeId == nodeId)
                    {
                        m_sessions.RemoveAt(ii);
                        break;
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

            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            SubscriptionDiagnosticsState tempDiagnosticsNode = null;
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

                // add reference to subscription array.
                diagnosticsNode.AddReference(
                    ReferenceTypeIds.HasComponent,
                    true,
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

                // wrap diagnostics in a thread safe object.
                var diagnosticsValue = new SubscriptionDiagnosticsValue(
                    diagnosticsNode,
                    diagnostics,
                    m_diagnosticsLock)
                {
                    CopyPolicy = VariableCopyPolicy.Never,
                    OnBeforeRead = OnBeforeReadDiagnostics,

                    // must ensure the first update gets sent.
                    Value = null,
                    Error = StatusCodes.BadWaitingForInitialData
                };

                m_subscriptions.Add(
                    new SubscriptionDiagnosticsData(diagnosticsValue, updateCallback));

                // add reference from subscription array.
                SubscriptionDiagnosticsArrayState array = FindPredefinedNode<SubscriptionDiagnosticsArrayState>(
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
                    array = (SubscriptionDiagnosticsArrayState)
                        sessionNode.CreateChild(
                            SystemContext,
                            QualifiedName.From(BrowseNames.SubscriptionDiagnosticsArray));

                    array?.AddReference(
                        ReferenceTypeIds.HasComponent,
                        false,
                        diagnosticsNode.NodeId);
                }

                // send initial update.
                DoScan(true);
            }
            finally
            {
                tempDiagnosticsNode?.Dispose();
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }

            return nodeId;
        }

        /// <inheritdoc/>
        public async ValueTask DeleteSubscriptionDiagnosticsAsync(
            ServerSystemContext systemContext,
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (int ii = 0; ii < m_subscriptions.Count; ii++)
                {
                    SubscriptionDiagnosticsData diagnostics = m_subscriptions[ii];

                    if (diagnostics.Value.Variable.NodeId == nodeId)
                    {
                        m_subscriptions.RemoveAt(ii);
                        break;
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
            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            HistoryServerCapabilitiesState tempCapabilitiesNode = null;
            try
            {
                if (m_historyCapabilities != null)
                {
                    return m_historyCapabilities;
                }

                // search the Node in PredefinedNodes.
                HistoryServerCapabilitiesState historyServerCapabilitiesNode
                    = FindPredefinedNode<HistoryServerCapabilitiesState>(
                    ObjectIds.HistoryServerCapabilities);

                if (historyServerCapabilitiesNode == null)
                {
                    // create new node if not found.
                    tempCapabilitiesNode = new HistoryServerCapabilitiesState(null);
                    historyServerCapabilitiesNode = tempCapabilitiesNode;

                    NodeId nodeId = await CreateNodeAsync(
                        SystemContext,
                        default,
                        ReferenceTypeIds.HasComponent,
                        QualifiedName.From(BrowseNames.HistoryServerCapabilities),
                        historyServerCapabilitiesNode,
                        cancellationToken).ConfigureAwait(false);
                    tempCapabilitiesNode = null; // ownership transferred to address space

                    historyServerCapabilitiesNode.AccessHistoryDataCapability.Value = false;
                    historyServerCapabilitiesNode.AccessHistoryEventsCapability.Value = false;
                    historyServerCapabilitiesNode.MaxReturnDataValues.Value = 0;
                    historyServerCapabilitiesNode.MaxReturnEventValues.Value = 0;
                    historyServerCapabilitiesNode.ReplaceDataCapability.Value = false;
                    historyServerCapabilitiesNode.UpdateDataCapability.Value = false;
                    historyServerCapabilitiesNode.InsertEventCapability.Value = false;
                    historyServerCapabilitiesNode.ReplaceEventCapability.Value = false;
                    historyServerCapabilitiesNode.UpdateEventCapability.Value = false;
                    historyServerCapabilitiesNode.InsertAnnotationCapability.Value = false;
                    historyServerCapabilitiesNode.InsertDataCapability.Value = false;
                    historyServerCapabilitiesNode.DeleteRawCapability.Value = false;
                    historyServerCapabilitiesNode.DeleteAtTimeCapability.Value = false;
                    historyServerCapabilitiesNode.ServerTimestampSupported.Value = false;

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

                m_historyCapabilities = historyServerCapabilitiesNode;
                return m_historyCapabilities;
            }
            finally
            {
                tempCapabilitiesNode?.Dispose();
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask UpdateServerEventNotifierAsync(CancellationToken cancellationToken = default)
        {
            // Get or create the history capabilities
            HistoryServerCapabilitiesState historyCapabilities = await GetDefaultHistoryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Find the Server object
                ServerObjectState serverObject = FindPredefinedNode<ServerObjectState>(
                    ObjectIds.Server);

                if (serverObject != null && historyCapabilities != null)
                {
                    // Update EventNotifier based on history capabilities
                    byte eventNotifier = serverObject.EventNotifier;

                    // Set HistoryRead bit if history events or data capabilities are enabled
                    if (historyCapabilities.AccessHistoryEventsCapability?.Value == true ||
                        historyCapabilities.AccessHistoryDataCapability?.Value == true)
                    {
                        eventNotifier |= EventNotifiers.HistoryRead;
                    }
                    else
                    {
                        eventNotifier = (byte)(eventNotifier & ~EventNotifiers.HistoryRead);
                    }

                    // Set HistoryWrite bit if history update capabilities are enabled
                    if (historyCapabilities.InsertEventCapability?.Value == true ||
                        historyCapabilities.ReplaceEventCapability?.Value == true ||
                        historyCapabilities.UpdateEventCapability?.Value == true ||
                        historyCapabilities.InsertDataCapability?.Value == true ||
                        historyCapabilities.UpdateDataCapability?.Value == true ||
                        historyCapabilities.ReplaceDataCapability?.Value == true)
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
            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            FolderState tempState = null;
            try
            {
                tempState = new FolderState(null)
                {
                    SymbolicName = aggregateName,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = ObjectTypeIds.AggregateFunctionType,
                    NodeId = aggregateId,
                    BrowseName = new QualifiedName(aggregateName, aggregateId.NamespaceIndex)
                };
                FolderState state = tempState;
                state.DisplayName = LocalizedText.From(state.BrowseName.Name);
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
                tempState = null; // ownership transferred to address space
            }
            finally
            {
                tempState?.Dispose();
                m_modifyAddressSpaceSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask AddModellingRuleAsync(
            NodeId modellingRuleId,
            string modellingRuleName,
            CancellationToken cancellationToken = default)
        {
            await m_modifyAddressSpaceSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            FolderState tempState = null;
            try
            {
                tempState = new FolderState(null)
                {
                    SymbolicName = modellingRuleName,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    TypeDefinitionId = ObjectTypeIds.ModellingRuleType,
                    NodeId = modellingRuleId,
                    BrowseName = new QualifiedName(modellingRuleName, modellingRuleId.NamespaceIndex)
                };
                FolderState state = tempState;
                state.DisplayName = LocalizedText.From(state.BrowseName.Name);
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
                tempState = null; // ownership transferred to address space
            }
            finally
            {
                tempState?.Dispose();
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

            ServiceResult result = m_serverDiagnosticsCallback(
                SystemContext,
                m_serverDiagnostics.Variable,
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
                newValue = null;
            }

            // update the value.
            m_serverDiagnostics.Value = newValue;
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
                newValue = null;
            }

            // update the value.
            diagnostics.Value.Value = newValue;
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
                newValue = null;
            }

            // update the value.
            diagnostics.SecurityValue.Value = newValue;
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
                newValue = null;
            }

            // update the value.
            diagnostics.Value.Value = newValue;
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
                list[index] = default;
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
                adminUser = node.NodeId == curSession || HasApplicationSecureAdminAccess(context);
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

                if (HiResClock.UtcNow < m_lastDiagnosticsScanTime.AddSeconds(1))
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
                    return StatusCodes.BadOutOfService;
                }

                if (HiResClock.UtcNow < m_lastDiagnosticsScanTime.AddSeconds(1))
                {
                    // diagnostic nodes already scanned.
                    return ServiceResult.Good;
                }

                if (node.NodeId ==
                    VariableIds
                        .Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray)
                {
                    // read session diagnostics.
                    var sessionArray = new SessionDiagnosticsDataType[m_sessions.Count];

                    for (int ii = 0; ii < m_sessions.Count; ii++)
                    {
                        SessionDiagnosticsData diagnostics = m_sessions[ii];
                        UpdateSessionDiagnostics(context, diagnostics, sessionArray, ii);
                    }

                    value = Variant.FromStructure(sessionArray.Where(s => s != null).ToArrayOf());
                }
                else if (node.NodeId ==
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray)
                {
                    // read session security diagnostics.
                    var sessionSecurityArray = new SessionSecurityDiagnosticsDataType[m_sessions
                        .Count];

                    for (int ii = 0; ii < m_sessions.Count; ii++)
                    {
                        UpdateSessionSecurityDiagnostics(
                            context,
                            m_sessions[ii],
                            sessionSecurityArray,
                            ii);
                    }
                    value = Variant.FromStructure(sessionSecurityArray.Where(s => s != null).ToArrayOf());
                }
                else if (node.NodeId == VariableIds
                    .Server_ServerDiagnostics_SubscriptionDiagnosticsArray)
                {
                    // read subscription diagnostics.
                    var subscriptionArray = new SubscriptionDiagnosticsDataType[m_subscriptions
                        .Count];

                    for (int ii = 0; ii < m_subscriptions.Count; ii++)
                    {
                        UpdateSubscriptionDiagnostics(
                            context,
                            m_subscriptions[ii],
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
        private void DoScan(object alwaysUpdateArrays)
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

                        m_lastDiagnosticsScanTime = HiResClock.UtcNow;

                        // update server diagnostics.
                        UpdateServerDiagnosticsSummary();

                        // update session diagnostics.
                        bool sessionsChanged = alwaysUpdateArrays != null;
                        var sessionArray = new SessionDiagnosticsDataType[m_sessions.Count];

                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            SessionDiagnosticsData diagnostics = m_sessions[ii];

                            if (UpdateSessionDiagnostics(null, diagnostics, sessionArray, ii))
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
                        var sessionSecurityArray = new SessionSecurityDiagnosticsDataType[m_sessions.Count];

                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            SessionDiagnosticsData diagnostics = m_sessions[ii];

                            if (UpdateSessionSecurityDiagnostics(
                                null,
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
                        var subscriptionArray = new SubscriptionDiagnosticsDataType[m_subscriptions
                            .Count];

                        for (int ii = 0; ii < m_subscriptions.Count; ii++)
                        {
                            SubscriptionDiagnosticsData diagnostics = m_subscriptions[ii];

                            if (UpdateSubscriptionDiagnostics(
                                null,
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

                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            SessionDiagnosticsData diagnostics = m_sessions[ii];
                            var subscriptionDiagnosticsArray
                                = new List<SubscriptionDiagnosticsDataType>();

                            NodeId sessionId = diagnostics.Summary.NodeId;

                            for (int jj = 0; jj < m_subscriptions.Count; jj++)
                            {
                                SubscriptionDiagnosticsData subscriptionDiagnostics
                                    = m_subscriptions[jj];

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
                            subscriptionsNode = (SubscriptionDiagnosticsArrayState)
                                diagnostics.Summary.CreateChild(
                                    SystemContext,
                                    QualifiedName.From(BrowseNames.SubscriptionDiagnosticsArray));

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
                m_logger.LogError(e, "Unexpected error during diagnostics scan.");
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

                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    Interlocked.Increment(ref m_diagnosticsMonitoringCount);

                    m_diagnosticsScanTimer ??= new Timer(DoScan, null, 1000, 1000);

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
                monitoredItem.MonitoringMode != MonitoringMode.Disabled)
            {
                Interlocked.Decrement(ref m_diagnosticsMonitoringCount);

                if (m_diagnosticsMonitoringCount == 0 && m_diagnosticsScanTimer != null)
                {
                    m_diagnosticsScanTimer.Dispose();
                    m_diagnosticsScanTimer = null;
                }

                if (m_diagnosticsScanTimer != null)
                {
                    DoScan(true);
                }
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
            if (previousMode != MonitoringMode.Disabled)
            {
                Interlocked.Decrement(ref m_diagnosticsMonitoringCount);
            }

            if (monitoringMode != MonitoringMode.Disabled)
            {
                Interlocked.Increment(ref m_diagnosticsMonitoringCount);
            }

            if (m_diagnosticsMonitoringCount == 0 && m_diagnosticsScanTimer != null)
            {
                m_diagnosticsScanTimer.Dispose();
                m_diagnosticsScanTimer = null;
            }
            else if (m_diagnosticsScanTimer != null)
            {
                m_diagnosticsScanTimer = new Timer(DoScan, null, 1000, 1000);
            }
            return default;
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
                NodeValueSimpleEventHandler securityUpdateCallback)
            {
                Summary = summary;
                Value = value;
                UpdateCallback = updateCallback;
                SecurityValue = securityValue;
                SecurityUpdateCallback = securityUpdateCallback;
            }

            public SessionDiagnosticsObjectState Summary;
            public SessionDiagnosticsVariableValue Value;
            public NodeValueSimpleEventHandler UpdateCallback;
            public SessionSecurityDiagnosticsValue SecurityValue;
            public NodeValueSimpleEventHandler SecurityUpdateCallback;
        }

        /// <summary>
        /// Stores the callback information for a subscription diagnostics structure.
        /// </summary>
        private class SubscriptionDiagnosticsData
        {
            public SubscriptionDiagnosticsData(
                SubscriptionDiagnosticsValue value,
                NodeValueSimpleEventHandler updateCallback)
            {
                Value = value;
                UpdateCallback = updateCallback;
            }

            public SubscriptionDiagnosticsValue Value;
            public NodeValueSimpleEventHandler UpdateCallback;
        }

        /// <summary>
        /// Creates a new sampled item.
        /// </summary>
        private void CreateSampledItem(
            double samplingInterval,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            m_sampledItems.TryAdd(monitoredItem.Id, monitoredItem);

            m_samplingTimer ??= new Timer(
                DoSample,
                null,
                (int)m_minimumSamplingInterval,
                (int)m_minimumSamplingInterval);
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
        private void DoSample(object state)
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
                            value);

                        if (ServiceResult.IsBad(error))
                        {
                            value = new DataValue(error.StatusCode);
                        }

                        value.ServerTimestamp = DateTime.UtcNow;

                        // queue the value.
                        monitoredItem.QueueValue(value, error);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error during diagnostics scan.");
            }
        }

        private readonly SemaphoreSlim m_modifyAddressSpaceSemaphoreSlim = new(1, 1);
        private readonly object m_diagnosticsLock = new();
        private readonly ushort m_namespaceIndex;
        private uint m_lastUsedId;
        private Timer m_diagnosticsScanTimer;
        private int m_diagnosticsMonitoringCount;
        private bool m_doScanBusy;
        private readonly bool m_durableSubscriptionsEnabled;
        private DateTime m_lastDiagnosticsScanTime;
        private ServerDiagnosticsSummaryValue m_serverDiagnostics;
        private NodeValueSimpleEventHandler m_serverDiagnosticsCallback;
        private readonly List<SessionDiagnosticsData> m_sessions;
        private readonly List<SubscriptionDiagnosticsData> m_subscriptions;
        private NodeId m_serverLockHolder;
        private Timer m_samplingTimer;
        private readonly ConcurrentDictionary<uint, ISampledDataChangeMonitoredItem> m_sampledItems;
        private readonly double m_minimumSamplingInterval;
        private HistoryServerCapabilitiesState m_historyCapabilities;

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
}
