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
using Opc.Ua;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Builds a small generated-record filter and projects its wire fields. Control
/// events remain in the same ordered stream as conditions. No SourceNode alias is
/// used as a condition ObjectId, including for custom condition subtypes.
/// </summary>
internal sealed class AlarmEventProjection
{
    public AlarmEventProjection()
    {
        m_registry = new EventRecordDecoderRegistry()
            .Register(ObjectTypeIds.ConditionType,
                ConditionTypeRecord.Decoder.StandardFields, ConditionTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.AcknowledgeableConditionType,
                AcknowledgeableConditionTypeRecord.Decoder.StandardFields,
                AcknowledgeableConditionTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.AlarmConditionType,
                AlarmConditionTypeRecord.Decoder.StandardFields, AlarmConditionTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.DialogConditionType,
                DialogConditionTypeRecord.Decoder.StandardFields, DialogConditionTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.RefreshStartEventType,
                RefreshStartEventTypeRecord.Decoder.StandardFields, RefreshStartEventTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.RefreshEndEventType,
                RefreshEndEventTypeRecord.Decoder.StandardFields, RefreshEndEventTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.RefreshRequiredEventType,
                RefreshRequiredEventTypeRecord.Decoder.StandardFields, RefreshRequiredEventTypeRecord.Decoder.Decode)
            .Register(ObjectTypeIds.EventQueueOverflowEventType,
                EventQueueOverflowEventTypeRecord.Decoder.StandardFields,
                EventQueueOverflowEventTypeRecord.Decoder.Decode);

        Filter = AlarmConditionTypeRecord.EventFilters.Build(m_registry);
        foreach (SimpleAttributeOperand clause in Filter.SelectClauses)
        {
            clause.TypeDefinitionId = DeclaringType(clause.BrowsePath);
        }
        ConditionIdIndex = Filter.SelectClauses.Count;
        Filter.SelectClauses = Filter.SelectClauses.AddItem(new SimpleAttributeOperand
        {
            TypeDefinitionId = ObjectTypeIds.ConditionType,
            AttributeId = Attributes.NodeId,
            BrowsePath = []
        });
        Filter.WhereClause = new ContentFilter
        {
            Elements =
            [
                Or(1, 2),
                OfType(ObjectTypeIds.ConditionType),
                Or(3, 4),
                OfType(ObjectTypeIds.RefreshStartEventType),
                Or(5, 6),
                OfType(ObjectTypeIds.RefreshEndEventType),
                Or(7, 8),
                OfType(ObjectTypeIds.RefreshRequiredEventType),
                OfType(ObjectTypeIds.EventQueueOverflowEventType)
            ]
        };
        m_eventTypeIndex = FindField(BrowseNames.EventType);
        m_branchIdIndex = FindField(BrowseNames.BranchId);
        m_retainIndex = FindField(BrowseNames.Retain);
        m_qualityIndex = FindField(BrowseNames.Quality);
        m_severityIndex = FindField(BrowseNames.Severity);
        m_responsesIndex = FindField(BrowseNames.ResponseOptionSet);
        m_dialogIndex = FindField(BrowseNames.DialogState, BrowseNames.Id);
        m_activeIndex = FindField(BrowseNames.ActiveState, BrowseNames.Id);
        m_ackedIndex = FindField(BrowseNames.AckedState, BrowseNames.Id);
    }

    public EventFilter Filter { get; }

    public int ConditionIdIndex { get; }

    public AlarmUpdate Decode(ArrayOf<Variant> fields, uint partitionId)
    {
        if (fields.Count != Filter.SelectClauses.Count ||
            !fields[m_eventTypeIndex].TryGetValue(out NodeId eventType) || eventType.IsNull)
        {
            return Invalid(partitionId, "An event did not match the selected alarm field layout.");
        }
        if (eventType == ObjectTypeIds.RefreshStartEventType)
        {
            return new AlarmUpdate(AlarmUpdateKind.RefreshStart, partitionId);
        }
        if (eventType == ObjectTypeIds.RefreshEndEventType)
        {
            return new AlarmUpdate(AlarmUpdateKind.RefreshEnd, partitionId);
        }
        if (eventType == ObjectTypeIds.RefreshRequiredEventType)
        {
            return new AlarmUpdate(AlarmUpdateKind.RefreshRequired, partitionId);
        }
        if (eventType == ObjectTypeIds.EventQueueOverflowEventType)
        {
            return Invalid(partitionId, "The server reported EventQueueOverflow; condition state may be missing.");
        }
        if (!fields[ConditionIdIndex].TryGetValue(out NodeId conditionId) || conditionId.IsNull ||
            !fields[m_branchIdIndex].TryGetValue(out NodeId branchId) ||
            !fields[m_retainIndex].TryGetValue(out bool retain))
        {
            return Invalid(partitionId, "ConditionId, BranchId or Retain was unavailable; no ObjectId was guessed.");
        }

        Variant[] values = fields.ToArray() ?? [];
        if (values[m_responsesIndex].TryGetValue(out ArrayOf<LocalizedText> responseOptions) &&
            responseOptions.Count > AlarmLimits.ResponseCount)
        {
            values[m_responsesIndex] = Variant.Null;
        }
        EventRecord? decoded = m_registry.Decode(values);
        if (decoded is not ConditionTypeRecord)
        {
            // OfType(ConditionType) already establishes the family. Custom type
            // metadata need not be browsed (or cached) to decode standard fields.
            NodeId family = fields[m_dialogIndex].TryGetValue(out bool _)
                ? ObjectTypeIds.DialogConditionType
                : fields[m_activeIndex].TryGetValue(out bool _)
                    ? ObjectTypeIds.AlarmConditionType
                    : fields[m_ackedIndex].TryGetValue(out bool _)
                        ? ObjectTypeIds.AcknowledgeableConditionType
                        : ObjectTypeIds.ConditionType;
            decoded = m_registry.DecodeAs(family, values);
        }
        if (decoded is not ConditionTypeRecord condition ||
            condition.EventId.IsNull || condition.EventId.Length is 0 or > AlarmLimits.EventIdLength ||
            !IsBounded(conditionId) || !IsBounded(branchId) ||
            !IsBounded(condition.SourceNode) || !IsBounded(eventType))
        {
            return Invalid(partitionId, "An alarm's identifiers were missing or exceeded document limits.");
        }

        var alarm = condition as AlarmConditionTypeRecord;
        var acknowledgeable = condition as AcknowledgeableConditionTypeRecord;
        var dialog = condition as DialogConditionTypeRecord;
        var responses = new List<string>();
        if (dialog?.ResponseOptionSet is { Length: <= AlarmLimits.ResponseCount } options)
        {
            foreach (LocalizedText option in options)
            {
                responses.Add(AlarmLimits.Text(option.Text));
            }
        }
        StatusCode quality = fields[m_qualityIndex].TryGetValue(out StatusCode status)
            ? status
            : StatusCodes.BadNoData;
        var projected = new AlarmCondition(
            new AlarmKey(conditionId, branchId),
            new ByteString(condition.EventId.ToArray()),
            eventType,
            condition.SourceNode,
            AlarmLimits.Text(condition.SourceName),
            AlarmLimits.Text(condition.ConditionName),
            AlarmLimits.Text(condition.Message.Text),
            fields[m_severityIndex].TryGetValue(out ushort severity) ? severity : null,
            condition.Time,
            condition.ReceiveTime,
            condition.EnabledStateId,
            alarm?.ActiveStateId,
            acknowledgeable?.AckedStateId,
            acknowledgeable?.ConfirmedStateId,
            retain,
            quality,
            dialog is not null ? AlarmConditionKind.Dialog :
                alarm is not null ? AlarmConditionKind.Alarm :
                    acknowledgeable is not null ? AlarmConditionKind.Acknowledgeable : AlarmConditionKind.Condition)
        {
            Suppressed = alarm?.SuppressedStateId,
            Latched = alarm?.LatchedStateId,
            MaxTimeShelved = alarm?.MaxTimeShelved,
            DialogActive = dialog?.DialogStateId,
            Prompt = AlarmLimits.Text(dialog?.Prompt.Text),
            Responses = [.. responses]
        };
        return new AlarmUpdate(AlarmUpdateKind.Condition, partitionId, projected);
    }

    private int FindField(string name, string? child = null)
    {
        for (int i = 0; i < m_registry.StandardFields.Length; i++)
        {
            QualifiedName[] path = m_registry.StandardFields[i];
            if (path.Length == (child is null ? 1 : 2) && path[0].Name == name &&
                (child is null || path[1].Name == child))
            {
                return i;
            }
        }
        throw new InvalidOperationException($"The generated alarm decoder does not select {name}.");
    }

    private static bool IsBounded(NodeId id)
    {
        if (id.TryGetValue(out string text) && text.Length > AlarmLimits.IdentifierLength)
        {
            return false;
        }
        if (id.TryGetValue(out ByteString opaque) && opaque.Length > AlarmLimits.IdentifierLength)
        {
            return false;
        }
        return id.ToString().Length <= AlarmLimits.IdentifierLength;
    }

    private static AlarmUpdate Invalid(uint partitionId, string detail)
    {
        return new AlarmUpdate(AlarmUpdateKind.Loss, partitionId, Detail: detail);
    }

    private static NodeId DeclaringType(ArrayOf<QualifiedName> path)
    {
        if (Contains(BaseEventTypeRecord.Decoder.StandardFields, path))
        {
            return ObjectTypeIds.BaseEventType;
        }
        if (Contains(ConditionTypeRecord.Decoder.StandardFields, path))
        {
            return ObjectTypeIds.ConditionType;
        }
        if (Contains(AcknowledgeableConditionTypeRecord.Decoder.StandardFields, path))
        {
            return ObjectTypeIds.AcknowledgeableConditionType;
        }
        return Contains(AlarmConditionTypeRecord.Decoder.StandardFields, path)
            ? ObjectTypeIds.AlarmConditionType
            : ObjectTypeIds.DialogConditionType;
    }

    private static bool Contains(QualifiedName[][] fields, ArrayOf<QualifiedName> path)
    {
        foreach (QualifiedName[] field in fields)
        {
            if (field.AsSpan().SequenceEqual(path.Span))
            {
                return true;
            }
        }
        return false;
    }

    private static ContentFilterElement Or(uint left, uint right)
    {
        return new ContentFilterElement
        {
            FilterOperator = FilterOperator.Or,
            FilterOperands =
            [
                new ExtensionObject(new ElementOperand { Index = left }),
                new ExtensionObject(new ElementOperand { Index = right })
            ]
        };
    }

    private static ContentFilterElement OfType(NodeId typeId)
    {
        return new ContentFilterElement
        {
            FilterOperator = FilterOperator.OfType,
            FilterOperands = [new ExtensionObject(new LiteralOperand(Variant.From(typeId)))]
        };
    }

    private readonly EventRecordDecoderRegistry m_registry;
    private readonly int m_eventTypeIndex;
    private readonly int m_branchIdIndex;
    private readonly int m_retainIndex;
    private readonly int m_qualityIndex;
    private readonly int m_severityIndex;
    private readonly int m_responsesIndex;
    private readonly int m_dialogIndex;
    private readonly int m_activeIndex;
    private readonly int m_ackedIndex;
}
