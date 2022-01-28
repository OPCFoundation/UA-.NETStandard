
using System;
using System.Collections.Generic;
using System.Diagnostics;

using Opc.Ua;
using Opc.Ua.Server;

#pragma warning disable CS0219

#pragma warning disable CS1591

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Alarm Implementation
    /// </summary>

    public class Alarms
    {
        private ReferenceNodeManager m_nodeManager;
        private ushort NameSpaceIndex = 0;
        Dictionary<string, AlarmHolder> m_alarms = new Dictionary<string, AlarmHolder>();

        Dictionary<string, Dictionary<string, SourceController>> m_triggerMap =
            new Dictionary<string, Dictionary<string, SourceController>>();

        private bool m_allowEntry = false;

        private SupportedAlarmConditionType[] m_ConditionTypes = {
                    new SupportedAlarmConditionType( "Process", "ProcessConditionClassType",  ObjectTypeIds.ProcessConditionClassType ),
                    new SupportedAlarmConditionType( "Maintenance", "MaintenanceConditionClassType",  ObjectTypeIds.MaintenanceConditionClassType ),
                    new SupportedAlarmConditionType( "System", "SystemConditionClassType",  ObjectTypeIds.SystemConditionClassType ) };

        public Alarms(ReferenceNodeManager nodeManager)
        {
            m_nodeManager = nodeManager;
            NameSpaceIndex = m_nodeManager.NamespaceIndex;
        }

        public ReferenceNodeManager GetNodeManager()
        {
            return m_nodeManager;
        }

        public void CreateAlarms(FolderState root)
        {
            string alarmsName = "Alarms";
            string alarmsNodeName = alarmsName;
            FolderState alarmsFolder = AlarmHelpers.CreateFolder(root, NameSpaceIndex, alarmsNodeName, alarmsName);

            Type alarmControllerType = Type.GetType("Quickstarts.ReferenceServer.AlarmController");
            int defaultInterval = 1000;
            string defaultIntervalString = defaultInterval.ToString();

            int conditionTypeIndex = 0;
            bool useOptional = true;
            string unitName = alarmsName;

            bool branch = false;
            if (unitName == "Branch")
            {
                branch = true;
            }

            string startMethodName = "Start";
            string startMethodNodeName = alarmsNodeName + "." + startMethodName;
            MethodState startMethod = AlarmHelpers.CreateMethod(alarmsFolder, NameSpaceIndex, startMethodNodeName, startMethodName);
            startMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnStart);

            string startBranchMethodName = "StartBranch";
            string startBranchMethodNodeName = alarmsNodeName + "." + startBranchMethodName;
            MethodState startBranchMethod = AlarmHelpers.CreateMethod(alarmsFolder, NameSpaceIndex, startBranchMethodNodeName, startBranchMethodName);
            startBranchMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnStartBranch);

            string endMethodName = "End";
            string endMethodNodeName = alarmsNodeName + "." + endMethodName;
            MethodState endMethod = AlarmHelpers.CreateMethod(alarmsFolder, NameSpaceIndex, endMethodNodeName, endMethodName);
            endMethod.OnCallMethod = new GenericMethodCalledEventHandler(OnEnd);

            Type controllerType = alarmControllerType;
            int interval = defaultInterval;
            string intervalString = defaultIntervalString;

            // Create Triggers.
            Dictionary<string, SourceController> triggerMap = new Dictionary<string, SourceController>();
            m_triggerMap.Add(alarmsName, triggerMap);

            string analogTriggerName = "AnalogSource";
            string analogTriggerNodeName = alarmsNodeName + "." + analogTriggerName;
            BaseDataVariableState analogTrigger = AlarmHelpers.CreateVariable(alarmsFolder,
                NameSpaceIndex, analogTriggerNodeName, analogTriggerName);
            analogTrigger.OnWriteValue = OnWriteAlarmTrigger;
            AlarmController analogAlarmController = (AlarmController)Activator.CreateInstance(controllerType, analogTrigger, interval, false);
            analogAlarmController.SupportsBranching = branch;
            SourceController analogSourceController = new SourceController(analogTrigger, analogAlarmController);
            triggerMap.Add("Analog", analogSourceController);

            string booleanTriggerName = "BooleanSource";
            string booleanTriggerNodeName = alarmsNodeName + "." + booleanTriggerName;
            BaseDataVariableState booleanTrigger = AlarmHelpers.CreateVariable(alarmsFolder,
                NameSpaceIndex, booleanTriggerNodeName, booleanTriggerName, boolValue: true);
            booleanTrigger.OnWriteValue = OnWriteAlarmTrigger;
            AlarmController booleanAlarmController = (AlarmController)Activator.CreateInstance(controllerType, booleanTrigger, interval, true);
            SourceController booleanSourceController = new SourceController(booleanTrigger, booleanAlarmController);
            triggerMap.Add("Boolean", booleanSourceController);



            string optionalName = intervalString + "Optional";
            string mandatoryName = intervalString + "Mandatory";




            AlarmHolder mandatoryExclusiveLevel = new ExclusiveLevelHolder(
                this,
                alarmsFolder,
                analogSourceController,
                mandatoryName,
                GetSupportedAlarmConditionType(ref conditionTypeIndex),
                controllerType,
                interval,
                optional: false);

            m_alarms.Add(mandatoryExclusiveLevel.AlarmNodeName, mandatoryExclusiveLevel);

            AlarmHolder mandatoryNonExclusiveLevel = new NonExclusiveLevelHolder(
                this,
                alarmsFolder,
                analogSourceController,
                mandatoryName,
                GetSupportedAlarmConditionType(ref conditionTypeIndex),
                controllerType,
                interval,
                optional: false);
            m_alarms.Add(mandatoryNonExclusiveLevel.AlarmNodeName, mandatoryNonExclusiveLevel);

            AlarmHolder offNormal = new OffNormalAlarmTypeHolder(
                this,
                alarmsFolder,
                booleanSourceController,
                intervalString,
                GetSupportedAlarmConditionType(ref conditionTypeIndex),
                controllerType,
                interval,
                optional: false);
            m_alarms.Add(offNormal.AlarmNodeName, offNormal);


            CheckAlarms(alarmsFolder);

            m_allowEntry = true;

        }

        public void CheckAlarms(FolderState alarmsFolder)
        {
            Dictionary<string, List<AlarmCheck>> alarmChecks = new Dictionary<string, List<AlarmCheck>>();

            Dictionary<string, AlarmHolder>.ValueCollection values = m_alarms.Values;
            ISystemContext systemContext = null;
            foreach( AlarmHolder alarmHolder  in m_alarms.Values )
            {
                systemContext = alarmHolder.SystemContext;
                break;
            }

            DialogConditionState dialog = new DialogConditionState(alarmsFolder);

            dialog.Create(systemContext, new NodeId("Dialog", NameSpaceIndex),
                new QualifiedName("Dialog", NameSpaceIndex),
                new LocalizedText("Dialog"), true);

            List<BaseInstanceState> dialogChildren = new List<BaseInstanceState>();

            dialog.GetChildren(systemContext, dialogChildren);

            CheckAlarms(systemContext, alarmChecks, dialog, dialogChildren);


            foreach ( AlarmHolder alarmHolder in m_alarms.Values )
            {
                BaseEventState alarm = alarmHolder.Alarm;

                List<BaseInstanceState> children = new List<BaseInstanceState>();

                alarm.GetChildren(alarmHolder.SystemContext, children);

                CheckAlarms(alarmHolder.SystemContext, alarmChecks, alarm, children);
            }
            foreach( KeyValuePair< string, List<AlarmCheck>> pair in alarmChecks)
            {
                List<AlarmCheck> checks = pair.Value;
                string methodName = pair.Key;

                //Utils.LogInfo( "Method " + pair.Key.ToString() + ":" + methodName 
                //    + " has " + pair.Value.Count.ToString() + " checks" );
                uint existsCount = 0;
                uint nonExistsCount = 0;
                List<string> nonExistingAlarms = new List<string>();
                foreach( AlarmCheck check in pair.Value )
                {
                    if ( check.Exists )
                    {
                        existsCount++;
                    }
                    else
                    {
                        nonExistsCount++;
                        nonExistingAlarms.Add(check.AlarmName);
                    }
                }

                if ( existsCount > 0 )
                {
                    foreach( string name in nonExistingAlarms )
                    {
                        Utils.LogInfo("Unexpected Method " + methodName + " Alarm " +
                            name + " doesn't exist when there are successes ");
                    }
                }

                Utils.LogInfo("Method " + pair.Key + " Exists " + existsCount + " Non " + nonExistsCount.ToString() );
            }
        }

        public void CheckAlarms(
            ISystemContext systemContext,
            Dictionary<string, List<AlarmCheck>> alarmChecks,
            BaseEventState alarm,
            List<BaseInstanceState> children )
        {
            foreach (BaseInstanceState child in children)
            {
                string name = child.SymbolicName;

                MethodState possibleMethod = child as MethodState;
                if (possibleMethod != null)
                {
                    AlarmCheck alarmCheck = new AlarmCheck();
                    alarmCheck.AlarmName = alarm.NodeId.ToString();
                    alarmCheck.MethodName = name;
                    alarmCheck.Exists = false;

                    NodeId methodDeclarationId = possibleMethod.MethodDeclarationId;
                    alarmCheck.MethodDeclarationId = methodDeclarationId;
                    if (methodDeclarationId != null && !methodDeclarationId.IsNullNodeId)
                    {
                        alarmCheck.Exists = true;
                    }

                    uint id = possibleMethod.NumericId;
                    if (!alarmChecks.ContainsKey(name))
                    {
                        List<AlarmCheck> list = new List<AlarmCheck>();
                        alarmChecks.Add(name, list);
                    }

                    List<AlarmCheck> checkList = alarmChecks[name];
                    checkList.Add(alarmCheck);
                }
                else
                {
                    List<BaseInstanceState> subChildren = new List<BaseInstanceState>();
                    child.GetChildren(systemContext, subChildren);
                    CheckAlarms(systemContext, alarmChecks, alarm, subChildren);
                }
            }

        }


        public SupportedAlarmConditionType GetSupportedAlarmConditionType(ref int index)
        {
            SupportedAlarmConditionType conditionType = m_ConditionTypes[index];
            index++;
            if (index >= m_ConditionTypes.Length)
            {
                index = 0;
            }
            return conditionType;
        }

        public bool GetUseOptional(string unitName, ref bool optional)
        {
            bool returnValue = optional;
            if (unitName == "Confirm" || unitName == "ExclusiveLevel")
            {
                returnValue = true;
            }
            else if (unitName == "Acknowledge")
            {
                returnValue = false;
            }
            else
            {
                optional = !optional;
                returnValue = optional;
            }

            return returnValue;
        }

        public NodeState GetAlarmNodeState(NodeHandle handle)
        {
            NodeState alarmState = null;

            NodeId handleNodeId = handle.NodeId;
            if (handleNodeId.IdType == IdType.String)
            {
                string nodeString = (string)handleNodeId.Identifier;
            }

            return alarmState;
        }

        public NodeHandle FindBranchNodeHandle(ISystemContext systemContext, NodeHandle initialHandle, CallMethodRequest methodToCall)
        {
            NodeHandle nodeHandle = initialHandle;

            if (IsAckConfirm(methodToCall.MethodId))
            {
                AlarmHolder holder = GetAlarmHolder(methodToCall.ObjectId);

                if (holder != null)
                {

                    if (holder.HasBranches())
                    {
                        byte[] eventId = GetEventIdFromAckConfirmMethod(methodToCall);

                        if (eventId != null)
                        {
                            BaseEventState state = holder.GetBranch(eventId);

                            if (state != null)
                            {
                                nodeHandle = new NodeHandle();

                                nodeHandle.NodeId = methodToCall.ObjectId;
                                nodeHandle.Node = state;
                                nodeHandle.Validated = true;
                            }
                        }
                    }
                }
            }

            return nodeHandle;
        }

        private bool IsAckConfirm(NodeId methodId)
        {
            bool isAckConfirm = false;
            if (methodId.Equals(Opc.Ua.MethodIds.AcknowledgeableConditionType_Acknowledge) ||
                 methodId.Equals(Opc.Ua.MethodIds.AcknowledgeableConditionType_Confirm))
            {
                isAckConfirm = true;

            }
            return isAckConfirm;
        }

        private byte[] GetEventIdFromAckConfirmMethod(CallMethodRequest request)
        {
            byte[] eventId = null;

            // Bad magic Numbers here
            if (request.InputArguments != null && request.InputArguments.Count == 2)
            {
                if (request.InputArguments[0].TypeInfo.BuiltInType.Equals(BuiltInType.ByteString))
                {
                    eventId = (byte[])request.InputArguments[0].Value;
                }
            }
            return eventId;
        }

        public void GetBranchesForConditionRefresh(List<IFilterTarget> events)
        {
            // Don't look at Certificates, they won't have branches
            foreach (AlarmHolder alarmHolder in m_alarms.Values)
            {
                alarmHolder.GetBranchesForConditionRefresh(events);
            }
        }


        private int m_missed = 0;
        private int m_success = 0;


        public void Loop()
        {
            if (m_allowEntry)
            {
                m_allowEntry = false;

                lock (m_alarms)
                {
                    m_success++;
                    try
                    {
                        foreach (Dictionary<string, SourceController> map in m_triggerMap.Values)
                        {
                            foreach (SourceController controller in map.Values)
                            {
                                bool updated = controller.Controller.Update(GetNodeManager().SystemContext);

                                IList<IReference> references = new List<IReference>();
                                controller.Source.GetReferences(GetNodeManager().SystemContext, references, ReferenceTypes.HasCondition, false);
                                foreach (IReference reference in references)
                                {
                                    string identifier = (string)reference.TargetId.ToString();
                                    if (m_alarms.ContainsKey(identifier))
                                    {
                                        AlarmHolder holder = m_alarms[identifier];
                                        holder.Update(updated);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Loop Exception " + ex.Message);

                    }
                }
                m_allowEntry = true;
            }
            else
            {
                if (m_success > 0)
                {
                    m_missed++;
                    Debug.WriteLine(DateTime.UtcNow.ToLocalTime().ToLongTimeString() + " Missed Loop " + m_missed.ToString() + " Success " + m_success.ToString());
                }
            }
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
                SourceController sourceController = GetSourceControllerFromNodeState(node, sourceControllers);

                if (sourceController == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                Utils.LogInfo("Manual Write " + value.ToString() + " to " + node.NodeId.ToString());

                lock (m_alarms)
                {
                    sourceController.Source.Value = value;
                    Type valueType = value.GetType();
                    sourceController.Controller.ManualWrite(value);
                    IList<IReference> references = new List<IReference>();
                    sourceController.Source.GetReferences(GetNodeManager().SystemContext, references, ReferenceTypes.HasCondition, false);
                    foreach (IReference reference in references)
                    {
                        string identifier = (string)reference.TargetId.ToString();
                        if (m_alarms.ContainsKey(identifier))
                        {
                            AlarmHolder holder = m_alarms[identifier];
                            holder.Update(true);
                        }
                    }
                }
            }

            return Opc.Ua.StatusCodes.Good;
        }


        public void OnStateChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changeMasks)
        {
            AlarmHolder alarmHolder = GetAlarmHolder(node);
            if (alarmHolder != null)
            {
                bool waiting = true;
            }
        }

        public ServiceResult OnEnableAutoRun(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return ServiceResult.Good;
        }

        public ServiceResult OnDisableAutoRun(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return ServiceResult.Good;
        }

        public ServiceResult OnClearBranches(
            ISystemContext context,
            NodeState node,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            lock (m_alarms)
            {
                foreach (AlarmHolder alarmHolder in m_alarms.Values)
                {
                    alarmHolder.ClearBranches();
                }
            }

            return ServiceResult.Good;
        }





        private AlarmHolder GetAlarmHolder(NodeState node)
        {
            return GetAlarmHolder(node.NodeId);
        }

        private AlarmHolder GetAlarmHolder(NodeId node)
        {

            AlarmHolder alarmHolder = null;

            Type nodeIdType = node.Identifier.GetType();
            if (nodeIdType.Name == "String")
            {
                string unmodifiedName = node.Identifier.ToString();

                // This is bad, but I'm not sure why the NodeName is being attached with an underscore, it messes with this lookup.
                string name = unmodifiedName.Replace("Alarms_", "Alarms.");

                string mapName = name;
                if (name.EndsWith(AlarmDefines.TRIGGER_EXTENSION) || name.EndsWith(AlarmDefines.ALARM_EXTENSION))
                {
                    int lastDot = name.LastIndexOf(".");
                    mapName = name.Substring(0, lastDot);
                }

                if (m_alarms.ContainsKey(mapName))
                {
                    alarmHolder = m_alarms[mapName];
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
            return m_triggerMap["Alarms"];
        }


        public string GetUnitFromNodeState(NodeState nodeState)
        {
            return GetUnitFromNodeId(nodeState.NodeId);
        }

        public string GetUnitFromNodeId(NodeId nodeId)
        {
            string unit = "";

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

        public SourceController GetSourceControllerFromNodeState(NodeState nodeState, Dictionary<string, SourceController> map)
        {
            SourceController sourceController = null;

            string name = GetSourceNameFromNodeState(nodeState);
            if (map.ContainsKey(name))
            {
                sourceController = map[name];
            }

            return sourceController;
        }

        public string GetSourceNameFromNodeState(NodeState nodeState)
        {
            return GetSourceNameFromNodeId(nodeState.NodeId);
        }

        public string GetSourceNameFromNodeId(NodeId nodeId)
        {
            string sourceName = "";

            if (nodeId.IdType == IdType.String)
            {
                string nodeIdString = (string)nodeId.Identifier;
                string[] splitString = nodeIdString.Split('.');
                // Alarms.UnitName.AnalogSource
                if (splitString.Length >= 2)
                {
                    sourceName = splitString[splitString.Length - 1].Replace("Source", "");
                }
            }

            return sourceName;


        }

        public ServiceResult OnStart(
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
                Utils.LogInfo("Starting up alarm group " + GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = new List<IReference>();
                        sourceController.Source.GetReferences(GetNodeManager().SystemContext, references, ReferenceTypes.HasCondition, false);
                        foreach (IReference reference in references)
                        {
                            string identifier = (string)reference.TargetId.ToString();
                            if (m_alarms.ContainsKey(identifier))
                            {
                                AlarmHolder holder = m_alarms[identifier];
                                holder.SetBranching(false);
                                holder.Start();
                                bool updated = holder.Controller.Update(GetNodeManager().SystemContext);
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
                Utils.LogInfo("Stopping alarm group " + GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = new List<IReference>();
                        sourceController.Source.GetReferences(GetNodeManager().SystemContext, references, ReferenceTypes.HasCondition, false);
                        foreach (IReference reference in references)
                        {
                            string identifier = (string)reference.TargetId.ToString();
                            if (m_alarms.ContainsKey(identifier))
                            {
                                AlarmHolder holder = m_alarms[identifier];
                                holder.ClearBranches();
                            }
                        }

                        sourceController.Controller.Stop();
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
            ServiceResult result = ServiceResult.Good;

            Dictionary<string, SourceController> sourceControllers = GetUnitAlarms(node);
            if (sourceControllers == null)
            {
                result = StatusCodes.BadNodeIdUnknown;
            }

            if (sourceControllers != null)
            {
                Utils.LogInfo("Starting up Branch for alarm group " + GetUnitFromNodeId(node.NodeId));

                lock (m_alarms)
                {
                    foreach (SourceController sourceController in sourceControllers.Values)
                    {
                        IList<IReference> references = new List<IReference>();
                        sourceController.Source.GetReferences(GetNodeManager().SystemContext, references, ReferenceTypes.HasCondition, false);
                        foreach (IReference reference in references)
                        {
                            string identifier = (string)reference.TargetId.ToString();
                            if (m_alarms.ContainsKey(identifier))
                            {
                                AlarmHolder holder = m_alarms[identifier];
                                holder.SetBranching(true);
                                holder.Start();
                                bool updated = holder.Controller.Update(GetNodeManager().SystemContext);
                                holder.Update(updated);
                            }
                        }
                    }
                }
            }


            return result;
        }

    }
}
