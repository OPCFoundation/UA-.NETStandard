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
using System.Linq;
using System.Threading;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One local event declaration and its bounded, generation-private
    /// occurrence routes. Neither the EventType nor the Condition instance
    /// is used as the identity of an Event occurrence.
    /// </summary>
    internal sealed class WotProjectedEventBinding
    {
        public WotProjectedEventBinding(
            ISystemContext context,
            WotProjectedEventSource source,
            BaseObjectState notifier,
            NodeId eventTypeId,
            ConditionState? condition,
            ExpandedNodeId sourceCondition,
            int maxRoutes,
            TimeProvider timeProvider,
            string resourceXid,
            string jsonPointer,
            WotProjectedEventRouteRegistry routeRegistry)
        {
            m_context = context;
            Source = source;
            Notifier = notifier;
            EventTypeId = eventTypeId;
            Condition = condition;
            m_sourceCondition = sourceCondition;
            m_maxRoutes = maxRoutes;
            m_timeProvider = timeProvider;
            ResourceXid = resourceXid;
            JsonPointer = jsonPointer;
            m_routeRegistry = routeRegistry;
            m_valueContext = new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris
            };
            WotEventSelection selection = source.Form.EventSelection ?? WotEventSelection.Default;
            ArrayOf<ArrayOf<string>> members = WotEventSelectClauses.GetMaterializedMemberPaths(selection.Clauses);
            var fields = new Field[selection.Clauses.Count];
            for (int i = 0; i < fields.Length; i++)
            {
                WotResolvedEventSelectClause clause = selection.Clauses[i];
                ArrayOf<QualifiedName> path = clause.PathElements
                    .ConvertAll(element => WotBindingValueMapper.ResolveBrowseName(element, context.NamespaceUris));
                fields[i] = new Field(members[i], path);
            }
            m_fields = fields;
            if (condition is not null && !m_fields.Contains(field =>
                field.Path.Count == 1 && field.Path[0] == QualifiedName.From(Ua.BrowseNames.EventId)))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "A projected Condition must select the namespace-zero EventId.");
            }
            if (condition is not null && sourceCondition.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "A projected Condition requires an unambiguous source-owned action target.");
            }
            if (condition is not null && !m_fields.Contains(field => field.Path.Count == 0) &&
                !CanIdentifyConditionWithoutField())
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "A broad event notifier requires a selected ConditionId to route Condition actions.");
            }
        }

        public WotProjectedEventSource Source { get; }

        public BaseObjectState Notifier { get; }

        public NodeId EventTypeId { get; }

        public ConditionState? Condition { get; }

        public string ResourceXid { get; }

        public string JsonPointer { get; }

        public ExpandedNodeId SourceCondition => m_sourceCondition;

        public BaseEventState? Project(WotNotification notification)
        {
            if (StatusCode.IsBad(notification.Value.StatusCode))
            {
                throw new ServiceResultException(notification.Value.StatusCode);
            }
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(WotProjectedEventBinding));
                }
                ExpandedNodeId sourceCondition = m_sourceCondition;
                if (m_fields.Contains(field => field.Path.Count == 0) &&
                    TryRead(notification, "ConditionId", out Variant conditionId))
                {
                    sourceCondition = ReadPortableNodeId(conditionId, notification.NamespaceUris);
                    if (!m_sourceCondition.IsNull && sourceCondition != m_sourceCondition)
                    {
                        return null;
                    }
                }
                else if (Condition is not null && !CanIdentifyConditionWithoutField())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "A broad event notifier requires a selected ConditionId to route Condition actions.");
                }

                ByteString originalEventId = default;
                if (TryRead(notification, Ua.BrowseNames.EventId, out Variant eventId))
                {
                    if (!eventId.TryGetValue(out originalEventId) || originalEventId.IsEmpty)
                    {
                        throw new ServiceResultException(StatusCodes.BadEventIdUnknown);
                    }
                }
                else if (Condition is not null)
                {
                    throw new ServiceResultException(StatusCodes.BadEventIdUnknown);
                }
                ExpandedNodeId branchId = m_fields.Contains(field => field.Path.Count == 1 &&
                    field.Path[0] == QualifiedName.From(Ua.BrowseNames.BranchId)) &&
                    TryRead(notification, Ua.BrowseNames.BranchId, out Variant branch)
                    ? ReadPortableNodeId(branch, notification.NamespaceUris) : ExpandedNodeId.Null;
                var occurrence = new Occurrence(originalEventId, sourceCondition, branchId);
                if (!originalEventId.IsEmpty && m_occurrences.ContainsKey(occurrence))
                {
                    return null;
                }

                ByteString localEventId = Uuid.NewUuid().ToByteString();
                var result = new WotProjectedEventState
                {
                    NodeId = Condition is null ? NodeId.Null : Condition.NodeId,
                    TypeDefinitionId = EventTypeId
                };
                IServiceMessageContext sourceContext = notification.Context ??
                    new ServiceMessageContext(m_context.Telemetry, m_context.EncodeableFactory)
                    {
                        NamespaceUris = notification.NamespaceUris.IsEmpty
                            ? new NamespaceTable() : new NamespaceTable(notification.NamespaceUris.Span.ToArray())
                    };
                foreach (Field field in m_fields)
                {
                    if (!notification.Data.TryGetValue(field.MemberPath, out DataValue value))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            "The event notification omitted a selected data member.");
                    }
                    if (field.Path.Count == 0)
                    {
                        continue;
                    }
                    Variant mapped = WotBindingValueMapper.Translate(
                        value.WrappedValue, sourceContext, m_valueContext, allowNamespaceGrowth: true);
                    if (field.Path.Count == 1 && field.Path[0] == QualifiedName.From(Ua.BrowseNames.BranchId))
                    {
                        mapped = new Variant(branchId.IsNull ? NodeId.Null :
                            new NodeId(result.NodeId + "#Branch:" + branchId, EventTypeId.NamespaceIndex));
                    }
                    SetField(result, field.Path, mapped, value.StatusCode, value.SourceTimestamp);
                    var projectedValue = new DataValue(
                        mapped, value.StatusCode, value.SourceTimestamp, value.ServerTimestamp);
                    result.AddField(field.Path, projectedValue);
                    if (Condition is not null && branchId.IsNull)
                    {
                        SetField(Condition, field.Path, mapped, value.StatusCode, value.SourceTimestamp);
                    }
                }

                DateTimeUtc now = m_timeProvider.GetUtcNow().UtcDateTime;
                SetIdentity(result, localEventId, now);
                if (Condition is not null && branchId.IsNull)
                {
                    SetIdentity(Condition, localEventId, now);
                }
                if (!originalEventId.IsEmpty)
                {
                    while (m_routes.Count >= m_maxRoutes)
                    {
                        ByteString expired = m_order.Dequeue();
                        if (m_routes.TryGetValue(expired, out Occurrence previous))
                        {
                            m_routes.Remove(expired);
                            m_occurrences.Remove(previous);
                        }
                    }
                    m_routes.Add(localEventId, occurrence);
                    m_occurrences.Add(occurrence, localEventId);
                    m_order.Enqueue(localEventId);
                }
                return result;
            }
        }

        public ServiceResult ResolveEventId(ByteString localEventId, out ByteString originalEventId)
        {
            return m_routeRegistry.Resolve(this, localEventId, out originalEventId);
        }

        public bool TryResolveOwnEventId(ByteString localEventId, out ByteString originalEventId)
        {
            lock (m_gate)
            {
                if (!m_disposed && m_routes.TryGetValue(localEventId, out Occurrence occurrence))
                {
                    originalEventId = occurrence.EventId;
                    return true;
                }
            }
            originalEventId = default;
            return false;
        }

        public void Clear()
        {
            lock (m_gate)
            {
                m_disposed = true;
                m_routes.Clear();
                m_occurrences.Clear();
                m_order.Clear();
            }
        }

        private bool CanIdentifyConditionWithoutField()
        {
            return !m_sourceCondition.IsNull &&
                ExpandedNodeId.Parse(Source.Form.Addressing.Target) == m_sourceCondition;
        }

        private static bool TryRead(WotNotification notification, string name, out Variant value)
        {
            if (notification.Data.TryGetValue([name], out DataValue field))
            {
                if (StatusCode.IsBad(field.StatusCode))
                {
                    throw new ServiceResultException(field.StatusCode);
                }
                value = field.WrappedValue;
                return true;
            }
            value = Variant.Null;
            return false;
        }

        private void SetIdentity(BaseEventState state, ByteString eventId, DateTimeUtc now)
        {
            state.EventId ??= PropertyState<ByteString>.With<VariantBuilder>(state);
            state.EventId.Value = eventId;
            state.EventType ??= PropertyState<NodeId>.With<VariantBuilder>(state);
            state.EventType.Value = EventTypeId;
            state.SourceNode ??= PropertyState<NodeId>.With<VariantBuilder>(state);
            state.SourceNode.Value = Notifier.NodeId;
            state.SourceName ??= PropertyState<string>.With<VariantBuilder>(
                state, Notifier.BrowseName.Name ?? string.Empty);
            state.Time ??= PropertyState<DateTimeUtc>.With<VariantBuilder>(state, now);
            state.ReceiveTime ??= PropertyState<DateTimeUtc>.With<VariantBuilder>(state, now);
            state.Message ??= PropertyState<LocalizedText>.With<VariantBuilder>(
                state, new LocalizedText(string.Empty));
            state.Severity ??= PropertyState<ushort>.With<VariantBuilder>(state, (ushort)EventSeverity.Medium);
        }

        private void SetField(
            NodeState state, ArrayOf<QualifiedName> path, Variant value, StatusCode status, DateTimeUtc timestamp)
        {
            NodeState parent = state;
            for (int i = 0; i < path.Count; i++)
            {
                var children = new List<BaseInstanceState>();
                parent.GetChildren(m_context, children);
                BaseInstanceState? child = children.FirstOrDefault(candidate => candidate.BrowseName == path[i]);
                if (child is null && path[i].NamespaceIndex == 0)
                {
                    child = parent.CreateChild(m_context, path[i], assignInstanceNodeIds: false);
                }
                if (child is null)
                {
                    child = new BaseDataVariableState(parent)
                    {
                        BrowseName = path[i],
                        DisplayName = new LocalizedText(path[i].Name),
                        DataType = Ua.DataTypeIds.BaseDataType,
                        ValueRank = ValueRanks.Any
                    };
                    parent.AddChild(child);
                }
                if (i == path.Count - 1)
                {
                    if (child is not BaseVariableState variable)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                    }
                    variable.Value = value;
                    variable.StatusCode = status;
                    variable.Timestamp = timestamp;
                }
                parent = child;
            }
        }

        internal static ExpandedNodeId ReadPortableNodeId(Variant value, ArrayOf<string> namespaces)
        {
            if (value.IsNull)
            {
                return ExpandedNodeId.Null;
            }
            if (value.TryGetValue(out ExpandedNodeId expanded))
            {
                if (expanded.ServerIndex != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A Condition must belong to the selected source server.");
                }
                if (expanded.IsAbsolute || expanded.NamespaceIndex == 0)
                {
                    return expanded;
                }
                NodeId local = ExpandedNodeId.ToNodeId(expanded, new NamespaceTable(namespaces.Span.ToArray()));
                value = new Variant(local);
            }
            if (!value.TryGetValue(out NodeId nodeId))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
            }
            if (nodeId.NamespaceIndex == 0)
            {
                return nodeId;
            }
            if (nodeId.NamespaceIndex >= namespaces.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "A Condition identity has no source namespace mapping.");
            }
            return new ExpandedNodeId(nodeId, namespaces[nodeId.NamespaceIndex]);
        }

        private readonly record struct Field(ArrayOf<string> MemberPath, ArrayOf<QualifiedName> Path);

        private readonly record struct Occurrence(
            ByteString EventId, ExpandedNodeId ConditionId, ExpandedNodeId BranchId);

        private readonly ISystemContext m_context;
        private readonly ExpandedNodeId m_sourceCondition;
        private readonly int m_maxRoutes;
        private readonly TimeProvider m_timeProvider;
        private readonly WotProjectedEventRouteRegistry m_routeRegistry;
        private readonly IServiceMessageContext m_valueContext;
        private readonly ArrayOf<Field> m_fields;
        private readonly Lock m_gate = new();
        private readonly Dictionary<ByteString, Occurrence> m_routes = [];
        private readonly Dictionary<Occurrence, ByteString> m_occurrences = [];
        private readonly Queue<ByteString> m_order = [];
        private bool m_disposed;
    }
}
