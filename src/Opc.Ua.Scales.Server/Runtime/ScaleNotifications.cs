/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Raises OPC 40200 <c>ScaleEventType</c> events and drives
    /// <c>ScaleAlarmType</c> conditions for one scale or scale system
    /// (OPC 40200 §8, Annex C).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both types carry a mandatory <c>NotificationCategory</c> and
    /// <c>NotificationId</c>, each a <c>MultiStateValueDiscreteType</c>. The
    /// publisher fills their <c>EnumValues</c> with the Annex C tables - so a
    /// client can resolve every id to its name - and the
    /// <c>ValueAsText</c> with the name of the value reported.
    /// </para>
    /// <para>
    /// An alarm is one condition per notification id, created below the
    /// source on first use and kept for the lifetime of the source, as OPC
    /// 10000-9 expects of a condition a client may have to acknowledge or
    /// refresh. The weighing runtime drives OVERLOAD_FAULT and
    /// UNDERLOAD_FAULT itself; every other id is the application's to raise.
    /// </para>
    /// </remarks>
    public sealed class ScaleNotifications
    {
        internal ScaleNotifications(
            ISystemContext context,
            BaseObjectState source,
            Action<NodeState>? register)
        {
            m_context = context;
            m_source = source;
            m_register = register;
        }

        /// <summary>
        /// Gets the source node events and alarms are reported for.
        /// </summary>
        public NodeId SourceNodeId => m_source.NodeId;

        /// <summary>
        /// Raises a <c>ScaleEventType</c> event with a notification id Annex C
        /// defines.
        /// </summary>
        /// <param name="notificationId">The notification id.</param>
        /// <param name="message">The event message.</param>
        /// <param name="severity">The event severity.</param>
        /// <param name="auxParameters">
        /// Values for placeholders in the message (<c>AuxParameters</c>).
        /// </param>
        public void RaiseEvent(
            ScaleNotificationId notificationId,
            LocalizedText message,
            EventSeverity severity = EventSeverity.Medium,
            params string[] auxParameters)
        {
            RaiseEventCore(
                (uint)notificationId,
                ScalesModel.CategoryOf(notificationId),
                ScalesModel.TextOf(notificationId),
                vendorNotificationId: null,
                message,
                severity,
                auxParameters);
        }

        /// <summary>
        /// Raises a <c>ScaleEventType</c> event with a vendor-specific
        /// notification id.
        /// </summary>
        /// <param name="notificationId">
        /// The vendor id; OPC 40200 §8.1.2 requires it to be greater than
        /// <see cref="ScalesModel.MinimumVendorNotificationId"/>.
        /// </param>
        /// <param name="category">The category the id belongs to.</param>
        /// <param name="name">The name published as the id's <c>ValueAsText</c>.</param>
        /// <param name="message">The event message.</param>
        /// <param name="severity">The event severity.</param>
        /// <exception cref="ArgumentOutOfRangeException">The id is reserved.</exception>
        public void RaiseVendorEvent(
            uint notificationId,
            ScaleNotificationCategory category,
            string name,
            LocalizedText message,
            EventSeverity severity = EventSeverity.Medium)
        {
            if (notificationId <= ScalesModel.MinimumVendorNotificationId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(notificationId),
                    notificationId,
                    "Vendor-specific notification ids shall be greater than 5000 (OPC 40200 §8.1.2).");
            }
            RaiseEventCore(
                notificationId,
                category,
                name ?? notificationId.ToString(CultureInfo.InvariantCulture),
                vendorNotificationId: name,
                message,
                severity,
                []);
        }

        /// <summary>
        /// Activates or deactivates the <c>ScaleAlarmType</c> condition of a
        /// notification id, creating the condition on first use.
        /// </summary>
        /// <param name="notificationId">The notification id.</param>
        /// <param name="active">Whether the alarm condition is active.</param>
        /// <param name="message">The condition message.</param>
        /// <param name="severity">The severity while active.</param>
        /// <returns>
        /// <see langword="true"/> when the active state changed and an event
        /// was reported.
        /// </returns>
        public bool SetAlarm(
            ScaleNotificationId notificationId,
            bool active,
            LocalizedText message,
            EventSeverity severity = EventSeverity.High)
        {
            lock (m_lock)
            {
                if (!m_alarms.TryGetValue(notificationId, out ScaleAlarmState? alarm))
                {
                    if (!active)
                    {
                        // An alarm that never activated has nothing to clear.
                        return false;
                    }
                    alarm = CreateAlarm(notificationId);
                    m_alarms[notificationId] = alarm;
                }

                bool wasActive = alarm.ActiveState?.Id?.Value == true;
                if (wasActive == active)
                {
                    return false;
                }

                alarm.SetActiveState(m_context, active);
                if (active)
                {
                    alarm.SetAcknowledgedState(m_context, false);
                }
                alarm.Retain!.Value = active || alarm.AckedState?.Id?.Value != true;
                alarm.SetSeverity(m_context, active ? severity : EventSeverity.Low);
                alarm.Message!.Value = message;
                alarm.Time!.Value = DateTimeUtc.Now;
                alarm.ReceiveTime!.Value = alarm.Time.Value;
                alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
                alarm.ClearChangeMasks(m_context, true);

                if (alarm.EnabledState?.Id?.Value == true)
                {
                    var snapshot = new InstanceStateSnapshot();
                    snapshot.Initialize(m_context, alarm);
                    alarm.ReportEvent(m_context, snapshot);
                }
                return true;
            }
        }

        /// <summary>
        /// Gets whether the alarm of a notification id is active.
        /// </summary>
        /// <param name="notificationId">The notification id.</param>
        public bool IsAlarmActive(ScaleNotificationId notificationId)
        {
            lock (m_lock)
            {
                return m_alarms.TryGetValue(notificationId, out ScaleAlarmState? alarm) &&
                    alarm.ActiveState?.Id?.Value == true;
            }
        }

        /// <summary>
        /// Gets the alarm condition of a notification id, or
        /// <see langword="null"/> when it was never raised.
        /// </summary>
        /// <param name="notificationId">The notification id.</param>
        public ScaleAlarmState? Alarm(ScaleNotificationId notificationId)
        {
            lock (m_lock)
            {
                return m_alarms.TryGetValue(notificationId, out ScaleAlarmState? alarm) ? alarm : null;
            }
        }

        private void RaiseEventCore(
            uint notificationId,
            ScaleNotificationCategory category,
            string idText,
            string? vendorNotificationId,
            LocalizedText message,
            EventSeverity severity,
            string[] auxParameters)
        {
            // Reported under the same lock as the alarms, so events and
            // condition changes of one source reach the notifier one at a
            // time, whichever thread raises them.
            lock (m_lock)
            {
                ScaleEventState e = m_context.CreateInstanceOfScaleEventType(m_source, default);
                e.Initialize(m_context, m_source, severity, message);
                Describe(e.NotificationCategory, (uint)category, ScalesModel.TextOf(category), s_categoryValues);
                Describe(e.NotificationId, notificationId, idText, IdValues(notificationId, idText));
                if (auxParameters is { Length: > 0 })
                {
                    e.AddAuxParameters(m_context);
                    e.AuxParameters!.Value = auxParameters.ToArrayOf();
                }
                if (vendorNotificationId != null)
                {
                    e.AddVendorNotificationId(m_context);
                    e.VendorNotificationId!.Value = vendorNotificationId;
                }
                m_source.ReportEvent(m_context, e);
            }
        }

        private ScaleAlarmState CreateAlarm(ScaleNotificationId notificationId)
        {
            string text = ScalesModel.TextOf(notificationId);
            var browseName = new QualifiedName(
                text,
                (ushort)m_context.NamespaceUris.GetIndexOrAppend(Namespaces.Scales));
            ScaleAlarmState alarm = m_context.CreateInstanceOfScaleAlarmType(m_source, browseName);
            alarm.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            alarm.DisplayName = new LocalizedText(text);
            m_source.AddChild(alarm);

            // A condition is never initialised like a transient event, so its
            // EventType has to be stamped for a client to recognise it.
            alarm.EventType!.Value = alarm.TypeDefinitionId;
            alarm.SourceNode!.Value = m_source.NodeId;
            alarm.SourceName!.Value = m_source.BrowseName.Name ?? string.Empty;
            alarm.ConditionName!.Value = text;
            alarm.BranchId!.Value = NodeId.Null;
            alarm.ClientUserId!.Value = string.Empty;
            alarm.Quality!.Value = StatusCodes.Good;
            alarm.Retain!.Value = false;
            alarm.Comment!.Value = new LocalizedText(string.Empty);
            alarm.SetEnableState(m_context, enabled: true);
            alarm.SetActiveState(m_context, false);
            alarm.SetAcknowledgedState(m_context, true);
            alarm.SetSeverity(m_context, EventSeverity.Low);

            ScaleNotificationCategory category = ScalesModel.CategoryOf(notificationId);
            Describe(alarm.NotificationCategory, (uint)category, ScalesModel.TextOf(category), s_categoryValues);
            Describe(alarm.NotificationId, (uint)notificationId, text, s_idValues);

            // OPC 10000-9 reaches a condition from its source through
            // HasCondition; the component reference alone does not make it
            // discoverable as the source's condition.
            m_source.AddReference(Opc.Ua.ReferenceTypeIds.HasCondition, false, alarm.NodeId);
            alarm.AddReference(Opc.Ua.ReferenceTypeIds.HasCondition, true, m_source.NodeId);

            alarm.OnAcknowledge = (context, condition, eventId, comment) =>
            {
                ScaleAlarmState acknowledged = (ScaleAlarmState)condition;
                acknowledged.Retain!.Value = acknowledged.ActiveState?.Id?.Value == true;
                return ServiceResult.Good;
            };

            m_register?.Invoke(alarm);

            // A condition added after a client subscribed to the source does
            // not see that subscription unless it inherits the source's
            // monitoring state; without it the condition's reports would stop
            // at the condition instead of reaching the source's notifier.
            if (m_source.AreEventsMonitored)
            {
                alarm.SetAreEventsMonitored(m_context, true, true);
            }
            return alarm;
        }

        /// <summary>
        /// Creates the inactive condition of a notification id now rather than
        /// on first activation, so it takes part in <c>ConditionRefresh</c>
        /// from the start.
        /// </summary>
        /// <param name="notificationId">The notification id.</param>
        /// <returns>The condition.</returns>
        public ScaleAlarmState EnsureAlarm(ScaleNotificationId notificationId)
        {
            lock (m_lock)
            {
                if (!m_alarms.TryGetValue(notificationId, out ScaleAlarmState? alarm))
                {
                    alarm = CreateAlarm(notificationId);
                    m_alarms[notificationId] = alarm;
                }
                return alarm;
            }
        }

        private static void Describe(
            MultiStateValueDiscreteState? variable,
            uint value,
            string text,
            ArrayOf<EnumValueType> enumValues)
        {
            if (variable == null)
            {
                return;
            }
            if (variable.EnumValues != null)
            {
                variable.EnumValues.Value = enumValues;
            }
            variable.WrappedValue = Variant.From(value);
            if (variable.ValueAsText != null)
            {
                variable.ValueAsText.Value = new LocalizedText(text);
            }
        }

        private static ArrayOf<EnumValueType> IdValues(uint notificationId, string text)
        {
            if (notificationId <= ScalesModel.MinimumVendorNotificationId)
            {
                return s_idValues;
            }
            // A vendor id is not in the Annex C table; EnumValues may be a
            // subset, so the reported id is published alongside it.
            var values = new List<EnumValueType>(s_idValues.Count + 1);
            foreach (EnumValueType entry in s_idValues)
            {
                values.Add(entry);
            }
            values.Add(Entry(notificationId, text));
            return values.ToArrayOf();
        }

        private static EnumValueType Entry(long value, string text)
        {
            return new EnumValueType
            {
                Value = value,
                DisplayName = new LocalizedText(text),
                Description = LocalizedText.Null
            };
        }

        private static ArrayOf<EnumValueType> BuildCategoryValues()
        {
            var values = new List<EnumValueType>();
            for (uint ii = (uint)ScaleNotificationCategory.Others; ii <= (uint)ScaleNotificationCategory.Environment; ii++)
            {
                values.Add(Entry(ii, ScalesModel.TextOf((ScaleNotificationCategory)ii)));
            }
            return values.ToArrayOf();
        }

        private static ArrayOf<EnumValueType> BuildIdValues()
        {
            ScaleNotificationId[] ids =
            [
                ScaleNotificationId.GeneralOtherFault,
                ScaleNotificationId.GeneralProcessFault,
                ScaleNotificationId.ProductDataFault,
                ScaleNotificationId.LabelFault,
                ScaleNotificationId.BadPack,
                ScaleNotificationId.CriticalExternalFault,
                ScaleNotificationId.RejectVerificationFault,
                ScaleNotificationId.ContaminationDetect,
                ScaleNotificationId.VisionInspection,
                ScaleNotificationId.ConsecutiveRejects,
                ScaleNotificationId.EmergencyStop,
                ScaleNotificationId.GeneralSystemFault,
                ScaleNotificationId.LoginFault,
                ScaleNotificationId.GeneralMemoryFault,
                ScaleNotificationId.GeneralComponentFault,
                ScaleNotificationId.PrinterFault,
                ScaleNotificationId.FeederFault,
                ScaleNotificationId.FeederNotRunning,
                ScaleNotificationId.GeneralCommunicationFault,
                ScaleNotificationId.GeneralWeighingModuleFault,
                ScaleNotificationId.OverloadFault,
                ScaleNotificationId.OutOfRangeFault,
                ScaleNotificationId.UnderloadFault,
                ScaleNotificationId.TareSettingFault,
                ScaleNotificationId.ZeroSettingFault,
                ScaleNotificationId.RezeroRequired,
                ScaleNotificationId.GeneralEnvironmentFault,
                ScaleNotificationId.PowerSupplyFault,
                ScaleNotificationId.AirPressureFault
            ];
            var values = new List<EnumValueType>(ids.Length);
            foreach (ScaleNotificationId id in ids)
            {
                values.Add(Entry((uint)id, ScalesModel.TextOf(id)));
            }
            return values.ToArrayOf();
        }

        private static readonly ArrayOf<EnumValueType> s_categoryValues = BuildCategoryValues();
        private static readonly ArrayOf<EnumValueType> s_idValues = BuildIdValues();
        private readonly ISystemContext m_context;
        private readonly BaseObjectState m_source;
        private readonly Action<NodeState>? m_register;
        private readonly Dictionary<ScaleNotificationId, ScaleAlarmState> m_alarms = [];
        private readonly Lock m_lock = new();
    }
}
