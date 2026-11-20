/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Quickstarts.Servers;

namespace Alarms
{
    public abstract class DiscreteHolder : AlarmConditionTypeHolder
    {
        protected DiscreteHolder(
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
            m_logger.DiscreteConstructorOptional(name, optional);
            if (create)
            {
                Initialize(ObjectTypes.DiscreteAlarmType, name, maxShelveTime);
            }
        }

        public new void Initialize(
            uint alarmTypeIdentifier,
            string name,
            double maxTimeShelved = AlarmDefines.NORMAL_MAX_TIME_SHELVED)
        {
            m_alarm ??= new DiscreteAlarmState(m_parent);

            // Call the base class to set parameters
            base.Initialize(alarmTypeIdentifier, name, maxTimeShelved);
        }

        public override void SetValue(string message = "")
        {
            bool active = m_alarmController.IsBooleanActive();
            int value = m_alarmController.GetValue();

            if (message.Length == 0)
            {
                message =
                    "Discrete Alarm analog value = " +
                    value.ToString(CultureInfo.InvariantCulture) +
                    ", active = " +
                    active.ToString();
            }

            base.SetValue(message);
        }
    }

    internal static partial class DiscreteHolderLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.DiscreteHolder + 0, Level = LogLevel.Trace,
            Message = "{Name} Discrete Constructor Optional = {Optional}")]
        public static partial void DiscreteConstructorOptional(this ILogger logger, string name, bool optional);
    }

}
