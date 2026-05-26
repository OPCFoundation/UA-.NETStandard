/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Strongly-typed record of a base condition event.
    /// </summary>
    public record ConditionRecord
    {
        /// <summary>The event id.</summary>
        public ByteString EventId { get; init; }

        /// <summary>The type definition of the event.</summary>
        public NodeId? EventType { get; init; }

        /// <summary>NodeId of the source of the event.</summary>
        public NodeId? SourceNode { get; init; }

        /// <summary>Source name string.</summary>
        public string? SourceName { get; init; }

        /// <summary>Event timestamp.</summary>
        public DateTime Time { get; init; }

        /// <summary>Receive timestamp.</summary>
        public DateTime ReceiveTime { get; init; }

        /// <summary>Server-side message.</summary>
        public LocalizedText Message { get; init; }

        /// <summary>Severity 1-1000.</summary>
        public ushort Severity { get; init; }

        /// <summary>The condition NodeId (event source).</summary>
        public NodeId? ConditionId { get; init; }

        /// <summary>The condition name.</summary>
        public string? ConditionName { get; init; }

        /// <summary>BranchId identifies the condition branch; NULL for trunk.</summary>
        public NodeId? BranchId { get; init; }

        /// <summary>Whether the condition is interesting to retain.</summary>
        public bool Retain { get; init; }

        /// <summary>Enabled state value (true/false).</summary>
        public bool? EnabledStateId { get; init; }

        /// <summary>Quality status code.</summary>
        public StatusCode Quality { get; init; }

        /// <summary>The last comment on the condition.</summary>
        public LocalizedText Comment { get; init; }

        /// <summary>Client user identifier.</summary>
        public string? ClientUserId { get; init; }
    }

    /// <summary>
    /// Acknowledgeable condition record adding ack/confirm states.
    /// </summary>
    public record AcknowledgeableConditionRecord : ConditionRecord
    {
        /// <summary>Whether the condition is acknowledged.</summary>
        public bool? AckedStateId { get; init; }

        /// <summary>Whether the condition is confirmed.</summary>
        public bool? ConfirmedStateId { get; init; }
    }

    /// <summary>
    /// Alarm record adding active/suppressed/shelved/latched states.
    /// </summary>
    public record AlarmRecord : AcknowledgeableConditionRecord
    {
        /// <summary>Whether the alarm is active.</summary>
        public bool? ActiveStateId { get; init; }

        /// <summary>InputNode (source variable being monitored).</summary>
        public NodeId? InputNode { get; init; }

        /// <summary>Whether the alarm is suppressed.</summary>
        public bool? SuppressedStateId { get; init; }

        /// <summary>Whether the alarm is out of service.</summary>
        public bool? OutOfServiceStateId { get; init; }

        /// <summary>Whether the alarm is latched.</summary>
        public bool? LatchedStateId { get; init; }

        /// <summary>Whether the alarm is silenced.</summary>
        public bool? SilenceStateId { get; init; }

        /// <summary>Suppressed-or-shelved flag.</summary>
        public bool? SuppressedOrShelved { get; init; }
    }

    /// <summary>
    /// Dialog condition record.
    /// </summary>
    public record DialogRecord : ConditionRecord
    {
        /// <summary>Whether the dialog is active.</summary>
        public bool? DialogStateId { get; init; }

        /// <summary>The dialog prompt.</summary>
        public LocalizedText Prompt { get; init; }

        /// <summary>Available response options.</summary>
        public LocalizedText[]? ResponseOptionSet { get; init; }
    }

    /// <summary>
    /// Decodes raw <see cref="Subscriptions.EventNotification"/> field
    /// arrays into typed condition/alarm/dialog records.
    /// </summary>
    /// <remarks>
    /// The decoder is paired with an <see cref="AlarmEventFilterBuilder"/>
    /// — the order of fields in <see cref="Subscriptions.EventNotification.Fields"/>
    /// matches the order of select clauses produced by the builder.
    /// </remarks>
    public static class AlarmEventDecoder
    {
        /// <summary>
        /// Standard field order used by <see cref="AlarmEventFilterBuilder"/>.
        /// Each entry is a browse path from the event type root.
        /// </summary>
        internal static readonly QualifiedName[][] StandardFields =
        [
            // Base event fields
            [QualifiedName.From(BrowseNames.EventId)],
            [QualifiedName.From(BrowseNames.EventType)],
            [QualifiedName.From(BrowseNames.SourceNode)],
            [QualifiedName.From(BrowseNames.SourceName)],
            [QualifiedName.From(BrowseNames.Time)],
            [QualifiedName.From(BrowseNames.ReceiveTime)],
            [QualifiedName.From(BrowseNames.Message)],
            [QualifiedName.From(BrowseNames.Severity)],
            // Condition fields
            [QualifiedName.From(BrowseNames.ConditionName)],
            [QualifiedName.From(BrowseNames.BranchId)],
            [QualifiedName.From(BrowseNames.Retain)],
            [QualifiedName.From(BrowseNames.EnabledState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.Quality)],
            [QualifiedName.From(BrowseNames.Comment)],
            [QualifiedName.From(BrowseNames.ClientUserId)],
            // Acknowledgeable condition fields
            [QualifiedName.From(BrowseNames.AckedState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.ConfirmedState), QualifiedName.From(BrowseNames.Id)],
            // Alarm condition fields
            [QualifiedName.From(BrowseNames.ActiveState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.InputNode)],
            [QualifiedName.From(BrowseNames.SuppressedState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.OutOfServiceState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.LatchedState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.SilenceState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.SuppressedOrShelved)],
            // Dialog fields
            [QualifiedName.From(BrowseNames.DialogState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.Prompt)],
            [QualifiedName.From(BrowseNames.ResponseOptionSet)],
            // LimitAlarmType fields
            [QualifiedName.From(BrowseNames.HighHighLimit)],
            [QualifiedName.From(BrowseNames.HighLimit)],
            [QualifiedName.From(BrowseNames.LowLimit)],
            [QualifiedName.From(BrowseNames.LowLowLimit)],
            // ExclusiveLimitAlarmType fields
            [QualifiedName.From(BrowseNames.LimitState), QualifiedName.From(BrowseNames.CurrentState)],
            [QualifiedName.From(BrowseNames.LimitState), QualifiedName.From(BrowseNames.CurrentState), QualifiedName.From(BrowseNames.Id)],
            // NonExclusiveLimitAlarmType fields
            [QualifiedName.From(BrowseNames.HighHighState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.HighState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.LowState), QualifiedName.From(BrowseNames.Id)],
            [QualifiedName.From(BrowseNames.LowLowState), QualifiedName.From(BrowseNames.Id)],
            // OffNormalAlarmType field
            [QualifiedName.From(BrowseNames.NormalState)],
            // CertificateExpirationAlarmType fields
            [QualifiedName.From(BrowseNames.ExpirationDate)],
            [QualifiedName.From(BrowseNames.ExpirationLimit)],
            [QualifiedName.From(BrowseNames.CertificateType)],
            [QualifiedName.From(BrowseNames.Certificate)],
            // DiscrepancyAlarmType fields
            [QualifiedName.From(BrowseNames.TargetValueNode)],
            [QualifiedName.From(BrowseNames.ExpectedTime)],
            [QualifiedName.From(BrowseNames.Tolerance)],
        ];

        /// <summary>
        /// Decodes the field array into a condition record. The returned
        /// type is upgraded based on which fields are populated:
        /// <see cref="AlarmRecord"/> if any alarm-only field is present,
        /// <see cref="AcknowledgeableConditionRecord"/> if acked/confirmed
        /// fields are present, <see cref="DialogRecord"/> if a dialog
        /// state field is present, otherwise <see cref="ConditionRecord"/>.
        /// </summary>
        /// <param name="fields">The event field values, matching the
        /// <see cref="StandardFields"/> order.</param>
        /// <returns>Decoded record, or <c>null</c> if fields is empty.</returns>
        public static ConditionRecord? Decode(IReadOnlyList<Variant> fields)
        {
            if (fields == null || fields.Count == 0)
            {
                return null;
            }

            // Field indices in StandardFields
            ByteString eventId = GetValue<ByteString>(fields, 0);
            NodeId? eventType = GetNodeId(fields, 1);
            NodeId? sourceNode = GetNodeId(fields, 2);
            string? sourceName = GetValue<string?>(fields, 3);
            DateTime time = GetValue<DateTime>(fields, 4);
            DateTime receiveTime = GetValue<DateTime>(fields, 5);
            LocalizedText message = GetValue<LocalizedText>(fields, 6);
            ushort severity = GetValue<ushort>(fields, 7);
            string? conditionName = GetValue<string?>(fields, 8);
            NodeId? branchId = GetNodeId(fields, 9);
            bool retain = GetValue<bool>(fields, 10);
            bool? enabledStateId = GetNullable<bool>(fields, 11);
            StatusCode quality = GetValue<StatusCode>(fields, 12);
            LocalizedText comment = GetValue<LocalizedText>(fields, 13);
            string? clientUserId = GetValue<string?>(fields, 14);

            bool? ackedStateId = GetNullable<bool>(fields, 15);
            bool? confirmedStateId = GetNullable<bool>(fields, 16);

            bool? activeStateId = GetNullable<bool>(fields, 17);
            NodeId? inputNode = GetNodeId(fields, 18);
            bool? suppressedStateId = GetNullable<bool>(fields, 19);
            bool? outOfServiceStateId = GetNullable<bool>(fields, 20);
            bool? latchedStateId = GetNullable<bool>(fields, 21);
            bool? silenceStateId = GetNullable<bool>(fields, 22);
            bool? suppressedOrShelved = GetNullable<bool>(fields, 23);

            bool? dialogStateId = GetNullable<bool>(fields, 24);
            LocalizedText prompt = GetValue<LocalizedText>(fields, 25);
            LocalizedText[]? responseOptionSet = fields.Count > 26 && !fields[26].IsNull
                ? fields[26].AsBoxedObject() as LocalizedText[]
                : null;

            // Determine record type
            if (dialogStateId.HasValue)
            {
                return new DialogRecord
                {
                    EventId = eventId,
                    EventType = eventType,
                    SourceNode = sourceNode,
                    SourceName = sourceName,
                    Time = time,
                    ReceiveTime = receiveTime,
                    Message = message,
                    Severity = severity,
                    ConditionId = sourceNode,
                    ConditionName = conditionName,
                    BranchId = branchId,
                    Retain = retain,
                    EnabledStateId = enabledStateId,
                    Quality = quality,
                    Comment = comment,
                    ClientUserId = clientUserId,
                    DialogStateId = dialogStateId,
                    Prompt = prompt,
                    ResponseOptionSet = responseOptionSet,
                };
            }

            if (activeStateId.HasValue || suppressedStateId.HasValue ||
                outOfServiceStateId.HasValue || latchedStateId.HasValue ||
                silenceStateId.HasValue || suppressedOrShelved.HasValue ||
                inputNode != null)
            {
                return BuildAlarmRecord(
                    fields, eventId, eventType, sourceNode, sourceName,
                    time, receiveTime, message, severity, conditionName, branchId,
                    retain, enabledStateId, quality, comment, clientUserId,
                    ackedStateId, confirmedStateId,
                    activeStateId, inputNode, suppressedStateId, outOfServiceStateId,
                    latchedStateId, silenceStateId, suppressedOrShelved);
            }

            if (ackedStateId.HasValue || confirmedStateId.HasValue)
            {
                return new AcknowledgeableConditionRecord
                {
                    EventId = eventId,
                    EventType = eventType,
                    SourceNode = sourceNode,
                    SourceName = sourceName,
                    Time = time,
                    ReceiveTime = receiveTime,
                    Message = message,
                    Severity = severity,
                    ConditionId = sourceNode,
                    ConditionName = conditionName,
                    BranchId = branchId,
                    Retain = retain,
                    EnabledStateId = enabledStateId,
                    Quality = quality,
                    Comment = comment,
                    ClientUserId = clientUserId,
                    AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId,
                };
            }

            return new ConditionRecord
            {
                EventId = eventId,
                EventType = eventType,
                SourceNode = sourceNode,
                SourceName = sourceName,
                Time = time,
                ReceiveTime = receiveTime,
                Message = message,
                Severity = severity,
                ConditionId = sourceNode,
                ConditionName = conditionName,
                BranchId = branchId,
                Retain = retain,
                EnabledStateId = enabledStateId,
                Quality = quality,
                Comment = comment,
                ClientUserId = clientUserId,
            };
        }

        /// <summary>
        /// Builds the right <see cref="AlarmRecord"/> subtype based on
        /// the populated fields (and falls back to <see cref="AlarmRecord"/>
        /// when no subtype-specific fields are present).
        /// </summary>
        private static AlarmRecord BuildAlarmRecord(
            IReadOnlyList<Variant> fields,
            ByteString eventId, NodeId? eventType, NodeId? sourceNode,
            string? sourceName, DateTime time, DateTime receiveTime,
            LocalizedText message, ushort severity, string? conditionName,
            NodeId? branchId, bool retain, bool? enabledStateId,
            StatusCode quality, LocalizedText comment, string? clientUserId,
            bool? ackedStateId, bool? confirmedStateId,
            bool? activeStateId, NodeId? inputNode,
            bool? suppressedStateId, bool? outOfServiceStateId,
            bool? latchedStateId, bool? silenceStateId,
            bool? suppressedOrShelved)
        {
            // LimitAlarm fields (27-30)
            double? hhLimit = GetNullable<double>(fields, 27);
            double? hLimit = GetNullable<double>(fields, 28);
            double? lLimit = GetNullable<double>(fields, 29);
            double? llLimit = GetNullable<double>(fields, 30);
            // ExclusiveLimitAlarm fields (31-32)
            LocalizedText currentLimitState = GetValue<LocalizedText>(fields, 31);
            NodeId? currentLimitStateId = GetNodeId(fields, 32);
            // NonExclusiveLimitAlarm fields (33-36)
            bool? hhStateId = GetNullable<bool>(fields, 33);
            bool? hStateId = GetNullable<bool>(fields, 34);
            bool? lStateId = GetNullable<bool>(fields, 35);
            bool? llStateId = GetNullable<bool>(fields, 36);
            // OffNormalAlarm field (37)
            NodeId? normalState = GetNodeId(fields, 37);
            // CertificateExpirationAlarm fields (38-41)
            DateTime? expirationDate = GetNullable<DateTime>(fields, 38);
            double? expirationLimitMs = GetNullable<double>(fields, 39);
            NodeId? certificateType = GetNodeId(fields, 40);
            ByteString certificate = GetValue<ByteString>(fields, 41);
            // DiscrepancyAlarm fields (42-44)
            NodeId? targetValueNode = GetNodeId(fields, 42);
            double? expectedTime = GetNullable<double>(fields, 43);
            double? tolerance = GetNullable<double>(fields, 44);

            // Choose the most specific record type based on which subtype-
            // specific fields are populated. ExpirationDate/Certificate
            // imply CertificateExpiration; NormalState alone implies
            // OffNormal; CurrentLimitState* implies Exclusive; HighHighState
            // etc. imply NonExclusive; HighHighLimit etc. imply Limit;
            // TargetValueNode implies Discrepancy.
            if (expirationDate.HasValue || !certificate.IsNull || certificateType != null)
            {
                return new CertificateExpirationAlarmRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity, ConditionId = sourceNode,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    NormalState = normalState,
                    ExpirationDate = expirationDate,
                    ExpirationLimit = expirationLimitMs.HasValue
                        ? TimeSpan.FromMilliseconds(expirationLimitMs.Value)
                        : null,
                    CertificateType = certificateType,
                    Certificate = certificate,
                };
            }

            if (targetValueNode != null || expectedTime.HasValue || tolerance.HasValue)
            {
                return new DiscrepancyAlarmRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity, ConditionId = sourceNode,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    TargetValueNode = targetValueNode,
                    ExpectedTime = expectedTime,
                    Tolerance = tolerance,
                };
            }

            if (normalState != null)
            {
                return new OffNormalAlarmRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity, ConditionId = sourceNode,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    NormalState = normalState,
                };
            }

            if (currentLimitStateId != null || !currentLimitState.IsNullOrEmpty)
            {
                return new ExclusiveLimitAlarmRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity, ConditionId = sourceNode,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    HighHighLimit = hhLimit, HighLimit = hLimit,
                    LowLimit = lLimit, LowLowLimit = llLimit,
                    CurrentLimitState = currentLimitState,
                    CurrentLimitStateId = currentLimitStateId,
                };
            }

            if (hhStateId.HasValue || hStateId.HasValue ||
                lStateId.HasValue || llStateId.HasValue)
            {
                return new NonExclusiveLimitAlarmRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity, ConditionId = sourceNode,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    HighHighLimit = hhLimit, HighLimit = hLimit,
                    LowLimit = lLimit, LowLowLimit = llLimit,
                    HighHighStateId = hhStateId, HighStateId = hStateId,
                    LowStateId = lStateId, LowLowStateId = llStateId,
                };
            }

            if (hhLimit.HasValue || hLimit.HasValue ||
                lLimit.HasValue || llLimit.HasValue)
            {
                return new LimitAlarmRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity, ConditionId = sourceNode,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    HighHighLimit = hhLimit, HighLimit = hLimit,
                    LowLimit = lLimit, LowLowLimit = llLimit,
                };
            }

            return new AlarmRecord
            {
                EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                Message = message, Severity = severity, ConditionId = sourceNode,
                ConditionName = conditionName, BranchId = branchId, Retain = retain,
                EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                ClientUserId = clientUserId, AckedStateId = ackedStateId,
                ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                InputNode = inputNode, SuppressedStateId = suppressedStateId,
                OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
            };
        }

        private static T GetValue<T>(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return default!;
            }

            object? value = fields[index].AsBoxedObject();
            if (value is T t)
            {
                return t;
            }

            return default!;
        }

        private static T? GetNullable<T>(IReadOnlyList<Variant> fields, int index)
            where T : struct
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }

            object? value = fields[index].AsBoxedObject();
            if (value is T t)
            {
                return t;
            }

            return null;
        }

        private static NodeId? GetNodeId(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }

            object? value = fields[index].AsBoxedObject();
            if (value is NodeId nodeId)
            {
                return nodeId;
            }
            return null;
        }
    }
}
