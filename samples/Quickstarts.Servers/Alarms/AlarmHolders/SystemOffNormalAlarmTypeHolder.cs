// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;

namespace Alarms
{
    public class SystemOffNormalAlarmTypeHolder : DiscreteHolder
    {
        public SystemOffNormalAlarmTypeHolder(
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
                alarmNodeManager.Server.Telemetry.CreateLogger<SystemOffNormalAlarmTypeHolder>(),
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
                Initialize(ObjectTypes.SystemOffNormalAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_alarm ??= new SystemOffNormalAlarmState(m_parent);

            SystemOffNormalAlarmState alarm = GetAlarm();

            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            alarm.NormalState!.Value = new NodeId();
        }

        private SystemOffNormalAlarmState GetAlarm()
        {
            return (SystemOffNormalAlarmState)m_alarm;
        }
    }
}
