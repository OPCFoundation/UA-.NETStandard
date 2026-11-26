// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ManagedMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItem;
using ManagedMonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Subscriptions
{
    internal abstract partial class Subscription
    {
        /// <summary>
        /// Whether recovery refresh was enabled when this partition was registered.
        /// </summary>
        internal bool RefreshValuesOnRecovery => m_valueRefresh != null;

        /// <summary>
        /// Installs the opt-in tracking before monitored-item processing starts.
        /// </summary>
        internal void EnableValueRefresh(SubscriptionValueSnapshotRegistration registration)
        {
            m_valueRefresh = new SubscriptionValueRefresh(registration);
            m_monitoredItems.EnableValueRefresh(m_valueRefresh);
        }

        /// <summary>
        /// Coalesces recovery requests into a single worker for this partition.
        /// </summary>
        internal void ScheduleValueRefresh(ISessionClient session, uint id, long generation, Func<bool> isCurrent)
        {
            SubscriptionValueRefresh? refresh = m_valueRefresh;
            if (refresh == null ||
                !refresh.Enqueue(new(session, id, generation, isCurrent)))
            {
                return;
            }
            m_backgroundWork.Run(nameof(RefreshValuesAsync), async ct =>
            {
                while (refresh.TryTake(out SubscriptionValueRefresh.Request request))
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        await RefreshValuesAsync(refresh, request, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Logger.SubscriptionRecoveryReadFailed(ex, request.Id);
                    }
                }
            });
        }

        private ReadOnlyMemory<DataValueChange> CreateTrackedDataChanges(
            DataChangeNotification data, AcknowledgementHandle handle)
        {
            ReadOnlyMemory<DataValueChange> changes = m_monitoredItems.CreateNotification(data, handle);
            m_valueRefresh?.Observe(changes.Span);
            return changes;
        }

        private async ValueTask RefreshValuesAsync(
            SubscriptionValueRefresh refresh, SubscriptionValueRefresh.Request request, CancellationToken ct)
        {
            if (!IsCurrent())
            {
                return;
            }
            try
            {
                ArrayOf<ServiceResult> results = await request.Session.ResendDataAsync([request.Id], ct)
                    .ConfigureAwait(false);
                if (ServiceResult.IsGood(results[0]))
                {
                    return;
                }
                Logger.SubscriptionRecoveryResendRejected(request.Id, results[0].StatusCode);
            }
            catch (ServiceResultException ex)
            {
                Logger.SubscriptionRecoveryResendRejected(request.Id, ex.StatusCode);
            }

            ct.ThrowIfCancellationRequested();
            if (!IsCurrent())
            {
                return;
            }
            refresh.Capture(out ArrayOf<ReadValueId> nodes, out List<SubscriptionValueRefresh.Candidate> candidates);
            if (nodes.Count == 0)
            {
                return;
            }
            ReadResponse response = await request.Session.ReadAsync(null, 0, TimestampsToReturn.Both, nodes, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodes);
            if (!IsCurrent())
            {
                return;
            }
            await DispatchValueSnapshotsAsync(() => refresh.Registration.DispatchAsync(DeliverAsync, ct), ct)
                .ConfigureAwait(false);

            bool IsCurrent() => !IsShuttingDown && CurrentPublishingEnabled && Id == request.Id &&
                MessageGeneration == request.Generation && request.IsCurrent();

            async ValueTask DeliverAsync()
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsCurrent())
                    {
                        return;
                    }
                    DataValue value = response.Results[i];
                    SubscriptionValueRefresh.Candidate candidate = candidates[i];
                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        Logger.SubscriptionRecoveryReadRejected(request.Id, candidate.ServerId, value.StatusCode);
                    }
                    else if (refresh.TryAccept(candidate, value))
                    {
                        try
                        {
                            await refresh.Registration.DeliverAsync(candidate.Item, value, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException &&
                            !(ex is OperationCanceledException && ct.IsCancellationRequested))
                        {
                            Logger.SubscriptionRecoverySnapshotHandlerFailed(ex, request.Id);
                        }
                    }
                }
            }
        }

        private SubscriptionValueRefresh? m_valueRefresh;
    }

    /// <summary>
    /// Tracks applied targets and freshness without keeping copies of value payloads.
    /// </summary>
    internal sealed class SubscriptionValueRefresh(SubscriptionValueSnapshotRegistration registration)
    {
        /// <summary>
        /// The logical callback destination shared by the partitions.
        /// </summary>
        public SubscriptionValueSnapshotRegistration Registration { get; } = registration;

        /// <summary>
        /// Replaces the pending request; returns true if a worker needs to be started.
        /// </summary>
        public bool Enqueue(Request request)
        {
            lock (m_lock)
            {
                m_pending = request;
                if (m_running)
                {
                    return false;
                }
                m_running = true;
                return true;
            }
        }

        /// <summary>
        /// Takes the latest pending request or releases ownership of the worker.
        /// </summary>
        public bool TryTake(out Request request)
        {
            lock (m_lock)
            {
                if (m_pending is { } next)
                {
                    request = next;
                    m_pending = null;
                    return true;
                }
                m_running = false;
                request = default;
                return false;
            }
        }

        /// <summary>
        /// Records the target accepted by CreateMonitoredItems.
        /// </summary>
        public void SetTarget(ManagedMonitoredItem item, ReadValueId target)
        {
            lock (m_lock)
            {
                if (target.AttributeId == Attributes.Value && !target.NodeId.IsNull)
                {
                    m_items[item] = new ItemState(target, item.ServerId);
                }
            }
        }

        /// <summary>
        /// Drops state when an item or its server-side binding is removed.
        /// </summary>
        public void Remove(ManagedMonitoredItem item)
        {
            lock (m_lock)
            {
                m_items.Remove(item);
            }
        }

        /// <summary>
        /// Invalidates the previous value state after a change in monitoring mode.
        /// </summary>
        public void ResetValue(ManagedMonitoredItem item)
        {
            lock (m_lock)
            {
                if (m_items.TryGetValue(item, out ItemState? state))
                {
                    state.Revision++;
                    state.ValueTime = null;
                }
            }
        }

        /// <summary>
        /// Records the latest value seen by ordinary notification dispatch.
        /// </summary>
        public void Observe(ReadOnlySpan<DataValueChange> changes)
        {
            lock (m_lock)
            {
                foreach (ref readonly DataValueChange change in changes)
                {
                    if (change.MonitoredItem is ManagedMonitoredItem item &&
                        m_items.TryGetValue(item, out ItemState? state) && IsReporting(item, state.ServerId))
                    {
                        state.Revision++;
                        state.ValueTime = ValueTime.From(change.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Captures the applied Read targets and their freshness revisions.
        /// </summary>
        public void Capture(out ArrayOf<ReadValueId> nodes, out List<Candidate> candidates)
        {
            lock (m_lock)
            {
                var targets = new List<ReadValueId>();
                candidates = [];
                foreach (KeyValuePair<ManagedMonitoredItem, ItemState> pair in m_items)
                {
                    ManagedMonitoredItem item = pair.Key;
                    ItemState state = pair.Value;
                    if (IsReporting(item, state.ServerId))
                    {
                        targets.Add(state.Target);
                        candidates.Add(new Candidate(item, item.ClientHandle, state.ServerId,
                            item.OptionsRevision, state.Target, state.Revision));
                    }
                }
                nodes = targets.ToArrayOf();
            }
        }

        /// <summary>
        /// Accepts only a snapshot that is current for the captured item and fresher than its last observed value.
        /// </summary>
        public bool TryAccept(Candidate candidate, in DataValue value)
        {
            lock (m_lock)
            {
                ManagedMonitoredItem item = candidate.Item;
                if (!m_items.TryGetValue(item, out ItemState? state) ||
                    !IsReporting(item, candidate.ServerId) || item.ClientHandle != candidate.ClientHandle ||
                    !ReferenceEquals(item.OptionsRevision, candidate.Options) ||
                    !ReferenceEquals(state.Target, candidate.Target) || state.Revision != candidate.Revision)
                {
                    return false;
                }
                ValueTime time = ValueTime.From(value);
                if (state.ValueTime is { } previous && !time.IsNewerThan(previous))
                {
                    return false;
                }
                state.Revision++;
                state.ValueTime = time;
                return true;
            }
        }

        private static bool IsReporting(ManagedMonitoredItem item, uint serverId)
        {
            return serverId != 0 && item.ServerId == serverId && item.CurrentMonitoringMode == MonitoringMode.Reporting;
        }

        /// <summary>
        /// A queued recovery pass tied to the current session and partition binding.
        /// </summary>
        internal readonly record struct Request(ISessionClient Session, uint Id, long Generation, Func<bool> IsCurrent);

        /// <summary>
        /// The item binding and revision to revalidate before delivering the result.
        /// </summary>
        internal readonly record struct Candidate(
            ManagedMonitoredItem Item, uint ClientHandle, uint ServerId, ManagedMonitoredItemOptions? Options,
            ReadValueId Target, long Revision);

        private sealed class ItemState(ReadValueId target, uint serverId)
        {
            public ReadValueId Target { get; } = target;
            public uint ServerId { get; } = serverId;
            public ValueTime? ValueTime { get; set; }
            public long Revision { get; set; }
        }

        private readonly record struct ValueTime(
            DateTimeUtc Source, ushort SourcePicoseconds, DateTimeUtc Server, ushort ServerPicoseconds)
        {
            public static ValueTime From(in DataValue value)
            {
                return new(value.SourceTimestamp, value.SourcePicoseconds,
                    value.ServerTimestamp, value.ServerPicoseconds);
            }

            public bool IsNewerThan(ValueTime previous)
            {
                if (Source != DateTimeUtc.MinValue && previous.Source != DateTimeUtc.MinValue)
                {
                    return Source > previous.Source ||
                        (Source == previous.Source && SourcePicoseconds > previous.SourcePicoseconds);
                }
                return Server != DateTimeUtc.MinValue && previous.Server != DateTimeUtc.MinValue &&
                    (Server > previous.Server ||
                        (Server == previous.Server && ServerPicoseconds > previous.ServerPicoseconds));
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<ManagedMonitoredItem, ItemState> m_items = [];
        private Request? m_pending;
        private bool m_running;
    }

    /// <summary>
    /// Diagnostics for best-effort value refresh during subscription recovery.
    /// </summary>
    internal static partial class SubscriptionValueRefreshLog
    {
        [LoggerMessage(EventId = ClientEventIds.SubscriptionValueRefresh, Level = LogLevel.Debug,
            Message = "ResendData failed for subscription {SubscriptionId} with {Status}. Reading current values.")]
        public static partial void SubscriptionRecoveryResendRejected(
            this ILogger logger, uint subscriptionId, StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionValueRefresh + 1, Level = LogLevel.Warning,
            Message = "Could not refresh recovery values for subscription {SubscriptionId}.")]
        public static partial void SubscriptionRecoveryReadFailed(
            this ILogger logger, Exception exception, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionValueRefresh + 2, Level = LogLevel.Debug,
            Message = "Recovery Read for subscription {SubscriptionId} item {MonitoredItemId} returned {Status}.")]
        public static partial void SubscriptionRecoveryReadRejected(
            this ILogger logger, uint subscriptionId, uint monitoredItemId, StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionValueRefresh + 3, Level = LogLevel.Warning,
            Message = "The recovery snapshot handler failed for subscription {SubscriptionId}.")]
        public static partial void SubscriptionRecoverySnapshotHandlerFailed(
            this ILogger logger, Exception exception, uint subscriptionId);
    }
}
