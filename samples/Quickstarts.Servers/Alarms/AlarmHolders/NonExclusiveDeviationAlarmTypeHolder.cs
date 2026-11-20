/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua;

namespace Alarms
{
    public class NonExclusiveDeviationAlarmTypeHolder : NonExclusiveLimitHolder
    {
        public NonExclusiveDeviationAlarmTypeHolder(
            AlarmNodeManager alarmNodeManager,
            BaseInstanceState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            NodeId setpointNodeId,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true)
            : base(
                alarmNodeManager.Server.Telemetry.CreateLogger<NonExclusiveDeviationAlarmTypeHolder>(),
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
            m_setpointNodeId = setpointNodeId;
            if (create)
            {
                Initialize(ObjectTypes.NonExclusiveDeviationAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_alarm ??= new NonExclusiveDeviationAlarmState(m_parent);

            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            NonExclusiveDeviationAlarmState alarm = GetAlarm();
            alarm.SetpointNode!.Value = m_setpointNodeId;
        }

        private NonExclusiveDeviationAlarmState GetAlarm()
        {
            return (NonExclusiveDeviationAlarmState)m_alarm;
        }

        private readonly NodeId m_setpointNodeId;
    }
}
