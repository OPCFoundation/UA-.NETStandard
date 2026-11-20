/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Alarms
{
    public abstract class LimitAlarmTypeHolder : AlarmConditionTypeHolder
    {
        protected LimitAlarmTypeHolder(
            ILogger logger,
            AlarmNodeManager alarmNodeManager,
            BaseInstanceState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true)
            : base(
                logger,
                alarmNodeManager,
                parent,
                trigger,
                name,
                alarmConditionType,
                controllerType,
                interval,
                optional,
                maxShelveTime,
                false)
        {
            if (create)
            {
                Initialize(ObjectTypes.LimitAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            // Create an alarm and trigger name - Create a base method for creating the trigger, just provide the name

            m_alarm ??= new LimitAlarmState(m_parent);

            LimitAlarmState alarm = GetAlarm();

            alarm.HighLimit ??= PropertyState<double>.With<VariantBuilder>(alarm);
            alarm.HighHighLimit ??= PropertyState<double>.With<VariantBuilder>(alarm);
            alarm.LowLimit ??= PropertyState<double>.With<VariantBuilder>(alarm);
            alarm.LowLowLimit ??= PropertyState<double>.With<VariantBuilder>(alarm);

            if (Optional)
            {
                alarm.BaseHighLimit = PropertyState<double>.With<VariantBuilder>(alarm);
                alarm.BaseHighHighLimit = PropertyState<double>.With<VariantBuilder>(alarm);
                alarm.BaseLowLimit = PropertyState<double>.With<VariantBuilder>(alarm);
                alarm.BaseLowLowLimit = PropertyState<double>.With<VariantBuilder>(alarm);
            }

            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            alarm.HighLimit.Value = AlarmDefines.HIGH_ALARM;
            alarm.HighHighLimit.Value = AlarmDefines.HIGHHIGH_ALARM;
            alarm.LowLimit.Value = AlarmDefines.LOW_ALARM;
            alarm.LowLowLimit.Value = AlarmDefines.LOWLOW_ALARM;

            if (Optional)
            {
                alarm.BaseHighLimit!.Value = AlarmDefines.HIGH_ALARM;
                alarm.BaseHighHighLimit!.Value = AlarmDefines.HIGHHIGH_ALARM;
                alarm.BaseLowLimit!.Value = AlarmDefines.LOW_ALARM;
                alarm.BaseLowLowLimit!.Value = AlarmDefines.LOWLOW_ALARM;
            }
            else
            {
                alarm.BaseHighHighLimit = null;
                alarm.BaseLowLimit = null;
                alarm.BaseLowLowLimit = null;
            }
        }

        private LimitAlarmState GetAlarm()
        {
            return (LimitAlarmState)m_alarm;
        }
    }
}
