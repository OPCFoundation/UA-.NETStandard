/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Quickstarts.Servers;

namespace Alarms
{
    public class NonExclusiveLevelHolder : NonExclusiveLimitHolder
    {
        public NonExclusiveLevelHolder(
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
                alarmNodeManager.Server.Telemetry.CreateLogger<NonExclusiveLevelHolder>(),
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
                Initialize(ObjectTypes.NonExclusiveLevelAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            // Create an alarm and trigger name - Create a base method for creating the trigger, just provide the name

            m_alarm ??= new NonExclusiveLevelAlarmState(m_parent);

            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);
            m_logger.AlarmTypeDefinition(m_alarm.TypeDefinitionId);
        }

        public override void SetBranching(bool value)
        {
            m_supportsBranching = value;
        }
    }

    internal static partial class NonExclusiveLevelHolderLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.NonExclusiveLevelHolder + 0, Level = LogLevel.Trace,
            Message = "NonExclusiveLevelHolder alarm typedefinition {TypeDefinitionId}")]
        public static partial void AlarmTypeDefinition(this ILogger logger, NodeId typeDefinitionId);
    }

}
