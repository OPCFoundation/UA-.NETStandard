/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A node manager the diagnostic information exposed by the server.
    /// </summary>
    public class DiagnosticsNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DiagnosticsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        :
            base(server, configuration)
        {
            this.AliasRoot = "Core";

            string[] namespaceUris = new string[2];
            namespaceUris[0] = Namespaces.OpcUa;
            namespaceUris[1] = Namespaces.OpcUa + "Diagnostics";
            SetNamespaces(namespaceUris);

            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);
            m_lastUsedId = (long)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            m_sessions = new List<SessionDiagnosticsData>();
            m_subscriptions = new List<SubscriptionDiagnosticsData>();
            m_diagnosticsEnabled = true;
            m_doScanBusy = false;
            m_sampledItems = new List<MonitoredItem>();
            m_minimumSamplingInterval = 100;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (Lock)
                {
                    Utils.SilentDispose(m_diagnosticsScanTimer);
                    m_diagnosticsScanTimer = null;

                    Utils.SilentDispose(m_samplingTimer);
                    m_samplingTimer = null;
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        #region INodeIdFactory Members
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
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                // sampling interval diagnostics not supported by the server.
                ServerDiagnosticsState serverDiagnosticsNode = (ServerDiagnosticsState)FindPredefinedNode(
                    ObjectIds.Server_ServerDiagnostics,
                    typeof(ServerDiagnosticsState));

                if (serverDiagnosticsNode != null)
                {
                    NodeState samplingDiagnosticsArrayNode = serverDiagnosticsNode.FindChild(
                        SystemContext,
                        BrowseNames.SamplingIntervalDiagnosticsArray);

                    if (samplingDiagnosticsArrayNode != null)
                    {
                        DeleteNode(SystemContext, VariableIds.Server_ServerDiagnostics_SamplingIntervalDiagnosticsArray);
                        serverDiagnosticsNode.SamplingIntervalDiagnosticsArray = null;
                    }
                }

                // The nodes are now loaded by the DiagnosticsNodeManager from the file
                // output by the ModelDesigner V2. These nodes are added to the CoreNodeManager
                // via the AttachNode() method when the DiagnosticsNodeManager starts.
                Server.CoreNodeManager.ImportNodes(SystemContext, PredefinedNodes.Values, true);

                // hook up the server GetMonitoredItems method.
                GetMonitoredItemsMethodState getMonitoredItems = (GetMonitoredItemsMethodState)FindPredefinedNode(
                    MethodIds.Server_GetMonitoredItems,
                    typeof(GetMonitoredItemsMethodState));

                if (getMonitoredItems != null)
                {
                    getMonitoredItems.OnCallMethod = OnGetMonitoredItems;
                }

                // set ArrayDimensions for GetMonitoredItems.OutputArguments.Value.
                PropertyState getMonitoredItemsOutputArguments = (PropertyState)FindPredefinedNode(
                    VariableIds.Server_GetMonitoredItems_OutputArguments,
                    typeof(PropertyState));

                if (getMonitoredItemsOutputArguments != null)
                {
                    Argument[] outputArgumentsValue = (Argument[])getMonitoredItemsOutputArguments.Value;

                    if (outputArgumentsValue != null)
                    {
                        foreach (Argument argument in outputArgumentsValue)
                        {
                            argument.ArrayDimensions = new UInt32Collection { 0 };
                        }

                        getMonitoredItemsOutputArguments.ClearChangeMasks(SystemContext, false);
                    }
                }

#if SUPPORT_DURABLE_SUBSCRIPTION
                // hook up the server SetSubscriptionDurable method.
                SetSubscriptionDurableMethodState setSubscriptionDurable= (SetSubscriptionDurableMethodState)FindPredefinedNode(
                    MethodIds.Server_SetSubscriptionDurable,
                    typeof(SetSubscriptionDurableMethodState));

                if (setSubscriptionDurable != null)
                {
                    setSubscriptionDurable.OnCall = OnSetSubscriptionDurable;
                }
#else
                // Subscription Durable mode not supported by the server.
                ServerObjectState serverObject = (ServerObjectState)FindPredefinedNode(
                    ObjectIds.Server,
                    typeof(ServerObjectState));

                if (serverObject != null)
                {
                    NodeState setSubscriptionDurableNode = serverObject.FindChild(
                        SystemContext,
                        BrowseNames.SetSubscriptionDurable);

                    if (setSubscriptionDurableNode != null)
                    {
                        DeleteNode(SystemContext, MethodIds.Server_SetSubscriptionDurable);
                        serverObject.SetSubscriptionDurable = null;
                    }
                }
#endif

                // hookup server ResendData method.

                ResendDataMethodState resendData = (ResendDataMethodState)FindPredefinedNode(
                    MethodIds.Server_ResendData,
                    typeof(ResendDataMethodState));

                if (resendData != null)
                {
                    resendData.OnCallMethod = OnResendData;
                }
            }
        }

        /// <summary>
        /// Called when a client sets a subscription as durable.
        /// </summary>

        public ServiceResult OnSetSubscriptionDurable(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId,
            uint lifetimeInHours,
            ref uint revisedLifetimeInHours)
        {
            revisedLifetimeInHours = 0;

            foreach (Subscription subscription in Server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription.Id == subscriptionId)
                {
                    if (subscription.SessionId != context.SessionId)
                    {
                        // user tries to access subscription of different session
                        return StatusCodes.BadUserAccessDenied;
                    }

                    ServiceResult result = subscription.SetSubscriptionDurable(lifetimeInHours, out uint revisedLifeTimeHours);

                    revisedLifetimeInHours = revisedLifeTimeHours;
                    return result;
                }
            }

            return StatusCodes.BadSubscriptionIdInvalid;
        }

        /// <summary>
        /// Called when a client gets the monitored items of a subscription.
        /// </summary>
        public ServiceResult OnGetMonitoredItems(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (inputArguments == null || inputArguments.Count != 1)
            {
                return StatusCodes.BadInvalidArgument;
            }

            uint? subscriptionId = inputArguments[0] as uint?;

            if (subscriptionId == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            uint[] serverHandles = null;
            uint[] clientHandles = null;

            foreach (Subscription subscription in Server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription.Id == subscriptionId)
                {
                    if (subscription.SessionId != context.SessionId)
                    {
                        // user tries to access subscription of different session
                        return StatusCodes.BadUserAccessDenied;
                    }

                    subscription.GetMonitoredItems(out serverHandles, out clientHandles);

                    outputArguments[0] = serverHandles;
                    outputArguments[1] = clientHandles;

                    return ServiceResult.Good;
                }
            }

            return StatusCodes.BadSubscriptionIdInvalid;
        }

        /// <summary>
        /// Called when a client initiates resending of all data monitored items in a Subscription.
        /// </summary>
        public ServiceResult OnResendData(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            if (inputArguments == null || inputArguments.Count != 1)
            {
                return StatusCodes.BadInvalidArgument;
            }

            uint? subscriptionId = inputArguments[0] as uint?;

            if (subscriptionId == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            foreach (Subscription subscription in Server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription.Id == subscriptionId)
                {
                    if (subscription.SessionId != context.SessionId)
                    {
                        // user tries to access subscription of different session
                        return StatusCodes.BadUserAccessDenied;
                    }

                    subscription.ResendData((OperationContext)((SystemContext)context)?.OperationContext);

                    return ServiceResult.Good;
                }
            }

            return StatusCodes.BadSubscriptionIdInvalid;
        }

        /// <summary>
        /// Called when a client locks the server.
        /// </summary>
        public ServiceResult OnLockServer(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServerSystemContext systemContext = context as ServerSystemContext;

            if (m_serverLockHolder != null)
            {
                if (m_serverLockHolder != systemContext.SessionId)
                {
                    return StatusCodes.BadSessionIdInvalid;
                }
            }

            m_serverLockHolder = systemContext.SessionId;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when a client locks the server.
        /// </summary>
        public ServiceResult OnUnlockServer(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServerSystemContext systemContext = context as ServerSystemContext;

            if (m_serverLockHolder != null)
            {
                if (m_serverLockHolder != systemContext.SessionId)
                {
                    return StatusCodes.BadSessionIdInvalid;
                }
            }

            m_serverLockHolder = null;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            var assembly = typeof(ArgumentCollection).GetTypeInfo().Assembly;
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Stack.Generated.Opc.Ua.PredefinedNodes.uanodes", assembly, true);
            return predefinedNodes;
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            BaseObjectState passiveNode = predefinedNode as BaseObjectState;

            if (passiveNode == null)
            {
                MethodState passiveMethod = predefinedNode as MethodState;

                if (passiveMethod == null)
                {
                    return predefinedNode;
                }

                if (passiveMethod.NodeId == MethodIds.ConditionType_ConditionRefresh)
                {
                    ConditionRefreshMethodState activeNode = new ConditionRefreshMethodState(passiveMethod.Parent);
                    activeNode.Create(context, passiveMethod);

                    // replace the node in the parent.
                    if (passiveMethod.Parent != null)
                    {
                        passiveMethod.Parent.ReplaceChild(context, activeNode);
                    }

                    activeNode.OnCall = OnConditionRefresh;

                    return activeNode;
                }
                else if (passiveMethod.NodeId == MethodIds.ConditionType_ConditionRefresh2)
                {
                    ConditionRefresh2MethodState activeNode = new ConditionRefresh2MethodState(passiveMethod.Parent);
                    activeNode.Create(context, passiveMethod);

                    // replace the node in the parent.
                    if (passiveMethod.Parent != null)
                    {
                        passiveMethod.Parent.ReplaceChild(context, activeNode);
                    }

                    activeNode.OnCall = OnConditionRefresh2;

                    return activeNode;
                }


                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || typeId.IdType != IdType.Numeric)
            {
                return predefinedNode;
            }

            switch ((uint)typeId.Identifier)
            {
                case ObjectTypes.ServerType:
                {
                    if (passiveNode is ServerObjectState)
                    {
                        break;
                    }

                    ServerObjectState activeNode = new ServerObjectState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    // add the server object as the root notifier.
                    AddRootNotifier(activeNode);

                    // replace the node in the parent.
                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }

                case ObjectTypes.HistoryServerCapabilitiesType:
                {
                    if (passiveNode is HistoryServerCapabilitiesState)
                    {
                        break;
                    }

                    HistoryServerCapabilitiesState activeNode = new HistoryServerCapabilitiesState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    // replace the node in the parent.
                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }
            }

            return predefinedNode;
        }

        /// <summary>
        /// Handles a request to refresh conditions for a subscription.
        /// </summary>
        private ServiceResult OnConditionRefresh(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId)
        {
            ServerSystemContext systemContext = context as ServerSystemContext;

            if (systemContext == null)
            {
                systemContext = this.SystemContext;
            }

            Server.ConditionRefresh(systemContext.OperationContext, subscriptionId);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles a request to refresh conditions for a subscription and specific monitored item.
        /// </summary>
        private ServiceResult OnConditionRefresh2(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint subscriptionId,
            uint monitoredItemId)
        {
            ServerSystemContext systemContext = context as ServerSystemContext;

            if (systemContext == null)
            {
                systemContext = this.SystemContext;
            }

            Server.ConditionRefresh2(systemContext.OperationContext, subscriptionId, monitoredItemId);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns true of the node is a diagnostics node.
        /// </summary>
        private bool IsDiagnosticsNode(NodeState node)
        {
            if (node == null)
            {
                return false;
            }

            if (!IsDiagnosticsStructureNode(node))
            {
                BaseInstanceState instance = node as BaseInstanceState;

                if (instance == null)
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
        private bool IsDiagnosticsStructureNode(NodeState node)
        {
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance == null)
            {
                return false;
            }

            NodeId typeId = instance.TypeDefinitionId;

            if (typeId == null || typeId.IdType != IdType.Numeric || typeId.NamespaceIndex != 0)
            {
                return false;
            }

            switch ((uint)typeId.Identifier)
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
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Force out of band diagnostics update after a change of diagnostics variables.
        /// </summary>
        public void ForceDiagnosticsScan()
        {
            m_lastDiagnosticsScanTime = DateTime.MinValue;
        }

        /// <summary>
        /// True is diagnostics are currently enabled.
        /// </summary>
        public bool DiagnosticsEnabled => m_diagnosticsEnabled;

        /// <summary>
        /// Sets the flag controlling whether diagnostics is enabled for the server.
        /// </summary>
        public void SetDiagnosticsEnabled(ServerSystemContext context, bool enabled)
        {
            List<NodeState> nodesToDelete = new List<NodeState>();

            lock (Lock)
            {
                if (enabled == m_diagnosticsEnabled)
                {
                    return;
                }

                m_diagnosticsEnabled = enabled;

                if (!enabled)
                {
                    // stop scans.
                    if (m_diagnosticsScanTimer != null)
                    {
                        m_diagnosticsScanTimer.Dispose();
                        m_diagnosticsScanTimer = null;
                    }

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
                            nodesToDelete.Add(m_sessions[ii].Value.Variable);
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
                    ServerDiagnosticsState diagnosticsNode = (ServerDiagnosticsState)FindPredefinedNode(
                        ObjectIds.Server_ServerDiagnostics,
                        typeof(ServerDiagnosticsState));

                    // clear arrays.
                    if (diagnosticsNode != null)
                    {
                        if (diagnosticsNode.SamplingIntervalDiagnosticsArray != null)
                        {
                            diagnosticsNode.SamplingIntervalDiagnosticsArray.Value = null;
                            diagnosticsNode.SamplingIntervalDiagnosticsArray.StatusCode = StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SamplingIntervalDiagnosticsArray.Timestamp = DateTime.UtcNow;
                        }

                        if (diagnosticsNode.SubscriptionDiagnosticsArray != null)
                        {
                            diagnosticsNode.SubscriptionDiagnosticsArray.Value = null;
                            diagnosticsNode.SubscriptionDiagnosticsArray.StatusCode = StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SubscriptionDiagnosticsArray.Timestamp = DateTime.UtcNow;
                        }

                        if (diagnosticsNode.SessionsDiagnosticsSummary != null)
                        {
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionDiagnosticsArray.Value = null;
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionDiagnosticsArray.StatusCode = StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionDiagnosticsArray.Timestamp = DateTime.UtcNow;
                        }

                        if (diagnosticsNode.SessionsDiagnosticsSummary != null)
                        {
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionSecurityDiagnosticsArray.Value = null;
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionSecurityDiagnosticsArray.StatusCode = StatusCodes.BadWaitingForInitialData;
                            diagnosticsNode.SessionsDiagnosticsSummary.SessionSecurityDiagnosticsArray.Timestamp = DateTime.UtcNow;
                        }
                    }

                    DoScan(true);
                }
            }

            for (int ii = 0; ii < nodesToDelete.Count; ii++)
            {
                DeleteNode(context, nodesToDelete[ii].NodeId);
            }
        }

        /// <summary>
        /// Creates the diagnostics node for the server.
        /// </summary>
        public void CreateServerDiagnostics(
            ServerSystemContext systemContext,
            ServerDiagnosticsSummaryDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback)
        {
            lock (Lock)
            {
                // get the node.
                ServerDiagnosticsSummaryState diagnosticsNode = (ServerDiagnosticsSummaryState)FindPredefinedNode(
                    VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary,
                    typeof(ServerDiagnosticsSummaryState));

                // wrap diagnostics in a thread safe object.
                ServerDiagnosticsSummaryValue diagnosticsValue = new ServerDiagnosticsSummaryValue(
                    diagnosticsNode,
                    diagnostics,
                    Lock);

                // must ensure the first update gets sent.
                diagnosticsValue.Value = null;
                diagnosticsValue.Error = StatusCodes.BadWaitingForInitialData;
                diagnosticsValue.CopyPolicy = Opc.Ua.VariableCopyPolicy.Never;
                diagnosticsValue.OnBeforeRead = OnBeforeReadDiagnostics;
                // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                diagnosticsNode.OnReadUserRolePermissions = OnReadUserRolePermissions;

                m_serverDiagnostics = diagnosticsValue;
                m_serverDiagnosticsCallback = updateCallback;

                // set up handler for session diagnostics array.
                SessionDiagnosticsArrayState array1 = (SessionDiagnosticsArrayState)FindPredefinedNode(
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                    typeof(SessionDiagnosticsArrayState));

                if (array1 != null)
                {
                    array1.OnSimpleReadValue = OnReadDiagnosticsArray;
                }

                // set up handler for session security diagnostics array.
                SessionSecurityDiagnosticsArrayState array2 = (SessionSecurityDiagnosticsArrayState)FindPredefinedNode(
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray,
                    typeof(SessionSecurityDiagnosticsArrayState));

                if (array2 != null)
                {
                    array2.OnSimpleReadValue = OnReadDiagnosticsArray;
                }

                // set up handler for subscription security diagnostics array.
                SubscriptionDiagnosticsArrayState array3 = (SubscriptionDiagnosticsArrayState)FindPredefinedNode(
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                    typeof(SubscriptionDiagnosticsArrayState));

                if (array3 != null)
                {
                    array3.OnSimpleReadValue = OnReadDiagnosticsArray;
                    // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                    array3.OnReadUserRolePermissions = OnReadUserRolePermissions;
                }

                // send initial update.
                DoScan(true);
            }
        }

        /// <summary>
        /// Creates the diagnostics node for a subscription.
        /// </summary>
        public NodeId CreateSessionDiagnostics(
            ServerSystemContext systemContext,
            SessionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback,
            SessionSecurityDiagnosticsDataType securityDiagnostics,
            NodeValueSimpleEventHandler updateSecurityCallback)
        {
            NodeId nodeId = null;

            lock (Lock)
            {
                SessionDiagnosticsObjectState sessionNode = new SessionDiagnosticsObjectState(null);

                // create a new instance and assign ids.
                nodeId = CreateNode(
                    systemContext,
                    null,
                    ReferenceTypeIds.HasComponent,
                    new QualifiedName(diagnostics.SessionName),
                    sessionNode);

                diagnostics.SessionId = nodeId;
                securityDiagnostics.SessionId = nodeId;

                // check if diagnostics have been enabled.
                if (!m_diagnosticsEnabled)
                {
                    return nodeId;
                }

                // add reference to session summary object.
                sessionNode.AddReference(
                    ReferenceTypeIds.HasComponent,
                    true,
                    ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary);

                // add reference from session summary object.
                SessionsDiagnosticsSummaryState summary = (SessionsDiagnosticsSummaryState)FindPredefinedNode(
                    ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary,
                    typeof(SessionsDiagnosticsSummaryState));

                if (summary != null)
                {
                    summary.AddReference(ReferenceTypeIds.HasComponent, false, sessionNode.NodeId);
                }

                // Hook the OnReadUserRolePermissions callback to control which user roles can access the services on this node
                sessionNode.OnReadUserRolePermissions = OnReadUserRolePermissions;

                // initialize diagnostics node.
                SessionDiagnosticsVariableState diagnosticsNode = sessionNode.CreateChild(
                   systemContext,
                   BrowseNames.SessionDiagnostics) as SessionDiagnosticsVariableState;

                // wrap diagnostics in a thread safe object.
                SessionDiagnosticsVariableValue diagnosticsValue = new SessionDiagnosticsVariableValue(
                    diagnosticsNode,
                    diagnostics,
                    Lock);

                // must ensure the first update gets sent.
                diagnosticsValue.Value = null;
                diagnosticsValue.Error = StatusCodes.BadWaitingForInitialData;
                diagnosticsValue.CopyPolicy = Opc.Ua.VariableCopyPolicy.Never;
                diagnosticsValue.OnBeforeRead = OnBeforeReadDiagnostics;

                // initialize security diagnostics node.
                SessionSecurityDiagnosticsState securityDiagnosticsNode = sessionNode.CreateChild(
                   systemContext,
                   BrowseNames.SessionSecurityDiagnostics) as SessionSecurityDiagnosticsState;

                // wrap diagnostics in a thread safe object.
                SessionSecurityDiagnosticsValue securityDiagnosticsValue = new SessionSecurityDiagnosticsValue(
                    securityDiagnosticsNode,
                    securityDiagnostics,
                    Lock);

                // must ensure the first update gets sent.
                securityDiagnosticsValue.Value = null;
                securityDiagnosticsValue.Error = StatusCodes.BadWaitingForInitialData;
                securityDiagnosticsValue.CopyPolicy = Opc.Ua.VariableCopyPolicy.Never;
                securityDiagnosticsValue.OnBeforeRead = OnBeforeReadDiagnostics;

                // save the session.
                SessionDiagnosticsData sessionData = new SessionDiagnosticsData(
                    sessionNode,
                    diagnosticsValue,
                    updateCallback,
                    securityDiagnosticsValue,
                    updateSecurityCallback);

                m_sessions.Add(sessionData);

                // send initial update.
                DoScan(true);
            }

            return nodeId;
        }

        /// <summary>
        /// Delete the diagnostics node for a session.
        /// </summary>
        public void DeleteSessionDiagnostics(
            ServerSystemContext systemContext,
            NodeId nodeId)
        {
            lock (Lock)
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
                    m_serverLockHolder = null;
                }
            }

            DeleteNode(systemContext, nodeId);
        }

        /// <summary>
        /// Creates the diagnostics node for a subscription.
        /// </summary>
        public NodeId CreateSubscriptionDiagnostics(
            ServerSystemContext systemContext,
            SubscriptionDiagnosticsDataType diagnostics,
            NodeValueSimpleEventHandler updateCallback)
        {
            NodeId nodeId = null;

            lock (Lock)
            {
                // check if diagnostics have been enabled.
                if (!m_diagnosticsEnabled)
                {
                    return null;
                }

                SubscriptionDiagnosticsState diagnosticsNode = new SubscriptionDiagnosticsState(null);

                // create a new instance and assign ids.
                nodeId = CreateNode(
                    systemContext,
                    null,
                    ReferenceTypeIds.HasComponent,
                    new QualifiedName(diagnostics.SubscriptionId.ToString()),
                    diagnosticsNode);

                // add reference to subscription array.
                diagnosticsNode.AddReference(
                    ReferenceTypeIds.HasComponent,
                    true,
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

                // wrap diagnostics in a thread safe object.
                SubscriptionDiagnosticsValue diagnosticsValue = new SubscriptionDiagnosticsValue(diagnosticsNode, diagnostics, Lock);
                diagnosticsValue.CopyPolicy = Opc.Ua.VariableCopyPolicy.Never;
                diagnosticsValue.OnBeforeRead = OnBeforeReadDiagnostics;

                // must ensure the first update gets sent.
                diagnosticsValue.Value = null;
                diagnosticsValue.Error = StatusCodes.BadWaitingForInitialData;

                m_subscriptions.Add(new SubscriptionDiagnosticsData(diagnosticsValue, updateCallback));

                // add reference from subscription array.
                SubscriptionDiagnosticsArrayState array = (SubscriptionDiagnosticsArrayState)FindPredefinedNode(
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                    typeof(SubscriptionDiagnosticsArrayState));

                if (array != null)
                {
                    array.AddReference(ReferenceTypeIds.HasComponent, false, diagnosticsNode.NodeId);
                }

                // add reference to session subscription array.
                diagnosticsNode.AddReference(
                    ReferenceTypeIds.HasComponent,
                    true,
                    diagnostics.SessionId);

                // add reference from session subscription array.
                SessionDiagnosticsObjectState sessionNode = (SessionDiagnosticsObjectState)FindPredefinedNode(
                    diagnostics.SessionId,
                    typeof(SessionDiagnosticsObjectState));

                if (sessionNode != null)
                {
                    // add reference from subscription array.
                    array = (SubscriptionDiagnosticsArrayState)sessionNode.CreateChild(
                        systemContext,
                        BrowseNames.SubscriptionDiagnosticsArray);

                    if (array != null)
                    {
                        array.AddReference(ReferenceTypeIds.HasComponent, false, diagnosticsNode.NodeId);
                    }
                }

                // send initial update.
                DoScan(true);
            }

            return nodeId;
        }

        /// <summary>
        /// Delete the diagnostics node for a subscription.
        /// </summary>
        public void DeleteSubscriptionDiagnostics(
            ServerSystemContext systemContext,
            NodeId nodeId)
        {
            lock (Lock)
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

            DeleteNode(systemContext, nodeId);
        }

        /// <summary>
        /// Gets the default history capabilities object.
        /// </summary>
        public HistoryServerCapabilitiesState GetDefaultHistoryCapabilities()
        {
            lock (Lock)
            {
                if (m_historyCapabilities != null)
                {
                    return m_historyCapabilities;
                }

                // search the Node in PredefinedNodes.
                HistoryServerCapabilitiesState historyServerCapabilitiesNode = (HistoryServerCapabilitiesState)FindPredefinedNode(
                    ObjectIds.HistoryServerCapabilities,
                    typeof(HistoryServerCapabilitiesState));

                if (historyServerCapabilitiesNode == null)
                {
                    // create new node if not found.
                    historyServerCapabilitiesNode = new HistoryServerCapabilitiesState(null);

                    NodeId nodeId = CreateNode(
                        SystemContext,
                        null,
                        ReferenceTypeIds.HasComponent,
                        new QualifiedName(BrowseNames.HistoryServerCapabilities),
                        historyServerCapabilitiesNode);

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

                    NodeState parent = FindPredefinedNode(ObjectIds.Server_ServerCapabilities, typeof(ServerCapabilitiesState));

                    if (parent != null)
                    {
                        parent.AddReference(ReferenceTypes.HasComponent, false, historyServerCapabilitiesNode.NodeId);
                        historyServerCapabilitiesNode.AddReference(ReferenceTypes.HasComponent, true, parent.NodeId);
                    }

                    AddPredefinedNode(SystemContext, historyServerCapabilitiesNode);
                }

                m_historyCapabilities = historyServerCapabilitiesNode;
                return m_historyCapabilities;
            }
        }

        /// <summary>
        /// Adds an aggregate function to the server capabilities object.
        /// </summary>
        public void AddAggregateFunction(NodeId aggregateId, string aggregateName, bool isHistorical)
        {
            lock (Lock)
            {
                FolderState state = new FolderState(null);

                state.SymbolicName = aggregateName;
                state.ReferenceTypeId = ReferenceTypes.HasComponent;
                state.TypeDefinitionId = ObjectTypeIds.AggregateFunctionType;
                state.NodeId = aggregateId;
                state.BrowseName = new QualifiedName(aggregateName, aggregateId.NamespaceIndex);
                state.DisplayName = state.BrowseName.Name;
                state.WriteMask = AttributeWriteMask.None;
                state.UserWriteMask = AttributeWriteMask.None;
                state.EventNotifier = EventNotifiers.None;

                NodeState folder = FindPredefinedNode(ObjectIds.Server_ServerCapabilities_AggregateFunctions, typeof(BaseObjectState));

                if (folder != null)
                {
                    folder.AddReference(ReferenceTypes.Organizes, false, state.NodeId);
                    state.AddReference(ReferenceTypes.Organizes, true, folder.NodeId);
                }

                if (isHistorical)
                {
                    folder = FindPredefinedNode(ObjectIds.HistoryServerCapabilities_AggregateFunctions, typeof(BaseObjectState));

                    if (folder != null)
                    {
                        folder.AddReference(ReferenceTypes.Organizes, false, state.NodeId);
                        state.AddReference(ReferenceTypes.Organizes, true, folder.NodeId);
                    }
                }

                AddPredefinedNode(SystemContext, state);
            }
        }

        /// <summary>
        /// Updates the server diagnostics summary structure.
        /// </summary>
        private bool UpdateServerDiagnosticsSummary()
        {
            // get the latest snapshot.
            object value = null;

            ServiceResult result = m_serverDiagnosticsCallback(
                SystemContext,
                m_serverDiagnostics.Variable,
                ref value);

            ServerDiagnosticsSummaryDataType newValue = value as ServerDiagnosticsSummaryDataType;

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
            object value = null;

            ServiceResult result = diagnostics.UpdateCallback(
                SystemContext,
                diagnostics.Value.Variable,
                ref value);

            SessionDiagnosticsDataType newValue = value as SessionDiagnosticsDataType;

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
            object value = null;

            ServiceResult result = diagnostics.SecurityUpdateCallback(
                SystemContext,
                diagnostics.SecurityValue.Variable,
                ref value);

            SessionSecurityDiagnosticsDataType newValue = value as SessionSecurityDiagnosticsDataType;

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
            object value = null;

            ServiceResult result = diagnostics.UpdateCallback(
                SystemContext,
                diagnostics.Value.Variable,
                ref value);

            SubscriptionDiagnosticsDataType newValue = value as SubscriptionDiagnosticsDataType;

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
        /// Filter out the members which corespond to users that are not allowed to see their contents
        /// Current user is allowed to read its data, together with users which have permissions
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="sessionId"></param>
        /// <param name="context"></param>
        /// <param name="index"></param>
        private void FilterOutUnAuthorized<T>(IList<T> list, NodeId sessionId, ISystemContext context, int index)
        {
            if ((sessionId != context.SessionId) &&
                    !HasApplicationSecureAdminAccess(context))
            {
                list[index] = default(T);
            }
        }

        /// <summary>
        /// Set custom role permissions for desired node
        /// </summary>
        /// <param name="context"></param>
        /// <param name="node"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private ServiceResult OnReadUserRolePermissions(
            ISystemContext context,
            NodeState node,
            ref RolePermissionTypeCollection value)
        {
            bool admitUser;

            if ((node.NodeId == VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary) ||
                 (node.NodeId == VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray))
            {
                admitUser = HasApplicationSecureAdminAccess(context);
            }
            else
            {
                admitUser = (node.NodeId == context.SessionId) ||
                            HasApplicationSecureAdminAccess(context);
            }

            if (admitUser)
            {
                var rolePermissionTypes = from roleId in m_kWellKnownRoles
                                          select new RolePermissionType() {
                                              RoleId = roleId,
                                              Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.ReadRolePermissions | PermissionType.Write)
                                          };

                value = new RolePermissionTypeCollection(rolePermissionTypes);
            }
            else
            {
                var rolePermissionTypes = from roleId in m_kWellKnownRoles
                                          select new RolePermissionType() {
                                              RoleId = roleId,
                                              Permissions = (uint)PermissionType.None
                                          };

                value = new RolePermissionTypeCollection(rolePermissionTypes);

            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Does a scan before the diagnostics are read.
        /// </summary>
        private void OnBeforeReadDiagnostics(
            ISystemContext context,
            BaseVariableValue variable,
            NodeState component)
        {
            lock (Lock)
            {
                if (!m_diagnosticsEnabled)
                {
                    return;
                }

                if (DateTime.UtcNow < m_lastDiagnosticsScanTime.AddSeconds(1))
                {
                    return;
                }

                DoScan(true);
            }
        }

        /// <summary>
        /// Does a scan before the diagnostics are read.
        /// </summary>
        private ServiceResult OnReadDiagnosticsArray(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            lock (Lock)
            {
                if (!m_diagnosticsEnabled)
                {
                    return StatusCodes.BadOutOfService;
                }

                if (DateTime.UtcNow < m_lastDiagnosticsScanTime.AddSeconds(1))
                {
                    // diagnostic nodes already scanned.
                    return ServiceResult.Good;
                }

                if (node.NodeId == VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray)
                {
                    // read session diagnostics.
                    SessionDiagnosticsDataType[] sessionArray = new SessionDiagnosticsDataType[m_sessions.Count];

                    for (int ii = 0; ii < m_sessions.Count; ii++)
                    {
                        SessionDiagnosticsData diagnostics = m_sessions[ii];
                        UpdateSessionDiagnostics(context, diagnostics, sessionArray, ii);
                    }
                    sessionArray = sessionArray.Where(s => s != null).ToArray();

                    value = sessionArray;
                }
                else if (node.NodeId == VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray)
                {
                    // read session security diagnostics.
                    SessionSecurityDiagnosticsDataType[] sessionSecurityArray = new SessionSecurityDiagnosticsDataType[m_sessions.Count];

                    for (int ii = 0; ii < m_sessions.Count; ii++)
                    {
                        UpdateSessionSecurityDiagnostics(context, m_sessions[ii], sessionSecurityArray, ii);
                    }
                    sessionSecurityArray = sessionSecurityArray.Where(s => s != null).ToArray();

                    value = sessionSecurityArray;
                }
                else if (node.NodeId == VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray)
                {
                    // read subscription diagnostics.
                    SubscriptionDiagnosticsDataType[] subscriptionArray = new SubscriptionDiagnosticsDataType[m_subscriptions.Count];

                    for (int ii = 0; ii < m_subscriptions.Count; ii++)
                    {
                        UpdateSubscriptionDiagnostics(context, m_subscriptions[ii], subscriptionArray, ii);
                    }
                    subscriptionArray = subscriptionArray.Where(s => s != null).ToArray();

                    value = subscriptionArray;
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <param name="context"></param>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        private bool HasApplicationSecureAdminAccess(ISystemContext context)
        {
            OperationContext operationContext = (context as SystemContext)?.OperationContext as OperationContext;
            if (operationContext != null)
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                {
                    return false;
                }

                SystemConfigurationIdentity user = context.UserIdentity as SystemConfigurationIdentity;
                if (user == null ||
                    user.TokenType == UserTokenType.Anonymous ||
                    !user.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin))
                {
                    return false;
                }

                return true;
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
                lock (Lock)
                {
                    if (!m_diagnosticsEnabled || m_doScanBusy)
                    {
                        return;
                    }

                    try
                    {
                        m_doScanBusy = true;

                        m_lastDiagnosticsScanTime = DateTime.UtcNow;

                        // update server diagnostics.
                        UpdateServerDiagnosticsSummary();

                        // update session diagnostics.
                        bool sessionsChanged = alwaysUpdateArrays != null;
                        SessionDiagnosticsDataType[] sessionArray = new SessionDiagnosticsDataType[m_sessions.Count];

                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            SessionDiagnosticsData diagnostics = m_sessions[ii];

                            if (UpdateSessionDiagnostics(null, diagnostics, sessionArray, ii))
                            {
                                sessionsChanged = true;
                            }
                        }

                        // check of the session diagnostics array node needs to be updated.
                        SessionDiagnosticsArrayState sessionsNode = (SessionDiagnosticsArrayState)FindPredefinedNode(
                            VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray,
                            typeof(SessionDiagnosticsArrayState));

                        if (sessionsNode != null && (sessionsNode.Value == null || StatusCode.IsBad(sessionsNode.StatusCode) || sessionsChanged))
                        {
                            sessionsNode.Value = sessionArray;
                            sessionsNode.ClearChangeMasks(SystemContext, false);
                        }

                        bool sessionsSecurityChanged = alwaysUpdateArrays != null;
                        SessionSecurityDiagnosticsDataType[] sessionSecurityArray = new SessionSecurityDiagnosticsDataType[m_sessions.Count];

                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            SessionDiagnosticsData diagnostics = m_sessions[ii];

                            if (UpdateSessionSecurityDiagnostics(null, diagnostics, sessionSecurityArray, ii))
                            {
                                sessionsSecurityChanged = true;
                            }
                        }

                        // check of the array node needs to be updated.
                        SessionSecurityDiagnosticsArrayState sessionsSecurityNode = (SessionSecurityDiagnosticsArrayState)FindPredefinedNode(
                            VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionSecurityDiagnosticsArray,
                            typeof(SessionSecurityDiagnosticsArrayState));

                        if (sessionsSecurityNode != null && (sessionsSecurityNode.Value == null || StatusCode.IsBad(sessionsSecurityNode.StatusCode) || sessionsSecurityChanged))
                        {
                            sessionsSecurityNode.Value = sessionSecurityArray;
                            sessionsSecurityNode.ClearChangeMasks(SystemContext, false);
                        }

                        bool subscriptionsChanged = alwaysUpdateArrays != null;
                        SubscriptionDiagnosticsDataType[] subscriptionArray = new SubscriptionDiagnosticsDataType[m_subscriptions.Count];

                        for (int ii = 0; ii < m_subscriptions.Count; ii++)
                        {
                            SubscriptionDiagnosticsData diagnostics = m_subscriptions[ii];

                            if (UpdateSubscriptionDiagnostics(null, diagnostics, subscriptionArray, ii))
                            {
                                subscriptionsChanged = true;
                            }
                        }

                        // check of the subscription node needs to be updated.
                        SubscriptionDiagnosticsArrayState subscriptionsNode = (SubscriptionDiagnosticsArrayState)FindPredefinedNode(
                            VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                            typeof(SubscriptionDiagnosticsArrayState));

                        if (subscriptionsNode != null && (subscriptionsNode.Value == null || StatusCode.IsBad(subscriptionsNode.StatusCode) || subscriptionsChanged))
                        {
                            subscriptionsNode.Value = subscriptionArray;
                            subscriptionsNode.ClearChangeMasks(SystemContext, false);
                        }

                        for (int ii = 0; ii < m_sessions.Count; ii++)
                        {
                            SessionDiagnosticsData diagnostics = m_sessions[ii];
                            List<SubscriptionDiagnosticsDataType> subscriptionDiagnosticsArray = new List<SubscriptionDiagnosticsDataType>();

                            NodeId sessionId = diagnostics.Summary.NodeId;

                            for (int jj = 0; jj < m_subscriptions.Count; jj++)
                            {
                                SubscriptionDiagnosticsData subscriptionDiagnostics = m_subscriptions[jj];

                                if (subscriptionDiagnostics.Value.Value == null)
                                {
                                    continue;
                                }

                                if (subscriptionDiagnostics.Value.Value.SessionId != sessionId)
                                {
                                    continue;
                                }

                                subscriptionDiagnosticsArray.Add(subscriptionDiagnostics.Value.Value);
                            }

                            // update session subscription array.
                            subscriptionsNode = (SubscriptionDiagnosticsArrayState)diagnostics.Summary.CreateChild(
                                SystemContext,
                                BrowseNames.SubscriptionDiagnosticsArray);

                            if (subscriptionsNode != null && (subscriptionsNode.Value == null || StatusCode.IsBad(subscriptionsNode.StatusCode) || subscriptionsChanged))
                            {
                                subscriptionsNode.Value = subscriptionDiagnosticsArray.ToArray();
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
                Utils.LogError(e, "Unexpected error during diagnostics scan.");
            }
        }

        /// <summary>
        /// Validates the view description passed to a browse request (throws on error).
        /// </summary>
        protected override void ValidateViewDescription(ServerSystemContext context, ViewDescription view)
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
            MonitoredItem monitoredItem)
        {
            // check if the variable needs to be sampled.
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (variable != null && variable.MinimumSamplingInterval > 0)
                {
                    CreateSampledItem(monitoredItem.SamplingInterval, monitoredItem);
                }
            }

            // check if diagnostics collection needs to be turned one.
            if (IsDiagnosticsNode(handle.Node))
            {
                monitoredItem.AlwaysReportUpdates = IsDiagnosticsStructureNode(handle.Node);

                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    m_diagnosticsMonitoringCount++;

                    if (m_diagnosticsScanTimer == null)
                    {
                        m_diagnosticsScanTimer = new Timer(DoScan, null, 1000, 1000);
                    }

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
        protected override void OnMonitoredItemDeleted(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem)
        {
            // check if diagnostics collection needs to be turned off.
            if (IsDiagnosticsNode(handle.Node))
            {
                if (monitoredItem.MonitoringMode != MonitoringMode.Disabled)
                {
                    m_diagnosticsMonitoringCount--;

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
            }

            // check if sampling needs to be turned off.
            if (monitoredItem.AttributeId == Attributes.Value)
            {
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (variable != null && variable.MinimumSamplingInterval > 0)
                {
                    DeleteSampledItem(monitoredItem);
                }
            }
        }

        /// <summary>
        /// Called after changing the MonitoringMode for a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="previousMode">The previous monitoring mode.</param>
        /// <param name="monitoringMode">The current monitoring mode.</param>
        protected override void OnMonitoringModeChanged(
            ServerSystemContext context,
            NodeHandle handle,
            MonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode)
        {
            if (previousMode != MonitoringMode.Disabled)
            {
                m_diagnosticsMonitoringCount--;
            }

            if (monitoringMode != MonitoringMode.Disabled)
            {
                m_diagnosticsMonitoringCount++;
            }

            if (m_diagnosticsMonitoringCount == 0 && m_diagnosticsScanTimer != null)
            {
                if (m_diagnosticsScanTimer != null)
                {
                    m_diagnosticsScanTimer.Dispose();
                    m_diagnosticsScanTimer = null;
                }
            }
            else
            {
                if (m_diagnosticsScanTimer != null)
                {
                    m_diagnosticsScanTimer = new Timer(DoScan, null, 1000, 1000);
                }
            }
        }
        #endregion

        #region Node Access Functions
#if V1_Methods
        /// <summary>
        /// Returns an index for the NamespaceURI (Adds it to the server namespace table if it does not already exist).
        /// </summary>
        /// <remarks>
        /// Returns the server's default index (1) if the namespaceUri is empty or null. 
        /// </remarks>
        public ushort GetNamespaceIndex(string namespaceUri)
        {
            int namespaceIndex = 1;

            if (!String.IsNullOrEmpty(namespaceUri))
            {
                namespaceIndex = Server.NamespaceUris.GetIndex(namespaceUri);

                if (namespaceIndex == -1)
                {
                    namespaceIndex = Server.NamespaceUris.Append(namespaceUri);
                }
            }

            return (ushort)namespaceIndex;
        }

        public NodeId FindTargetId(NodeId sourceId, NodeId referenceTypeId, bool isInverse, QualifiedName browseName)
        {
            return null;
        }
    
        public ILocalNode GetLocalNode(NodeId nodeId)
        {
            return null;
        }

        public ILocalNode GetTargetNode(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            return null;
        }

        private ILocalNode GetTargetNode(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            return null;
        }

        public void AttachNode(ILocalNode node)
        {
        }

        public void ReplaceNode(ILocalNode existingNode, ILocalNode newNode)
        {
        }

        public void DeleteNode(NodeId nodeId, bool deleteChildren, bool silent)
        {
        }       

        public ILocalNode ReferenceSharedNode(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            QualifiedName browseName)
        {
            return null;
        }

        public ILocalNode UnreferenceSharedNode(
            ILocalNode source,
            NodeId referenceTypeId,
            bool isInverse,
            QualifiedName browseName)
        {
            return null;
        }
        
        public NodeId CreateUniqueNodeId()
        {
            return null;
        }

        public NodeId CreateObject(
            NodeId parentId,
            NodeId referenceTypeId,
            NodeId nodeId,
            QualifiedName browseName,
            ObjectAttributes attributes,
            ExpandedNodeId typeDefinitionId)
        {
            return null;
        }

        public NodeId CreateObjectType(
            NodeId parentId,
            NodeId nodeId,
            QualifiedName browseName,
            ObjectTypeAttributes attributes)
        {
            return null;
        }
                
        public NodeId CreateVariable(
            NodeId parentId,
            NodeId referenceTypeId,
            NodeId nodeId,
            QualifiedName browseName,
            VariableAttributes attributes,
            ExpandedNodeId typeDefinitionId)
        {
            return null;
        }

        public NodeId CreateVariableType(
            NodeId                 parentId,
            NodeId                 nodeId,
            QualifiedName          browseName,
            VariableTypeAttributes attributes)
        {
            return null;
        }

        public NodeId CreateMethod(
            NodeId           parentId,
            NodeId           referenceTypeId,
            NodeId           nodeId,
            QualifiedName    browseName,
            MethodAttributes attributes)
        {
            return null;
        }
#endif
        #endregion

        #region SessionDiagnosticsData Class
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
        #endregion

        #region SubscriptionDiagnosticsData Class
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
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates a new sampled item.
        /// </summary>
        private void CreateSampledItem(double samplingInterval, MonitoredItem monitoredItem)
        {
            m_sampledItems.Add(monitoredItem);

            if (m_samplingTimer == null)
            {
                m_samplingTimer = new Timer(DoSample, null, (int)m_minimumSamplingInterval, (int)m_minimumSamplingInterval);
            }
        }

        /// <summary>
        /// Deletes a sampled item.
        /// </summary>
        private void DeleteSampledItem(MonitoredItem monitoredItem)
        {
            for (int ii = 0; ii < m_sampledItems.Count; ii++)
            {
                if (Object.ReferenceEquals(monitoredItem, m_sampledItems[ii]))
                {
                    m_sampledItems.RemoveAt(ii);
                    break;
                }
            }

            if (m_sampledItems.Count == 0)
            {
                if (m_samplingTimer != null)
                {
                    m_samplingTimer.Dispose();
                    m_samplingTimer = null;
                }
            }
        }

        /// <summary>
        /// Polls each monitored item which requires sample. 
        /// </summary>
        private void DoSample(object state)
        {
            try
            {
                lock (Lock)
                {
                    for (int ii = 0; ii < m_sampledItems.Count; ii++)
                    {
                        MonitoredItem monitoredItem = m_sampledItems[ii];

                        // get the handle.
                        NodeHandle handle = monitoredItem.ManagerHandle as NodeHandle;

                        if (handle == null)
                        {
                            continue;
                        }

                        // check if it is time to sample.
                        if (monitoredItem.TimeToNextSample > m_minimumSamplingInterval)
                        {
                            continue;
                        }

                        // read the value.
                        DataValue value = new DataValue();

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
                Utils.LogError(e, "Unexpected error during diagnostics scan.");
            }
        }
        #endregion

        #region Private Fields
        private ushort m_namespaceIndex;
        private long m_lastUsedId;
        private Timer m_diagnosticsScanTimer;
        private int m_diagnosticsMonitoringCount;
        private bool m_diagnosticsEnabled;
        private bool m_doScanBusy;
        private DateTime m_lastDiagnosticsScanTime;
        private ServerDiagnosticsSummaryValue m_serverDiagnostics;
        private NodeValueSimpleEventHandler m_serverDiagnosticsCallback;
        private List<SessionDiagnosticsData> m_sessions;
        private List<SubscriptionDiagnosticsData> m_subscriptions;
        private NodeId m_serverLockHolder;
        private Timer m_samplingTimer;
        private List<MonitoredItem> m_sampledItems;
        private double m_minimumSamplingInterval;
        private HistoryServerCapabilitiesState m_historyCapabilities;
        #endregion

        #region Private Readonly Fields
        private static readonly NodeId[] m_kWellKnownRoles = {
            ObjectIds.WellKnownRole_Anonymous,
            ObjectIds.WellKnownRole_AuthenticatedUser,
            ObjectIds.WellKnownRole_ConfigureAdmin,
            ObjectIds.WellKnownRole_Engineer,
            ObjectIds.WellKnownRole_Observer,
            ObjectIds.WellKnownRole_Operator,
            ObjectIds.WellKnownRole_SecurityAdmin,
            ObjectIds.WellKnownRole_Supervisor };
        #endregion
    }
}
