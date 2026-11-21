// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;

namespace Alarms
{
    public class ExclusiveLimitAlarmHolder : ExclusiveLimitHolder
    {
        public ExclusiveLimitAlarmHolder(
            AlarmNodeManager alarmNodeManager,
            BaseInstanceState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional = true,
            double maxShelveTime = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
            : base(
                alarmNodeManager.Server.Telemetry.CreateLogger<ExclusiveLimitAlarmHolder>(),
                alarmNodeManager,
                parent,
                trigger,
                name,
                alarmConditionType,
                controllerType,
                interval,
                optional,
                maxShelveTime,
                true)
        {
        }
    }
}
