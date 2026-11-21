// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;

namespace Alarms
{
    public class ExclusiveRateOfChangeAlarmTypeHolder : ExclusiveLimitHolder
    {
        public ExclusiveRateOfChangeAlarmTypeHolder(
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
                alarmNodeManager.Server.Telemetry.CreateLogger<ExclusiveRateOfChangeAlarmTypeHolder>(),
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
                Initialize(ObjectTypes.ExclusiveRateOfChangeAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_alarm ??= new ExclusiveRateOfChangeAlarmState(m_parent);

            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);
        }
    }
}
