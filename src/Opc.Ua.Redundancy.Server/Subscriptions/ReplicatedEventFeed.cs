// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Commits exact occurrences before publishing them, and replays them on a redundant server set.
    /// </summary>
    public sealed class ReplicatedEventFeed :
        IReplicatedConditionMethodRelay, IServerStartupTask, IServerPreStartupTask, IAsyncDisposable
    {
        /// <summary>
        /// Creates an event feed over the enrolled store and the election that owns its committed write grant.
        /// </summary>
        public ReplicatedEventFeed(
            RedundancySupport mode,
            Uuid groupId,
            FreshnessProtectedKeyValueStore store,
            ILeaderElection election,
            IReplicatedEventSource source,
            IServiceLevelProvider serviceLevelProvider,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_fences = election as ISharedStoreWriteFenceSource ??
                throw new ArgumentException("The event feed needs a committed writer grant.", nameof(election));
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            m_serviceLevel = serviceLevelProvider as LeaderServiceLevelProvider ??
                throw new ArgumentException("The event feed requires the built-in ServiceLevel policy.",
                    nameof(serviceLevelProvider));
            m_logger = (telemetry ?? throw new ArgumentNullException(nameof(telemetry)))
                .CreateLogger<ReplicatedEventFeed>();
            m_time = timeProvider ?? TimeProvider.System;
            if (!store.SupportsGuardedDeadlines)
            {
                throw new ArgumentException("The event feed needs atomic deadline-guarded commits.", nameof(store));
            }
            m_groupId = groupId;
            m_keyPrefix = "event-feed/" + groupId.Guid.ToString("D", CultureInfo.InvariantCulture) + "/";
            m_reporter = new ReplicatedEventReporter(mode, groupId, this);
        }

        /// <summary>
        /// Gets whether this replica caught up under its current committed writer acquisition.
        /// </summary>
        public bool IsReady
        {
            get
            {
                Grant? grant = Volatile.Read(ref m_readyGrant);
                return grant != null && !grant.Read().Value.IsNull;
            }
        }

        /// <summary>
        /// Registers a stable notifier before startup replay runs.
        /// </summary>
        public void BindNotifier(NodeState notifier)
        {
            if (notifier == null)
            {
                throw new ArgumentNullException(nameof(notifier));
            }
            if (notifier.NodeId.IsNull || !m_notifiers.TryAdd(notifier.NodeId, notifier))
            {
                throw new ArgumentException("An unused stable notifier NodeId is required.", nameof(notifier));
            }
        }

        /// <summary>
        /// Registers a condition and its standard methods before startup replay runs.
        /// </summary>
        public void BindCondition(AcknowledgeableConditionState condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }
            if (condition.NodeId.IsNull || !m_conditions.TryAdd(condition.NodeId, condition))
            {
                throw new ArgumentException("An unused stable condition NodeId is required.", nameof(condition));
            }
            try
            {
                m_reporter.BindConditionMethods(condition);
            }
            catch
            {
                m_conditions.TryRemove(condition.NodeId, out _);
                throw;
            }
        }

        /// <summary>
        /// Commits and publishes an ordinary event at the writer; the trusted caller supplies the occurrence UUID.
        /// </summary>
        public ValueTask<ByteString> PublishEventAsync(
            NodeId notifierId, Uuid occurrenceId, Variant payload, CancellationToken cancellationToken = default)
        {
            return PublishAsync(
                new Entry(Kind.Ordinary, occurrenceId, notifierId, default, payload), cancellationToken);
        }

        /// <summary>
        /// Commits a native ordinary event and publishes it using the shared occurrence ID.
        /// </summary>
        /// <remarks>
        /// Supply a stable occurrence UUID from the authoritative producer and reuse it on retry.
        /// The event and its field values must remain unchanged during this call.
        /// A local EventId is ignored and the original event is not modified.
        /// </remarks>
        public ValueTask<ByteString> PublishNativeEventAsync(
            NodeId notifierId,
            Uuid occurrenceId,
            BaseEventState eventState,
            CancellationToken cancellationToken = default)
        {
            if (eventState == null)
            {
                throw new ArgumentNullException(nameof(eventState));
            }
            if (eventState is ConditionState ||
                eventState.Clone() is not BaseEventState snapshot || snapshot.GetType() != eventState.GetType())
            {
                throw new ArgumentException("Expected a cloneable ordinary event.", nameof(eventState));
            }
            if (occurrenceId == Guid.Empty || !m_notifiers.ContainsKey(notifierId))
            {
                throw new ArgumentException("A bound source and non-empty occurrence ID are required.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            IServerContext server = m_server ??
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The event feed has not started.");
            snapshot.EventId = null;
            NameBaseEventFields(snapshot);
            Variant payload = Variant.From(NodeStateSerializer.Serialize(server.DefaultSystemContext, snapshot));
            ReadNativeEvent(server.DefaultSystemContext, payload);
            return PublishAsync(
                new Entry(Kind.NativeOrdinary, occurrenceId, notifierId, default, payload), cancellationToken);
        }

        /// <summary>
        /// Connects a fluent native event stream to this feed instead of local event reporting.
        /// </summary>
        /// <remarks>
        /// Bind the notifier before server startup. Run the stream on the ready writer only, and use
        /// occurrence UUIDs and timestamps assigned by the authoritative event source. The stream
        /// runs without subscribers so an occurrence can survive a failover before a client connects.
        /// </remarks>
        public EventPublishOptions CreateNativeEventPublishOptions(
            Func<BaseEventState, Uuid> getOccurrenceId,
            EventPublishOptions? options = null)
        {
            if (getOccurrenceId == null)
            {
                throw new ArgumentNullException(nameof(getOccurrenceId));
            }
            if (options?.DispatchAsync != null)
            {
                throw new ArgumentException("The event stream already has a dispatcher.", nameof(options));
            }

            return (options ?? new EventPublishOptions()) with
            {
                AlwaysOn = true,
                DispatchAsync = async (notifier, state, ct) =>
                    await PublishNativeEventAsync(notifier.NodeId, getOccurrenceId(state), state, ct)
                        .ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Commits and publishes an application condition update, with a stable BranchId for a branch update.
        /// </summary>
        public ValueTask<ByteString> PublishConditionAsync(
            NodeId conditionId, NodeId branchId, Uuid occurrenceId, Variant payload,
            CancellationToken cancellationToken = default)
        {
            return PublishAsync(
                new Entry(Kind.Condition, occurrenceId, conditionId, branchId, payload), cancellationToken);
        }

        /// <summary>
        /// Commits the variable values of a detached native condition state and publishes the resulting occurrence.
        /// </summary>
        /// <remarks>
        /// Prepare the state without changing the bound condition. Use the same condition type, field layout,
        /// occurrence UUID and values on retry. Only field values and status codes are applied; methods,
        /// permissions, references and the live condition's identity remain local. The feed assigns EventId
        /// and BranchId before rebuilding the acknowledgement and confirmation associations.
        /// </remarks>
        public ValueTask<ByteString> PublishNativeConditionAsync(
            NodeId conditionId,
            NodeId branchId,
            Uuid occurrenceId,
            AcknowledgeableConditionState detachedState,
            CancellationToken cancellationToken = default)
        {
            if (detachedState == null)
            {
                throw new ArgumentNullException(nameof(detachedState));
            }
            if (occurrenceId == Guid.Empty ||
                !m_conditions.TryGetValue(conditionId, out AcknowledgeableConditionState? root) ||
                ReferenceEquals(root, detachedState) ||
                detachedState.Time is not { Value.IsNull: false })
            {
                throw new ArgumentException("Expected a bound condition, a detached state, a time and an occurrence ID.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            IServerContext server = m_server ??
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The event feed has not started.");
            ISystemContext context = server.DefaultSystemContext;
            if (detachedState.Clone() is not AcknowledgeableConditionState snapshot ||
                snapshot.GetType() != detachedState.GetType())
            {
                throw new ArgumentException("Expected a cloneable condition state.", nameof(detachedState));
            }
            snapshot.EventId = null;
            snapshot.BranchId = null;
            ByteString payload = NodeStateSerializer.Serialize(context, snapshot);
            NativeConditionFields.ReadUpdates(context, root, payload);
            return PublishAsync(
                new Entry(Kind.NativeCondition, occurrenceId, conditionId, branchId, Variant.From(payload)),
                cancellationToken);
        }

        /// <summary>
        /// Commits retirement of a branch after its final event has been committed.
        /// </summary>
        public async ValueTask RetireBranchAsync(
            NodeId conditionId, NodeId branchId, Uuid recordId, CancellationToken cancellationToken = default)
        {
            if (branchId.IsNull)
            {
                throw new ArgumentException("The condition trunk cannot be retired as a branch.", nameof(branchId));
            }
            await PublishAsync(new Entry(Kind.Retire, recordId, conditionId, branchId, Variant.Null), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartingAsync(
            IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (server is ServerInternalData native)
            {
                native.SetInitialServiceLevel(ServiceLevels.NoData);
            }
            m_serviceLevel.SetEventReadiness(() => IsReady);
            await m_store.VerifyEpochAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask OnServerStartedAsync(
            IServerContext server, CancellationToken cancellationToken = default)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            await m_store.VerifyEpochAsync(cancellationToken).ConfigureAwait(false);
            await CatchUpAsync(publish: false, cancellationToken).ConfigureAwait(false);
            m_worker = PollAsync(m_stop.Token);
        }

        /// <inheritdoc/>
        async ValueTask<ServiceResult> IReplicatedConditionMethodRelay.CallAsync(
            Uuid groupId,
            ISystemContext context,
            NodeId conditionId,
            ReplicatedConditionMethod method,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken cancellationToken)
        {
            if (groupId != m_groupId || !m_conditions.TryGetValue(conditionId, out AcknowledgeableConditionState? node))
            {
                return StatusCodes.BadEventIdUnknown;
            }
            if (method is not (ReplicatedConditionMethod.Acknowledge or ReplicatedConditionMethod.Confirm))
            {
                return StatusCodes.BadMethodInvalid;
            }

            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Grant grant = RequireReady();
                await ReplayAsync(true, cancellationToken).ConfigureAwait(false);
                bool confirm = method == ReplicatedConditionMethod.Confirm;
                ServiceResult validation = node.ValidateReplicatedMethod(
                    eventId, confirm, out AcknowledgeableConditionState? target);
                if (ServiceResult.IsBad(validation))
                {
                    return validation;
                }
                if (target == null)
                {
                    return StatusCodes.BadEventIdUnknown;
                }
                if (confirm
                    ? node.OnConfirm != null || target.OnConfirm != null
                    : node.OnAcknowledge != null || target.OnAcknowledge != null)
                {
                    return ServiceResult.Create(StatusCodes.BadConfigurationError,
                        "A native condition callback has not been migrated to the replicated authorization policy.");
                }

                ServiceResult permitted;
                try
                {
                    permitted = m_source.AuthorizeConditionMethod(context, target, method, eventId, comment);
                }
                catch (Exception ex) when (ex is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
                {
                    return ServiceResult.Create(ex, StatusCodes.BadUnexpectedError, confirm
                        ? "Unexpected error confirming a Condition."
                        : "Unexpected error acknowledging a Condition.");
                }
                if (!ServiceResult.IsGood(permitted))
                {
                    return permitted;
                }

                var entry = new Entry(confirm ? Kind.Confirm : Kind.Acknowledge, Uuid.NewUuid(),
                    conditionId, target.BranchId!.Value, Variant.Null, comment, context.UserId,
                    new DateTimeUtc(m_time.GetUtcNow()));
                await CommitAsync(entry, grant, cancellationToken).ConfigureAwait(false);
                return ServiceResult.Good;
            }
            finally
            {
                m_serial.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposing, 1) != 0)
            {
                return;
            }
            Volatile.Write(ref m_readyGrant, null);
            await m_stop.CancelAsync().ConfigureAwait(false);
            if (m_worker != null)
            {
                await m_worker.ConfigureAwait(false);
            }
            Volatile.Write(ref m_readyGrant, null);
            m_stop.Dispose();
            await m_serial.WaitAsync().ConfigureAwait(false);
            m_serial.Dispose();
        }

        /// <summary>
        /// Applies the complete visible prefix and only then marks the captured acquisition ready.
        /// </summary>
        internal async ValueTask CatchUpAsync(bool publish, CancellationToken cancellationToken)
        {
            await m_serial.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Grant? grant = m_fences.TryCaptureWriteFence(
                    m_store, out string key, out Func<SharedStoreWriteGrant>? read) ? new Grant(key, read) : null;
                await ReplayAsync(publish, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_readyGrant, m_next <= kMaxEntries && grant != null &&
                    !grant.Read().Value.IsNull ? grant : null);
            }
            catch
            {
                Volatile.Write(ref m_readyGrant, null);
                throw;
            }
            finally
            {
                m_serial.Release();
            }
        }

        private async ValueTask<ByteString> PublishAsync(Entry entry, CancellationToken ct)
        {
            if (entry.OccurrenceId == Guid.Empty ||
                (IsOrdinary(entry.Type)
                    ? !m_notifiers.ContainsKey(entry.SourceId)
                    : !m_conditions.ContainsKey(entry.SourceId)))
            {
                throw new ArgumentException("A bound source and non-empty occurrence ID are required.");
            }

            await m_serial.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Grant grant = RequireReady();
                await ReplayAsync(true, ct).ConfigureAwait(false);
                await CommitAsync(entry, grant, ct).ConfigureAwait(false);
                return m_reporter.GetEventId(new ReplicatedEventIdentity(m_groupId, entry.OccurrenceId));
            }
            finally
            {
                m_serial.Release();
            }
        }

        private async ValueTask CommitAsync(Entry entry, Grant grant, CancellationToken ct)
        {
            ByteString encoded = Encode(entry);
            if (m_seen.TryGetValue(entry.OccurrenceId, out ByteString existing))
            {
                if (existing == Digest(encoded))
                {
                    return;
                }
                throw new ServiceResultException(
                    StatusCodes.BadEventIdUnknown, "The occurrence UUID was already used.");
            }
            if (m_next > kMaxEntries)
            {
                throw new ServiceResultException(
                    StatusCodes.BadResourceUnavailable, "The retained event feed is full.");
            }

            try
            {
                Entry committed = Decode(encoded);
                SharedStoreWriteGrant bound = grant.Read();
                if (bound.Value.IsNull || !await m_store.CompareAndSwapBeforeAsync(
                    NextKey(), default, encoded, grant.Key, bound.Value, bound.Deadline, ct).ConfigureAwait(false))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotWritable, "The event feed lost its writer fence.");
                }
                await ApplyAsync(committed, encoded, publish: true).ConfigureAwait(false);
                if (m_next > kMaxEntries)
                {
                    Volatile.Write(ref m_readyGrant, null);
                }
            }
            catch
            {
                Volatile.Write(ref m_readyGrant, null);
                throw;
            }
        }

        private async ValueTask ReplayAsync(bool publish, CancellationToken ct)
        {
            if (m_applyFailed)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "Restart the replica after repairing the failed event application.");
            }
            try
            {
                while (true)
                {
                    (bool found, ByteString value) = await m_store.TryGetRetainedAsync(NextKey(), ct)
                        .ConfigureAwait(false);
                    if (!found)
                    {
                        return;
                    }
                    await ApplyAsync(Decode(value), value, publish).ConfigureAwait(false);
                }
            }
            catch
            {
                Volatile.Write(ref m_readyGrant, null);
                throw;
            }
        }

        private async ValueTask ApplyAsync(Entry entry, ByteString encoded, bool publish)
        {
            try
            {
                ISystemContext context = m_server!.DefaultSystemContext;
                var identity = new ReplicatedEventIdentity(m_groupId, entry.OccurrenceId);
                if (m_seen.ContainsKey(entry.OccurrenceId))
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Duplicate committed occurrence.");
                }
                if (IsOrdinary(entry.Type))
                {
                    NodeState notifier = m_notifiers[entry.SourceId];
                    if (publish)
                    {
                        BaseEventState state = entry.Type == Kind.NativeOrdinary
                            ? ReadNativeEvent(context, entry.Payload)
                            : m_source.CreateEvent(context, notifier, entry.Payload);
                        await m_reporter.ReportEventAsync(context, notifier, state, identity).ConfigureAwait(false);
                    }
                }
                else
                {
                    AcknowledgeableConditionState root = m_conditions[entry.SourceId];
                    if (entry.Type != Kind.Retire)
                    {
                        await m_reporter.ApplyConditionOccurrenceAsync(context, root, entry.BranchId, identity,
                            target => ApplyCondition(context, (AcknowledgeableConditionState)target, entry), publish)
                            .ConfigureAwait(false);
                    }
                    if (!entry.BranchId.IsNull)
                    {
                        if (entry.Type is Kind.Confirm or Kind.Retire ||
                            (entry.Type == Kind.Acknowledge && !root.SupportsConfirm()))
                        {
                            m_reporter.RetireConditionBranch(root, entry.BranchId);
                        }
                        root.RefreshReplicatedRetain();
                    }
                }
                m_seen.Add(entry.OccurrenceId, Digest(encoded));
                m_next++;
            }
            catch
            {
                // A notification or application callback can fail after modifying live condition state.
                m_applyFailed = true;
                Volatile.Write(ref m_readyGrant, null);
                throw;
            }
        }

        private static bool IsOrdinary(Kind kind)
        {
            return kind is Kind.Ordinary or Kind.NativeOrdinary;
        }

        private static BaseEventState ReadNativeEvent(ISystemContext context, Variant payload)
        {
            if (!payload.TryGetValue(out ByteString encoded) || encoded.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid native event payload.");
            }
            BaseEventState state = NodeStateSerializer.DeserializeEvent(context, encoded);
            if (state.EventType is not { Value.IsNull: false } ||
                state.Time is not { Value.IsNull: false } ||
                state.ReceiveTime is not { Value.IsNull: false })
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "A native event requires an EventType and authoritative timestamps.");
            }
            return state;
        }

        private static void NameBaseEventFields(BaseEventState snapshot)
        {
            Name(snapshot.EventType, BrowseNames.EventType);
            Name(snapshot.SourceNode, BrowseNames.SourceNode);
            Name(snapshot.SourceName, BrowseNames.SourceName);
            Name(snapshot.Time, BrowseNames.Time);
            Name(snapshot.ReceiveTime, BrowseNames.ReceiveTime);
            Name(snapshot.Severity, BrowseNames.Severity);
            Name(snapshot.Message, BrowseNames.Message);

            static void Name(BaseInstanceState? field, string name)
            {
                if (field is { BrowseName.IsNull: true })
                {
                    field.BrowseName = QualifiedName.From(name);
                }
            }
        }

        private void ApplyCondition(ISystemContext context, AcknowledgeableConditionState target, Entry entry)
        {
            if (entry.Type == Kind.NativeCondition)
            {
                if (!entry.Payload.TryGetValue(out ByteString payload) || payload.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid native condition payload.");
                }
                foreach ((BaseVariableState field, BaseVariableState saved) in
                    NativeConditionFields.ReadUpdates(context, target, payload))
                {
                    field.WrappedValue = saved.WrappedValue;
                    field.StatusCode = saved.StatusCode;
                }
                return;
            }
            if (entry.Type == Kind.Condition)
            {
                m_source.ApplyCondition(context, target, entry.Payload);
                return;
            }

            if (entry.Type == Kind.Acknowledge)
            {
                target.SetAcknowledgedState(context, true, clearChangeMasks: false, updateRetainState: true);
                target.AckedState!.Timestamp = entry.Time;
                if (target.SupportsConfirm())
                {
                    target.SetConfirmedState(context, false);
                    target.ConfirmedState!.Timestamp = entry.Time;
                }
            }
            else
            {
                target.SetConfirmedState(context, true);
                target.ConfirmedState!.Timestamp = entry.Time;
            }
            if (!string.IsNullOrEmpty(entry.Comment.Locale) || !string.IsNullOrEmpty(entry.Comment.Text))
            {
                var normalized = new LocalizedText(entry.Comment.Locale, entry.Comment.Text ?? string.Empty);
                target.SetComment(context, normalized, entry.UserId ?? string.Empty);
                if (target.Comment?.SourceTimestamp is { } timestamp)
                {
                    timestamp.Value = entry.Time;
                }
            }
            target.Time!.Value = entry.Time;
            target.ReceiveTime!.Value = entry.Time;
        }

        private async Task PollAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await m_time.Delay(s_pollInterval, ct).ConfigureAwait(false);
                    try
                    {
                        await CatchUpAsync(true, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        m_logger.EventFeedUnavailable(ex);
                        if (m_applyFailed)
                        {
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        private Grant RequireReady()
        {
            Grant? grant = Volatile.Read(ref m_readyGrant);
            return grant != null && !grant.Read().Value.IsNull
                ? grant
                : throw new ServiceResultException(StatusCodes.BadNoData, "The event feed is not ready as writer.");
        }

        private string NextKey()
        {
            return m_keyPrefix + m_next.ToString(CultureInfo.InvariantCulture);
        }

        private ByteString Encode(Entry entry)
        {
            using var encoder = new BinaryEncoder(m_server!.MessageContext);
            encoder.WriteByte(null, 1);
            encoder.WriteByte(null, (byte)entry.Type);
            encoder.WriteGuid(null, entry.OccurrenceId);
            encoder.WriteNodeId(null, entry.SourceId);
            encoder.WriteNodeId(null, entry.BranchId);
            encoder.WriteVariant(null, entry.Payload);
            encoder.WriteLocalizedText(null, entry.Comment);
            encoder.WriteString(null, entry.UserId);
            encoder.WriteDateTime(null, entry.Time);
            ByteString value = ByteString.From(encoder.CloseAndReturnBuffer());
            if (value.Length > kMaxRecordBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return value;
        }

        private Entry Decode(ByteString value)
        {
            if (value.IsEmpty || value.Length > kMaxRecordBytes || m_next > kMaxEntries)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            using var decoder = new BinaryDecoder(value.ToArray(), m_server!.MessageContext);
            if (decoder.ReadByte(null) != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            var type = (Kind)decoder.ReadByte(null);
            Uuid id = decoder.ReadGuid(null);
            if (type is < Kind.Ordinary or > Kind.NativeCondition || id == Guid.Empty)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            var entry = new Entry(type, id, decoder.ReadNodeId(null), decoder.ReadNodeId(null),
                decoder.ReadVariant(null), decoder.ReadLocalizedText(null), decoder.ReadString(null),
                decoder.ReadDateTime(null));
            if (entry.SourceId.IsNull || decoder.Position != value.Length ||
                (type == Kind.Retire && entry.BranchId.IsNull))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            return entry;
        }

        private static ByteString Digest(ByteString value)
        {
#if NET5_0_OR_GREATER
            return ByteString.From(SHA256.HashData(value.Span));
#else
            using var hash = SHA256.Create();
            return ByteString.From(hash.ComputeHash(value.ToArray()));
#endif
        }

        private const int kMaxRecordBytes = 256 * 1024;
        private const int kMaxEntries = 100_000;
        private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(250);
        private readonly FreshnessProtectedKeyValueStore m_store;
        private readonly ISharedStoreWriteFenceSource m_fences;
        private readonly IReplicatedEventSource m_source;
        private readonly LeaderServiceLevelProvider m_serviceLevel;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_time;
        private readonly Uuid m_groupId;
        private readonly string m_keyPrefix;
        private readonly ReplicatedEventReporter m_reporter;
        private readonly ConcurrentDictionary<NodeId, NodeState> m_notifiers = new();
        private readonly ConcurrentDictionary<NodeId, AcknowledgeableConditionState> m_conditions = new();
        private readonly Dictionary<Uuid, ByteString> m_seen = [];
        private readonly SemaphoreSlim m_serial = new(1, 1);
        private readonly CancellationTokenSource m_stop = new();
        private IServerContext? m_server;
        private Task? m_worker;
        private Grant? m_readyGrant;
        private bool m_applyFailed;
        private long m_next = 1;
        private int m_disposing;

        private enum Kind : byte
        {
            Ordinary,
            Condition,
            Acknowledge,
            Confirm,
            Retire,
            NativeOrdinary,
            NativeCondition
        }

        private sealed record Grant(string Key, Func<SharedStoreWriteGrant> Read);

        private sealed record Entry(
            Kind Type, Uuid OccurrenceId, NodeId SourceId, NodeId BranchId, Variant Payload,
            LocalizedText Comment = default, string? UserId = null, DateTimeUtc Time = default);
    }

    /// <summary>
    /// Logs runtime event-feed failures.
    /// </summary>
    internal static partial class ReplicatedEventFeedLog
    {
        /// <summary>
        /// Reports that the event feed could not establish a verified prefix.
        /// </summary>
        [LoggerMessage(
            EventId = RedundancyServerEventIds.ReplicatedEventFeed, Level = LogLevel.Warning,
            Message = "The replicated event feed is unavailable.")]
        public static partial void EventFeedUnavailable(this ILogger logger, Exception exception);
    }
}
