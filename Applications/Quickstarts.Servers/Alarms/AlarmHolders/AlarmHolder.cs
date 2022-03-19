using System;
using System.Collections.Generic;


using Opc.Ua;

#pragma warning disable CS1591

namespace Alarms
{
    public class AlarmHolder
    {
        public AlarmHolder(AlarmNodeManager alarmNodeManager, FolderState parent, SourceController trigger, Type controllerType, int interval)
        {
            m_alarmNodeManager = alarmNodeManager;
            m_parent = parent;
            m_trigger = trigger.Source;
            m_alarmController = trigger.Controller;
            m_alarmControllerType = trigger.Controller.GetType();
            m_interval = interval;
        }

        protected void Initialize(uint alarmTypeIdentifier, string name)
        {
            m_alarmTypeIdentifier = alarmTypeIdentifier;
            m_alarmTypeName = GetAlarmTypeName(m_alarmTypeIdentifier);

            string extraName = "";
            if (name.Length > 0)
            {
                extraName = "." + name;
            }

            m_alarmRootName = m_alarmTypeName + extraName;
            m_mapName = (string)m_parent.NodeId.Identifier + "." + m_alarmRootName;

            InitializeInternal(m_alarm);
        }

        public bool HasBranches()
        {
            bool hasBranches = false;

            ConditionState alarm = m_alarm as ConditionState;
            if (alarm != null)
            {
                hasBranches = alarm.GetBranchCount() > 0;
            }

            return hasBranches;
        }

        public BaseEventState GetBranch(byte[] eventId)
        {
            BaseEventState state = null;
            string eventIdString = Utils.ToHexString(eventId);

            ConditionState alarm = m_alarm as ConditionState;
            if (alarm != null)
            {
                state = alarm.GetBranch(eventId);
            }

            return state;
        }

        public void ClearBranches()
        {
            ConditionState alarm = m_alarm as ConditionState;
            if (alarm != null)
            {
                alarm.ClearBranches();
                m_alarmController.SetBranchCount(0);
            }
        }

        public void GetBranchesForConditionRefresh(List<IFilterTarget> events)
        {
            ConditionState alarm = m_alarm as ConditionState;
            if (alarm != null)
            {
                Dictionary<string, ConditionState> branches = alarm.GetBranches();
                foreach (BaseEventState branch in branches.Values)
                {
                    events.Add(branch);
                }
            }
        }


        protected virtual void CreateBranch()
        {
        }

        private void InitializeInternal(BaseEventState alarm, NodeId branchId = null)
        {
            string triggerNodeId = (string)m_parent.NodeId.Identifier + "." + TriggerName;

            string alarmName = AlarmName/* + GetBranchNodeIdString(branchId)*/;
            string alarmNodeId = (string)m_parent.NodeId.Identifier + "." + AlarmName;

            alarm.SymbolicName = alarmName;

            NodeId createNodeId = null;
            QualifiedName createQualifiedName = new QualifiedName(alarmName, NamespaceIndex);
            LocalizedText createLocalizedText = null;


            bool isBranch = IsBranch(branchId);
            createNodeId = new NodeId(alarmNodeId, NamespaceIndex);
            createLocalizedText = new LocalizedText(alarmName);

            alarm.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            alarm.Create(
                SystemContext,
                createNodeId,
                createQualifiedName,
                createLocalizedText,
                true);


            if (!isBranch)
            {
                m_trigger.AddReference(ReferenceTypes.HasCondition, false, m_alarm.NodeId);
                m_parent.AddChild(alarm);
            }

        }

        private bool IsBranch(NodeId branchId)
        {
            bool isBranch = false;
            if (branchId != null && !branchId.IsNullNodeId)
            {
                isBranch = true;
            }
            return isBranch;
        }

        public NodeId GetNewBranchId()
        {
            return new NodeId(++m_branchCounter, NamespaceIndex);
        }

        public virtual string GetBranchNodeIdString(NodeId branchId)
        {
            string nodeIdString = "";

            if (IsBranch(branchId))
            {
                // Just the numeric
                nodeIdString = "." + branchId.Identifier.ToString();
            }
            return nodeIdString;
        }

        public virtual void Update(bool updated)
        {
            DelayedEvents();
            if (updated)
            {
                SetValue();
            }
        }


        public virtual void DelayedEvents()
        {
            // Method calls are done by the core.  Delayed events are expected events to be logged to file.
            while (m_delayedMessages.Count > 0)
            {
                string message = GetMessage("Delayed:" + m_delayedMessages[0], " Event Time: " + m_alarm.Time.Value.ToString());
                Utils.LogInfo(message);
                m_delayedMessages.RemoveAt(0);
            }
        }

        protected void Log(string caller, string message, BaseEventState alarm = null)
        {
            Utils.LogInfo(GetMessage(caller, message));
        }

        protected void LogError(string caller, string message, BaseEventState alarm = null)
        {
            Utils.LogError(GetMessage(caller, message));
        }

        protected string GetMessage(string caller, string message)
        {
            return caller + ": " + m_mapName + " EventId " +
                Utils.ToHexString(m_alarm.EventId.Value) + " " + message;
        }


        public virtual void SetValue(string message = "")
        {
            Utils.LogInfo(Utils.TraceMasks.Error, "AlarmHolder.SetValue() - Should not be called");
        }

        public void Start()
        {
            ClearBranches();
            m_alarmController.Start();
        }

        public void Stop()
        {
            ClearBranches();
            m_alarmController.Stop();
        }

        protected virtual bool UpdateShelving()
        {
            Utils.LogInfo(Utils.TraceMasks.Error, "AlarmHolder.UpdateShelving() - Should not be called");
            return false;
        }

        protected virtual bool UpdateSuppression()
        {
            Utils.LogInfo(Utils.TraceMasks.Error, "AlarmHolder.UpdateSuppression() - Should not be called");
            return false;
        }

        protected virtual void SetActive(BaseEventState state, bool activeState)
        {

        }

        #region Methods

        public ServiceResult OnWriteAlarmTrigger(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            if (Trigger.Value != value)
            {
                Trigger.Value = value;
                SetValue("Manual Write to trigger " + value.ToString());
            }




            return Opc.Ua.StatusCodes.Good;
        }

        #endregion

        #region Properties

        public ISystemContext SystemContext
        {
            get { return GetNodeManager().SystemContext; }
        }

        public ushort NamespaceIndex
        {
            get { return GetNodeManager().NamespaceIndex; }
        }

        public BaseEventState Alarm
        {
            get { return m_alarm; }
        }

        public AlarmController Controller
        {
            get { return m_alarmController; }
        }

        public BaseDataVariableState Trigger
        {
            get { return m_trigger; }
        }

        public string MapName
        {
            get { return m_mapName; }
        }

        public string TriggerName
        {
            get { return m_alarmRootName + AlarmDefines.TRIGGER_EXTENSION; }
        }

        public string AlarmName
        {
            get { return m_alarmRootName + AlarmDefines.ALARM_EXTENSION; }
        }

        public string AlarmNodeName
        {
            get { return m_alarm.NodeId.ToString(); }
            //            get { return m_alarmRootName + AlarmDefines.ALARM_EXTENSION; }
        }

        public bool Analog
        {
            get { return m_analog; }
        }
        public bool Optional
        {
            get { return m_optional; }
        }

        public bool SupportsBranching
        {
            get { return m_supportsBranching; }
        }

        public virtual void SetBranching( bool value )
        {

        }





        #endregion

        #region Helpers

        public PropertyState<NodeId> GetEventType()
        {
            return m_alarm.EventType;
        }

        protected AlarmNodeManager GetNodeManager()
        {
            return m_alarmNodeManager;
        }

        protected string GetAlarmTypeName(UInt32 alarmTypeIdentifier)
        {
            string alarmTypeName = "";

            switch (alarmTypeIdentifier)
            {
                case Opc.Ua.ObjectTypes.ConditionType:
                    alarmTypeName = "ConditionType";
                    break;

                case Opc.Ua.ObjectTypes.DialogConditionType:
                    alarmTypeName = "DialogConditionType";
                    break;

                case Opc.Ua.ObjectTypes.AcknowledgeableConditionType:
                    alarmTypeName = "AcknowledgeableConditionType";
                    break;

                case Opc.Ua.ObjectTypes.AlarmConditionType:
                    alarmTypeName = "AlarmConditionType";
                    break;

                case Opc.Ua.ObjectTypes.AlarmGroupType:
                    alarmTypeName = "AlarmGroupType";
                    break;

                case Opc.Ua.ObjectTypes.ShelvedStateMachineType:
                    alarmTypeName = "ShelvedStateMachineType";
                    break;

                case Opc.Ua.ObjectTypes.LimitAlarmType:
                    alarmTypeName = "LimitAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.ExclusiveLimitStateMachineType:
                    alarmTypeName = "ExclusiveLimitStateMachineType";
                    break;

                case Opc.Ua.ObjectTypes.ExclusiveLimitAlarmType:
                    alarmTypeName = "ExclusiveLimitAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.NonExclusiveLimitAlarmType:
                    alarmTypeName = "NonExclusiveLimitAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.NonExclusiveLevelAlarmType:
                    alarmTypeName = "NonExclusiveLevelAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.ExclusiveLevelAlarmType:
                    alarmTypeName = "ExclusiveLevelAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.NonExclusiveDeviationAlarmType:
                    alarmTypeName = "NonExclusiveDeviationAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.NonExclusiveRateOfChangeAlarmType:
                    alarmTypeName = "NonExclusiveRateOfChangeAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.ExclusiveDeviationAlarmType:
                    alarmTypeName = "ExclusiveDeviationAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.ExclusiveRateOfChangeAlarmType:
                    alarmTypeName = "ExclusiveRateOfChangeAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.DiscreteAlarmType:
                    alarmTypeName = "DiscreteAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.OffNormalAlarmType:
                    alarmTypeName = "OffNormalAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.SystemOffNormalAlarmType:
                    alarmTypeName = "SystemOffNormalAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.TripAlarmType:
                    alarmTypeName = "TripAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.InstrumentDiagnosticAlarmType:
                    alarmTypeName = "InstrumentDiagnosticAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.SystemDiagnosticAlarmType:
                    alarmTypeName = "SystemDiagnosticAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.CertificateExpirationAlarmType:
                    alarmTypeName = "CertificateExpirationAlarmType";
                    break;

                case Opc.Ua.ObjectTypes.DiscrepancyAlarmType:
                    alarmTypeName = "DiscrepancyAlarmType";
                    break;

                default:
                    break;
            }

            return alarmTypeName;
        }

        /// <summary>
        /// Function is to modify the namespace if this is a derived type.
        /// If no derived types, it's 0
        /// </summary>
        /// <param name="alarmTypeIdentifier"></param>
        /// <returns>ushort namespaceindex</returns>
        protected ushort GetNameSpaceIndex(UInt32 alarmTypeIdentifier)
        {
            ushort nameSpaceIndex = 0;

            return nameSpaceIndex;
        }

        #endregion

        #region Private Fields
        protected AlarmNodeManager m_alarmNodeManager = null;
        protected BaseEventState m_alarm = null;
        protected Type m_alarmControllerType = null;
        protected AlarmController m_alarmController = null;
        protected BaseDataVariableState m_trigger = null;
        protected string m_alarmRootName = "";
        protected string m_mapName = "";
        protected bool m_analog = true;
        protected bool m_optional = false;
        protected int m_interval = 0;
        protected uint m_branchCounter = 0;
        protected bool m_supportsBranching = false;
        protected FolderState m_parent = null;
        protected uint m_alarmTypeIdentifier = 0;
        protected string m_alarmTypeName = "";
        protected SupportedAlarmConditionType m_alarmConditionType = null;
        protected List<string> m_delayedMessages = new List<string>(); 
        #endregion


    }
}
