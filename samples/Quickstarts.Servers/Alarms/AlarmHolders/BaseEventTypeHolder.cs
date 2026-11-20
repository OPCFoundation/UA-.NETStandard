// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Alarms
{
    public abstract class BaseEventTypeHolder : AlarmHolder
    {
        protected BaseEventTypeHolder(
            ILogger logger,
            AlarmNodeManager alarmNodeManager,
            BaseInstanceState parent,
            SourceController trigger,
            string name,
            SupportedAlarmConditionType alarmConditionType,
            Type controllerType,
            int interval,
            bool optional)
            : base(
                logger,
                alarmNodeManager,
                parent,
                trigger,
                controllerType,
                interval)
        {
            m_optional = optional;
        }

        protected new void Initialize(uint alarmTypeIdentifier, string name)
        {
            m_alarmTypeIdentifier = alarmTypeIdentifier;

            if (m_alarm != null)
            {
                // Call the base class to set parameters
                base.Initialize(alarmTypeIdentifier, name);

                BaseEventState alarm = GetAlarm();

                alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
                alarm.EventType!.Value = new NodeId(
                    alarmTypeIdentifier,
                    GetNameSpaceIndex(alarmTypeIdentifier));
                alarm.SourceNode!.Value = m_trigger.NodeId;
                alarm.SourceName!.Value = m_trigger.SymbolicName;
                alarm.Time!.Value = DateTimeUtc.Now;
                alarm.ReceiveTime!.Value = alarm.Time.Value;
                alarm.Message!.Value = LocalizedText.From(name + " Initialized");
                alarm.Severity!.Value = AlarmDefines.INACTIVE_SEVERITY;

                // TODO Implement for Optionals - Needs to go to all places where Time is set.
                alarm.LocalTime = null;
            }
        }

        public override void SetValue(string message = "")
        {
        }

        private BaseEventState GetAlarm(BaseEventState? alarm = null)
        {
            alarm ??= m_alarm;
            return alarm;
        }

        protected bool IsEvent(ByteString eventId)
        {
            bool isEvent = false;
            if (GetAlarm().EventId!.Value == eventId)
            {
                isEvent = true;
            }

            return isEvent;
        }
    }
}
