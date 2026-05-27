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
    /// Decodes raw <see cref="Subscriptions.EventNotification"/> field
    /// arrays into the source-generated <c>*TypeRecord</c> records
    /// emitted by the <c>EventRecordGenerator</c>.
    /// </summary>
    /// <remarks>
    /// The decoder is paired with <see cref="AlarmEventFilterBuilder"/>
    /// — the order of fields in <see cref="Subscriptions.EventNotification.Fields"/>
    /// matches the order of select clauses produced by the builder
    /// (defined by <see cref="StandardFields"/>).
    /// <para>
    /// The returned records (<see cref="ConditionTypeRecord"/>,
    /// <see cref="AcknowledgeableConditionTypeRecord"/>,
    /// <see cref="AlarmConditionTypeRecord"/>,
    /// <see cref="DialogConditionTypeRecord"/>, …) are generated from
    /// the standard OPC UA NodeSet by the
    /// <c>Opc.Ua.SourceGeneration</c> source generator. Vendor models
    /// generate their own subtypes (e.g. a vendor-defined
    /// <c>VibrationAlarmType</c> emits <c>VibrationAlarmTypeRecord</c>)
    /// automatically.
    /// </para>
    /// <para>
    /// This decoder uses a heuristic dispatch — it inspects which
    /// fields are populated to pick the most specific record subtype.
    /// For Part 9 alarm conditions this is reliable because each
    /// subtype introduces unique fields (Dialog: Prompt;
    /// CertificateExpiration: Certificate; etc.). For
    /// <c>EventType</c>-keyed dispatch backed by the source-generated
    /// per-type decoders (<c>{Type}TypeRecord.Decoder.Decode</c>),
    /// use <see cref="EventRecordDecoderRegistry.Default"/> instead.
    /// That path scales to vendor types without hand-rolled changes
    /// here.
    /// </para>
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
        /// Decodes the field array into a generated event record. The
        /// returned runtime type is the most-specific generated
        /// <c>*TypeRecord</c> whose fields are populated:
        /// <see cref="CertificateExpirationAlarmTypeRecord"/>,
        /// <see cref="DiscrepancyAlarmTypeRecord"/>,
        /// <see cref="OffNormalAlarmTypeRecord"/>,
        /// <see cref="ExclusiveLimitAlarmTypeRecord"/>,
        /// <see cref="NonExclusiveLimitAlarmTypeRecord"/>,
        /// <see cref="LimitAlarmTypeRecord"/>,
        /// <see cref="AlarmConditionTypeRecord"/>,
        /// <see cref="AcknowledgeableConditionTypeRecord"/>,
        /// <see cref="DialogConditionTypeRecord"/>, or
        /// <see cref="ConditionTypeRecord"/>.
        /// </summary>
        /// <param name="fields">The event field values, matching the
        /// <see cref="StandardFields"/> order.</param>
        /// <returns>Decoded record, or <c>null</c> if fields is empty.</returns>
        public static ConditionTypeRecord? Decode(IReadOnlyList<Variant> fields)
        {
            if (fields == null || fields.Count == 0)
            {
                return null;
            }

            // Field indices in StandardFields
            ByteString eventId = GetByteString(fields, 0);
            NodeId eventType = GetNodeId(fields, 1);
            NodeId sourceNode = GetNodeId(fields, 2);
            string? sourceName = GetString(fields, 3);
            DateTime time = GetDateTime(fields, 4);
            DateTime receiveTime = GetDateTime(fields, 5);
            LocalizedText message = GetLocalizedText(fields, 6);
            ushort severity = GetUInt16(fields, 7);
            string? conditionName = GetString(fields, 8);
            NodeId branchId = GetNodeId(fields, 9);
            bool retain = GetBool(fields, 10);
            bool? enabledStateId = GetNullableBool(fields, 11);
            StatusCode quality = GetStatusCode(fields, 12);
            LocalizedText comment = GetLocalizedText(fields, 13);
            string? clientUserId = GetString(fields, 14);

            bool? ackedStateId = GetNullableBool(fields, 15);
            bool? confirmedStateId = GetNullableBool(fields, 16);

            bool? activeStateId = GetNullableBool(fields, 17);
            NodeId inputNode = GetNodeId(fields, 18);
            bool? suppressedStateId = GetNullableBool(fields, 19);
            bool? outOfServiceStateId = GetNullableBool(fields, 20);
            bool? latchedStateId = GetNullableBool(fields, 21);
            bool? silenceStateId = GetNullableBool(fields, 22);
            bool? suppressedOrShelved = GetNullableBool(fields, 23);

            bool? dialogStateId = GetNullableBool(fields, 24);
            LocalizedText prompt = GetLocalizedText(fields, 25);
            LocalizedText[]? responseOptionSet = GetLocalizedTextArray(fields, 26);

            // Determine record type
            if (dialogStateId.HasValue)
            {
                return new DialogConditionTypeRecord
                {
                    EventId = eventId,
                    EventType = eventType,
                    SourceNode = sourceNode,
                    SourceName = sourceName,
                    Time = time,
                    ReceiveTime = receiveTime,
                    Message = message,
                    Severity = severity,
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
                !inputNode.IsNull)
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
                return new AcknowledgeableConditionTypeRecord
                {
                    EventId = eventId,
                    EventType = eventType,
                    SourceNode = sourceNode,
                    SourceName = sourceName,
                    Time = time,
                    ReceiveTime = receiveTime,
                    Message = message,
                    Severity = severity,
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

            return new ConditionTypeRecord
            {
                EventId = eventId,
                EventType = eventType,
                SourceNode = sourceNode,
                SourceName = sourceName,
                Time = time,
                ReceiveTime = receiveTime,
                Message = message,
                Severity = severity,
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
        /// Builds the right <see cref="AlarmConditionTypeRecord"/>
        /// subtype based on the populated fields (and falls back to
        /// <see cref="AlarmConditionTypeRecord"/> when no subtype-
        /// specific fields are present).
        /// </summary>
        private static AlarmConditionTypeRecord BuildAlarmRecord(
            IReadOnlyList<Variant> fields,
            ByteString eventId, NodeId eventType, NodeId sourceNode,
            string? sourceName, DateTime time, DateTime receiveTime,
            LocalizedText message, ushort severity, string? conditionName,
            NodeId branchId, bool retain, bool? enabledStateId,
            StatusCode quality, LocalizedText comment, string? clientUserId,
            bool? ackedStateId, bool? confirmedStateId,
            bool? activeStateId, NodeId inputNode,
            bool? suppressedStateId, bool? outOfServiceStateId,
            bool? latchedStateId, bool? silenceStateId,
            bool? suppressedOrShelved)
        {
            // LimitAlarm fields (27-30)
            double? hhLimit = GetNullableDouble(fields, 27);
            double? hLimit = GetNullableDouble(fields, 28);
            double? lLimit = GetNullableDouble(fields, 29);
            double? llLimit = GetNullableDouble(fields, 30);
            // ExclusiveLimitAlarm fields (31-32) — index 31 is the
            // current limit state localized text, index 32 is the
            // limit-state Id NodeId. Generated record only exposes the
            // Id (matches the StateMachine model).
            NodeId currentLimitStateId = GetNodeId(fields, 32);
            // NonExclusiveLimitAlarm fields (33-36)
            bool? hhStateId = GetNullableBool(fields, 33);
            bool? hStateId = GetNullableBool(fields, 34);
            bool? lStateId = GetNullableBool(fields, 35);
            bool? llStateId = GetNullableBool(fields, 36);
            // OffNormalAlarm field (37)
            NodeId normalState = GetNodeId(fields, 37);
            // CertificateExpirationAlarm fields (38-41)
            DateTime? expirationDate = GetNullableDateTime(fields, 38);
            double? expirationLimitMs = GetNullableDouble(fields, 39);
            NodeId certificateType = GetNodeId(fields, 40);
            ByteString certificate = GetByteString(fields, 41);
            // DiscrepancyAlarm fields (42-44)
            NodeId targetValueNode = GetNodeId(fields, 42);
            double? expectedTime = GetNullableDouble(fields, 43);
            double? tolerance = GetNullableDouble(fields, 44);

            // Choose the most specific record type based on which subtype-
            // specific fields are populated.
            if (expirationDate.HasValue || !certificate.IsNull || !certificateType.IsNull)
            {
                return new CertificateExpirationAlarmTypeRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity,
                    ConditionName = conditionName, BranchId = branchId, Retain = retain,
                    EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                    ClientUserId = clientUserId, AckedStateId = ackedStateId,
                    ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                    InputNode = inputNode, SuppressedStateId = suppressedStateId,
                    OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                    SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
                    NormalState = normalState,
                    ExpirationDate = expirationDate,
                    ExpirationLimit = expirationLimitMs,
                    CertificateType = certificateType,
                    Certificate = certificate,
                };
            }

            if (!targetValueNode.IsNull || expectedTime.HasValue || tolerance.HasValue)
            {
                return new DiscrepancyAlarmTypeRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity,
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

            if (!normalState.IsNull)
            {
                return new OffNormalAlarmTypeRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity,
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

            if (!currentLimitStateId.IsNull)
            {
                return new ExclusiveLimitAlarmTypeRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity,
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

            if (hhStateId.HasValue || hStateId.HasValue ||
                lStateId.HasValue || llStateId.HasValue)
            {
                return new NonExclusiveLimitAlarmTypeRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity,
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

            if (hhLimit.HasValue || hLimit.HasValue ||
                lLimit.HasValue || llLimit.HasValue)
            {
                return new LimitAlarmTypeRecord
                {
                    EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                    SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                    Message = message, Severity = severity,
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

            return new AlarmConditionTypeRecord
            {
                EventId = eventId, EventType = eventType, SourceNode = sourceNode,
                SourceName = sourceName, Time = time, ReceiveTime = receiveTime,
                Message = message, Severity = severity,
                ConditionName = conditionName, BranchId = branchId, Retain = retain,
                EnabledStateId = enabledStateId, Quality = quality, Comment = comment,
                ClientUserId = clientUserId, AckedStateId = ackedStateId,
                ConfirmedStateId = confirmedStateId, ActiveStateId = activeStateId,
                InputNode = inputNode, SuppressedStateId = suppressedStateId,
                OutOfServiceStateId = outOfServiceStateId, LatchedStateId = latchedStateId,
                SilenceStateId = silenceStateId, SuppressedOrShelved = suppressedOrShelved,
            };
        }

        private static ByteString GetByteString(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out ByteString v)
                ? v : default;

        private static string? GetString(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out string v)
                ? v : null;

        private static DateTime GetDateTime(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out DateTimeUtc v)
                ? (DateTime)v : default;

        private static LocalizedText GetLocalizedText(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out LocalizedText v)
                ? v : LocalizedText.Null;

        private static ushort GetUInt16(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out ushort v)
                ? v : (ushort)0;

        private static bool GetBool(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out bool v) && v;

        private static StatusCode GetStatusCode(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out StatusCode v)
                ? v : default;

        private static bool? GetNullableBool(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out bool v) ? v : null;
        }

        private static double? GetNullableDouble(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out double v) ? v : null;
        }

        private static DateTime? GetNullableDateTime(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out DateTimeUtc v) ? (DateTime)v : null;
        }

        private static LocalizedText[]? GetLocalizedTextArray(IReadOnlyList<Variant> fields, int index)
        {
            if (index >= fields.Count || fields[index].IsNull)
            {
                return null;
            }
            return fields[index].TryGetValue(out ArrayOf<LocalizedText> arr)
                ? arr.ToArray() : null;
        }

        private static NodeId GetNodeId(IReadOnlyList<Variant> fields, int index)
            => index < fields.Count && fields[index].TryGetValue(out NodeId v)
                ? v : NodeId.Null;
    }
}
