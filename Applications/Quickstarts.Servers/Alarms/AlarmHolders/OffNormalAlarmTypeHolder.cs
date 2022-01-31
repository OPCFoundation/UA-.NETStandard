using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Opc.Ua;

#pragma warning disable CS1591

namespace Alarms
{
    public class OffNormalAlarmTypeHolder : DiscreteHolder
    {
        public OffNormalAlarmTypeHolder(
            Alarms alarms,
            FolderState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true) :
            base(alarms, parent, trigger, name, alarmConditionType, controllerType, interval, optional, maxShelveTime, false)
        {
            if (create)
            {
                Initialize(Opc.Ua.ObjectTypes.OffNormalAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            if (m_alarm == null)
            {
                m_alarm = new OffNormalAlarmState(m_parent);
            }

            OffNormalAlarmState alarm = GetAlarm();

            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            alarm.NormalState.Value = new NodeId();
        }

        #region Helpers

        private OffNormalAlarmState GetAlarm()
        {
            return (OffNormalAlarmState)m_alarm;
        }

        #endregion

    }
}
