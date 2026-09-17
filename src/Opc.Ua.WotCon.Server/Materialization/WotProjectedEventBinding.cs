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
using System.Threading.Tasks;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One local event declaration and its bounded, generation-private
    /// occurrence routes. Neither the EventType nor the Condition instance
    /// is used as the identity of an Event occurrence.
    /// </summary>
    internal sealed partial class WotProjectedEventBinding
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
            WotProjectedEventRouteRegistry routeRegistry,
            WoTEventIdentityModeEnum identityMode = WoTEventIdentityModeEnum.LocalReEmission,
            bool isCondition = false,
            Func<NodeId, CancellationToken, ValueTask<ConditionState>>? createCondition = null,
            bool nativePublication = true)
        {
            if (identityMode is not (WoTEventIdentityModeEnum.LocalReEmission or
                WoTEventIdentityModeEnum.TransparentForwarding))
            {
                throw new ArgumentOutOfRangeException(nameof(identityMode));
            }
            m_context = context;
            Source = source;
            Notifier = notifier;
            EventTypeId = eventTypeId;
            Condition = condition;
            IsCondition = isCondition || condition is not null;
            m_createCondition = createCondition;
            m_nativePublication = nativePublication;
            SourceCondition = sourceCondition;
            m_maxRoutes = maxRoutes;
            m_timeProvider = timeProvider;
            ResourceXid = resourceXid;
            JsonPointer = jsonPointer;
            m_routeRegistry = routeRegistry;
            IdentityMode = identityMode;
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
                if (path.Count == 1 && path[0] == QualifiedName.From(Ua.BrowseNames.EventId))
                {
                    m_eventIdMemberPath = members[i];
                }
            }
            m_fields = fields;
            if (IsCondition && m_eventIdMemberPath.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "A projected Condition must select the namespace-zero EventId.");
            }
            if (IsCondition &&
                !m_fields.Contains(field => field.Path.Count == 0) &&
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

        public bool IsCondition { get; }

        public string ResourceXid { get; }

        public string JsonPointer { get; }

        public ExpandedNodeId SourceCondition { get; }

        public WoTEventIdentityModeEnum IdentityMode { get; }

        public bool IsFaulted
        {
            get
            {
                lock (m_gate)
                {
                    return StatusCode.IsBad(m_failureStatus);
                }
            }
        }

        public BaseEventState? Project(WotNotification notification)
        {
            return Project(notification, Condition);
        }

        public async ValueTask<BaseEventState?> ProjectAsync(
            WotNotification notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StatusCode.IsBad(notification.Value.StatusCode) || !IsCondition)
            {
                return Project(notification, Condition);
            }
            if (IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding)
            {
                AdmitTransparentOccurrence(notification.CapturedEvent);
            }
            ExpandedNodeId sourceCondition = SourceCondition;
            if (notification.CapturedEvent is { HasConditionId: true } captured)
            {
                captured.Source.Validate();
                sourceCondition = captured.ConditionId;
            }
            else if (TryRead(notification, ["ConditionId"], out Variant value))
            {
                sourceCondition = ReadPortableNodeId(value, notification.NamespaceUris);
            }
            if (sourceCondition.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "A Condition notification requires its source Condition identity.");
            }
            if (m_createCondition is null && Condition is not null &&
                !SourceCondition.IsNull && sourceCondition != SourceCondition)
            {
                return null;
            }
            ConditionState condition = await GetConditionAsync(sourceCondition, cancellationToken)
                .ConfigureAwait(false);
            return Project(notification, condition);
        }

        private WotProjectedEventState? Project(WotNotification notification, ConditionState? condition)
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
                WotCapturedEvent? captured = notification.CapturedEvent;
                captured?.Source.Validate();
                if (IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding)
                {
                    AdmitTransparentOccurrence(captured);
                }
                ExpandedNodeId sourceCondition = SourceCondition;
                if (m_fields.Contains(field => field.Path.Count == 0) &&
                    TryRead(notification, ["ConditionId"], out Variant conditionId))
                {
                    sourceCondition = ReadPortableNodeId(conditionId, notification.NamespaceUris);
                }
                else if (IsCondition && !CanIdentifyConditionWithoutField())
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "A broad event notifier requires a selected ConditionId to route Condition actions.");
                }

                ByteString originalEventId = default;
                if (m_eventIdMemberPath.Count != 0 &&
                    TryRead(notification, m_eventIdMemberPath, out Variant eventId))
                {
                    if (!eventId.TryGetValue(out originalEventId) || originalEventId.IsEmpty)
                    {
                        throw new ServiceResultException(StatusCodes.BadEventIdUnknown);
                    }
                }
                else if (IsCondition)
                {
                    throw new ServiceResultException(StatusCodes.BadEventIdUnknown);
                }
                ExpandedNodeId branchId = m_fields.Contains(field => field.Path.Count == 1 &&
                    field.Path[0] == QualifiedName.From(Ua.BrowseNames.BranchId)) &&
                    TryRead(notification, [Ua.BrowseNames.BranchId], out Variant branch)
                    ? ReadPortableNodeId(branch, notification.NamespaceUris) : ExpandedNodeId.Null;
                var occurrence = new Occurrence(originalEventId, sourceCondition, branchId);
                if (!m_nativePublication && !originalEventId.IsEmpty && m_occurrences.ContainsKey(occurrence))
                {
                    return null;
                }
                IServiceMessageContext sourceContext = captured?.Source.Context ?? notification.Context ??
                    new ServiceMessageContext(m_context.Telemetry, m_context.EncodeableFactory)
                    {
                        NamespaceUris = notification.NamespaceUris.IsEmpty
                            ? new NamespaceTable() : new NamespaceTable(notification.NamespaceUris.Span.ToArray())
                    };
                ArrayOf<DataValue> sourceFields = CaptureFields(notification, sourceContext);
                DateTimeUtc now = m_timeProvider.GetUtcNow().UtcDateTime;
                if (m_nativePublication && !originalEventId.IsEmpty &&
                    m_sourceEventIds.TryGetValue(originalEventId, out ByteString retainedId))
                {
                    Origin previous = m_origins[retainedId];
                    if (m_rejectedEvents.Contains(retainedId) ||
                        !SameCapturedSource(previous.Captured, captured) ||
                        !SameOccurrenceFields(previous.Fields, sourceFields))
                    {
                        m_rejectedEvents.Add(retainedId);
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The source reused an EventId for a different occurrence or state.");
                    }
                    m_origins[retainedId] = new Origin(captured, now, previous.Fields);
                    UpdateDescriptor(captured);
                    return null;
                }

                ByteString localEventId = IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding
                    ? captured!.EventId : Uuid.NewUuid().ToByteString();
                var result = new WotProjectedEventState
                {
                    NodeId = condition is null ? NodeId.Null : condition.NodeId,
                    TypeDefinitionId = EventTypeId
                };
                for (int index = 0; index < m_fields.Count; index++)
                {
                    Field field = m_fields[index];
                    DataValue value = sourceFields[index];
                    if (field.Path.Count == 0)
                    {
                        continue;
                    }
                    Variant mapped = value.WrappedValue;
                    if (IdentityMode == WoTEventIdentityModeEnum.LocalReEmission &&
                        field.Path.Count == 1 && field.Path[0] == QualifiedName.From(Ua.BrowseNames.BranchId))
                    {
                        mapped = new Variant(branchId.IsNull ? NodeId.Null :
                            new NodeId(result.NodeId + "#Branch:" + branchId, EventTypeId.NamespaceIndex));
                    }
                    SetField(result, field.Path, mapped, value.StatusCode, value.SourceTimestamp);
                    var projectedValue = new DataValue(
                        mapped, value.StatusCode, value.SourceTimestamp, value.ServerTimestamp);
                    result.AddField(field.Path, projectedValue);
                    if (condition is not null && branchId.IsNull)
                    {
                        SetField(condition, field.Path, mapped, value.StatusCode, value.SourceTimestamp);
                    }
                }

                SetIdentity(result, localEventId, now);
                if (condition is not null && branchId.IsNull)
                {
                    SetIdentity(condition, localEventId, now);
                }
                while (m_order.Count >= m_maxRoutes)
                {
                    ByteString expired = m_order.Dequeue();
                    m_origins.Remove(expired);
                    m_rejectedEvents.Remove(expired);
                    if (m_routes.TryGetValue(expired, out Occurrence previous))
                    {
                        m_routes.Remove(expired);
                        m_occurrences.Remove(previous);
                        m_sourceEventIds.Remove(previous.EventId);
                        m_routeRegistry.ReleaseTransparentEvent(this, previous.EventId);
                    }
                }
                if (!originalEventId.IsEmpty)
                {
                    if (IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding)
                    {
                        m_routeRegistry.AdmitTransparentEvent(this, captured!);
                    }
                    m_routes.Add(localEventId, occurrence);
                    m_occurrences.Add(occurrence, localEventId);
                    if (m_nativePublication)
                    {
                        m_sourceEventIds.Add(originalEventId, localEventId);
                    }
                }
                m_order.Enqueue(localEventId);
                m_origins[localEventId] = new Origin(captured, now, sourceFields);
                UpdateDescriptor(captured);
                return result;
            }
        }

        public ServiceResult ResolveEventId(ByteString localEventId, out ByteString originalEventId)
        {
            return m_routeRegistry.Resolve(this, localEventId, out originalEventId);
        }

        public void RegisterAction(string action, WotCapturedConditionAction captured)
        {
            m_actions.Add(action, captured);
        }

        public bool TryGetAction(string action, out WotCapturedConditionAction? captured)
        {
            lock (m_gate)
            {
                if (!m_disposed)
                {
                    return m_actions.TryGetValue(action, out captured);
                }
            }
            captured = null;
            return false;
        }

        public ServiceResult ResolveAction(
            ByteString eventId,
            string action,
            out WotCapturedEvent? occurrence,
            out WotCapturedConditionAction? capturedAction)
        {
            return m_routeRegistry.ResolveAction(this, eventId, action, out occurrence, out capturedAction);
        }

        public bool TryResolveOwnAction(
            ByteString eventId,
            string action,
            out WotCapturedEvent? occurrence,
            out WotCapturedConditionAction? capturedAction)
        {
            lock (m_gate)
            {
                if (!m_disposed && !m_rejectedEvents.Contains(eventId) &&
                    m_origins.TryGetValue(eventId, out Origin? origin) &&
                    origin.Captured is { HasConditionId: true } source &&
                    source.ConditionId == SourceCondition &&
                    m_actions.TryGetValue(action, out capturedAction))
                {
                    occurrence = source;
                    return true;
                }
            }
            occurrence = null;
            capturedAction = null;
            return false;
        }

        public bool TryResolveOwnEventId(ByteString localEventId, out ByteString originalEventId)
        {
            lock (m_gate)
            {
                if (!m_disposed && !m_rejectedEvents.Contains(localEventId) &&
                    m_routes.TryGetValue(localEventId, out Occurrence occurrence) &&
                    (SourceCondition.IsNull || occurrence.ConditionId == SourceCondition))
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
                m_conditions.Clear();
                m_origins.Clear();
                m_sourceEventIds.Clear();
                m_rejectedEvents.Clear();
                m_actions.Clear();
                MarkDescriptorRetired();
            }
        }

        private async ValueTask<ConditionState> GetConditionAsync(
            ExpandedNodeId sourceCondition, CancellationToken cancellationToken)
        {
            TaskCompletionSource<ConditionState>? pending = null;
            Task<ConditionState> task;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(WotProjectedEventBinding));
                }
                if (Condition is not null && sourceCondition == SourceCondition)
                {
                    return Condition;
                }
                if (!m_conditions.TryGetValue(sourceCondition, out task!))
                {
                    if (m_createCondition is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported,
                            "The configured Condition factory cannot create independent source instances.");
                    }
                    int conditionCount = m_conditions.Count + (Condition is null ? 0 : 1);
                    if (conditionCount >= m_maxRoutes)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations, "The generation's Condition instance bound was reached.");
                    }
                    pending = new TaskCompletionSource<ConditionState>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    task = pending.Task;
                    m_conditions.Add(sourceCondition, task);
                }
            }
            if (pending is not null)
            {
                try
                {
                    NodeId nodeId = IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding
                        ? ExpandedNodeId.ToNodeId(sourceCondition, m_context.NamespaceUris)
                        : new NodeId("Condition-" + Guid.NewGuid().ToString("N"), EventTypeId.NamespaceIndex);
                    ConditionState created = await m_createCondition!(nodeId, cancellationToken).ConfigureAwait(false);
                    if (created.NodeId != nodeId)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid,
                            "The Condition factory changed the admitted instance identity.");
                    }
                    foreach (string action in s_conditionMethods)
                    {
                        if (created.FindChild(m_context, QualifiedName.From(action)) is MethodState method)
                        {
                            method.Executable = false;
                            method.UserExecutable = false;
                        }
                    }
                    lock (m_gate)
                    {
                        if (m_disposed)
                        {
                            throw new ObjectDisposedException(nameof(WotProjectedEventBinding));
                        }
                    }
                    pending.TrySetResult(created);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    pending.TrySetException(exception);
                    _ = pending.Task.Exception;
                    throw;
                }
            }
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private bool CanIdentifyConditionWithoutField()
        {
            return !SourceCondition.IsNull &&
                ExpandedNodeId.Parse(Source.Form.Addressing.Target) == SourceCondition;
        }

        private ArrayOf<DataValue> CaptureFields(WotNotification notification, IServiceMessageContext sourceContext)
        {
            var result = new DataValue[m_fields.Count];
            for (int index = 0; index < m_fields.Count; index++)
            {
                if (!notification.Data.TryGetValue(m_fields[index].MemberPath, out DataValue value))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "The event notification omitted a selected data member.");
                }
                Variant mapped = WotBindingValueMapper.Translate(
                    value.WrappedValue, sourceContext, m_valueContext, allowNamespaceGrowth: true);
                result[index] = new DataValue(mapped, value.StatusCode, value.SourceTimestamp, value.ServerTimestamp);
            }
            return result;
        }

        private bool SameOccurrenceFields(ArrayOf<DataValue> previous, ArrayOf<DataValue> current)
        {
            if (previous.Count != current.Count)
            {
                return false;
            }
            for (int index = 0; index < previous.Count; index++)
            {
                Field field = m_fields[index];
                if (field.Path.Count == 1 && field.Path[0] == QualifiedName.From(Ua.BrowseNames.ReceiveTime))
                {
                    continue;
                }
                DataValue left = previous[index];
                DataValue right = current[index];
                if (!left.StatusCode.Equals(right.StatusCode, StatusCodeComparison.AllBits) ||
                    left.SourceTimestamp != right.SourceTimestamp ||
                    !left.WrappedValue.Equals(right.WrappedValue))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SameCapturedSource(WotCapturedEvent? previous, WotCapturedEvent? current)
        {
            return previous is null ? current is null :
                current is not null && ReferenceEquals(previous.Source, current.Source);
        }

        private void AdmitTransparentOccurrence(WotCapturedEvent? captured)
        {
            if (captured is null || !captured.Source.IsAuthenticated ||
                !captured.HasEventId || captured.EventId.IsEmpty ||
                !captured.HasEventType || captured.EventType.IsNull ||
                !captured.HasSourceNode || !captured.HasTime || !captured.HasReceiveTime)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Transparent forwarding requires captured authenticated source identity and time facts.");
            }
            ExpandedNodeId localType = NodeId.ToExpandedNodeId(EventTypeId, m_context.NamespaceUris);
            if (captured.EventType != localType)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeDefinitionInvalid,
                    "Transparent forwarding cannot replace the source EventType with a local overlay.");
            }
            if (IsCondition && (!captured.HasConditionId || !captured.HasBranchId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadEventIdUnknown,
                    "A transparent Condition requires its captured source Condition and branch identities.");
            }
            m_routeRegistry.ValidateTransparentSource(this, captured);
        }

        private static bool TryRead(
            WotNotification notification, ArrayOf<string> memberPath, out Variant value)
        {
            if (notification.Data.TryGetValue(memberPath, out DataValue field))
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
            if (IdentityMode == WoTEventIdentityModeEnum.LocalReEmission)
            {
                state.SourceNode.Value = Notifier.NodeId;
            }
            state.SourceName ??= PropertyState<string>.With<VariantBuilder>(
                state, Notifier.BrowseName.Name ?? string.Empty);
            state.Time ??= PropertyState<DateTimeUtc>.With<VariantBuilder>(state, now);
            state.ReceiveTime ??= PropertyState<DateTimeUtc>.With<VariantBuilder>(state, now);
            if (m_nativePublication)
            {
                state.ReceiveTime.Value = now;
            }
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
                var local = ExpandedNodeId.ToNodeId(expanded, new NamespaceTable(namespaces.Span.ToArray()));
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
        private readonly int m_maxRoutes;
        private readonly TimeProvider m_timeProvider;
        private readonly WotProjectedEventRouteRegistry m_routeRegistry;
        private readonly IServiceMessageContext m_valueContext;
        private readonly ArrayOf<Field> m_fields;
        private readonly ArrayOf<string> m_eventIdMemberPath;
        private readonly Func<NodeId, CancellationToken, ValueTask<ConditionState>>? m_createCondition;
        private readonly bool m_nativePublication;
        private readonly Lock m_gate = new();
        private readonly Dictionary<ExpandedNodeId, Task<ConditionState>> m_conditions = [];
        private readonly Dictionary<ByteString, Occurrence> m_routes = [];
        private readonly Dictionary<Occurrence, ByteString> m_occurrences = [];
        private readonly Dictionary<ByteString, ByteString> m_sourceEventIds = [];
        private readonly HashSet<ByteString> m_rejectedEvents = [];
        private readonly Dictionary<string, WotCapturedConditionAction> m_actions = [];
        private readonly Queue<ByteString> m_order = [];
        private bool m_disposed;
        private static readonly ArrayOf<string> s_conditionMethods =
            ["Enable", "Disable", "AddComment", "Acknowledge", "Confirm"];
    }
}
