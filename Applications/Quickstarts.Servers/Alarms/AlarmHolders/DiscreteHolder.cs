using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Opc.Ua;

#pragma warning disable CS0219

#pragma warning disable CS1591

namespace Alarms
{
    public class DiscreteHolder : AlarmConditionTypeHolder
    {
        public DiscreteHolder(
            AlarmNodeManager alarmNodeManager,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true) :
            base(alarmNodeManager, parent, trigger, name, alarmConditionType, controllerType, interval, optional, maxShelveTime, false)
        {
            Utils.LogInfo(name + " Discrete Constructor Optional = " + optional.ToString());
            if (create)
            {
                Initialize(Opc.Ua.ObjectTypes.DiscreteAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_analog = false;

            if (m_alarm == null)
            {
                m_alarm = new DiscreteAlarmState(m_parent);
            }

            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);
        }

        #region Overrides

        public override void SetValue(string message = "")
        {

            DiscreteAlarmState alarm = GetAlarm();
            bool active = m_alarmController.IsBooleanActive();
            int value = m_alarmController.GetValue();

            if ( message.Length == 0 )
            {
                message = "Discrete Alarm analog value = " + value.ToString() + ", active = " + active.ToString();
            }

            base.SetValue(message);
        }

        #endregion


        #region Helpers
        private DiscreteAlarmState GetAlarm()
        {
            return (DiscreteAlarmState)m_alarm;
        }

        #endregion



    }


}
