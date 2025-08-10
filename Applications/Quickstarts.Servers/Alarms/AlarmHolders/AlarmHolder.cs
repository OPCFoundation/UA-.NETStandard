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
using Microsoft.Extensions.Logging;
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

            if (m_alarm is ConditionState alarm)
            {
                hasBranches = alarm.GetBranchCount() > 0;
            }

            return hasBranches;
        }

        public BaseEventState GetBranch(byte[] eventId)
        {
            BaseEventState state = null;

            if (m_alarm is ConditionState alarm)
            {
                state = alarm.GetBranch(eventId);
            }

            return state;
        }

        public void ClearBranches()
        {
            if (m_alarm is ConditionState alarm)
            {
                alarm.ClearBranches();
                m_alarmController.SetBranchCount(0);
            }
        }

        public void GetBranchesForConditionRefresh(List<IFilterTarget> events)
        {
            if (m_alarm is ConditionState alarm)
            {
                Dictionary<string, ConditionState> branches = alarm.GetBranches();
                events.AddRange(branches.Values);
            }
        }

        protected virtual void CreateBranch()
        {
        }

        private void InitializeInternal(BaseEventState alarm, NodeId branchId = null)
        {
            string alarmName = AlarmName;
            string alarmNodeId = (string)m_parent.NodeId.Identifier + "." + AlarmName;

            alarm.SymbolicName = alarmName;
            var createQualifiedName = new QualifiedName(alarmName, NamespaceIndex);

            bool isBranch = IsBranch(branchId);
            var createNodeId = new NodeId(alarmNodeId, NamespaceIndex);
            var createLocalizedText = new LocalizedText(alarmName);
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

        private static bool IsBranch(NodeId branchId)
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
            // Method calls are done by the core.
            // Delayed events are expected events to be logged to file.
            while (m_delayedMessages.Count > 0)
            {
                Utils.LogWarning("Delayed:{0} Event Time: {1}", m_delayedMessages[0], m_alarm.Time.Value);
                m_delayedMessages.RemoveAt(0);
            }
        }

        protected void Log(string caller, string message, BaseEventState alarm = null)
        {
            LogMessage(LogLevel.Information, caller, message);
        }

        protected void LogError(string caller, string message, BaseEventState alarm = null)
        {
            LogMessage(LogLevel.Error, caller, message);
        }

        protected void LogMessage(LogLevel logLevel, string caller, string message)
        {
            Utils.Log(logLevel, "{0}: {1} EventId {2} {3}", caller, m_mapName, Utils.ToHexString(m_alarm.EventId.Value), message);
        }

        public virtual void SetValue(string message = "")
        {
            Utils.LogError("AlarmHolder.SetValue() - Should not be called");
        }

        public void Start(uint seconds)
        {
            ClearBranches();
            m_alarmController.Start(seconds);
        }

        public void Stop()
        {
            ClearBranches();
            m_alarmController.Stop();
        }

        protected virtual bool UpdateShelving()
        {
            Utils.LogError("AlarmHolder.UpdateShelving() - Should not be called");
            return false;
        }

        protected virtual bool UpdateSuppression()
        {
            Utils.LogError("AlarmHolder.UpdateSuppression() - Should not be called");
            return false;
        }

        protected virtual void SetActive(BaseEventState state, bool activeState)
        {
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
            if (Trigger.Value != value)
            {
                Trigger.Value = value;
                SetValue("Manual Write to trigger " + value);
            }

            return StatusCodes.Good;
        }

        public ISystemContext SystemContext => GetNodeManager().SystemContext;

        public ushort NamespaceIndex => GetNodeManager().NamespaceIndex;

        public BaseEventState Alarm => m_alarm;

        public AlarmController Controller => m_alarmController;

        public BaseDataVariableState Trigger => m_trigger;

        public string MapName => m_mapName;

        public string TriggerName => m_alarmRootName + AlarmDefines.TRIGGER_EXTENSION;

        public string AlarmName => m_alarmRootName + AlarmDefines.ALARM_EXTENSION;

        public string AlarmNodeName => m_alarm.NodeId.ToString();

        public bool Analog => m_analog;
        public bool Optional => m_optional;

        public bool SupportsBranching => m_supportsBranching;

        public virtual void SetBranching(bool value)
        {
        }

        public PropertyState<NodeId> GetEventType()
        {
            return m_alarm.EventType;
        }

        protected AlarmNodeManager GetNodeManager()
        {
            return m_alarmNodeManager;
        }

        protected string GetAlarmTypeName(uint alarmTypeIdentifier)
        {
            string alarmTypeName = "";

            switch (alarmTypeIdentifier)
            {
                case ObjectTypes.ConditionType:
                    alarmTypeName = "ConditionType";
                    break;

                case ObjectTypes.DialogConditionType:
                    alarmTypeName = "DialogConditionType";
                    break;

                case ObjectTypes.AcknowledgeableConditionType:
                    alarmTypeName = "AcknowledgeableConditionType";
                    break;

                case ObjectTypes.AlarmConditionType:
                    alarmTypeName = "AlarmConditionType";
                    break;

                case ObjectTypes.AlarmGroupType:
                    alarmTypeName = "AlarmGroupType";
                    break;

                case ObjectTypes.ShelvedStateMachineType:
                    alarmTypeName = "ShelvedStateMachineType";
                    break;

                case ObjectTypes.LimitAlarmType:
                    alarmTypeName = "LimitAlarmType";
                    break;

                case ObjectTypes.ExclusiveLimitStateMachineType:
                    alarmTypeName = "ExclusiveLimitStateMachineType";
                    break;

                case ObjectTypes.ExclusiveLimitAlarmType:
                    alarmTypeName = "ExclusiveLimitAlarmType";
                    break;

                case ObjectTypes.NonExclusiveLimitAlarmType:
                    alarmTypeName = "NonExclusiveLimitAlarmType";
                    break;

                case ObjectTypes.NonExclusiveLevelAlarmType:
                    alarmTypeName = "NonExclusiveLevelAlarmType";
                    break;

                case ObjectTypes.ExclusiveLevelAlarmType:
                    alarmTypeName = "ExclusiveLevelAlarmType";
                    break;

                case ObjectTypes.NonExclusiveDeviationAlarmType:
                    alarmTypeName = "NonExclusiveDeviationAlarmType";
                    break;

                case ObjectTypes.NonExclusiveRateOfChangeAlarmType:
                    alarmTypeName = "NonExclusiveRateOfChangeAlarmType";
                    break;

                case ObjectTypes.ExclusiveDeviationAlarmType:
                    alarmTypeName = "ExclusiveDeviationAlarmType";
                    break;

                case ObjectTypes.ExclusiveRateOfChangeAlarmType:
                    alarmTypeName = "ExclusiveRateOfChangeAlarmType";
                    break;

                case ObjectTypes.DiscreteAlarmType:
                    alarmTypeName = "DiscreteAlarmType";
                    break;

                case ObjectTypes.OffNormalAlarmType:
                    alarmTypeName = "OffNormalAlarmType";
                    break;

                case ObjectTypes.SystemOffNormalAlarmType:
                    alarmTypeName = "SystemOffNormalAlarmType";
                    break;

                case ObjectTypes.TripAlarmType:
                    alarmTypeName = "TripAlarmType";
                    break;

                case ObjectTypes.InstrumentDiagnosticAlarmType:
                    alarmTypeName = "InstrumentDiagnosticAlarmType";
                    break;

                case ObjectTypes.SystemDiagnosticAlarmType:
                    alarmTypeName = "SystemDiagnosticAlarmType";
                    break;

                case ObjectTypes.CertificateExpirationAlarmType:
                    alarmTypeName = "CertificateExpirationAlarmType";
                    break;

                case ObjectTypes.DiscrepancyAlarmType:
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
        protected ushort GetNameSpaceIndex(uint alarmTypeIdentifier)
        {
            return 0;
        }

        protected AlarmNodeManager m_alarmNodeManager;
        protected BaseEventState m_alarm;
        protected Type m_alarmControllerType;
        protected AlarmController m_alarmController;
        protected BaseDataVariableState m_trigger;
        protected string m_alarmRootName = "";
        protected string m_mapName = "";
        protected bool m_analog = true;
        protected bool m_optional;
        protected int m_interval;
        protected uint m_branchCounter;
        protected bool m_supportsBranching;
        protected FolderState m_parent;
        protected uint m_alarmTypeIdentifier;
        protected string m_alarmTypeName = "";
        protected SupportedAlarmConditionType m_alarmConditionType;
        protected List<string> m_delayedMessages = [];
    }
}
