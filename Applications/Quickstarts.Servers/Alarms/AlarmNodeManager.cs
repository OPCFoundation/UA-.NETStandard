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
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Alarms;

namespace Alarms
{
    /// <summary>
    /// The factory for the Alarm Node Manager.
    /// </summary>
    public class AlarmNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            // CA2000: ownership of the returned IAsyncNodeManager
            // transfers to the MasterNodeManager which disposes it.
#pragma warning disable CA2000
            return new ValueTask<IAsyncNodeManager>(
                new AlarmNodeManager(server, configuration, NamespacesUris.ToArray()!));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
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
    public class AlarmNodeManager : AsyncCustomNodeManager
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
            m_logger.LogInformation("Alarms: Created AlarmNodeManager");
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeTimer();
                m_suppressionEngine?.Dispose();
                m_suppressionEngine = null;

                m_logger.LogInformation("Alarms: Disposed AlarmNodeManager");
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null &&
                instance.Parent.NodeId.TryGetValue(out string id))
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
        /// <exception cref="InvalidOperationException">The Alarms.AlarmController type could not be found.</exception>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (!externalReferences.TryGetValue(
                ObjectIds.ObjectsFolder,
                out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = [];
            }

            FolderState? alarmsFolder = null;
            MethodState? startMethod = null;
            MethodState? startBranchMethod = null;
            MethodState? endMethod = null;
            try
            {
                const string alarmsName = "Alarms";
                const string alarmsNodeName = alarmsName;

                Type alarmControllerType = Type.GetType("Alarms.AlarmController") ??
                    throw new InvalidOperationException(
                        "Alarms.AlarmController type not found.");
                const int interval = 1000;
                string intervalString = interval.ToString(CultureInfo.InvariantCulture);

                int conditionTypeIndex = 0;

                alarmsFolder = CreateFolder(null, alarmsNodeName, alarmsName);
                alarmsFolder.AddReference(
                    ReferenceTypeIds.Organizes,
                    true,
                    ObjectIds.ObjectsFolder);
                references.Add(
                    new NodeStateReference(
                        ReferenceTypeIds.Organizes,
                        false,
                        alarmsFolder.NodeId));
                alarmsFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                await AddRootNotifierAsync(alarmsFolder, cancellationToken).ConfigureAwait(false);

                const string startMethodName = "Start";
                const string startMethodNodeName = alarmsNodeName + "." + startMethodName;
                startMethod = AlarmHelpers.CreateMethod(
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
                startBranchMethod = AlarmHelpers.CreateMethod(
                    alarmsFolder,
                    NamespaceIndex,
                    startBranchMethodNodeName,
                    startBranchMethodName);
                AlarmHelpers.AddStartInputParameters(startBranchMethod, NamespaceIndex);
                startBranchMethod.OnCallMethod
                    = new GenericMethodCalledEventHandler(OnStartBranch);

                const string endMethodName = "End";
                const string endMethodNodeName = alarmsNodeName + "." + endMethodName;
                endMethod = AlarmHelpers.CreateMethod(
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
                        Server.Telemetry)!;
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
                        Server.Telemetry)!;
                var booleanSourceController = new SourceController(
                    booleanTrigger,
                    booleanAlarmController);
                m_triggerMap.Add("Boolean", booleanSourceController);

                const string setpointSourceName = "SetpointSource";
                const string setpointSourceNodeName =
                    alarmsNodeName +
                    "." +
                    setpointSourceName;
                BaseDataVariableState setpointSource = AlarmHelpers.CreateVariable(
                    alarmsFolder,
                    NamespaceIndex,
                    setpointSourceNodeName,
                    setpointSourceName);

                const string discrepancyTargetSourceName = AlarmDefines.DISCREPANCY_TARGET_NAME;
                const string discrepancyTargetSourceNodeName =
                    alarmsNodeName +
                    "." +
                    discrepancyTargetSourceName;
                BaseDataVariableState discrepancyTargetSource = AlarmHelpers.CreateVariable(
                    alarmsFolder,
                    NamespaceIndex,
                    discrepancyTargetSourceNodeName,
                    discrepancyTargetSourceName);

                AlarmHolder mandatoryExclusiveLevel = new ExclusiveLevelHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);

                m_alarms.Add(mandatoryExclusiveLevel.AlarmNodeName, mandatoryExclusiveLevel);

                AlarmHolder mandatoryNonExclusiveLevel = new NonExclusiveLevelHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(
                    mandatoryNonExclusiveLevel.AlarmNodeName,
                    mandatoryNonExclusiveLevel);

                AlarmHolder offNormal = new OffNormalAlarmTypeHolder(
                    this,
                    booleanTrigger,
                    booleanSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(offNormal.AlarmNodeName, offNormal);

                AlarmHolder alarmCondition = new AlarmConditionHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(alarmCondition.AlarmNodeName, alarmCondition);

                AlarmHolder discrepancyAlarm = new DiscrepancyAlarmTypeHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    discrepancyTargetSource.NodeId,
                    optional: true);
                m_alarms.Add(discrepancyAlarm.AlarmNodeName, discrepancyAlarm);

                AlarmHolder limitAlarm = new LimitAlarmHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(limitAlarm.AlarmNodeName, limitAlarm);

                AlarmHolder exclusiveLimitAlarm = new ExclusiveLimitAlarmHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(exclusiveLimitAlarm.AlarmNodeName, exclusiveLimitAlarm);

                AlarmHolder exclusiveDeviationAlarm = new ExclusiveDeviationAlarmTypeHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    setpointSource.NodeId,
                    optional: true);
                m_alarms.Add(exclusiveDeviationAlarm.AlarmNodeName, exclusiveDeviationAlarm);

                AlarmHolder exclusiveRateOfChangeAlarm = new ExclusiveRateOfChangeAlarmTypeHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(exclusiveRateOfChangeAlarm.AlarmNodeName, exclusiveRateOfChangeAlarm);

                AlarmHolder nonExclusiveLimitAlarm = new NonExclusiveLimitAlarmHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(nonExclusiveLimitAlarm.AlarmNodeName, nonExclusiveLimitAlarm);

                AlarmHolder nonExclusiveDeviationAlarm = new NonExclusiveDeviationAlarmTypeHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    setpointSource.NodeId,
                    optional: true);
                m_alarms.Add(nonExclusiveDeviationAlarm.AlarmNodeName, nonExclusiveDeviationAlarm);

                AlarmHolder nonExclusiveRateOfChangeAlarm = new NonExclusiveRateOfChangeAlarmTypeHolder(
                    this,
                    analogTrigger,
                    analogSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(
                    nonExclusiveRateOfChangeAlarm.AlarmNodeName,
                    nonExclusiveRateOfChangeAlarm);

                AlarmHolder discreteAlarm = new DiscreteAlarmHolder(
                    this,
                    booleanTrigger,
                    booleanSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(discreteAlarm.AlarmNodeName, discreteAlarm);

                AlarmHolder systemOffNormalAlarm = new SystemOffNormalAlarmTypeHolder(
                    this,
                    booleanTrigger,
                    booleanSourceController,
                    intervalString,
                    GetSupportedAlarmConditionType(ref conditionTypeIndex),
                    alarmControllerType,
                    interval,
                    optional: true);
                m_alarms.Add(systemOffNormalAlarm.AlarmNodeName, systemOffNormalAlarm);

                // Set up the alarm group + suppression engine demo. The
                // analog alarms are added to an AlarmGroupState and a
                // MaintenanceMode boolean is registered as the
                // suppression source. When the source flips true, the
                // engine suppresses every alarm member; when it flips
                // back to false the suppression clears automatically.
                m_analogGroup = CreateAlarmGroup(alarmsFolder, "AnalogGroup");
                foreach (AlarmHolder holder in m_alarms.Values)
                {
                    if (holder.Alarm is AlarmConditionState alarmState)
                    {
                        m_analogGroup.AddMember(alarmState);
                    }
                }

                m_maintenanceMode = AlarmHelpers.CreateVariable(
                    alarmsFolder,
                    NamespaceIndex,
                    alarmsNodeName + ".MaintenanceMode",
                    "MaintenanceMode",
                    boolValue: false);
                m_maintenanceMode.OnWriteValue = OnMaintenanceModeWritten;

                m_suppressionEngine = new AlarmSuppressionEngine();
                m_suppressionEngine.RegisterSuppressionGroup(
                    m_analogGroup.State,
                    () => m_maintenanceMode != null
                          && m_maintenanceMode.Value.TryGetValue(out bool b)
                          && b,
                    [.. GetAlarmStates()]);

                await AddPredefinedNodeAsync(SystemContext, alarmsFolder, cancellationToken).ConfigureAwait(false);

                // ownership transferred to predefined nodes
                alarmsFolder = null;
                startMethod = null;
                startBranchMethod = null;
                endMethod = null;

                StartTimer();
                m_allowEntry = true;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error creating the AlarmNodeManager address space.");
            }
        }

        /// <summary>
        /// Creates an <see cref="AlarmGroupState"/> inside the given
        /// folder and wraps it in an <see cref="AlarmGroup"/> helper.
        /// </summary>
        private AlarmGroup CreateAlarmGroup(FolderState parent, string name)
        {
            var state = new AlarmGroupState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ObjectTypeIds.AlarmGroupType,
                NodeId = new NodeId(parent.NodeId.IdentifierAsString + "." + name, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText("en", name)
            };
            parent.AddChild(state);

            // Opt the group + its parent into NodeVersion-based model
            // change tracking so any future Create/DeleteNodeAsync on
            // members of the group emits a GeneralModelChangeEvent.
            EnableModelChangeTrackingFor(state);
            EnableModelChangeTrackingFor(parent);

            return new AlarmGroup(state);
        }

        /// <summary>
        /// Returns every <see cref="AlarmConditionState"/> instance the
        /// node manager currently owns; used to register the suppression
        /// engine members.
        /// </summary>
        private IEnumerable<AlarmConditionState> GetAlarmStates()
        {
            foreach (AlarmHolder holder in m_alarms.Values)
            {
                if (holder.Alarm is AlarmConditionState alarm)
                {
                    yield return alarm;
                }
            }
        }

        /// <summary>
        /// Re-evaluates the suppression engine whenever the
        /// <c>MaintenanceMode</c> variable is written. Suppresses every
        /// alarm in the analog group when MaintenanceMode is true.
        /// </summary>
        private ServiceResult OnMaintenanceModeWritten(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            if (node is BaseDataVariableState variable)
            {
                variable.Value = value;
            }
            m_suppressionEngine?.Evaluate(SystemContext);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new folder.
        /// </summary>
        private FolderState CreateFolder(NodeState? parent, string path, string name)
        {
            var folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
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

        private void DoSimulation(object? state)
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
                                ReferenceTypeIds.HasCondition,
                                false);
                            foreach (IReference reference in references)
                            {
                                string identifier = reference.TargetId.ToString();
                                if (m_alarms.TryGetValue(identifier, out AlarmHolder? holder))
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
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
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

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            m_logger.LogInformation("Starting up alarm group {NodeId}", GetUnitFromNodeId(node.NodeId));

            lock (m_alarms)
            {
                foreach (SourceController sourceController in sourceControllers.Values)
                {
                    IList<IReference> references = [];
                    sourceController.Source.GetReferences(
                        SystemContext,
                        references,
                        ReferenceTypeIds.HasCondition,
                        false);
                    foreach (IReference reference in references)
                    {
                        string identifier = reference.TargetId.ToString();
                        if (m_alarms.TryGetValue(identifier, out AlarmHolder? holder))
                        {
                            holder.SetBranching(false);
                            holder.Start(seconds);
                            bool updated = holder.Controller.Update(SystemContext);
                            holder.Update(updated);
                        }
                    }
                }
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnStartBranch(
            ISystemContext context,
            NodeState node,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
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

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
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
                        ReferenceTypeIds.HasCondition,
                        false);
                    foreach (IReference reference in references)
                    {
                        string identifier = reference.TargetId.ToString();
                        if (m_alarms.TryGetValue(identifier, out AlarmHolder? holder))
                        {
                            holder.SetBranching(true);
                            holder.Start(seconds);
                            bool updated = holder.Controller.Update(SystemContext);
                            holder.Update(updated);
                        }
                    }
                }
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnEnd(
            ISystemContext context,
            NodeState node,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            m_logger.LogInformation("Stopping alarm group {Name}", GetUnitFromNodeId(node.NodeId));

            lock (m_alarms)
            {
                foreach (SourceController sourceController in sourceControllers.Values)
                {
                    IList<IReference> references = [];
                    sourceController.Source.GetReferences(
                        SystemContext,
                        references,
                        ReferenceTypeIds.HasCondition,
                        false);
                    foreach (IReference reference in references)
                    {
                        string identifier = reference.TargetId.ToString();
                        if (m_alarms.TryGetValue(identifier, out AlarmHolder? holder))
                        {
                            holder.ClearBranches();
                        }
                    }

                    sourceController.Controller.Stop();
                }
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnWriteAlarmTrigger(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            SourceController? sourceController = GetSourceControllerFromNodeState(
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
                    ReferenceTypeIds.HasCondition,
                    false);
                foreach (IReference reference in references)
                {
                    string identifier = reference.TargetId.ToString();
                    if (m_alarms.TryGetValue(identifier, out AlarmHolder? holder))
                    {
                        holder.Update(true);
                    }
                }
            }

            return StatusCodes.Good;
        }

        private AlarmHolder? GetAlarmHolder(NodeId node)
        {
            AlarmHolder? alarmHolder = null;

            if (node.TryGetValue(out string unmodifiedName))
            {
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
                if (name.EndsWith(AlarmDefines.TRIGGER_EXTENSION, StringComparison.Ordinal) ||
                    name.EndsWith(AlarmDefines.ALARM_EXTENSION, StringComparison.Ordinal))
                {
                    int lastDot = name.LastIndexOf('.');
                    mapName = name[..lastDot];
                }

                if (m_alarms.TryGetValue(mapName, out AlarmHolder? value))
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

            if (nodeId.TryGetValue(out string nodeIdString))
            {
                string[] splitString = nodeIdString.Split('.');
                // Alarms.UnitName.MethodName
                if (splitString.Length >= 1)
                {
                    unit = splitString[1];
                }
            }

            return unit;
        }

        public SourceController? GetSourceControllerFromNodeState(
            NodeState nodeState,
            Dictionary<string, SourceController> map)
        {
            SourceController? sourceController = null;

            string name = GetSourceNameFromNodeState(nodeState);
            if (map.TryGetValue(name, out SourceController? value))
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

            if (nodeId.TryGetValue(out string nodeIdString))
            {
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
        public override ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            // TBD
            return base.DeleteAddressSpaceAsync(cancellationToken);
        }

        /// <summary>
        /// Calls a method on the specified nodes.
        /// </summary>
        public override async ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
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

                // check for valid handle.
                NodeHandle? initialHandle = await GetManagerHandleAsync(
                    systemContext,
                    methodToCall.ObjectId,
                    operationCache,
                    cancellationToken).ConfigureAwait(false);

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
                NodeState? source = await ValidateNodeAsync(
                    systemContext,
                    handle,
                    operationCache,
                    cancellationToken).ConfigureAwait(false);

                if (source == null)
                {
                    errors[ii] = StatusCodes.BadNodeIdUnknown;
                    continue;
                }

                // find the method.
                MethodState? method = await FindMethodStateAsync(
                    context,
                    methodToCall,
                    cancellationToken).ConfigureAwait(false);

                if (method == null)
                {
                    errors[ii] = StatusCodes.BadMethodInvalid;
                    continue;
                }

                // call the method.
                CallMethodResult result = results[ii] = new CallMethodResult();

                errors[ii] = await CallAsync(
                    systemContext,
                    methodToCall,
                    method,
                    result,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Override ConditionRefresh.
        /// </summary>
        public override ValueTask<ServiceResult> ConditionRefreshAsync(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
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

                // check for server subscription.
                if (monitoredItem.NodeId == ObjectIds.Server)
                {
                    if (RootNotifiers != null)
                    {
                        nodesToRefresh.AddRange(RootNotifiers.Values);
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

                // block and wait for the refresh.
                for (int jj = 0; jj < nodesToRefresh.Count; jj++)
                {
                    nodesToRefresh[jj].ConditionRefresh(systemContext, events, true);
                }

                // This is where I can add branch events
                GetBranchesForConditionRefresh(events);

                // queue the events.
                for (int jj = 0; jj < events.Count; jj++)
                {
                    monitoredItem.QueueEvent(events[jj]);
                }
            }

            // all done.
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public NodeHandle FindBranchNodeHandle(
            ISystemContext systemContext,
            NodeHandle initialHandle,
            CallMethodRequest methodToCall)
        {
            NodeHandle nodeHandle = initialHandle;

            if (IsAckConfirm(methodToCall.MethodId))
            {
                AlarmHolder? holder = GetAlarmHolder(methodToCall.ObjectId);

                if (holder != null && holder.HasBranches())
                {
                    ByteString eventId = GetEventIdFromAckConfirmMethod(methodToCall);

                    if (!eventId.IsEmpty)
                    {
                        BaseEventState? state = holder.GetBranch(eventId);

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

        private static ByteString GetEventIdFromAckConfirmMethod(CallMethodRequest request)
        {
            ByteString eventId = default;

            // Bad magic Numbers hereStart
            if (request.InputArguments.Count == 2 &&
                request.InputArguments[0].TryGetValue(out ByteString byteString))
            {
                eventId = byteString;
            }
            return eventId;
        }

        /// <summary>
        /// Starts the timer to detect Alarms.
        /// </summary>
        private void StartTimer()
        {
            m_logger.LogInformation("Alarms: Starting simulation");

            m_simulationTimer?.Dispose();
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
            m_simulationTimer?.Dispose();
            m_simulationTimer = null;

            m_logger.LogInformation("Alarms: Stopped simulation");
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
        private Timer? m_simulationTimer;
        private AlarmGroup? m_analogGroup;
        private BaseDataVariableState? m_maintenanceMode;
        private AlarmSuppressionEngine? m_suppressionEngine;
    }
}
