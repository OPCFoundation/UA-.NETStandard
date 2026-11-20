// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;

namespace Alarms
{
    public class OffNormalAlarmTypeHolder : DiscreteHolder
    {
        public OffNormalAlarmTypeHolder(
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
                alarmNodeManager.Server.Telemetry.CreateLogger<OffNormalAlarmTypeHolder>(),
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
                Initialize(ObjectTypes.OffNormalAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_alarm ??= new OffNormalAlarmState(m_parent);

            OffNormalAlarmState alarm = GetAlarm();

            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            alarm.NormalState!.Value = new NodeId();
        }

        private OffNormalAlarmState GetAlarm()
        {
            return (OffNormalAlarmState)m_alarm;
        }
    }
}
