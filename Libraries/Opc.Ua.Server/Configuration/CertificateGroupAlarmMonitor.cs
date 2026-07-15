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
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Drives the two optional certificate-group alarm instances defined by
    /// OPC 10000-12 §7.8.3 - <c>CertificateExpired</c>
    /// (<see cref="CertificateExpirationAlarmState"/>) and
    /// <c>TrustListOutOfDate</c> (<see cref="TrustListOutOfDateAlarmState"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Instances are created and owned by the
    /// <see cref="ConfigurationNodeManager"/>. The monitor evaluates the
    /// certificate expiration and trust-list staleness on each periodic tick
    /// (and after a committed certificate/TrustList change) and drives the
    /// standard active/inactive, severity, retain and acknowledgement state of
    /// the two alarms per OPC 10000-9 (Alarms and Conditions) and
    /// OPC 10000-12.
    /// </para>
    /// <para>
    /// A transition event is reported when the active state changes, or when an
    /// already-active alarm changes severity (for example
    /// <c>CertificateExpired</c> crossing from approaching-expiry Medium to
    /// actually-expired High). Repeated ticks that observe the same active state
    /// and severity never emit duplicate events. All time reads flow through the
    /// injected <see cref="TimeProvider"/> so the behaviour is deterministic in
    /// tests.
    /// </para>
    /// </remarks>
    internal sealed class CertificateGroupAlarmMonitor
    {
        /// <summary>
        /// The default lead time before a certificate's expiration at which
        /// the <c>CertificateExpired</c> alarm activates when no explicit
        /// <c>ExpirationLimit</c> is configured (two weeks, per the property
        /// default described in OPC 10000-12).
        /// </summary>
        public static readonly TimeSpan DefaultExpirationLimit = TimeSpan.FromDays(14);

        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateGroupAlarmMonitor"/> class.
        /// </summary>
        /// <param name="groupNode">The certificate group whose optional alarm children are driven.</param>
        /// <param name="sourceName">The source name reported by the alarms.</param>
        /// <param name="timeProvider">The time provider used for every timestamp and threshold.</param>
        /// <param name="logger">The logger.</param>
        public CertificateGroupAlarmMonitor(
            CertificateGroupState groupNode,
            string sourceName,
            TimeProvider timeProvider,
            ILogger logger)
        {
            m_groupNode = groupNode ?? throw new ArgumentNullException(nameof(groupNode));
            m_sourceName = sourceName ?? string.Empty;
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the <c>CertificateExpired</c> alarm instance, or <see langword="null"/>
        /// when the optional node was not created.
        /// </summary>
        public CertificateExpirationAlarmState? CertificateExpired => m_groupNode.CertificateExpired;

        /// <summary>
        /// Gets the <c>TrustListOutOfDate</c> alarm instance, or <see langword="null"/>
        /// when the optional node was not created.
        /// </summary>
        public TrustListOutOfDateAlarmState? TrustListOutOfDate => m_groupNode.TrustListOutOfDate;

        /// <summary>
        /// Performs the one-time, event-free initialization of the alarm
        /// condition fields (enabled, inactive, acknowledged, not retained) so
        /// the nodes present a valid state before monitoring begins.
        /// </summary>
        /// <param name="context">The system context.</param>
        public void InitializeQuiet(ISystemContext context)
        {
            if (CertificateExpired is { } certificateExpired)
            {
                InitializeAlarm(
                    context,
                    certificateExpired,
                    ObjectTypeIds.CertificateExpirationAlarmType,
                    BrowseNames.CertificateExpired);

                // ExpirationLimit is created by the node manager
                // (AddExpirationLimit); default it to two weeks when unset.
                if (certificateExpired.ExpirationLimit is { } expirationLimit &&
                    expirationLimit.Value <= 0)
                {
                    expirationLimit.Value = DefaultExpirationLimit.TotalMilliseconds;
                }
                certificateExpired.Message!.Value = LocalizedText.From("The certificate is valid.");
                certificateExpired.ClearChangeMasks(context, includeChildren: true);
            }

            if (TrustListOutOfDate is { } trustListOutOfDate)
            {
                InitializeAlarm(
                    context,
                    trustListOutOfDate,
                    ObjectTypeIds.TrustListOutOfDateAlarmType,
                    BrowseNames.TrustListOutOfDate);

                trustListOutOfDate.Message!.Value = LocalizedText.From("The TrustList is up to date.");
                trustListOutOfDate.ClearChangeMasks(context, includeChildren: true);
            }
        }

        /// <summary>
        /// Publishes the current certificate state onto the
        /// <c>CertificateExpired</c> alarm inputs (expiration date, encoded
        /// certificate and certificate type) without emitting any event.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="expirationUtc">The earliest expiration of the group certificates, or <see langword="null"/> when unknown.</param>
        /// <param name="certificate">The encoded certificate that expires first.</param>
        /// <param name="certificateType">The type of the certificate that expires first.</param>
        public void SetCertificateExpiration(
            ISystemContext context,
            DateTime? expirationUtc,
            ByteString certificate,
            NodeId certificateType)
        {
            if (CertificateExpired is not { } alarm)
            {
                return;
            }

            if (alarm.ExpirationDate != null)
            {
                alarm.ExpirationDate.Value = expirationUtc.HasValue
                    ? (DateTimeUtc)DateTime.SpecifyKind(expirationUtc.Value, DateTimeKind.Utc)
                    : (DateTimeUtc)DateTime.MaxValue;
            }
            if (alarm.Certificate != null)
            {
                alarm.Certificate.Value = certificate;
            }
            if (alarm.CertificateType != null && !certificateType.IsNull)
            {
                alarm.CertificateType.Value = certificateType;
            }

            alarm.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Publishes the current trust-list state onto the
        /// <c>TrustListOutOfDate</c> alarm inputs (trust-list node id, last
        /// update time and expected update frequency) without emitting any
        /// event.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="trustListId">The node id of the trust list.</param>
        /// <param name="lastUpdateUtc">The last time the trust list was written.</param>
        /// <param name="updateFrequencyMs">The expected update frequency in milliseconds; a non-positive value disables the staleness check.</param>
        public void SetTrustListStatus(
            ISystemContext context,
            NodeId trustListId,
            DateTime lastUpdateUtc,
            double updateFrequencyMs)
        {
            if (TrustListOutOfDate is not { } alarm)
            {
                return;
            }

            if (alarm.TrustListId != null && !trustListId.IsNull)
            {
                alarm.TrustListId.Value = trustListId;
            }
            if (alarm.LastUpdateTime != null)
            {
                alarm.LastUpdateTime.Value = (DateTimeUtc)DateTime.SpecifyKind(lastUpdateUtc, DateTimeKind.Utc);
            }
            if (alarm.UpdateFrequency != null)
            {
                alarm.UpdateFrequency.Value = updateFrequencyMs;
            }

            alarm.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Evaluates both alarms against the current time and drives their
        /// active/inactive transitions. Events are emitted only when
        /// <paramref name="emitEvents"/> is <see langword="true"/> and the
        /// active state actually changed.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="emitEvents">Whether transition events may be reported.</param>
        public void Evaluate(ISystemContext context, bool emitEvents)
        {
            lock (m_evaluationLock)
            {
                DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                EvaluateCertificateExpired(context, now, emitEvents);
                EvaluateTrustListOutOfDate(context, now, emitEvents);
            }
        }

        private void EvaluateCertificateExpired(ISystemContext context, DateTime now, bool emitEvents)
        {
            if (CertificateExpired is not { } alarm || alarm.ExpirationDate == null)
            {
                return;
            }

            try
            {
                var expiration = (DateTime)alarm.ExpirationDate.Value;

                // A MaxValue expiration means there is no certificate to watch.
                if (expiration == DateTime.MaxValue)
                {
                    Transition(context, alarm, active: false, EventSeverity.Min,
                        "The certificate is valid.", emitEvents,
                        ref m_certificateExpiredActive, ref m_certificateExpiredSeverity);
                    return;
                }

                double limitMs = alarm.ExpirationLimit?.Value ?? DefaultExpirationLimit.TotalMilliseconds;
                if (limitMs <= 0)
                {
                    limitMs = DefaultExpirationLimit.TotalMilliseconds;
                }

                DateTime threshold = SubtractSafe(expiration, TimeSpan.FromMilliseconds(limitMs));
                bool active = now >= threshold;

                if (!active)
                {
                    Transition(context, alarm, active: false, EventSeverity.Min,
                        "The certificate is valid.", emitEvents,
                        ref m_certificateExpiredActive, ref m_certificateExpiredSeverity);
                    return;
                }

                bool expired = now >= expiration;
                EventSeverity severity = expired ? EventSeverity.High : EventSeverity.Medium;
                string message = expired
                    ? $"The certificate has expired on {expiration:u}."
                    : $"The certificate will expire on {expiration:u}.";

                Transition(context, alarm, active: true, severity, message, emitEvents,
                    ref m_certificateExpiredActive, ref m_certificateExpiredSeverity);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Failed to evaluate CertificateExpired alarm for group {Group}.",
                    m_sourceName);
            }
        }

        private void EvaluateTrustListOutOfDate(ISystemContext context, DateTime now, bool emitEvents)
        {
            if (TrustListOutOfDate is not { } alarm || alarm.LastUpdateTime == null)
            {
                return;
            }

            try
            {
                double frequencyMs = alarm.UpdateFrequency?.Value ?? 0;

                // A non-positive update frequency disables the staleness check.
                if (frequencyMs <= 0)
                {
                    Transition(context, alarm, active: false, EventSeverity.Min,
                        "The TrustList is up to date.", emitEvents,
                        ref m_trustListOutOfDateActive, ref m_trustListOutOfDateSeverity);
                    return;
                }

                var lastUpdate = (DateTime)alarm.LastUpdateTime.Value;
                DateTime deadline = AddSafe(lastUpdate, TimeSpan.FromMilliseconds(frequencyMs));
                bool active = now > deadline;

                string message = active
                    ? $"The TrustList has not been updated since {lastUpdate:u}."
                    : "The TrustList is up to date.";

                Transition(context, alarm, active, EventSeverity.Medium, message, emitEvents,
                    ref m_trustListOutOfDateActive, ref m_trustListOutOfDateSeverity);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Failed to evaluate TrustListOutOfDate alarm for group {Group}.",
                    m_sourceName);
            }
        }

        private void Transition(
            ISystemContext context,
            SystemOffNormalAlarmState alarm,
            bool active,
            EventSeverity severity,
            string message,
            bool emitEvents,
            ref bool? lastActive,
            ref EventSeverity lastSeverity)
        {
            EventSeverity effectiveSeverity = active ? severity : EventSeverity.Min;

            // A transition is required when the active state changes or - while
            // the alarm stays active - its severity escalates/de-escalates (e.g.
            // CertificateExpired crossing from approaching-expiry Medium to
            // actually-expired High). Repeated ticks that observe the same
            // active state *and* the same severity never emit duplicate events.
            bool activeChanged = lastActive != active;
            bool severityChanged = !activeChanged && active && lastSeverity != effectiveSeverity;
            if (!activeChanged && !severityChanged)
            {
                return;
            }

            bool firstEvaluation = lastActive == null;
            lastActive = active;
            lastSeverity = effectiveSeverity;

            alarm.SetActiveState(context, active);
            alarm.SetSeverity(context, effectiveSeverity);

            // A newly active alarm requires acknowledgement (and confirmation
            // when supported) again. A severity escalation on an already-active
            // alarm does not re-clear the existing acknowledgement.
            if (active && activeChanged)
            {
                alarm.SetAcknowledgedState(context, acknowledged: false);
                if (alarm.SupportsConfirm())
                {
                    alarm.SetConfirmedState(context, confirmed: false);
                }
            }

            alarm.Message!.Value = LocalizedText.From(message);
            UpdateRetain(alarm, active);

            // The initial (quiet) evaluation establishes the baseline inactive
            // state; it must not raise an event before the subscription
            // infrastructure is ready.
            if (emitEvents && !(firstEvaluation && !active))
            {
                ReportEvent(context, alarm);
            }
            else
            {
                alarm.ClearChangeMasks(context, includeChildren: true);
            }
        }

        private void UpdateRetain(SystemOffNormalAlarmState alarm, bool active)
        {
            // Retain mirrors OPC 10000-9: an alarm is retained while it is
            // active, or while it still needs acknowledgement/confirmation.
            bool retain = active
                || !alarm.AckedState!.Id!.Value
                || (alarm.SupportsConfirm() && !alarm.ConfirmedState!.Id!.Value);
            alarm.Retain!.Value = retain;
        }

        private void ReportEvent(ISystemContext context, SystemOffNormalAlarmState alarm)
        {
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            alarm.Time!.Value = (DateTimeUtc)m_timeProvider.GetUtcNow().UtcDateTime;
            alarm.ReceiveTime!.Value = alarm.Time.Value;

            alarm.ClearChangeMasks(context, includeChildren: true);

            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(context, alarm);
            alarm.ReportEvent(context, snapshot);
        }

        private void InitializeAlarm(
            ISystemContext context,
            SystemOffNormalAlarmState alarm,
            NodeId eventTypeId,
            string conditionName)
        {
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;

            alarm.EventType!.Value = eventTypeId;
            alarm.SourceNode!.Value = m_groupNode.NodeId;
            alarm.SourceName!.Value = m_sourceName;
            alarm.ConditionName!.Value = conditionName;
            alarm.BranchId!.Value = NodeId.Null;
            alarm.ClientUserId?.Value = string.Empty;
            alarm.EventId!.Value = Uuid.NewUuid().ToByteString();
            alarm.Time!.Value = (DateTimeUtc)now;
            alarm.ReceiveTime!.Value = alarm.Time.Value;

            if (alarm.ConditionClassId != null)
            {
                alarm.ConditionClassId.Value = ObjectTypeIds.BaseConditionClassType;
            }
            alarm.ConditionClassName?.Value = LocalizedText.From("BaseConditionClass");

            alarm.SetEnableState(context, enabled: true);
            alarm.Quality!.Value = StatusCodes.Good;
            alarm.LastSeverity!.Value = (ushort)EventSeverity.Min;
            alarm.Severity!.Value = (ushort)EventSeverity.Min;
            alarm.Comment!.Value = LocalizedText.From(string.Empty);

            alarm.SetActiveState(context, active: false);
            alarm.SetAcknowledgedState(context, acknowledged: true);
            if (alarm.ConfirmedState != null)
            {
                alarm.SetConfirmedState(context, confirmed: true);
            }
            alarm.Retain!.Value = false;
            alarm.AutoReportStateChanges = true;
        }

        private static DateTime SubtractSafe(DateTime value, TimeSpan delta)
        {
            return value - DateTime.MinValue < delta ? DateTime.MinValue : value - delta;
        }

        private static DateTime AddSafe(DateTime value, TimeSpan delta)
        {
            return DateTime.MaxValue - value < delta ? DateTime.MaxValue : value + delta;
        }

        private readonly CertificateGroupState m_groupNode;
        private readonly string m_sourceName;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly Lock m_evaluationLock = new();
        private bool? m_certificateExpiredActive;
        private bool? m_trustListOutOfDateActive;
        private EventSeverity m_certificateExpiredSeverity = EventSeverity.Min;
        private EventSeverity m_trustListOutOfDateSeverity = EventSeverity.Min;
    }
}
