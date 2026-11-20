// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;

namespace Alarms
{
    public class AcknowledgeableConditionHolder : AcknowledgeableConditionTypeHolder
    {
        public AcknowledgeableConditionHolder(
            AlarmNodeManager alarmNodeManager,
            BaseInstanceState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval)
            : base(
                alarmNodeManager.Server.Telemetry.CreateLogger<AcknowledgeableConditionHolder>(),
                alarmNodeManager,
                parent,
                trigger,
                name,
                alarmConditionType,
                controllerType,
                interval,
                optional: false)
        {
        }
    }
}
