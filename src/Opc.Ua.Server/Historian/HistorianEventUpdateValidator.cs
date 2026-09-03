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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Validates and decodes Part 11 historical event updates.
    /// </summary>
    internal static class HistorianEventUpdateValidator
    {
        public static ServiceResult Validate(
            ServerSystemContext systemContext,
            NodeState node,
            UpdateEventDetails details,
            HistorianNodeCapabilities capabilities,
            out HistorianEventUpdatePlan plan)
        {
            plan = default;
            if (node is not BaseObjectState notifier ||
                (notifier.EventNotifier & EventNotifiers.HistoryWrite) == 0)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            if (details.Filter == null || details.Filter.SelectClauses.Count == 0)
            {
                return StatusCodes.BadEventFilterInvalid;
            }
            if (details.Filter.WhereClause.Elements.Count != 0)
            {
                return StatusCodes.BadEventFilterInvalid;
            }
            if (details.PerformInsertReplace is not PerformUpdateType.Insert and
                not PerformUpdateType.Replace and
                not PerformUpdateType.Update)
            {
                return StatusCodes.BadInvalidArgument;
            }

            IServerInternal server = systemContext.Server;
            var filterContext = new FilterContext(
                server.NamespaceUris,
                server.TypeTree,
                systemContext.OperationContext,
                server.Telemetry);
            EventFilter.Result validation = details.Filter.Validate(filterContext);
            if (ServiceResult.IsBad(validation.Status))
            {
                return StatusCodes.BadEventFilterInvalid;
            }

            int eventIdIndex = FindClause(
                server.TypeTree,
                details.Filter,
                BrowseNames.EventId);
            int eventTypeIndex = FindClause(
                server.TypeTree,
                details.Filter,
                BrowseNames.EventType);
            int timeIndex = FindClause(
                server.TypeTree,
                details.Filter,
                BrowseNames.Time);
            int sourceNodeIndex = FindClause(
                server.TypeTree,
                details.Filter,
                BrowseNames.SourceNode);
            if (eventIdIndex == kAmbiguousClause ||
                eventTypeIndex == kAmbiguousClause ||
                timeIndex == kAmbiguousClause ||
                sourceNodeIndex == kAmbiguousClause)
            {
                return StatusCodes.BadEventFilterInvalid;
            }
            bool insertRules = details.PerformInsertReplace is
                PerformUpdateType.Insert or PerformUpdateType.Update;

            if (details.PerformInsertReplace == PerformUpdateType.Replace &&
                eventIdIndex < 0)
            {
                return StatusCodes.BadArgumentsMissing;
            }
            if (details.PerformInsertReplace == PerformUpdateType.Replace &&
                !string.IsNullOrEmpty(
                    details.Filter.SelectClauses[eventIdIndex].IndexRange))
            {
                return StatusCodes.BadIndexRangeInvalid;
            }
            if (insertRules && (eventTypeIndex < 0 || timeIndex < 0))
            {
                return StatusCodes.BadArgumentsMissing;
            }
            if (insertRules)
            {
                for (int i = 0; i < capabilities.MandatoryEventFields.Count; i++)
                {
                    SimpleAttributeOperand mandatory =
                        capabilities.MandatoryEventFields[i];
                    if (mandatory == null ||
                        !ContainsClause(details.Filter, mandatory))
                    {
                        return StatusCodes.BadArgumentsMissing;
                    }
                }
            }
            if (insertRules)
            {
                for (int i = 0; i < details.Filter.SelectClauses.Count; i++)
                {
                    if (!string.IsNullOrEmpty(details.Filter.SelectClauses[i].IndexRange))
                    {
                        return StatusCodes.BadIndexRangeInvalid;
                    }
                }
            }

            plan = new HistorianEventUpdatePlan(
                eventIdIndex,
                eventTypeIndex,
                timeIndex,
                sourceNodeIndex,
                node.NodeId,
                details.PerformInsertReplace);
            return ServiceResult.Good;
        }

        public static async ValueTask<HistorianEventDecodeResult> DecodeAsync(
            ServerSystemContext systemContext,
            HistorianNodeCapabilities capabilities,
            HistoryEventFieldList incoming,
            EventFilter filter,
            HistorianEventUpdatePlan plan,
            CancellationToken cancellationToken)
        {
            if (incoming == null ||
                incoming.EventFields.Count < filter.SelectClauses.Count)
            {
                return new HistorianEventDecodeResult(
                    StatusCodes.BadArgumentsMissing,
                    null);
            }
            if (incoming.EventFields.Count > filter.SelectClauses.Count)
            {
                return new HistorianEventDecodeResult(
                    StatusCodes.BadInvalidArgument,
                    null);
            }
            ByteString eventId = ByteString.Empty;
            if (plan.EventIdIndex >= 0)
            {
                Variant value = incoming.EventFields[plan.EventIdIndex];
                if (!value.IsNull && !value.TryGetValue(out eventId))
                {
                    return InvalidField(
                        StatusCodes.BadInvalidArgument,
                        plan.EventIdIndex,
                        BrowseNames.EventId);
                }
            }
            if (plan.UpdateType == PerformUpdateType.Replace && eventId.IsEmpty)
            {
                return InvalidField(
                    StatusCodes.BadInvalidArgument,
                    plan.EventIdIndex,
                    BrowseNames.EventId);
            }
            if (eventId.IsEmpty)
            {
                eventId = ByteString.From(Guid.NewGuid().ToByteArray());
            }

            NodeId eventType = NodeId.Null;
            if (plan.EventTypeIndex >= 0)
            {
                Variant value = incoming.EventFields[plan.EventTypeIndex];
                if (value.IsNull)
                {
                    if (capabilities.EventTypes.Count == 1)
                    {
                        eventType = capabilities.EventTypes[0];
                    }
                    else
                    {
                        return InvalidField(
                            StatusCodes.BadInvalidArgument,
                            plan.EventTypeIndex,
                            BrowseNames.EventType);
                    }
                }
                else if (!value.TryGetValue(out eventType) || eventType.IsNull)
                {
                    return InvalidField(
                        StatusCodes.BadInvalidArgument,
                        plan.EventTypeIndex,
                        BrowseNames.EventType);
                }
                if (!systemContext.Server.TypeTree.IsTypeOf(
                    eventType,
                    ObjectTypeIds.BaseEventType))
                {
                    return InvalidField(
                        StatusCodes.BadTypeDefinitionInvalid,
                        plan.EventTypeIndex,
                        BrowseNames.EventType);
                }
                if (!capabilities.EventTypes.IsEmpty)
                {
                    NodeId storedType = NodeId.Null;
                    for (int i = 0; i < capabilities.EventTypes.Count; i++)
                    {
                        NodeId supportedType = capabilities.EventTypes[i];
                        if (eventType == supportedType)
                        {
                            storedType = supportedType;
                            break;
                        }
                        if (storedType.IsNull &&
                            systemContext.Server.TypeTree.IsTypeOf(
                                eventType,
                                supportedType))
                        {
                            storedType = supportedType;
                        }
                    }
                    if (storedType.IsNull)
                    {
                        return InvalidField(
                            StatusCodes.BadTypeDefinitionInvalid,
                            plan.EventTypeIndex,
                            BrowseNames.EventType);
                    }
                }
            }

            DateTimeUtc sourceTimestamp = DateTimeUtc.MinValue;
            if (plan.TimeIndex >= 0)
            {
                Variant value = incoming.EventFields[plan.TimeIndex];
                if (value.IsNull)
                {
                    TimeProvider timeProvider =
                        (systemContext.Server as ITimeProviderProvider)?
                            .TimeProvider ??
                        TimeProvider.System;
                    sourceTimestamp = timeProvider.GetUtcNow().UtcDateTime;
                }
                else if (!value.TryGetValue(out sourceTimestamp) ||
                    sourceTimestamp == DateTimeUtc.MinValue)
                {
                    return InvalidField(
                        StatusCodes.BadInvalidArgument,
                        plan.TimeIndex,
                        BrowseNames.Time);
                }
            }
            NodeId sourceNode = plan.NodeId;
            if (plan.SourceNodeIndex >= 0)
            {
                Variant value = incoming.EventFields[plan.SourceNodeIndex];
                if (!value.IsNull)
                {
                    if (!value.TryGetValue(out sourceNode) || sourceNode.IsNull)
                    {
                        return InvalidField(
                            StatusCodes.BadSourceNodeIdInvalid,
                            plan.SourceNodeIndex,
                            BrowseNames.SourceNode);
                    }
                    (object? handle, IAsyncNodeManager? _) = await systemContext
                        .Server.NodeManager.GetManagerHandleAsync(
                            sourceNode,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (handle == null)
                    {
                        return InvalidField(
                            StatusCodes.BadSourceNodeIdInvalid,
                            plan.SourceNodeIndex,
                            BrowseNames.SourceNode);
                    }
                }
            }

            var fields = new Dictionary<string, Variant>(StringComparer.Ordinal);
            var qualifiedFields =
                new Dictionary<HistorianEventFieldKey, Variant>();
            var ignoredIndexes = new List<int>();
            var ignoredNames = new List<string>();
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand operand = filter.SelectClauses[i];
                if (!IsSupportedField(
                    systemContext.Server.TypeTree,
                    eventType,
                    operand,
                    capabilities))
                {
                    ignoredIndexes.Add(i);
                    ignoredNames.Add(
                        HistorianEventFieldKey.BuildPath(
                            operand.BrowsePath));
                    continue;
                }
                Variant value = incoming.EventFields[i].Copy();
                if (value.IsNull &&
                    ContainsOperand(
                        capabilities.MandatoryEventFields,
                        operand))
                {
                    return InvalidField(
                        StatusCodes.BadInvalidArgument,
                        i,
                        HistorianEventFieldKey.BuildPath(
                            operand.BrowsePath));
                }
                fields[HistorianEventFieldKey.BuildPath(operand.BrowsePath)] = value;
                qualifiedFields[HistorianEventFieldKey.FromOperand(operand)] = value;
            }
            fields[BrowseNames.EventId] = new Variant(eventId);
            qualifiedFields[CreateBaseEventFieldKey(BrowseNames.EventId)] =
                new Variant(eventId);
            if (plan.EventIdIndex >= 0)
            {
                qualifiedFields[
                    HistorianEventFieldKey.FromOperand(
                        filter.SelectClauses[plan.EventIdIndex])] =
                    new Variant(eventId);
            }
            if (plan.EventTypeIndex >= 0)
            {
                fields[BrowseNames.EventType] = new Variant(eventType);
                qualifiedFields[CreateBaseEventFieldKey(BrowseNames.EventType)] =
                    new Variant(eventType);
                qualifiedFields[
                    HistorianEventFieldKey.FromOperand(
                        filter.SelectClauses[plan.EventTypeIndex])] =
                    new Variant(eventType);
            }
            if (plan.SourceNodeIndex >= 0)
            {
                fields[BrowseNames.SourceNode] = new Variant(sourceNode);
                qualifiedFields[CreateBaseEventFieldKey(BrowseNames.SourceNode)] =
                    new Variant(sourceNode);
                qualifiedFields[
                    HistorianEventFieldKey.FromOperand(
                        filter.SelectClauses[plan.SourceNodeIndex])] =
                    new Variant(sourceNode);
            }
            if (plan.TimeIndex >= 0)
            {
                fields[BrowseNames.Time] = new Variant(sourceTimestamp);
                qualifiedFields[CreateBaseEventFieldKey(BrowseNames.Time)] =
                    new Variant(sourceTimestamp);
                qualifiedFields[
                    HistorianEventFieldKey.FromOperand(
                        filter.SelectClauses[plan.TimeIndex])] =
                    new Variant(sourceTimestamp);
            }

            var record = new HistorianEventRecord(
                eventId,
                eventType,
                sourceTimestamp,
                fields.ToArrayOf())
            {
                QualifiedFields = qualifiedFields.ToArrayOf()
            };
            return new HistorianEventDecodeResult(
                ignoredIndexes.Count == 0
                    ? StatusCodes.Good
                    : StatusCodes.GoodDataIgnored,
                record,
                ignoredIndexes.ToArrayOf(),
                ignoredNames.ToArrayOf());
        }

        private static int FindClause(
            TypeTable typeTree,
            EventFilter filter,
            string browseName)
        {
            int found = -1;
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[i];
                if (clause.AttributeId == Attributes.Value &&
                    clause.BrowsePath.Count == 1 &&
                    clause.BrowsePath[0].NamespaceIndex == 0 &&
                    string.Equals(
                        clause.BrowsePath[0].Name,
                        browseName,
                        StringComparison.Ordinal) &&
                    (clause.TypeDefinitionId ==
                        ObjectTypeIds.BaseEventType ||
                        typeTree.IsTypeOf(
                            clause.TypeDefinitionId,
                            ObjectTypeIds.BaseEventType)))
                {
                    if (found >= 0)
                    {
                        return kAmbiguousClause;
                    }
                    found = i;
                }
            }
            return found;
        }

        private static bool ContainsClause(
            EventFilter filter,
            SimpleAttributeOperand expected)
        {
            var expectedKey =
                HistorianEventFieldKey.FromOperand(expected);
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[i];
                var actualKey =
                    HistorianEventFieldKey.FromOperand(clause);
                if (actualKey.TypeDefinitionId == expectedKey.TypeDefinitionId &&
                    actualKey.AttributeId == expectedKey.AttributeId &&
                    string.Equals(
                        actualKey.IndexRange,
                        expectedKey.IndexRange,
                        StringComparison.Ordinal) &&
                    PathsEqual(
                        actualKey.BrowsePath,
                        expectedKey.BrowsePath))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PathsEqual(
            ArrayOf<QualifiedName> left,
            ArrayOf<QualifiedName> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static HistorianEventFieldKey CreateBaseEventFieldKey(
            string browseName)
        {
            return new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName(browseName)],
                Attributes.Value,
                null);
        }

        private static bool IsSupportedField(
            TypeTable typeTree,
            NodeId eventType,
            SimpleAttributeOperand operand,
            HistorianNodeCapabilities capabilities)
        {
            if (IsBaseEventField(typeTree, operand))
            {
                return true;
            }
            if (!eventType.IsNull &&
                !operand.TypeDefinitionId.IsNull &&
                eventType != operand.TypeDefinitionId &&
                !typeTree.IsTypeOf(
                    eventType,
                    operand.TypeDefinitionId))
            {
                return false;
            }
            if (capabilities.EventFields.IsEmpty)
            {
                return true;
            }
            return ContainsOperand(capabilities.EventFields, operand) ||
                ContainsOperand(
                    capabilities.MandatoryEventFields,
                    operand);
        }

        private static bool IsBaseEventField(
            TypeTable typeTree,
            SimpleAttributeOperand operand)
        {
            if (operand.AttributeId != Attributes.Value ||
                operand.BrowsePath.Count != 1 ||
                operand.BrowsePath[0].NamespaceIndex != 0 ||
                operand.TypeDefinitionId.IsNull ||
                (operand.TypeDefinitionId != ObjectTypeIds.BaseEventType &&
                    !typeTree.IsTypeOf(
                        operand.TypeDefinitionId,
                        ObjectTypeIds.BaseEventType)))
            {
                return false;
            }
            string path = operand.BrowsePath[0].Name ?? string.Empty;
            return string.Equals(path, BrowseNames.EventId, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.EventType, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.SourceNode, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.SourceName, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.Time, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.ReceiveTime, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.LocalTime, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.Message, StringComparison.Ordinal) ||
                string.Equals(path, BrowseNames.Severity, StringComparison.Ordinal);
        }

        private static bool ContainsOperand(
            ArrayOf<SimpleAttributeOperand> operands,
            SimpleAttributeOperand expected)
        {
            var expectedKey =
                HistorianEventFieldKey.FromOperand(expected);
            for (int i = 0; i < operands.Count; i++)
            {
                SimpleAttributeOperand operand = operands[i];
                if (operand == null)
                {
                    continue;
                }
                var actualKey =
                    HistorianEventFieldKey.FromOperand(operand);
                if (actualKey.TypeDefinitionId == expectedKey.TypeDefinitionId &&
                    actualKey.AttributeId == expectedKey.AttributeId &&
                    string.Equals(
                        actualKey.IndexRange,
                        expectedKey.IndexRange,
                        StringComparison.Ordinal) &&
                    PathsEqual(
                        actualKey.BrowsePath,
                        expectedKey.BrowsePath))
                {
                    return true;
                }
            }
            return false;
        }

        private static HistorianEventDecodeResult InvalidField(
            StatusCode statusCode,
            int index,
            string name)
        {
            return new HistorianEventDecodeResult(
                statusCode,
                null,
                [index],
                [name]);
        }

        private const int kAmbiguousClause = -2;
    }

    internal readonly record struct HistorianEventUpdatePlan(
        int EventIdIndex,
        int EventTypeIndex,
        int TimeIndex,
        int SourceNodeIndex,
        NodeId NodeId,
        PerformUpdateType UpdateType);

    internal readonly record struct HistorianEventDecodeResult(
        StatusCode StatusCode,
        HistorianEventRecord? Record,
        ArrayOf<int> FieldIndexes = default,
        ArrayOf<string> FieldNames = default);
}
