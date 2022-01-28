using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;

#pragma warning disable CS1591

namespace Quickstarts.ReferenceServer
{
    public class BaseEventTypeHolder : AlarmHolder
    {
        protected BaseEventTypeHolder(
            Alarms alarms,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional) :
            base(alarms, parent, trigger, controllerType, interval)
        {
            m_optional = optional;
        }

        protected new void Initialize(
            uint alarmTypeIdentifier,
            string name)
        {
            m_alarmTypeIdentifier = alarmTypeIdentifier;

            if (m_alarm != null)
            {
                // Call the base class to set parameters
                base.Initialize(alarmTypeIdentifier, name);

                BaseEventState alarm = GetAlarm();

                alarm.EventId.Value = Guid.NewGuid().ToByteArray();
                alarm.EventType.Value = new NodeId( alarmTypeIdentifier, GetNameSpaceIndex( alarmTypeIdentifier ) );
                alarm.SourceNode.Value = m_trigger.NodeId;
                alarm.SourceName.Value = m_trigger.SymbolicName;
                alarm.Time.Value = DateTime.UtcNow;
                alarm.ReceiveTime.Value = alarm.Time.Value;
                alarm.Message.Value = name + " Initialized";
                alarm.Severity.Value = AlarmDefines.INACTIVE_SEVERITY;

                // TODO Implement for Optionals - Needs to go to all places where Time is set.
                alarm.LocalTime = null;
            }
        }

        public override BaseEventState CreateBranch(BaseEventState branch, NodeId branchId)
        {
            BaseEventState branchEvent = base.CreateBranch(branch, branchId);

            BaseEventState alarm = GetAlarm();

            branchEvent.EventId.Value = Guid.NewGuid().ToByteArray();
            branchEvent.EventType.Value = new NodeId(alarm.EventType.Value);
            branchEvent.SourceNode.Value = m_trigger.NodeId;
            branchEvent.SourceName.Value = m_trigger.SymbolicName;
            branchEvent.Time.Value = DateTime.UtcNow;
            branchEvent.ReceiveTime.Value = alarm.Time.Value;
            // Do the Message in the ConditionType, as it has the branchId
            //branchEvent.Message.Value = MapName + " Branch  " + branchEvent.Br.ToString() + " Created";
            branchEvent.Severity.Value = alarm.Severity.Value;

            return branchEvent;
        }

        #region Overrides

        public override void SetValue(string message = "")
        {
            
        }

        #endregion

        #region Helpers

        private BaseEventState GetAlarm(BaseEventState alarm = null)
        {
            if ( alarm == null )
            {
                alarm = m_alarm;
            }
            return (BaseEventState)alarm;
        }


        #endregion

        #region Child Helpers

        protected bool IsEvent( byte[] eventId )
        {
            bool isEvent = false;
            if (GetAlarm().EventId.Value.SequenceEqual(eventId) )
            {
                isEvent = true;
            }

            return isEvent;
        }


        #endregion

    }
}
