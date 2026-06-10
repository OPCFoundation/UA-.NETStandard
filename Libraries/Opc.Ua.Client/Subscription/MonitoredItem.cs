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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// A monitored item that can be extended to add extra
    /// information as context in the subscription.
    /// </summary>
    internal abstract class MonitoredItem : IMonitoredItem, IAsyncDisposable
    {
        /// <inheritdoc/>
        public string Name { get; }

        /// <inheritdoc/>
        public uint Order => m_currentOptions?.Order ?? 0u;

        /// <inheritdoc/>
        public uint ServerId { get; private set; }

        /// <inheritdoc/>
        public bool Created => ServerId != 0;

        /// <inheritdoc/>
        public ServiceResult Error { get; private set; }

        /// <inheritdoc/>
        public MonitoringFilterResult? FilterResult { get; private set; }

        /// <inheritdoc/>
        public MonitoringMode CurrentMonitoringMode { get; internal set; }

        /// <inheritdoc/>
        public TimeSpan CurrentSamplingInterval { get; private set; }

        /// <inheritdoc/>
        public uint CurrentQueueSize { get; private set; }

        /// <inheritdoc/>
        public uint ClientHandle { get; private set; }

        /// <inheritdoc/>
        public IEnumerable<IMonitoredItem> TriggeringItems
        {
            get
            {
                IReadOnlyList<string> names = DesiredTriggeredByNames;
                if (names.Count == 0)
                {
                    yield break;
                }
                for (int i = 0; i < names.Count; i++)
                {
                    if (Context.TryGetMonitoredItemByName(
                            names[i], out IMonitoredItem? item) &&
                        item != null)
                    {
                        yield return item;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<IMonitoredItem> TriggeredItems
        {
            get
            {
                string thisName = Name;
                foreach (IMonitoredItem sibling in Context.Items)
                {
                    if (ReferenceEquals(sibling, this))
                    {
                        continue;
                    }
                    if (sibling is MonitoredItem concrete &&
                        ContainsOrdinal(concrete.DesiredTriggeredByNames,
                            thisName))
                    {
                        yield return sibling;
                    }
                }
            }
        }

        /// <summary>
        /// Stable, manager-unique names of items that trigger this item
        /// (OPC UA Part 4 §5.13.5 SetTriggering). This is the canonical
        /// runtime "desired" state, initialized from
        /// <see cref="MonitoredItemOptions.TriggeredByNames"/> at
        /// construction and mutated by both the imperative
        /// <c>SetTriggeringAsync</c> API and by subsequent options
        /// changes that touch <c>TriggeredByNames</c>. Reads return an
        /// immutable snapshot; the field is swapped atomically on
        /// every mutation so readers always see a coherent list.
        /// </summary>
        internal IReadOnlyList<string> DesiredTriggeredByNames
            => Volatile.Read(ref m_desiredTriggeredByNames);

        /// <summary>
        /// Add a triggering-item name to
        /// <see cref="DesiredTriggeredByNames"/>. Idempotent: returns
        /// <c>false</c> if the name was already present. This method is
        /// self-synchronizing via a lock-free CAS loop on
        /// <c>m_desiredTriggeredByNames</c>, so it is safe to call from
        /// concurrent options-change deltas, imperative SetTriggering
        /// callers, and the engine's apply-pass rollback paths without
        /// any external lock coordination.
        /// </summary>
        internal bool AddDesiredTriggeredBy(string triggeringName)
        {
            if (string.IsNullOrWhiteSpace(triggeringName))
            {
                throw new ArgumentException(
                    "Triggering item name must not be null/empty/whitespace.",
                    nameof(triggeringName));
            }
            while (true)
            {
                IReadOnlyList<string> current = Volatile.Read(
                    ref m_desiredTriggeredByNames);
                if (ContainsOrdinal(current, triggeringName))
                {
                    return false;
                }
                var updated = new string[current.Count + 1];
                for (int i = 0; i < current.Count; i++)
                {
                    updated[i] = current[i];
                }
                updated[current.Count] = triggeringName;
                if (ReferenceEquals(
                        Interlocked.CompareExchange(
                            ref m_desiredTriggeredByNames, updated, current),
                        current))
                {
                    return true;
                }
                // Lost the race — re-read and retry.
            }
        }

        /// <summary>
        /// Remove a triggering-item name from
        /// <see cref="DesiredTriggeredByNames"/>. Returns <c>true</c> if
        /// the name was present (and removed), <c>false</c> if it was
        /// not in the list. Self-synchronizing via a lock-free CAS loop
        /// on <c>m_desiredTriggeredByNames</c>; see
        /// <see cref="AddDesiredTriggeredBy"/>.
        /// </summary>
        internal bool RemoveDesiredTriggeredBy(string triggeringName)
        {
            if (string.IsNullOrEmpty(triggeringName))
            {
                return false;
            }
            while (true)
            {
                IReadOnlyList<string> current = Volatile.Read(
                    ref m_desiredTriggeredByNames);
                int index = -1;
                for (int i = 0; i < current.Count; i++)
                {
                    if (string.Equals(current[i], triggeringName,
                        StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0)
                {
                    return false;
                }
                IReadOnlyList<string> updated;
                if (current.Count == 1)
                {
                    updated = Array.Empty<string>();
                }
                else
                {
                    var arr = new string[current.Count - 1];
                    int dst = 0;
                    for (int i = 0; i < current.Count; i++)
                    {
                        if (i == index)
                        {
                            continue;
                        }
                        arr[dst++] = current[i];
                    }
                    updated = arr;
                }
                if (ReferenceEquals(
                        Interlocked.CompareExchange(
                            ref m_desiredTriggeredByNames, updated, current),
                        current))
                {
                    return true;
                }
                // Lost the race — re-read and retry.
            }
        }

        /// <summary>
        /// Replace <see cref="DesiredTriggeredByNames"/> with the
        /// supplied set. Names are validated (reject null / empty /
        /// whitespace via <see cref="ArgumentException"/>) and
        /// de-duplicated with an ordinal-case-sensitive comparer;
        /// insertion order is preserved. Intended for whole-collection
        /// replace scenarios (initial construction, snapshot apply);
        /// concurrent callers should use
        /// <see cref="AddDesiredTriggeredBy"/> /
        /// <see cref="RemoveDesiredTriggeredBy"/> instead to avoid
        /// last-writer-wins clobber semantics.
        /// </summary>
        internal void SetDesiredTriggeredByNames(IEnumerable<string>? names)
        {
            if (names == null)
            {
                Volatile.Write(ref m_desiredTriggeredByNames,
                    Array.Empty<string>());
                return;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>();
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(
                        "TriggeredByNames must not contain null/empty/whitespace entries.",
                        nameof(names));
                }
                if (seen.Add(name))
                {
                    list.Add(name);
                }
            }
            Volatile.Write(ref m_desiredTriggeredByNames,
                list.Count == 0 ? Array.Empty<string>() : list.ToArray());
        }

        private static bool ContainsOrdinal(
            IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Apply server-side identifiers from a saved snapshot to this
        /// freshly-created monitored item. Used by
        /// <see cref="ISubscriptionManager.LoadAsync"/> when the caller
        /// requests <c>transferSubscriptions</c> so the loaded item can
        /// be matched to its server-side state via
        /// <c>TransferSubscriptions</c>.
        /// </summary>
        /// <remarks>
        /// Re-assigning <see cref="ClientHandle"/> after construction
        /// matters because the V2 publish loop dispatches notifications
        /// by client handle. The snapshot's client handle is the one the
        /// server still uses; the post-construction one is freshly
        /// generated and would never match an incoming notification.
        /// </remarks>
        internal void ApplyTransferState(uint clientHandle, uint serverId)
        {
            ClientHandle = clientHandle;
            ServerId = serverId;
        }

        /// <summary>
        /// Install fully-loaded state from a snapshot during V2
        /// transfer-on-load (<see cref="ISubscriptionManager.LoadAsync"/>
        /// with <c>transferSubscriptions: true</c>). Unlike
        /// <see cref="ApplyTransferState"/>, this also:
        /// <list type="bullet">
        /// <item><description>Abandons the create-or-modify
        /// <see cref="Change"/> the ctor's <see cref="QueuePendingChanges"/>
        /// just queued, so the V2 state machine does not issue a
        /// redundant <c>CreateMonitoredItems</c> request whose
        /// <c>ClientHandle</c> would mismatch the snapshot's
        /// (the source of the <c>Debug.Assert</c> failure that
        /// motivated this work).</description></item>
        /// <item><description>Re-installs the saved triggering links
        /// so a subsequent <c>SetTriggering</c> replay reflects the
        /// snapshot.</description></item>
        /// </list>
        /// The caller is responsible for binding the (possibly
        /// freshly-revised) server-side state via the
        /// <c>TransferSubscriptions</c> / <c>GetMonitoredItems</c> path
        /// that runs in <see cref="MonitoredItemManager.TrySynchronizeHandlesAsync"/>.
        /// </summary>
        internal void ApplyLoadState(MonitoredItemLoadState state)
        {
            // Abandon any pending changes queued during the ctor.
            while (TryGetPendingChange(out Change? change))
            {
                change.Abandon();
            }
            // Snapshot the current options so the V2 state machine
            // treats the item as "configured to the loaded values" and
            // skips the create path until a real change arrives.
            m_currentOptions = m_options.CurrentValue;
            ClientHandle = state.ClientHandle;
            ServerId = state.ServerId;
            // Install the saved desired triggering set as the runtime
            // canonical state. For TransferSubscriptions this restores
            // the local view without re-issuing SetTriggering (the
            // server preserves links across transfer per Part 4
            // §5.13.5); for Recreate the engine will diff this set
            // against current=empty during Reset and enqueue
            // triggering operations to replay.
            SetDesiredTriggeredByNames(state.TriggeredByNames);
        }

        /// <summary>
        /// The subscription that owns the monitored item.
        /// </summary>
        protected IMonitoredItemContext Context { get; }

        /// <summary>
        /// Current monitored item options
        /// </summary>
        internal IOptionsMonitor<MonitoredItemOptions> Options
        {
            get => m_options;
            set
            {
                if (m_options != value)
                {
                    m_options = value;
                    QueuePendingChanges(m_options.CurrentValue, m_currentOptions);
                    m_changeTracking?.Dispose();
                    m_changeTracking = m_options.OnChange(
                        (o, _) => OnOptionsChanged(o));
                }
            }
        }

        /// <summary>
        /// Create monitored item
        /// </summary>
        /// <param name="context"></param>
        /// <param name="name"></param>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        protected MonitoredItem(IMonitoredItemContext context, string name,
            IOptionsMonitor<MonitoredItemOptions> options, ILogger logger)
        {
            Context = context;
            Name = name;
            Error = ServiceResult.Good;
            ClientHandle = Utils.IncrementIdentifier(ref GlobalClientHandleUint);

            m_logger = logger;
            // Seed runtime desired-state from the initial options
            // before we hook the options monitor; this keeps the diff
            // logic in OnOptionsChanged consistent ("first call sees
            // the seeded state as the previous value") and avoids
            // emitting spurious triggering operations on the very
            // first ApplyChangesAsync pass.
            SetDesiredTriggeredByNames(options.CurrentValue.TriggeredByNames);
            m_options = Options = options;
            m_logger.LogDebug("{Item} CREATED.", this);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return DisposeAsync(disposing: true);
        }

        /// <summary>
        /// Capture an immutable snapshot of this item's configuration
        /// + identifiers + triggering state.
        /// </summary>
        public MonitoredItemStateSnapshot Snapshot()
        {
            return MonitoredItemStateSnapshot.AsOptions(
                Name,
                m_options.CurrentValue,
                ClientHandle,
                ServerId,
                DesiredTriggeredByNames);
        }

        /// <inheritdoc/>
        public ValueTask ConditionRefreshAsync(CancellationToken ct = default)
        {
            if (!Created)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadMonitoredItemIdInvalid,
                    "Monitored item has not been created on the server.");
            }
            return Context.ConditionRefreshAsync(ServerId, ct);
        }

        /// <inheritdoc/>
        public override string? ToString()
        {
            StringBuilder sb = new StringBuilder()
              .Append(Context)
              .Append('#')
              .Append(ClientHandle)
              .Append('|')
              .Append(ServerId)
              .Append(" (")
              .Append(Name)
              .Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Dispose monitored item
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual ValueTask DisposeAsync(bool disposing)
        {
            if (disposing && !m_disposedValue)
            {
                while (TryGetPendingChange(out Change? change))
                {
                    change.Abandon();
                }

                Context.NotifyItemChange(this, true);
                m_logger.LogDebug("{Item} REMOVED.", this);

                ServerId = 0;
                m_changeTracking?.Dispose();
                m_disposedValue = true;
            }
            return default;
        }

        /// <summary>
        /// Called when the subscription state changed
        /// </summary>
        /// <param name="state"></param>
        /// <param name="publishingInterval"></param>
        protected internal virtual void OnSubscriptionStateChange(
            SubscriptionState state, TimeSpan publishingInterval)
        {
            MonitoredItemOptions? options = m_currentOptions;
            if (options == null ||
                (state != SubscriptionState.Created &&
                    state != SubscriptionState.Modified))
            {
                return;
            }
            uint queueSize = options.QueueSize;
            if (!options.AutoSetQueueSize)
            {
                return;
            }
            if (publishingInterval == TimeSpan.Zero)
            {
                return;
            }
            TimeSpan samplingInterval = CurrentSamplingInterval;
            if (samplingInterval == TimeSpan.Zero)
            {
                samplingInterval = options.SamplingInterval;
            }
            if (samplingInterval <= TimeSpan.Zero)
            {
                return;
            }
            queueSize = Math.Max(queueSize, (uint)Math.Ceiling(
                publishingInterval.TotalMilliseconds / samplingInterval.TotalMilliseconds)) +
                1;
            if (queueSize == options.QueueSize)
            {
                return;
            }
            OnOptionsChanged(options with { QueueSize = queueSize });
        }

        /// <summary>
        /// Called when the options change
        /// </summary>
        /// <param name="options"></param>
        protected virtual void OnOptionsChanged(MonitoredItemOptions options)
        {
            QueuePendingChanges(options, m_currentOptions);
            Context.NotifyItemChange(this);
        }

        /// <summary>
        /// Notify subscription that the subscription manager has paused or
        /// resumed operations.
        /// </summary>
        /// <param name="paused"></param>
        protected internal virtual void NotifySubscriptionManagerPaused(bool paused)
        {
            // empty
        }

        /// <summary>
        /// Get the current pending change in the change list. The change list
        /// collects the changes to be made to the monitored item while the
        /// subscription is applying state changes.
        /// </summary>
        /// <param name="change"></param>
        /// <returns></returns>
        internal bool TryGetPendingChange([NotNullWhen(true)] out Change? change)
        {
            return m_pendingChanges.TryPeek(out change);
        }

        /// <summary>
        /// Updates the object with the results of a transfer subscription request.
        /// </summary>
        /// <param name="clientHandle"></param>
        /// <param name="serverHandle"></param>
        /// <exception cref="ObjectDisposedException"></exception>
        internal void SetTransferResult(uint clientHandle, uint serverHandle)
        {
            if (m_disposedValue)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            // ensure the global counter is not duplicating future handle ids
            if (clientHandle != ClientHandle)
            {
                m_logger.LogInformation("{Item}: UPDATE CLIENT ID from {Old} to {New}.",
                    this, ClientHandle, clientHandle);

                ClientHandle = clientHandle;

                Utils.SetIdentifierToAtLeast(ref GlobalClientHandleUint, clientHandle);
            }
            if (serverHandle != ServerId)
            {
                m_logger.LogInformation("{Item}: UPDATE SERVER ID from {Old} to {New}.",
                    this, ServerId, serverHandle);

                ServerId = serverHandle;
            }
        }

        /// <summary>
        /// Reset the monitored item to its initial state for recreation
        /// on server side. Runtime <c>DesiredTriggeredByNames</c> is
        /// preserved across reset so that the batched apply pass can
        /// replay the desired triggering topology after the item
        /// finishes re-creating; this is the piece that closes the
        /// "triggering links not replayed on recreate" gap
        /// (issue #3834).
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        internal void Reset()
        {
            if (m_disposedValue)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            m_logger.LogDebug("{Item}: RESET.", this);
            ServerId = 0;

            MonitoredItemOptions? options = m_currentOptions;
            while (TryGetPendingChange(out Change? change))
            {
                change.Abandon();
                options = change.Options;
            }
            m_currentOptions = null;
            if (options == null)
            {
                return;
            }
            QueuePendingChanges(options, null);

            // After server-side state is cleared, every desired
            // triggering link needs to be re-issued via SetTriggering.
            // Enqueue one add-delta per preserved triggering name; the
            // engine resolves and batches them once the re-created
            // items reach Created. Preserves the canonical runtime
            // desired state — we do NOT mutate DesiredTriggeredByNames
            // here.
            IReadOnlyList<string> desired = DesiredTriggeredByNames;
            if (desired.Count > 0)
            {
                Context.EnqueueTriggeringDelta(
                    this,
                    desired,
                    Array.Empty<string>());
            }
        }

        /// <summary>
        /// Queues changes into the change queue for the item
        /// </summary>
        /// <param name="options"></param>
        /// <param name="currentOptions"></param>
        private void QueuePendingChanges(MonitoredItemOptions options,
            MonitoredItemOptions? currentOptions)
        {
            if (currentOptions != null && currentOptions == options)
            {
                // No changes
                Context.NotifyItemChangeResult(this, 0, options, new ServiceResult(
                    StatusCodes.BadNothingToDo), true, null);
                return;
            }

            if (options.StartNodeId.IsNull)
            {
                // Not valid
                Context.NotifyItemChangeResult(this, 0, options, new ServiceResult(
                    StatusCodes.BadNodeIdInvalid), true, null);
                return;
            }
            // When this is an options-change event (currentOptions is
            // non-null) compute the diff between the previous and new
            // options' TriggeredByNames and apply only that delta to
            // the runtime DesiredTriggeredByNames. Diffing against the
            // runtime would erase any imperative writes that happened
            // between the two options pushes; diffing against the
            // previous options keeps them.
            //
            // Initial construction (Options setter wired in ctor) and
            // Reset (recreate path) both call this with
            // currentOptions=null. Those paths intentionally skip the
            // diff: TryAdd enqueues the initial triggering set
            // explicitly after construction, and Reset replays from
            // the preserved runtime DesiredTriggeredByNames in its own
            // block.
            //
            // Validation is performed inline (reject null/empty/
            // whitespace, dedupe ordinal). We do NOT throw here on
            // bad entries — bad entries are reported via the
            // existing NotifyItemChangeResult path.
            if (currentOptions != null)
            {
                TriggeredByNamesDiff diff = DiffTriggeredByNames(
                    options.TriggeredByNames,
                    currentOptions.TriggeredByNames);
                if (diff.Error != null)
                {
                    Context.NotifyItemChangeResult(this, 0, options,
                        diff.Error, true, null);
                    return;
                }
                if (diff.Add != null)
                {
                    foreach (string name in diff.Add)
                    {
                        AddDesiredTriggeredBy(name);
                    }
                }
                if (diff.Remove != null)
                {
                    foreach (string name in diff.Remove)
                    {
                        RemoveDesiredTriggeredBy(name);
                    }
                }
                if (diff.Add != null || diff.Remove != null)
                {
                    Context.EnqueueTriggeringDelta(
                        this,
                        (IReadOnlyList<string>?)diff.Add ?? Array.Empty<string>(),
                        (IReadOnlyList<string>?)diff.Remove ?? Array.Empty<string>());
                }
            }

            m_currentOptions = options;
            m_pendingChanges.Enqueue(new Change(this, options, currentOptions));
        }

        /// <summary>
        /// Carrier for the diff produced by
        /// <see cref="DiffTriggeredByNames"/>: the set of names to add
        /// to and remove from <see cref="DesiredTriggeredByNames"/>,
        /// plus an optional validation error.
        /// </summary>
        private readonly record struct TriggeredByNamesDiff(
            List<string>? Add,
            List<string>? Remove,
            ServiceResult? Error);

        /// <summary>
        /// Compute the diff between the previous options'
        /// <c>TriggeredByNames</c> and the new options'
        /// <c>TriggeredByNames</c> (both validated against null/empty/
        /// whitespace and dedup'd ordinal). Returns the names added in
        /// the new options and removed in the new options as
        /// side-by-side lists, or a non-null <see cref="ServiceResult"/>
        /// describing a validation error.
        /// </summary>
        private static TriggeredByNamesDiff DiffTriggeredByNames(
            IReadOnlyList<string> proposed,
            IReadOnlyList<string> current)
        {
            // Validate + dedupe the proposed set.
            var proposedSet = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new List<string>(proposed.Count);
            for (int i = 0; i < proposed.Count; i++)
            {
                string name = proposed[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    return new TriggeredByNamesDiff(null, null,
                        ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "TriggeredByNames must not contain null/empty/" +
                            "whitespace entries."));
                }
                if (proposedSet.Add(name))
                {
                    normalized.Add(name);
                }
            }
            // Compute added (in normalized, not in current).
            List<string>? addList = null;
            for (int i = 0; i < normalized.Count; i++)
            {
                if (!ContainsOrdinal(current, normalized[i]))
                {
                    addList ??= [];
                    addList.Add(normalized[i]);
                }
            }
            // Compute removed (in current, not in normalized).
            List<string>? removeList = null;
            for (int i = 0; i < current.Count; i++)
            {
                if (!proposedSet.Contains(current[i]))
                {
                    removeList ??= [];
                    removeList.Add(current[i]);
                }
            }
            return new TriggeredByNamesDiff(addList, removeList, null);
        }

        /// <summary>
        /// Complete a change
        /// </summary>
        /// <param name="change"></param>
        /// <returns></returns>
        internal bool CompleteChange(Change change)
        {
            return m_pendingChanges.TryDequeue(out Change? completed) &&
                change == completed;
        }

        /// <summary>
        /// Change steps to apply to the monitored items in the
        /// subscription context. The change list is a queue of
        /// steps to perform inside the subscription.
        /// see Subscription.ApplyMonitoredItemChangesAsync for
        /// more information
        /// </summary>
        internal sealed class Change
        {
            /// <summary>
            /// The item on which the changes are applied
            /// </summary>
            public MonitoredItem Item { get; }

            /// <summary>
            /// Timestamps to return
            /// </summary>
            public TimestampsToReturn Timestamps => Options.TimestampsToReturn;

            /// <summary>
            /// Create request if the item is not yet created
            /// </summary>
            public MonitoredItemCreateRequest? Create { get; }

            /// <summary>
            /// Modification request if the item is already created
            /// </summary>
            public MonitoredItemModifyRequest? Modify { get; private set; }

            /// <summary>
            /// Monitoring mode change pending
            /// </summary>
            public MonitoringMode? MonitoringModeChange { get; private set; }

            /// <summary>
            /// Force recreating the item due to changes in the
            /// read item information.
            /// </summary>
            public bool ForceRecreate { get; private set; }

            /// <summary>
            /// Current retry count of this change
            /// </summary>
            public int RetryCount { get; private set; }

            /// <summary>
            /// Options that are the source of the change
            /// </summary>
            public MonitoredItemOptions Options { get; }

            /// <summary>
            /// Create change
            /// </summary>
            /// <param name="item"></param>
            /// <param name="options"></param>
            /// <param name="currentOptions"></param>
            public Change(MonitoredItem item, MonitoredItemOptions options,
                MonitoredItemOptions? currentOptions)
            {
                Debug.Assert(!options.StartNodeId.IsNull);
                Options = options;
                Item = item;

                var parameters = new MonitoringParameters
                {
                    ClientHandle = item.ClientHandle,
                    SamplingInterval = (int)options.SamplingInterval.TotalMilliseconds,
                    QueueSize = options.QueueSize,
                    DiscardOldest = options.DiscardOldest,
                    Filter = options.Filter != null ?
                        new ExtensionObject(options.Filter) : default
                };

                Create = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = options.StartNodeId,
                        AttributeId = options.AttributeId,
                        IndexRange = options.IndexRange,
                        DataEncoding = options.Encoding ?? QualifiedName.Null
                    },
                    MonitoringMode = options.MonitoringMode,
                    RequestedParameters = parameters
                };

                if (currentOptions == null ||
                    currentOptions.StartNodeId != options.StartNodeId ||
                    currentOptions.AttributeId != options.AttributeId ||
                    currentOptions.IndexRange != options.IndexRange ||
                    currentOptions.Encoding != options.Encoding)
                {
                    Modify = null;
                    MonitoringModeChange = null;
                    ForceRecreate = true;
                }
                else
                {
                    Modify = new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = item.ServerId,
                        RequestedParameters = parameters
                    };

                    if (currentOptions.MonitoringMode != options.MonitoringMode)
                    {
                        MonitoringModeChange = options.MonitoringMode;

                        // If only monitoring mode changed, no need to modify
                        if (currentOptions with
                        {
                            MonitoringMode = MonitoringModeChange.Value
                        } == options)
                        {
                            Modify = null;
                        }
                    }
                    else
                    {
                        MonitoringModeChange = null;
                    }
                }
            }

            /// <summary>
            /// Updates the object with the results of a create monitored item request.
            /// </summary>
            /// <param name="request"></param>
            /// <param name="result"></param>
            /// <param name="index"></param>
            /// <param name="diagnosticInfos"></param>
            /// <param name="responseHeader"></param>
            internal void SetCreateResult(MonitoredItemCreateRequest request,
                MonitoredItemCreateResult result, int index,
                ArrayOf<DiagnosticInfo> diagnosticInfos, ResponseHeader responseHeader)
            {
                Debug.Assert(request.RequestedParameters.ClientHandle == Item.ClientHandle);
                ServiceResult error = ServiceResult.Good;

                if (StatusCode.IsBad(result.StatusCode))
                {
                    error = ClientBase.GetResult(result.StatusCode, index,
                        diagnosticInfos, responseHeader);
                }

                Item.CurrentMonitoringMode = request.MonitoringMode;
                Item.CurrentSamplingInterval = TimeSpan.FromMilliseconds(
                    request.RequestedParameters.SamplingInterval);
                Item.CurrentQueueSize = request.RequestedParameters.QueueSize;

                if (ServiceResult.IsGood(error))
                {
                    Item.ServerId = result.MonitoredItemId;
                    Item.CurrentSamplingInterval =
                        TimeSpan.FromMilliseconds(result.RevisedSamplingInterval);
                    Item.CurrentQueueSize = result.RevisedQueueSize;

                    Item.LogRevisedSamplingRateAndQueueSize(Options, true);
                    Notify(error, true, result.FilterResult);
                    return;
                }

                // Declare final if not communication error which includes success
                RetryCount++;
                Notify(error, false, result.FilterResult);
            }

            /// <summary>
            /// Updates the object with the results of a modify monitored item request.
            /// </summary>
            /// <param name="request"></param>
            /// <param name="result"></param>
            /// <param name="index"></param>
            /// <param name="diagnosticInfos"></param>
            /// <param name="responseHeader"></param>
            internal void SetModifyResult(MonitoredItemModifyRequest request,
                MonitoredItemModifyResult result, int index,
                ArrayOf<DiagnosticInfo> diagnosticInfos, ResponseHeader responseHeader)
            {
                Debug.Assert(request.RequestedParameters.ClientHandle == Item.ClientHandle);
                ServiceResult error = ServiceResult.Good;
                if (StatusCode.IsBad(result.StatusCode))
                {
                    error = ClientBase.GetResult(result.StatusCode, index,
                        diagnosticInfos, responseHeader);
                }

                if (ServiceResult.IsGood(error))
                {
                    Item.CurrentSamplingInterval = TimeSpan.FromMilliseconds(
                        request.RequestedParameters.SamplingInterval);
                    Item.CurrentQueueSize = request.RequestedParameters.QueueSize;

                    Item.CurrentSamplingInterval = TimeSpan.FromMilliseconds(
                        result.RevisedSamplingInterval);
                    Item.CurrentQueueSize = result.RevisedQueueSize;

                    if (MonitoringModeChange == null)
                    {
                        Item.LogRevisedSamplingRateAndQueueSize(Options, false);
                    }

                    // Declare final
                    Notify(error, MonitoringModeChange == null, result.FilterResult);
                    return;
                }

                if (!IsCommunicationError(error))
                {
                    // Do not apply the mode change but force a recreate
                    MonitoringModeChange = null;
                    Modify = null;
                    ForceRecreate = true;
                }

                if (MonitoringModeChange == null)
                {
                    // Retry the modification request
                    RetryCount++;
                }
                Notify(error, false, result.FilterResult);
            }

            /// <summary>
            /// Set monitoring mode result
            /// </summary>
            /// <param name="monitoringMode"></param>
            /// <param name="statusCode"></param>
            /// <param name="index"></param>
            /// <param name="diagnosticInfos"></param>
            /// <param name="responseHeader"></param>
            /// <exception cref="NotImplementedException"></exception>
            internal void SetMonitoringModeResult(MonitoringMode monitoringMode,
                StatusCode statusCode, int index, ArrayOf<DiagnosticInfo> diagnosticInfos,
                ResponseHeader responseHeader)
            {
                ServiceResult error = ServiceResult.Good;
                if (StatusCode.IsBad(statusCode))
                {
                    error = ClientBase.GetResult(statusCode, index, diagnosticInfos,
                        responseHeader);
                }

                if (ServiceResult.IsGood(error))
                {
                    Item.CurrentMonitoringMode = monitoringMode;
                    Item.LogRevisedSamplingRateAndQueueSize(Options, false);

                    Notify(error, true);
                    return;
                }

                if (!IsCommunicationError(error))
                {
                    // Reapply the mode change
                    Modify = null;
                    ForceRecreate = true;
                }
                // Retry
                RetryCount++;
                Notify(error, false);
            }

            /// <summary>
            /// Set result of delete
            /// </summary>
            /// <param name="statusCode"></param>
            /// <param name="index"></param>
            /// <param name="diagnosticInfos"></param>
            /// <param name="responseHeader"></param>
            internal void SetDeleteResult(StatusCode statusCode, int index,
                ArrayOf<DiagnosticInfo> diagnosticInfos, ResponseHeader responseHeader)
            {
                ServiceResult error = ServiceResult.Good;
                if (StatusCode.IsBad(statusCode))
                {
                    error = ClientBase.GetResult(statusCode, index, diagnosticInfos,
                        responseHeader);
                }

                bool final = Create == null && Modify == null;
                if (ServiceResult.IsGood(error) ||
                    error.StatusCode == StatusCodes.BadMonitoredItemIdInvalid ||
                    final)
                {
                    Item.ServerId = 0;
                    ForceRecreate = false;
                    // Now state is !Created so continue here to recreate
                }
                else
                {
                    // Retry
                    RetryCount++;
                }
                Notify(error, final);
            }

            /// <summary>
            /// Abandon the change
            /// </summary>
            internal void Abandon()
            {
                Notify(new ServiceResult(StatusCodes.BadOperationAbandoned), true);
            }

            /// <summary>
            /// Notify and handle retries
            /// </summary>
            /// <param name="error"></param>
            /// <param name="final"></param>
            /// <param name="filterResultExtensionObject"></param>
            private void Notify(ServiceResult error, bool final,
                ExtensionObject? filterResultExtensionObject = null)
            {
                MonitoringFilterResult? filterResult = null;
                if (filterResultExtensionObject.HasValue &&
                    filterResultExtensionObject.Value.TryGetValue(out MonitoringFilterResult? fr))
                {
                    filterResult = fr;
                }
                bool stop = Item.Context.NotifyItemChangeResult(
                    Item, RetryCount, Options, error, final, filterResult);
                if (final || stop)
                {
                    Item.CompleteChange(this);
                }
                Item.Error = error;
                Item.FilterResult = filterResult == null ? Item.FilterResult :
                    (MonitoringFilterResult)filterResult.Clone();
            }

            /// <summary>
            /// Returns true if communication issue and not an error
            /// in subscription or even success or uncertain states
            /// </summary>
            /// <param name="error"></param>
            /// <returns></returns>
            private static bool IsCommunicationError(ServiceResult error)
            {
                StatusCode code = error.StatusCode;
                return code == StatusCodes.BadCommunicationError ||
                    code == StatusCodes.BadNotConnected ||
                    code == StatusCodes.BadSecureChannelClosed;
            }
        }

        /// <summary>
        /// Log revised sampling rate and queue size
        /// </summary>
        /// <param name="options"></param>
        /// <param name="created"></param>
        public void LogRevisedSamplingRateAndQueueSize(MonitoredItemOptions options,
            bool created)
        {
            if (options.SamplingInterval != CurrentSamplingInterval &&
                options.QueueSize != CurrentQueueSize &&
                CurrentQueueSize != 0)
            {
                m_logger.LogInformation(
                    "{Item}: {Action} SamplingInterval was " +
                    "revised from {SamplingInterval} to {CurrentSamplingInterval} " +
                    "and QueueSize from {QueueSize} to {CurrentQueueSize}.",
                    this, created ? "CREATED" : "UPDATED",
                    options.SamplingInterval, CurrentSamplingInterval,
                    options.QueueSize, CurrentQueueSize);
            }
            else if (options.SamplingInterval != CurrentSamplingInterval)
            {
                m_logger.LogInformation(
                    "{Item}: {Action} SamplingInterval was " +
                    "revised from {SamplingInterval} to {CurrentSamplingInterval}.",
                    this, created ? "CREATED" : "UPDATED",
                    options.SamplingInterval, CurrentSamplingInterval);
            }
            else if (options.QueueSize != CurrentQueueSize && CurrentQueueSize != 0)
            {
                m_logger.LogInformation(
                    "{Item}: {Action} QueueSize was " +
                    "revised from {QueueSize} to {CurrentQueueSize}.",
                    this, created ? "CREATED" : "UPDATED",
                    options.QueueSize, CurrentQueueSize);
            }
            else
            {
                m_logger.LogDebug(
                    "{Item}: {Action} with desired configuration.",
                    this, created ? "CREATED" : "UPDATED");
            }
        }

        private bool m_disposedValue;
        private MonitoredItemOptions? m_currentOptions;
#pragma warning disable CA2213 // Disposed in DisposeAsync(bool)
        private IDisposable? m_changeTracking;
#pragma warning restore CA2213
        private readonly ConcurrentQueue<Change> m_pendingChanges = new();
        private readonly ILogger m_logger;
        internal static uint GlobalClientHandleUint;
        private IOptionsMonitor<MonitoredItemOptions> m_options;
        private IReadOnlyList<string> m_desiredTriggeredByNames
            = Array.Empty<string>();
    }
}
