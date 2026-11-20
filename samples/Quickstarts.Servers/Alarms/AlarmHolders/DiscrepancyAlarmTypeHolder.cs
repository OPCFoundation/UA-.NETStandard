// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;

namespace Alarms
{
    public class DiscrepancyAlarmTypeHolder : AlarmConditionTypeHolder
    {
        public DiscrepancyAlarmTypeHolder(
            AlarmNodeManager alarmNodeManager,
            BaseInstanceState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            NodeId targetValueNodeId,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED,
            bool create = true)
            : base(
                alarmNodeManager.Server.Telemetry.CreateLogger<DiscrepancyAlarmTypeHolder>(),
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
            m_targetValueNodeId = targetValueNodeId;
            if (create)
            {
                Initialize(ObjectTypes.DiscrepancyAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_alarm ??= new DiscrepancyAlarmState(m_parent);

            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);

            DiscrepancyAlarmState alarm = GetAlarm();
            alarm.TargetValueNode!.Value = m_targetValueNodeId;
            alarm.ExpectedTime!.Value = 0.0;
        }

        private DiscrepancyAlarmState GetAlarm()
        {
            return (DiscrepancyAlarmState)m_alarm;
        }

        private readonly NodeId m_targetValueNodeId;
    }
}
