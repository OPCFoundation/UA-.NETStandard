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
using Opc.Ua.Server;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One local event declaration and its bounded, generation-owned occurrence
    /// evidence. Action routability does not determine identity ownership.
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
            bool nativePublication = true,
            EventManager? eventManager = null,
            bool requireServerIdentityAdmission = false)
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
            m_eventManager = eventManager ?? (context as ServerSystemContext)?.Server.EventManager;
            m_requireServerIdentityAdmission = requireServerIdentityAdmission;
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
            if (IsCondition && !m_nativePublication && m_eventIdMemberPath.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "A projected Condition must select the namespace-zero EventId.");
            }
            if (IsCondition && !m_nativePublication &&
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

        public void EnsureAvailable()
        {
            lock (m_gate)
            {
                EnsureAvailableCore();
            }
        }

        public async ValueTask<BaseEventState?> ProjectAsync(
            WotNotification notification, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completion = await BeginProjectionAsync(cancellationToken).ConfigureAwait(false);
            PreparedOccurrence? prepared = null;
            bool committed = false;
            try
            {
                lock (m_gate)
                {
                    EnsureAvailableCore();
                    prepared = PrepareOccurrence(notification);
                }
                if (prepared is null)
                {
                    return null;
                }
                ConditionState? condition = IsCondition
                    ? await GetConditionAsync(prepared.Occurrence.ConditionId, cancellationToken).ConfigureAwait(false)
                    : Condition;
                lock (m_gate)
                {
                    EnsureAvailableCore();
                    cancellationToken.ThrowIfCancellationRequested();
                    prepared.Captured?.Source.Validate();
                    WotProjectedEventState result = CommitOccurrence(prepared, condition);
                    committed = true;
                    return result;
                }
            }
            finally
            {
                lock (m_gate)
                {
                    if (!committed && prepared is not null)
                    {
                        if (!prepared.IsRetainedRefresh)
                        {
                            prepared.IdentityReservation?.Dispose();
                        }
                        if (prepared.OwnsTransparentReservation)
                        {
                            m_routeRegistry.ReleaseTransparentEvent(this, prepared.Occurrence.EventId);
                        }
                    }
                    m_pendingProjection = null;
                }
                completion.TrySetResult(true);
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
                foreach (Origin origin in m_origins.Values)
                {
                    origin.IdentityReservation?.Dispose();
                }
                m_routes.Clear();
                m_conditions.Clear();
                m_origins.Clear();
                m_sourceEventIds.Clear();
                m_rejectedEvents.Clear();
                m_actions.Clear();
                MarkDescriptorRetired();
            }
        }

        private async ValueTask<TaskCompletionSource<bool>> BeginProjectionAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task pending;
                lock (m_gate)
                {
                    EnsureAvailableCore();
                    if (m_pendingProjection is null)
                    {
                        m_pendingProjection = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        return m_pendingProjection;
                    }
                    pending = m_pendingProjection.Task;
                }
                await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private void EnsureAvailableCore()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(WotProjectedEventBinding));
            }
            if (StatusCode.IsBad(m_failureStatus))
            {
                throw new ServiceResultException(
                    m_failureStatus, "The event binding is unavailable; replace and drain its owning generation.");
            }
        }

        private PreparedOccurrence? PrepareOccurrence(WotNotification notification)
        {
            if (StatusCode.IsBad(notification.Value.StatusCode))
            {
                throw new ServiceResultException(notification.Value.StatusCode);
            }
            WotCapturedEvent? captured = notification.CapturedEvent;
            captured?.Source.Validate();
            if (IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding)
            {
                AdmitTransparentOccurrence(captured);
            }
            ExpandedNodeId sourceCondition = SourceCondition;
            if (captured is { HasConditionId: true })
            {
                sourceCondition = captured.ConditionId;
            }
            else if (IsCondition && captured is not null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The source did not supply its required Condition identity.");
            }
            else if (m_fields.Contains(field => field.Path.Count == 0) &&
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
            if (IsCondition)
            {
                if (sourceCondition.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid,
                        "A Condition notification requires its source Condition identity.");
                }
                if (m_createCondition is null && Condition is not null &&
                    !SourceCondition.IsNull && sourceCondition != SourceCondition)
                {
                    return null;
                }
            }
            ByteString originalEventId = default;
            if (captured is { HasEventId: true })
            {
                originalEventId = captured.EventId;
                if (originalEventId.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadEventIdUnknown);
                }
            }
            else if (m_eventIdMemberPath.Count != 0 &&
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
            if (IsCondition && captured is not null && (!captured.HasEventId || !captured.HasBranchId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadEventIdUnknown, "The source did not supply its required occurrence identities.");
            }
            ExpandedNodeId branchId = captured is { HasBranchId: true } ? captured.BranchId :
                m_fields.Contains(field => field.Path.Count == 1 &&
                    field.Path[0] == QualifiedName.From(Ua.BrowseNames.BranchId)) &&
                    TryRead(notification, [Ua.BrowseNames.BranchId], out Variant branch)
                    ? ReadPortableNodeId(branch, notification.NamespaceUris) : ExpandedNodeId.Null;
            var occurrence = new Occurrence(originalEventId, sourceCondition, branchId);
            if (!m_nativePublication && captured is null && !originalEventId.IsEmpty &&
                m_sourceEventIds.TryGetValue(originalEventId, out ByteString legacyId) &&
                !m_rejectedEvents.Contains(legacyId) &&
                m_origins[legacyId] is { Captured: null } legacy && legacy.Occurrence == occurrence)
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
            if (!originalEventId.IsEmpty && m_sourceEventIds.TryGetValue(originalEventId, out ByteString retainedId))
            {
                Origin previous = m_origins[retainedId];
                if (m_rejectedEvents.Contains(retainedId) || previous.Occurrence != occurrence ||
                    !SameCapturedOccurrence(previous.Captured, captured) ||
                    !SameOccurrenceFields(previous.Fields, sourceFields,
                        IsRetainedCondition(captured) ? previous.RefreshableFields : [],
                        out bool propertyChanged))
                {
                    m_rejectedEvents.Add(retainedId);
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The source reused an EventId for a different occurrence or state.");
                }
                if (propertyChanged)
                {
                    return new PreparedOccurrence(occurrence, retainedId, captured, sourceFields, now)
                    {
                        IsRetainedRefresh = true,
                        IdentityReservation = previous.IdentityReservation
                    };
                }
                m_origins[retainedId] = new Origin(
                    captured, now, previous.Fields, occurrence, previous.IdentityReservation,
                    previous.RefreshableFields);
                UpdateDescriptor(captured);
                return null;
            }
            if (m_origins.Count >= m_maxRoutes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations,
                    "The generation's occurrence evidence capacity was reached; replace and drain the generation.");
            }
            ByteString localEventId = IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding
                ? captured!.EventId : Uuid.NewUuid().ToByteString();
            var prepared = new PreparedOccurrence(occurrence, localEventId, captured, sourceFields, now);
            try
            {
                if (m_requireServerIdentityAdmission)
                {
                    if (m_eventManager?.SupportsEventIdentityAdmission != true)
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            "Native event projection requires the host's server-wide event admission pipeline.");
                    }
                    prepared.IdentityReservation = m_eventManager.ReserveEventIdentity(localEventId,
                        new ProjectedIdentitySource(
                            captured, IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding));
                }
                if (IdentityMode == WoTEventIdentityModeEnum.TransparentForwarding)
                {
                    prepared.OwnsTransparentReservation = m_routeRegistry.AdmitTransparentEvent(this, captured!);
                }
                return prepared;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                prepared.IdentityReservation?.Dispose();
                throw;
            }
        }

        private WotProjectedEventState CommitOccurrence(PreparedOccurrence prepared, ConditionState? condition)
        {
            Occurrence occurrence = prepared.Occurrence;
            var result = new WotProjectedEventState
            {
                NodeId = condition is null ? NodeId.Null : condition.NodeId,
                TypeDefinitionId = EventTypeId
            };
            if (prepared.Captured is { } captured)
            {
                PopulateCapturedFields(result, condition, captured, occurrence.BranchId);
            }
            for (int index = 0; index < m_fields.Count; index++)
            {
                Field field = m_fields[index];
                DataValue value = prepared.Fields[index];
                if (field.Path.Count == 0 || HasCapturedField(prepared.Captured, field.Path))
                {
                    continue;
                }
                PopulateField(result, condition, field.Path, value, occurrence.BranchId);
            }
            SetIdentity(result, prepared.LocalEventId, prepared.ReceiveTime);
            prepared.IdentityReservation?.Attach(m_context, result);
            if (condition is not null && occurrence.BranchId.IsNull)
            {
                SetIdentity(condition, prepared.LocalEventId, prepared.ReceiveTime);
                prepared.IdentityReservation?.Attach(m_context, condition);
            }
            if (!prepared.IsRetainedRefresh && !occurrence.EventId.IsEmpty)
            {
                m_routes.Add(prepared.LocalEventId, occurrence);
                m_sourceEventIds.Add(occurrence.EventId, prepared.LocalEventId);
            }
            ArrayOf<bool> refreshableFields = prepared.IsRetainedRefresh
                ? m_origins[prepared.LocalEventId].RefreshableFields
                : CaptureRefreshableFields(condition, prepared.Captured);
            m_origins[prepared.LocalEventId] = new Origin(
                prepared.Captured, prepared.ReceiveTime, prepared.Fields, occurrence,
                prepared.IdentityReservation, refreshableFields);
            UpdateDescriptor(prepared.Captured);
            return result;
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
                    lock (m_gate)
                    {
                        if (m_conditions.TryGetValue(sourceCondition, out Task<ConditionState>? current) &&
                            ReferenceEquals(current, pending.Task))
                        {
                            m_conditions.Remove(sourceCondition);
                        }
                    }
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

        private bool SameOccurrenceFields(
            ArrayOf<DataValue> previous,
            ArrayOf<DataValue> current,
            ArrayOf<bool> refreshableFields,
            out bool propertyChanged)
        {
            propertyChanged = false;
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
                    left.SourceTimestamp != right.SourceTimestamp)
                {
                    return false;
                }
                if (!left.WrappedValue.Equals(right.WrappedValue))
                {
                    if (refreshableFields.Count != m_fields.Count || !refreshableFields[index] ||
                        left.WrappedValue.TypeInfo != right.WrappedValue.TypeInfo)
                    {
                        return false;
                    }
                    propertyChanged = true;
                }
            }
            return true;
        }

        private ArrayOf<bool> CaptureRefreshableFields(ConditionState? condition, WotCapturedEvent? captured)
        {
            if (condition is null || !IsRetainedCondition(captured))
            {
                return [];
            }
            NodeId coreType = condition switch
            {
                LimitAlarmState => Ua.ObjectTypeIds.LimitAlarmType,
                AlarmConditionState => Ua.ObjectTypeIds.AlarmConditionType,
                AcknowledgeableConditionState => Ua.ObjectTypeIds.AcknowledgeableConditionType,
                _ => Ua.ObjectTypeIds.ConditionType
            };
            if (!m_context.TypeTable.IsTypeOf(EventTypeId, coreType))
            {
                return [];
            }
            var children = new List<BaseInstanceState>();
            condition.GetChildren(m_context, children);
            var permitted = new bool[m_fields.Count];
            for (int index = 0; index < m_fields.Count; index++)
            {
                Field field = m_fields[index];
                if (field.Path.Count == 1 && field.Path[0].NamespaceIndex == 0 &&
                    !HasCapturedField(captured, field.Path))
                {
                    permitted[index] = children.Any(child =>
                        child is PropertyState property && child.BrowseName == field.Path[0] &&
                        IsNonStateCoreProperty(condition, property));
                }
            }
            return permitted;
        }

        private static bool IsNonStateCoreProperty(ConditionState condition, PropertyState property)
        {
            if (condition is AlarmConditionState alarm &&
                (ReferenceEquals(property, alarm.MaxTimeShelved) ||
                    ReferenceEquals(property, alarm.OnDelay) ||
                    ReferenceEquals(property, alarm.OffDelay) ||
                    ReferenceEquals(property, alarm.ReAlarmTime)))
            {
                return true;
            }
            return condition is LimitAlarmState limit &&
                (ReferenceEquals(property, limit.HighHighLimit) ||
                    ReferenceEquals(property, limit.HighLimit) ||
                    ReferenceEquals(property, limit.LowLimit) ||
                    ReferenceEquals(property, limit.LowLowLimit) ||
                    ReferenceEquals(property, limit.BaseHighHighLimit) ||
                    ReferenceEquals(property, limit.BaseHighLimit) ||
                    ReferenceEquals(property, limit.BaseLowLimit) ||
                    ReferenceEquals(property, limit.BaseLowLowLimit) ||
                    ReferenceEquals(property, limit.SeverityHighHigh) ||
                    ReferenceEquals(property, limit.SeverityHigh) ||
                    ReferenceEquals(property, limit.SeverityLow) ||
                    ReferenceEquals(property, limit.SeverityLowLow) ||
                    ReferenceEquals(property, limit.HighHighDeadband) ||
                    ReferenceEquals(property, limit.HighDeadband) ||
                    ReferenceEquals(property, limit.LowDeadband) ||
                    ReferenceEquals(property, limit.LowLowDeadband));
        }

        private static bool IsRetainedCondition(WotCapturedEvent? captured)
        {
            if (captured is not { HasConditionId: true, HasBranchId: true, HasEventType: true, HasTime: true })
            {
                return false;
            }
            for (int index = 0; index < captured.Clauses.Count; index++)
            {
                WotResolvedEventSelectClause clause = captured.Clauses[index];
                if (clause.TypeDefinitionId == WotCapturedEvent.ConditionTypeId &&
                    clause.BrowsePath == Ua.BrowseNames.Retain)
                {
                    return captured.Fields[index].TryGetValue(out bool retained) && retained;
                }
            }
            return false;
        }

        private static bool SameCapturedOccurrence(WotCapturedEvent? previous, WotCapturedEvent? current)
        {
            if (previous is null)
            {
                return current is null;
            }
            if (current is null || !ReferenceEquals(previous.Source, current.Source) ||
                previous.Clauses.Count != current.Clauses.Count)
            {
                return false;
            }
            for (int index = 0; index < previous.Clauses.Count; index++)
            {
                WotResolvedEventSelectClause clause = previous.Clauses[index];
                if (!clause.Equals(current.Clauses[index]) ||
                    (clause.BrowsePath != Ua.BrowseNames.ReceiveTime &&
                        !previous.Fields[index].Equals(current.Fields[index])))
                {
                    return false;
                }
            }
            return true;
        }

        private void PopulateCapturedFields(
            WotProjectedEventState result, ConditionState? condition,
            WotCapturedEvent captured, ExpandedNodeId branchId)
        {
            for (int index = 0; index < captured.Clauses.Count; index++)
            {
                WotResolvedEventSelectClause clause = captured.Clauses[index];
                if (clause.IsConditionIdSelection || !IsApplicableCapturedClause(clause))
                {
                    continue;
                }
                Variant value = captured.Fields[index];
                if (value.IsNull)
                {
                    if (IdentityMode == WoTEventIdentityModeEnum.LocalReEmission &&
                        clause.TypeDefinitionId == WotEventSelectClauses.BaseEventTypeId && clause.BrowsePath is
                            Ua.BrowseNames.EventId or Ua.BrowseNames.EventType or Ua.BrowseNames.SourceNode or
                            Ua.BrowseNames.Time or Ua.BrowseNames.ReceiveTime)
                    {
                        continue;
                    }
                    throw new ServiceResultException(
                        StatusCodes.BadNoData, "The source did not supply a required Core event field: " + clause);
                }
                ArrayOf<QualifiedName> path = clause.PathElements.ConvertAll(
                    element => WotBindingValueMapper.ResolveBrowseName(element, m_context.NamespaceUris));
                Variant mapped = WotBindingValueMapper.Translate(
                    value, captured.Source.Context, m_valueContext, allowNamespaceGrowth: true);
                var field = new DataValue(mapped, StatusCodes.Good, captured.Time, captured.ReceiveTime);
                PopulateField(result, condition, path, field, branchId);
            }
        }

        private bool IsApplicableCapturedClause(WotResolvedEventSelectClause clause)
        {
            if (clause.TypeDefinitionId == WotCapturedEvent.ConditionTypeId)
            {
                return IsCondition;
            }
            if (clause.TypeDefinitionId == WotCapturedEvent.AcknowledgeableConditionTypeId)
            {
                return IsCondition && m_context.TypeTable.IsTypeOf(
                    EventTypeId, Ua.ObjectTypeIds.AcknowledgeableConditionType);
            }
            if (clause.TypeDefinitionId == WotCapturedEvent.AlarmConditionTypeId)
            {
                return IsCondition && m_context.TypeTable.IsTypeOf(EventTypeId, Ua.ObjectTypeIds.AlarmConditionType);
            }
            return true;
        }

        private bool HasCapturedField(WotCapturedEvent? captured, ArrayOf<QualifiedName> path)
        {
            if (captured is null || path.Contains(name => name.NamespaceIndex != 0))
            {
                return false;
            }
            for (int index = 0; index < captured.Clauses.Count; index++)
            {
                WotResolvedEventSelectClause clause = captured.Clauses[index];
                ArrayOf<string> capturedPath = clause.PathElements;
                if (!IsApplicableCapturedClause(clause) ||
                    captured.Fields[index].IsNull || path.Count != capturedPath.Count)
                {
                    continue;
                }
                int element = 0;
                while (element < path.Count && path[element].Name == capturedPath[element])
                {
                    element++;
                }
                if (element == path.Count)
                {
                    return true;
                }
            }
            return false;
        }

        private void PopulateField(
            WotProjectedEventState result, ConditionState? condition,
            ArrayOf<QualifiedName> path, in DataValue value, ExpandedNodeId branchId)
        {
            Variant mapped = value.WrappedValue;
            if (IdentityMode == WoTEventIdentityModeEnum.LocalReEmission &&
                path.Count == 1 && path[0] == QualifiedName.From(Ua.BrowseNames.BranchId))
            {
                mapped = new Variant(branchId.IsNull ? NodeId.Null :
                    new NodeId(result.NodeId + "#Branch:" + branchId, EventTypeId.NamespaceIndex));
            }
            SetField(result, path, mapped, value.StatusCode, value.SourceTimestamp);
            var projectedValue = new DataValue(mapped, value.StatusCode, value.SourceTimestamp, value.ServerTimestamp);
            result.AddField(path, projectedValue);
            if (condition is not null && branchId.IsNull)
            {
                SetField(condition, path, mapped, value.StatusCode, value.SourceTimestamp);
            }
        }

        private void AdmitTransparentOccurrence(WotCapturedEvent? captured)
        {
            ValidatePreparedSource();
            if (m_preparedSource is not null && !ReferenceEquals(captured?.Source, m_preparedSource))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The event did not originate from its prepared source binding.");
            }
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

        private sealed record PreparedOccurrence(
            Occurrence Occurrence,
            ByteString LocalEventId,
            WotCapturedEvent? Captured,
            ArrayOf<DataValue> Fields,
            DateTimeUtc ReceiveTime)
        {
            public bool IsRetainedRefresh { get; init; }

            public bool OwnsTransparentReservation { get; set; }

            public EventManager.EventIdentityReservation? IdentityReservation { get; set; }
        }

        private sealed class ProjectedIdentitySource(WotCapturedEvent? captured, bool transparent)
            : IEventIdentitySource
        {
            private WotCapturedEvent? Captured { get; } = captured;

            private bool Transparent { get; } = transparent;

            public bool IsSameOccurrence(IEventIdentitySource other)
            {
                return ReferenceEquals(this, other) ||
                    (Transparent && Captured is not null &&
                        other is ProjectedIdentitySource { Transparent: true } projected &&
                        ReferenceEquals(Captured, projected.Captured));
            }
        }

        private readonly ISystemContext m_context;
        private readonly int m_maxRoutes;
        private readonly TimeProvider m_timeProvider;
        private readonly WotProjectedEventRouteRegistry m_routeRegistry;
        private readonly IServiceMessageContext m_valueContext;
        private readonly ArrayOf<Field> m_fields;
        private readonly ArrayOf<string> m_eventIdMemberPath;
        private readonly Func<NodeId, CancellationToken, ValueTask<ConditionState>>? m_createCondition;
        private readonly bool m_nativePublication;
        private readonly EventManager? m_eventManager;
        private readonly bool m_requireServerIdentityAdmission;
        private readonly Lock m_gate = new();
        private readonly Dictionary<ExpandedNodeId, Task<ConditionState>> m_conditions = [];
        private readonly Dictionary<ByteString, Occurrence> m_routes = [];
        private readonly Dictionary<ByteString, ByteString> m_sourceEventIds = [];
        private readonly HashSet<ByteString> m_rejectedEvents = [];
        private readonly Dictionary<string, WotCapturedConditionAction> m_actions = [];
        private TaskCompletionSource<bool>? m_pendingProjection;
        private bool m_disposed;
        private static readonly ArrayOf<string> s_conditionMethods =
            ["Enable", "Disable", "AddComment", "Acknowledge", "Confirm"];
    }
}
