/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace Alarms
{
    /// <summary>
    /// The factory for the Alarm Node Manager.
    /// </summary>
    public class AlarmNodeManagerFactory : INodeManagerFactory
    {
        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new AlarmNodeManager(server, configuration, [.. NamespacesUris]);
        }

        /// <inheritdoc/>
        public StringCollection NamespacesUris
        {
            get
            {
                const string uri = Namespaces.Alarms;
                const string instanceUri = uri + "Instance";
                return [uri, instanceUri];
            }
        }
    }

    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class AlarmNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public AlarmNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string[] namespaceUris)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<AlarmNodeManager>(),
                  namespaceUris)
        {
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeTimer();
            }
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null &&
                instance.Parent.NodeId.Identifier is string id)
            {
                return new NodeId(
                    id + "_" + instance.SymbolicName,
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                if (!externalReferences.TryGetValue(
                    ObjectIds.ObjectsFolder,
                    out IList<IReference> references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = [];
                }

                try
                {
                    const string alarmsName = "Alarms";
                    const string alarmsNodeName = alarmsName;

                    var alarmControllerType = Type.GetType("Alarms.AlarmController");
                    const int interval = 1000;
                    string intervalString = interval.ToString(CultureInfo.InvariantCulture);

                    int conditionTypeIndex = 0;

                    FolderState alarmsFolder = CreateFolder(null, alarmsNodeName, alarmsName);
                    alarmsFolder.AddReference(
                        ReferenceTypes.Organizes,
                        true,
                        ObjectIds.ObjectsFolder);
                    references.Add(
                        new NodeStateReference(
                            ReferenceTypes.Organizes,
                            false,
                            alarmsFolder.NodeId));
                    alarmsFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                    AddRootNotifier(alarmsFolder);

                    const string startMethodName = "Start";
                    const string startMethodNodeName = alarmsNodeName + "." + startMethodName;
                    MethodState startMethod = AlarmHelpers.CreateMethod(
                        alarmsFolder,
                        NamespaceIndex,
                        startMethodNodeName,
                        startMethodName);
                    AlarmHelpers.AddStartInputParameters(startMethod, NamespaceIndex);
                    startMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnStart);

                    const string startBranchMethodName = "StartBranch";
                    const string startBranchMethodNodeName = alarmsNodeName +
                        "." +
                        startBranchMethodName;
                    MethodState startBranchMethod = AlarmHelpers.CreateMethod(
                        alarmsFolder,
                        NamespaceIndex,
                        startBranchMethodNodeName,
                        startBranchMethodName);
                    AlarmHelpers.AddStartInputParameters(startBranchMethod, NamespaceIndex);
                    startBranchMethod.OnCallMethod
                        = new GenericMethodCalledEventHandler(OnStartBranch);

                    const string endMethodName = "End";
                    const string endMethodNodeName = alarmsNodeName + "." + endMethodName;
                    MethodState endMethod = AlarmHelpers.CreateMethod(
                        alarmsFolder,
                        NamespaceIndex,
                        endMethodNodeName,
                        endMethodName);
                    endMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnEnd);

                    const string analogTriggerName = "AnalogSource";
                    const string analogTriggerNodeName = alarmsNodeName + "." + analogTriggerName;
                    BaseDataVariableState analogTrigger = AlarmHelpers.CreateVariable(
                        alarmsFolder,
                        NamespaceIndex,
                        analogTriggerNodeName,
                        analogTriggerName);
                    analogTrigger.OnWriteValue = OnWriteAlarmTrigger;
                    var analogAlarmController = (AlarmController)
                        Activator.CreateInstance(
                            alarmControllerType,
                            analogTrigger,
                            interval,
                            false,
                            Server.Telemetry);
                    var analogSourceController = new SourceController(
                        analogTrigger,
                        analogAlarmController);
                    m_triggerMap.Add("Analog", analogSourceController);

                    const string booleanTriggerName = "BooleanSource";
                    const string booleanTriggerNodeName = alarmsNodeName + "." + booleanTriggerName;
                    BaseDataVariableState booleanTrigger = AlarmHelpers.CreateVariable(
                        alarmsFolder,
                        NamespaceIndex,
                        booleanTriggerNodeName,
                        booleanTriggerName,
                        boolValue: true);
                    booleanTrigger.OnWriteValue = OnWriteAlarmTrigger;
                    var booleanAlarmController = (AlarmController)
                        Activator.CreateInstance(
                            alarmControllerType,
                            booleanTrigger,
                            interval,
                            true,
                            Server.Telemetry);
                    var booleanSourceController = new SourceController(
                        booleanTrigger,
                        booleanAlarmController);
                    m_triggerMap.Add("Boolean", booleanSourceController);

                    AlarmHolder mandatoryExclusiveLevel = new ExclusiveLevelHolder(
                        this,
                        alarmsFolder,
                        analogSourceController,
                        intervalString,
                        GetSupportedAlarmConditionType(ref conditionTypeIndex),
                        alarmControllerType,
                        interval,
                        optional: false);

                    m_alarms.Add(mandatoryExclusiveLevel.AlarmNodeName, mandatoryExclusiveLevel);

                    AlarmHolder mandatoryNonExclusiveLevel = new NonExclusiveLevelHolder(
                        this,
                        alarmsFolder,
                        analogSourceController,
                        intervalString,
                        GetSupportedAlarmConditionType(ref conditionTypeIndex),
                        alarmControllerType,
                        interval,
                        optional: false);
                    m_alarms.Add(
                        mandatoryNonExclusiveLevel.AlarmNodeName,
                        mandatoryNonExclusiveLevel);

                    AlarmHolder offNormal = new OffNormalAlarmTypeHolder(
                        this,
                        alarmsFolder,
                        booleanSourceController,
                        intervalString,
                        GetSupportedAlarmConditionType(ref conditionTypeIndex),
                        alarmControllerType,
                        interval,
                        optional: false);
                    m_alarms.Add(offNormal.AlarmNodeName, offNormal);

                    AddPredefinedNode(SystemContext, alarmsFolder);
                    StartTimer();
                    m_allowEntry = true;
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Error creating the AlarmNodeManager address space.");
                }
            }
        }

        /// <summary>
        /// Creates a new folder.
        /// </summary>
        private FolderState CreateFolder(NodeState parent, string path, string name)
        {
            var folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };

            parent?.AddChild(folder);

            return folder;
        }

        private void DoSimulation(object state)
        {
            if (m_allowEntry)
            {
                m_allowEntry = false;

                lock (m_alarms)
                {
                    m_success++;
                    try
                    {
                        foreach (SourceController controller in m_triggerMap.Values)
                        {
                            bool updated = controller.Controller.Update(SystemContext);

                            IList<IReference> references = [];
                            controller.Source.GetReferences(
                                SystemContext,
                                references,
                                ReferenceTypes.HasCondition,
                                false);
                            foreach (IReference reference in references)
                            {
                                string identifier = reference.TargetId.ToString();
                                if (m_alarms.TryGetValue(identifier, out AlarmHolder holder))
                                {
                                    holder.Update(updated);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogInformation(ex, "Alarm Loop Exception");
                    }
                }
                m_allowEntry = true;
            }
            else if (m_success > 0)
            {
                m_missed++;
                m_logger.LogInformation("Alarms: Missed Loop {Missed} Success {Success}", m_missed, m_success);
            }
        }

        public ServiceResult OnStart(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            uint seconds;
            if (inputArguments.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                seconds = (uint)inputArguments[0];
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                m_logger.LogInformation("Starting up alarm group {NodeId}", GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = [];
                        sourceController.Source.GetReferences(
                            SystemContext,
                            references,
                            ReferenceTypes.HasCondition,
                            false);
                        foreach (IReference reference in references)
                        {
                            string identifier = reference.TargetId.ToString();
                            if (m_alarms.TryGetValue(identifier, out AlarmHolder holder))
                            {
                                holder.SetBranching(false);
                                holder.Start(seconds);
                                bool updated = holder.Controller.Update(SystemContext);
                                holder.Update(updated);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public ServiceResult OnStartBranch(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            uint seconds;
            if (inputArguments.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                seconds = (uint)inputArguments[0];
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                m_logger.LogInformation(
                    "Starting up Branch for alarm group {Name}",
                    GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = [];
                        sourceController.Source.GetReferences(
                            SystemContext,
                            references,
                            ReferenceTypes.HasCondition,
                            false);
                        foreach (IReference reference in references)
                        {
                            string identifier = reference.TargetId.ToString();
                            if (m_alarms.TryGetValue(identifier, out AlarmHolder holder))
                            {
                                holder.SetBranching(true);
                                holder.Start(seconds);
                                bool updated = holder.Controller.Update(SystemContext);
                                holder.Update(updated);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public ServiceResult OnEnd(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                m_logger.LogInformation("Stopping alarm group {Name}", GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = [];
                        sourceController.Source.GetReferences(
                            SystemContext,
                            references,
                            ReferenceTypes.HasCondition,
                            false);
                        foreach (IReference reference in references)
                        {
                            string identifier = reference.TargetId.ToString();
                            if (m_alarms.TryGetValue(identifier, out AlarmHolder holder))
                            {
                                holder.ClearBranches();
                            }
                        }

                        sourceController.Controller.Stop();
                    }
                }
            }

            return result;
        }

        public ServiceResult OnWriteAlarmTrigger(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                SourceController sourceController = GetSourceControllerFromNodeState(
                    node,
                    sourceControllers);

                if (sourceController == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                m_logger.LogInformation("Manual Write {Value} to {NodeId}", value, node.NodeId);

                lock (m_alarms)
                {
                    sourceController.Source.Value = value;
                    Type valueType = value.GetType();
                    sourceController.Controller.ManualWrite(value);
                    IList<IReference> references = [];
                    sourceController.Source.GetReferences(
                        SystemContext,
                        references,
                        ReferenceTypes.HasCondition,
                        false);
                    foreach (IReference reference in references)
                    {
                        string identifier = reference.TargetId.ToString();
                        if (m_alarms.TryGetValue(identifier, out AlarmHolder holder))
                        {
                            holder.Update(true);
                        }
                    }
                }
            }

            return StatusCodes.Good;
        }

        private AlarmHolder GetAlarmHolder(NodeId node)
        {
            AlarmHolder alarmHolder = null;

            Type nodeIdType = node.Identifier.GetType();
            if (nodeIdType.Name == "String")
            {
                string unmodifiedName = node.Identifier.ToString();

                // This is bad, but I'm not sure why the NodeName is being attached with an underscore, it messes with this lookup.
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                string name = unmodifiedName.Replace(
                    "Alarms_",
                    "Alarms.",
                    StringComparison.Ordinal);
#else
                string name = unmodifiedName.Replace("Alarms_", "Alarms.");
#endif

                string mapName = name;
                if (name.EndsWith(AlarmDefines.TRIGGER_EXTENSION) ||
                    name.EndsWith(AlarmDefines.ALARM_EXTENSION))
                {
                    int lastDot = name.LastIndexOf('.');
                    mapName = name[..lastDot];
                }

                if (m_alarms.TryGetValue(mapName, out AlarmHolder value))
                {
                    alarmHolder = value;
                }
            }

            return alarmHolder;
        }

        public ServiceResult OnEnableDisableAlarm(
            ISystemContext context,
            ConditionState condition,
            bool enabling)
        {
            return ServiceResult.Good;
        }

        public Dictionary<string, SourceController> GetUnitAlarms(NodeState nodeState)
        {
            return m_triggerMap;
        }

        public string GetUnitFromNodeState(NodeState nodeState)
        {
            return GetUnitFromNodeId(nodeState.NodeId);
        }

        public string GetUnitFromNodeId(NodeId nodeId)
        {
            string unit = string.Empty;

            if (nodeId.IdType == IdType.String)
            {
                string nodeIdString = (string)nodeId.Identifier;
                string[] splitString = nodeIdString.Split('.');
                // Alarms.UnitName.MethodName
                if (splitString.Length >= 1)
                {
                    unit = splitString[1];
                }
            }

            return unit;
        }

        public SourceController GetSourceControllerFromNodeState(
            NodeState nodeState,
            Dictionary<string, SourceController> map)
        {
            SourceController sourceController = null;

            string name = GetSourceNameFromNodeState(nodeState);
            if (map.TryGetValue(name, out SourceController value))
            {
                sourceController = value;
            }

            return sourceController;
        }

        public string GetSourceNameFromNodeState(NodeState nodeState)
        {
            return GetSourceNameFromNodeId(nodeState.NodeId);
        }

        public string GetSourceNameFromNodeId(NodeId nodeId)
        {
            string sourceName = string.Empty;

            if (nodeId.IdType == IdType.String)
            {
                string nodeIdString = (string)nodeId.Identifier;
                string[] splitString = nodeIdString.Split('.');
                // Alarms.UnitName.AnalogSource
                if (splitString.Length >= 2)
                {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    sourceName = splitString[^1].Replace(
                        "Source",
                        string.Empty,
                        StringComparison.Ordinal);
#else
                    sourceName = splitString[^1].Replace("Source", string.Empty);
#endif
                }
            }

            return sourceName;
        }

        public SupportedAlarmConditionType GetSupportedAlarmConditionType(ref int index)
        {
            SupportedAlarmConditionType conditionType = m_conditionTypes[index];
            index++;
            if (index >= m_conditionTypes.Length)
            {
                index = 0;
            }
            return conditionType;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Calls a method on the specified nodes.
        /// </summary>
        public override void Call(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);
            IDictionary<NodeId, NodeState> operationCache = new NodeIdDictionary<NodeState>();

            bool didRefresh = false;

            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                CallMethodRequest methodToCall = methodsToCall[ii];

                bool refreshMethod =
                    methodToCall.MethodId.Equals(MethodIds.ConditionType_ConditionRefresh) ||
                    methodToCall.MethodId.Equals(MethodIds.ConditionType_ConditionRefresh2);

                if (refreshMethod)
                {
                    if (didRefresh)
                    {
                        errors[ii] = StatusCodes.BadRefreshInProgress;
                        methodToCall.Processed = true;
                        continue;
                    }

                    didRefresh = true;
                }

                bool ackMethod = methodToCall.MethodId
                    .Equals(MethodIds.AcknowledgeableConditionType_Acknowledge);
                bool confirmMethod = methodToCall.MethodId
                    .Equals(MethodIds.AcknowledgeableConditionType_Confirm);
                bool commentMethod = methodToCall.MethodId
                    .Equals(MethodIds.ConditionType_AddComment);
                bool ackConfirmMethod = ackMethod || confirmMethod || commentMethod;

                // Need to try to capture any calls to ConditionType::Acknowledge
                if (methodToCall.ObjectId.Equals(ObjectTypeIds.ConditionType) && ackConfirmMethod)
                {
                    // Mantis Issue 6944 which is a duplicate of 5544 - result is Confirm should be Bad_NodeIdInvalid
                    // Override any other errors that may be there, even if this is 'Processed'
                    errors[ii] = StatusCodes.BadNodeIdInvalid;
                    methodToCall.Processed = true;
                    continue;
                }

                // skip items that have already been processed.
                if (methodToCall.Processed)
                {
                    continue;
                }

                MethodState method = null;

                lock (Lock)
                {
                    // check for valid handle.
                    NodeHandle initialHandle = GetManagerHandle(
                        systemContext,
                        methodToCall.ObjectId,
                        operationCache);

                    if (initialHandle == null)
                    {
                        if (ackConfirmMethod)
                        {
                            // Mantis 6944
                            errors[ii] = StatusCodes.BadNodeIdUnknown;
                            methodToCall.Processed = true;
                        }

                        continue;
                    }

                    // owned by this node manager.
                    methodToCall.Processed = true;

                    // Look for an alarm branchId to operate on.
                    NodeHandle handle = FindBranchNodeHandle(
                        systemContext,
                        initialHandle,
                        methodToCall);

                    // validate the source node.
                    NodeState source = ValidateNode(systemContext, handle, operationCache);

                    if (source == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }

                    // find the method.
                    method = source.FindMethod(systemContext, methodToCall.MethodId);

                    if (method == null)
                    {
                        // check for loose coupling.
                        if (source.ReferenceExists(
                            ReferenceTypeIds.HasComponent,
                            false,
                            methodToCall.MethodId))
                        {
                            method = (MethodState)FindPredefinedNode(
                                methodToCall.MethodId,
                                typeof(MethodState));
                        }

                        if (method == null)
                        {
                            errors[ii] = StatusCodes.BadMethodInvalid;
                            continue;
                        }
                    }
                }

                // call the method.
                CallMethodResult result = results[ii] = new CallMethodResult();

                errors[ii] = Call(systemContext, methodToCall, method, result);
            }
        }

        /// <summary>
        /// Override ConditionRefresh.
        /// </summary>
        public override ServiceResult ConditionRefresh(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            ServerSystemContext systemContext = SystemContext.Copy(context);

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                IEventMonitoredItem monitoredItem = monitoredItems[ii];

                if (monitoredItem == null)
                {
                    continue;
                }

                var events = new List<IFilterTarget>();
                var nodesToRefresh = new List<NodeState>();

                lock (Lock)
                {
                    // check for server subscription.
                    if (monitoredItem.NodeId == ObjectIds.Server)
                    {
                        if (RootNotifiers != null)
                        {
                            nodesToRefresh.AddRange(RootNotifiers);
                        }
                    }
                    else
                    {
                        // check if monitored Item is managed by this node manager
                        if (!MonitoredItems.ContainsKey(monitoredItem.Id))
                        {
                            continue;
                        }

                        // get the refresh events.
                        nodesToRefresh.Add(((NodeHandle)monitoredItem.ManagerHandle).Node);
                    }
                }

                // block and wait for the refresh.
                for (int jj = 0; jj < nodesToRefresh.Count; jj++)
                {
                    nodesToRefresh[jj].ConditionRefresh(systemContext, events, true);
                }

                lock (Lock)
                {
                    // This is where I can add branch events
                    GetBranchesForConditionRefresh(events);
                }

                // queue the events.
                for (int jj = 0; jj < events.Count; jj++)
                {
                    monitoredItem.QueueEvent(events[jj]);
                }
            }

            // all done.
            return ServiceResult.Good;
        }

        public NodeHandle FindBranchNodeHandle(
            ISystemContext systemContext,
            NodeHandle initialHandle,
            CallMethodRequest methodToCall)
        {
            NodeHandle nodeHandle = initialHandle;

            if (IsAckConfirm(methodToCall.MethodId))
            {
                AlarmHolder holder = GetAlarmHolder(methodToCall.ObjectId);

                if (holder != null && holder.HasBranches())
                {
                    byte[] eventId = GetEventIdFromAckConfirmMethod(methodToCall);

                    if (eventId != null)
                    {
                        BaseEventState state = holder.GetBranch(eventId);

                        if (state != null)
                        {
                            nodeHandle = new NodeHandle
                            {
                                NodeId = methodToCall.ObjectId,
                                Node = state,
                                Validated = true
                            };
                        }
                    }
                }
            }

            return nodeHandle;
        }

        public void GetBranchesForConditionRefresh(List<IFilterTarget> events)
        {
            // Don't look at Certificates, they won't have branches
            foreach (AlarmHolder alarmHolder in m_alarms.Values)
            {
                alarmHolder.GetBranchesForConditionRefresh(events);
            }
        }

        private static bool IsAckConfirm(NodeId methodId)
        {
            bool isAckConfirm = false;
            if (methodId.Equals(MethodIds.AcknowledgeableConditionType_Acknowledge) ||
                methodId.Equals(MethodIds.AcknowledgeableConditionType_Confirm))
            {
                isAckConfirm = true;
            }
            return isAckConfirm;
        }

        private static byte[] GetEventIdFromAckConfirmMethod(CallMethodRequest request)
        {
            byte[] eventId = null;

            // Bad magic Numbers here
            if (request.InputArguments != null &&
                request.InputArguments.Count == 2 &&
                request.InputArguments[0].TypeInfo.BuiltInType.Equals(BuiltInType.ByteString))
            {
                eventId = (byte[])request.InputArguments[0].Value;
            }
            return eventId;
        }

        /// <summary>
        /// Starts the timer to detect Alarms.
        /// </summary>
        private void StartTimer()
        {
            Utils.SilentDispose(m_simulationTimer);
            m_simulationTimer = new Timer(
                DoSimulation,
                null,
                kSimulationInterval,
                kSimulationInterval);
        }

        /// <summary>
        /// Disposes the timer.
        /// </summary>
        private void DisposeTimer()
        {
            Utils.SilentDispose(m_simulationTimer);
            m_simulationTimer = null;
        }

        private readonly Dictionary<string, AlarmHolder> m_alarms = [];
        private readonly Dictionary<string, SourceController> m_triggerMap = [];
        private bool m_allowEntry;
        private uint m_success;
        private uint m_missed;

        private readonly SupportedAlarmConditionType[] m_conditionTypes =
        [
            new SupportedAlarmConditionType(
                "Process",
                "ProcessConditionClassType",
                ObjectTypeIds.ProcessConditionClassType
            ),
            new SupportedAlarmConditionType(
                "Maintenance",
                "MaintenanceConditionClassType",
                ObjectTypeIds.MaintenanceConditionClassType
            ),
            new SupportedAlarmConditionType(
                "System",
                "SystemConditionClassType",
                ObjectTypeIds.SystemConditionClassType)
        ];

        private const ushort kSimulationInterval = 100;
        private Timer m_simulationTimer;
    }
}
