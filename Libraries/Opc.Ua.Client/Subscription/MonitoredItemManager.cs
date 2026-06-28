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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// A managed set of monitored items inside a subscription.
    /// </summary>
    internal sealed class MonitoredItemManager : IMonitoredItemCollection,
        IMonitoredItemContext, IAsyncDisposable
    {
        /// <inheritdoc/>
        public IEnumerable<IMonitoredItem> Items
        {
            get
            {
                lock (m_monitoredItemsLock)
                {
                    return [.. m_monitoredItems.Values];
                }
            }
        }

        /// <inheritdoc/>
        public uint Count
        {
            get
            {
                lock (m_monitoredItemsLock)
                {
                    return (uint)m_monitoredItems.Count;
                }
            }
        }

        /// <summary>
        /// Create monitored item manager
        /// </summary>
        /// <param name="context"></param>
        /// <param name="telemetry"></param>
        public MonitoredItemManager(IMonitoredItemManagerContext context,
            ITelemetryContext telemetry)
        {
            m_logger = telemetry.LoggerFactory.CreateLogger<MonitoredItemManager>();
            m_context = context;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (MonitoredItem? monitoredItem in m_monitoredItems.Values.ToList())
                {
                    await monitoredItem.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_monitoredItems.Clear();
                m_monitoredItemsByName.Clear();
                m_pendingByTriggeringName.Clear();
                m_pendingTriggeringCount = 0;
            }
        }

        /// <inheritdoc/>
        public bool TryAdd(string name, IOptionsMonitor<MonitoredItemOptions> options,
            out IMonitoredItem? monitoredItem)
        {
            IReadOnlyList<string>? initialTriggers = null;
            lock (m_monitoredItemsLock)
            {
                if (m_monitoredItemsByName.TryGetValue(name, out MonitoredItem? item))
                {
                    monitoredItem = item;
                    return false;
                }
                item = m_context.CreateMonitoredItem(name, options, this);
                m_monitoredItems.Add(item.ClientHandle, item);
                m_monitoredItemsByName.Add(name, item);
                monitoredItem = item;
                // Drain any pending unresolved triggering-name deltas
                // keyed by this just-added item's name — declarative
                // adds that referenced this item before it existed get
                // materialized now into real ops on the queue.
                DrainPendingForTriggeringItem(item);
                // Enqueue an initial triggering delta for any
                // TriggeredByNames declared in the options at add
                // time. The MonitoredItem ctor seeds the runtime
                // DesiredTriggeredByNames from options but does NOT
                // enqueue a delta (so AddLoaded — which OVERWRITES
                // the runtime state from the snapshot — never
                // accidentally enqueues redundant SetTriggering RPCs
                // for links the server already has after transfer).
                // TryAdd is the one path where the engine should
                // actively reconcile the initial declaration with the
                // server.
                IReadOnlyList<string> triggers = options.CurrentValue.TriggeredByNames;
                if (triggers.Count > 0)
                {
                    initialTriggers = triggers;
                }
            }
            if (initialTriggers != null && monitoredItem != null)
            {
                EnqueueTriggeringDelta(monitoredItem, initialTriggers,
                    []);
            }
            m_context.Update();
            return true;
        }

        /// <summary>
        /// Construct + register a monitored item that was loaded from a
        /// snapshot (see <see cref="MonitoredItemLoadState"/>). The
        /// freshly-constructed item is bound to the saved
        /// <see cref="MonitoredItem.ClientHandle"/> / <see cref="MonitoredItem.ServerId"/>
        /// via <see cref="MonitoredItem.ApplyLoadState"/> and any pending
        /// create-request queued during construction is abandoned, so
        /// the V2 state machine does not issue a redundant
        /// <c>CreateMonitoredItems</c>. The owning subscription drives
        /// the take-over via the standard transfer flow
        /// (<see cref="Subscription.TryCompleteTransferAsync"/>).
        /// </summary>
        internal bool AddLoaded(MonitoredItemLoadState state)
        {
            lock (m_monitoredItemsLock)
            {
                if (m_monitoredItemsByName.ContainsKey(state.Name))
                {
                    return false;
                }
                MonitoredItem item = m_context.CreateMonitoredItem(
                    state.Name, state.Options, this);
                item.ApplyLoadState(state);
                m_monitoredItems.Add(item.ClientHandle, item);
                m_monitoredItemsByName.Add(state.Name, item);
                // Drain any pending unresolved triggering-name deltas
                // (e.g. a triggered item already loaded that references
                // this item by name): pending entries materialize into
                // real triggering ops only when the named item appears.
                DrainPendingForTriggeringItem(item);
                return true;
            }
            // Intentionally NOT calling m_context.Update() — Update()
            // signals the subscription's state-control auto-reset event
            // which would prematurely wake StateManagerAsync and have it
            // try to create what we just loaded.
        }

        /// <inheritdoc/>
        public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
            [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
        {
            lock (m_monitoredItemsLock)
            {
                bool result = m_monitoredItems.TryGetValue(clientHandle, out MonitoredItem? item);
                monitoredItem = item;
                return result;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMonitoredItemByName(string name,
            [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
        {
            lock (m_monitoredItemsLock)
            {
                bool result = m_monitoredItemsByName.TryGetValue(name, out MonitoredItem? item);
                monitoredItem = item;
                return result;
            }
        }

        /// <inheritdoc/>
        public bool TryRemove(uint clientHandle)
        {
            lock (m_monitoredItemsLock)
            {
                if (!m_monitoredItems.Remove(clientHandle, out MonitoredItem? monitoredItem))
                {
                    return false;
                }
                m_monitoredItemsByName.Remove(monitoredItem.Name);
                // Purge pending unresolved triggering deltas that
                // reference this removed item (either by name as the
                // pending triggering key, or as an inner triggered
                // entry). Without this, a later AddLoaded/TryAdd of an
                // item with the same name would materialize a stale
                // pending into a real op against a dead reference.
                DropPendingForRemovedItem(monitoredItem);
                m_deletedItems.Add(monitoredItem);
            }
            m_context.Update();
            return true;
        }

        /// <inheritdoc/>
        public IReadOnlyList<IMonitoredItem> Update(IReadOnlyList<(
            string Name, IOptionsMonitor<MonitoredItemOptions> Options)> state)
        {
            var monitoredItems = new List<IMonitoredItem>(state.Count);
            // Track initial triggering deltas for newly created items
            // — we enqueue them outside the lock to keep the manager
            // lock acquisition short and consistent (the Enqueue path
            // re-acquires under a separate code-path).
            List<(IMonitoredItem Item, IReadOnlyList<string> Triggers)>? initialTriggers = null;
            lock (m_monitoredItemsLock)
            {
                // Capture current state
                IDictionary<uint, MonitoredItem> remove = m_monitoredItems.ToDictionary();

                // Generate items
                foreach ((string? name, IOptionsMonitor<MonitoredItemOptions>? options) in state)
                {
                    if (!m_monitoredItemsByName.TryGetValue(name, out MonitoredItem? item))
                    {
                        // Create new item
                        item = m_context.CreateMonitoredItem(name, options, this);
                        m_monitoredItems.Add(item.ClientHandle, item);
                        m_monitoredItemsByName.Add(name, item);
                        // Drain pending unresolved triggering-name
                        // deltas that referenced this name before it
                        // existed (declarative TriggeredByNames adds
                        // queued on prior items become real ops here).
                        DrainPendingForTriggeringItem(item);
                        IReadOnlyList<string> triggers = options.CurrentValue.TriggeredByNames;
                        if (triggers.Count > 0)
                        {
                            initialTriggers ??= [];
                            initialTriggers.Add((item, triggers));
                        }
                    }
                    else
                    {
                        item.Options = options;
                        remove.Remove(item.ClientHandle);
                    }
                    monitoredItems.Add(item);
                }

                // Remove all items not in state
                foreach (uint clientHandle in remove.Keys)
                {
                    if (m_monitoredItems.Remove(clientHandle, out MonitoredItem? monitoredItem))
                    {
                        m_monitoredItemsByName.Remove(monitoredItem.Name);
                        DropPendingForRemovedItem(monitoredItem);
                        m_deletedItems.Add(monitoredItem);
                    }
                }
            }
            if (initialTriggers != null)
            {
                foreach ((IMonitoredItem item, IReadOnlyList<string> triggers) in initialTriggers)
                {
                    EnqueueTriggeringDelta(item, triggers, []);
                }
            }
            m_context.Update();
            return monitoredItems;
        }

        /// <inheritdoc/>
        public void NotifyItemChange(MonitoredItem monitoredItem, bool itemDisposed)
        {
            Debug.Assert(monitoredItem != null);
            if (!itemDisposed)
            {
                m_context.Update();
            }
        }

        /// <inheritdoc/>
        public bool NotifyItemChangeResult(
            MonitoredItem monitoredItem,
            int retryCount,
            MonitoredItemOptions source,
            ServiceResult serviceResult,
            bool final,
            MonitoringFilterResult? filterResult)
        {
            // Reactive fallback for the V2 unbounded-item mode:
            // surface a Bad_TooManyMonitoredItems response so the
            // owning logical subscription can mark this partition
            // no-grow. The placement policy then skips this partition
            // for subsequent TryAdd calls; the failed item is reported
            // up to the caller via its standard error path.
            if (serviceResult != null &&
                serviceResult.StatusCode == StatusCodes.BadTooManyMonitoredItems &&
                m_context is Subscription owningPartition)
            {
                owningPartition.OnPartitionCapReached?.Invoke(serviceResult);
            }
            return final || retryCount > 5; // TODO: Resiliency policy
        }

        /// <inheritdoc/>
        public async ValueTask ConditionRefreshAsync(
            uint monitoredItemServerId,
            CancellationToken ct = default)
        {
            ArrayOf<CallMethodRequest> methodsToCall =
            [
                new CallMethodRequest
                {
                    ObjectId = ObjectTypeIds.ConditionType,
                    MethodId = MethodIds.ConditionType_ConditionRefresh2,
                    InputArguments =
                    [
                        new Variant(m_context.Id),
                        new Variant(monitoredItemServerId)
                    ]
                }
            ];
            CallResponse response = await m_context.MethodServiceSet
                .CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
            ArrayOf<CallMethodResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;
            ClientBase.ValidateResponse(results, methodsToCall);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, methodsToCall);
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(ClientBase.GetResult(
                    results[0].StatusCode, 0, diagnosticInfos,
                    response.ResponseHeader));
            }
        }

        /// <summary>
        /// Create notifications for monitored items
        /// </summary>
        /// <param name="notification"></param>
        /// <returns></returns>
        internal ReadOnlyMemory<DataValueChange> CreateNotification(
            DataChangeNotification notification)
        {
            var memory = new DataValueChange[notification.MonitoredItems.Count];
            uint partitionId = m_context.Id;
            lock (m_monitoredItemsLock)
            {
                for (int i = 0; i < notification.MonitoredItems.Count; i++)
                {
                    MonitoredItemNotification item = notification.MonitoredItems[i];
                    m_monitoredItems.TryGetValue(item.ClientHandle, out MonitoredItem? monitored);
                    memory[i] = new DataValueChange(monitored,
                        item.Value, item.DiagnosticInfo)
                    {
                        PartitionServerId = partitionId
                    };
                }
            }
            // TODO: Sort on order of monitored items
            // memory.AsSpan().Sort((x, y) => x.MonitoredItem!.ClientHandle.CompareTo(
            //     y.MonitoredItem!.ClientHandle));
            return memory;
        }

        /// <summary>
        /// Create notifications for monitored items
        /// </summary>
        /// <param name="notification"></param>
        /// <returns></returns>
        internal ReadOnlyMemory<EventNotification> CreateNotification(
            EventNotificationList notification)
        {
            var memory = new EventNotification[notification.Events.Count];
            uint partitionId = m_context.Id;
            lock (m_monitoredItemsLock)
            {
                for (int i = 0; i < notification.Events.Count; i++)
                {
                    EventFieldList item = notification.Events[i];
                    m_monitoredItems.TryGetValue(item.ClientHandle, out MonitoredItem? monitored);
                    memory[i] = new EventNotification(monitored, item.EventFields)
                    {
                        PartitionServerId = partitionId
                    };
                }
            }
            // memory.AsSpan().Sort((x, y) => x.MonitoredItem!.ClientHandle.CompareTo(
            //     y.MonitoredItem!.ClientHandle));
            return memory;
        }

        /// <summary>
        /// Apply changes to monitored items
        /// </summary>
        /// <param name="once"></param>
        /// <param name="resetAll"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async Task<bool> ApplyChangesAsync(bool once, bool resetAll,
            CancellationToken ct)
        {
            bool modified = false;
            while (!ct.IsCancellationRequested &&
                TryGetMonitoredItemChanges(
                    out List<MonitoredItem>? itemsToDelete, out List<MonitoredItem.Change>? itemsToModify, resetAll))
            {
                await ApplyMonitoredItemChangesAsync(itemsToDelete,
                    itemsToModify, ct).ConfigureAwait(false);
                // While there are changes pending to be applied apply them
                modified = true;
                if (once)
                {
                    break;
                }
            }
            // Drain the triggering-operations queue. Runs after
            // Create/Modify/SetMonitoringMode so that the items
            // referenced by triggering links have valid server ids by
            // the time the RPC fires. Pending operations whose
            // referenced items are not yet Created get re-queued (with
            // a bounded retry budget) for the next ApplyChangesAsync
            // pass.
            if (!ct.IsCancellationRequested &&
                await ApplyTriggeringOperationsAsync(ct).ConfigureAwait(false))
            {
                modified = true;
            }
            return modified;
        }

        /// <summary>
        /// Notify monitored items of the state changes in the subscription
        /// </summary>
        /// <param name="state"></param>
        /// <param name="currentPublishingInterval"></param>
        internal void OnSubscriptionStateChange(SubscriptionState state,
            TimeSpan currentPublishingInterval)
        {
            lock (m_monitoredItemsLock)
            {
                foreach (MonitoredItem item in m_monitoredItems.Values)
                {
                    item.OnSubscriptionStateChange(state, currentPublishingInterval);
                }
            }
        }

        /// <summary>
        /// Notify monitored items that the subscription manager is paused
        /// </summary>
        /// <param name="paused"></param>
        internal void NotifySubscriptionManagerPaused(bool paused)
        {
            lock (m_monitoredItemsLock)
            {
                foreach (MonitoredItem item in m_monitoredItems.Values)
                {
                    item.NotifySubscriptionManagerPaused(paused);
                }
            }
        }

        /// <summary>
        /// Apply monitored item changes
        /// </summary>
        /// <param name="itemsToDelete"></param>
        /// <param name="itemsToModify"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async ValueTask ApplyMonitoredItemChangesAsync(List<MonitoredItem> itemsToDelete,
            List<MonitoredItem.Change> itemsToModify, CancellationToken ct)
        {
            if (itemsToDelete.Count != 0)
            {
                foreach (MonitoredItem? dispose in itemsToDelete.Where(c => !c.Created))
                {
                    await dispose.DisposeAsync().ConfigureAwait(false);
                }
                itemsToDelete.RemoveAll(c => !c.Created);
            }
            if (itemsToDelete.Count != 0)
            {
                try
                {
                    var monitoredItemIds = new ArrayOf<uint>(itemsToDelete
                        .Select(m => m.ServerId).ToArray());
                    DeleteMonitoredItemsResponse response = await m_context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(null, m_context.Id,
                        monitoredItemIds, ct).ConfigureAwait(false);
                    ClientBase.ValidateResponse(response.Results, monitoredItemIds);
                    ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos,
                        monitoredItemIds);

                    for (int index = 0; index < response.Results.Count; index++)
                    {
                        if (StatusCode.IsGood(response.Results[index]) ||
                            response.Results[index] == StatusCodes.BadMonitoredItemIdInvalid ||
                            !itemsToDelete[index].Created)
                        {
                            await itemsToDelete[index].DisposeAsync().ConfigureAwait(false);
                            continue;
                        }
                        m_deletedItems.Add(itemsToDelete[index]); // Retry this
                        // TODO: Give up after a while
                    }
                }
                catch (Exception ex)
                {
                    m_deletedItems.AddRange(itemsToDelete);
                    m_logger.LogInformation(ex, "Failed to delete monitored items.");
                }
            }

            // To force recreate if item is created therefore, we need to delete the item first.
            var deletes = itemsToModify
                .Where(c => c.Item.Created && c.ForceRecreate)
                .ToList();
            if (deletes.Count > 0)
            {
                var monitoredItemIds = new ArrayOf<uint>(deletes.Select(c => c.Item.ServerId).ToArray());
                DeleteMonitoredItemsResponse response = await m_context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(null, m_context.Id,
                    monitoredItemIds, ct).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, monitoredItemIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);
                for (int index = 0; index < response.Results.Count; index++)
                {
                    deletes[index].SetDeleteResult(
                        response.Results[index], index, response.DiagnosticInfos,
                        response.ResponseHeader);
                }
            }

            // Modify all items that need to be modified.
            var modifications = itemsToModify
                .Where(c => c.Item.Created)
                .ToList();
            foreach (IGrouping<TimestampsToReturn, MonitoredItem.Change> group in modifications
                .Where(c => c.Modify != null)
                .GroupBy(c => c.Timestamps))
            {
                var monitoredItems = group.ToList();
                var requests = new ArrayOf<MonitoredItemModifyRequest>(group.Select(c => c.Modify!).ToArray());
                if (requests.Count > 0)
                {
                    ModifyMonitoredItemsResponse response = await m_context.MonitoredItemServiceSet.ModifyMonitoredItemsAsync(null, m_context.Id,
                        group.Key, requests, ct).ConfigureAwait(false);
                    ClientBase.ValidateResponse(response.Results, requests);
                    ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                    // update results.
                    for (int index = 0; index < response.Results.Count; index++)
                    {
                        monitoredItems[index].SetModifyResult(requests[index],
                            response.Results[index], index, response.DiagnosticInfos,
                            response.ResponseHeader);
                    }
                }
            }

            // Finalize by updating the monitoring mode where needed
            foreach (IGrouping<MonitoringMode, MonitoredItem.Change> mode in modifications
                .Where(c => c.MonitoringModeChange != null)
                .GroupBy(c => c.MonitoringModeChange!.Value))
            {
                var monitoredItems = mode
                    .Where(c => c.Item.Created && c.Item.CurrentMonitoringMode != mode.Key)
                    .ToList();
                var requests = new ArrayOf<uint>(monitoredItems.Select(c => c.Item.ServerId).ToArray());
                if (requests.Count > 0)
                {
                    SetMonitoringModeResponse response = await m_context.MonitoredItemServiceSet.SetMonitoringModeAsync(null, m_context.Id,
                        mode.Key, requests, ct).ConfigureAwait(false);
                    ClientBase.ValidateResponse(response.Results, requests);
                    ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                    // update results.
                    for (int index = 0; index < response.Results.Count; index++)
                    {
                        monitoredItems[index].SetMonitoringModeResult(
                            mode.Key, response.Results[index], index, response.DiagnosticInfos,
                            response.ResponseHeader);
                    }
                }
            }

            // Create all items
            var creations = itemsToModify
                .Where(c => !c.Item.Created)
                .ToList();
            foreach (IGrouping<TimestampsToReturn, MonitoredItem.Change> group in creations
                .Where(c => c.Create != null)
                .GroupBy(c => c.Timestamps))
            {
                var monitoredItems = group.ToList();
                var requests = new ArrayOf<MonitoredItemCreateRequest>(group.Select(c => c.Create!).ToArray());
                if (requests.Count > 0)
                {
                    CreateMonitoredItemsResponse response = await m_context.MonitoredItemServiceSet.CreateMonitoredItemsAsync(null, m_context.Id,
                        group.Key, requests, ct).ConfigureAwait(false);
                    ClientBase.ValidateResponse(response.Results, requests);
                    ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                    // update results.
                    for (int index = 0; index < response.Results.Count; index++)
                    {
                        monitoredItems[index].SetCreateResult(requests[index],
                            response.Results[index], index, response.DiagnosticInfos,
                            response.ResponseHeader);
                    }
                }
            }
        }

        /// <summary>
        /// Get monitored item changes
        /// </summary>
        /// <param name="itemsToDelete"></param>
        /// <param name="itemsToModify"></param>
        /// <param name="resetAll"></param>
        /// <returns></returns>
        internal bool TryGetMonitoredItemChanges(out List<MonitoredItem> itemsToDelete,
            out List<MonitoredItem.Change> itemsToModify, bool resetAll = false)
        {
            // modify the subscription.
            itemsToModify = [];
            lock (m_monitoredItemsLock)
            {
                itemsToDelete = [.. m_deletedItems];
                m_deletedItems.Clear();

                foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                {
                    if (resetAll)
                    {
                        monitoredItem.Reset();
                    }
                    // build item request.
                    if (monitoredItem.TryGetPendingChange(out MonitoredItem.Change? change))
                    {
                        itemsToModify.Add(change);
                    }
                }
            }
            return itemsToDelete.Count != 0 ||
                itemsToModify.Count != 0;
        }

        /// <summary>
        /// <para>
        /// If a Client called CreateMonitoredItems during the network interruption
        /// and the call succeeded in the Server but did not return to the Client,
        /// then the Client does not know if the call succeeded. The Client may
        /// receive data changes for these monitored items but is not able to remove
        /// them since it does not know the Server handle.
        /// </para>
        /// <para>
        /// There is also no way for the Client to detect if the create succeeded.
        /// To delete and recreate the Subscription is also not an option since
        /// there may be several monitored items operating normally that should
        /// not be interrupted.
        /// </para>
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async ValueTask<bool> TrySynchronizeHandlesAsync(
            CancellationToken ct)
        {
            (bool success, IReadOnlyList<(uint serverHandle, uint clientHandle)>? serverHandleStateMap) = await GetMonitoredItemsAsync(
                ct).ConfigureAwait(false);

            ArrayOf<uint> itemsToDelete;
            lock (m_monitoredItemsLock)
            {
                if (!success)
                {
                    // Reset all items
                    foreach (MonitoredItem monitoredItem in m_monitoredItems.Values)
                    {
                        monitoredItem.Reset();
                    }
                    return false;
                }

                IDictionary<uint, MonitoredItem> monitoredItems = m_monitoredItems.ToDictionary();
                m_monitoredItems.Clear();

                //
                // Assign the server id to the item with the matching client handle. This
                // handles the case where the CreateMonitoredItems call succeeded on the
                // server side, but the response was not provided back.
                //
                var clientServerHandleMap = serverHandleStateMap
                    .ToDictionary(m => m.clientHandle, m => m.serverHandle);
                foreach (KeyValuePair<uint, MonitoredItem> monitoredItem in monitoredItems.ToList())
                {
                    //
                    // Adjust any items where the server handles does not map to the
                    // handle the server has assigned.
                    //
                    uint clientHandle = monitoredItem.Value.ClientHandle;
                    if (clientServerHandleMap.Remove(clientHandle, out uint serverHandle))
                    {
                        monitoredItem.Value.SetTransferResult(clientHandle, serverHandle);
                        monitoredItems.Remove(monitoredItem.Key);
                        m_monitoredItems.Add(clientHandle, monitoredItem.Value);
                    }
                }

                //
                // Assign the server side client handle to the item with the same server id
                // This handles the case where we are recreating the subscription from a
                // previously stored state.
                //
                var serverClientHandleMap = clientServerHandleMap
                    .ToDictionary(m => m.Value, m => m.Key);
                foreach (KeyValuePair<uint, MonitoredItem> monitoredItem in monitoredItems.ToList())
                {
                    uint serverHandle = monitoredItem.Value.ServerId;
                    if (serverClientHandleMap.Remove(serverHandle, out uint clientHandle))
                    {
                        //
                        // There should not be any more item with the same client handle
                        // in this subscription, they were updated before. TODO: Assert
                        //
                        monitoredItem.Value.SetTransferResult(clientHandle, serverHandle);
                        monitoredItems.Remove(monitoredItem.Key);
                        m_monitoredItems.Add(clientHandle, monitoredItem.Value);
                    }
                }

                m_deletedItems.Clear();
                itemsToDelete = new ArrayOf<uint>(serverClientHandleMap.Keys.ToArray());

                // Remaining items do not exist anymore on the server and need to be recreated
                foreach (MonitoredItem? missingOnServer in monitoredItems.Values)
                {
                    if (missingOnServer.Created)
                    {
                        m_logger.LogDebug("{Subscription}: Recreate client item {Item}.", this,
                            missingOnServer);
                        missingOnServer.Reset();
                    }
                    m_monitoredItems.Add(missingOnServer.ClientHandle, missingOnServer);
                }
            }
            if (itemsToDelete.Count == 0)
            {
                // Completed
                return true;
            }

            // Ensure all items on the server that are not in the subscription are deleted
            try
            {
                await m_context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(null, m_context.Id, itemsToDelete,
                    ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "{Subscription}: Failed to delete missing items.", this);
                return false;
            }
        }

        private record struct MonitoredItemsHandles(bool Success,
            IReadOnlyList<(uint serverHandle, uint clientHandle)> Handles);

        /// <summary>
        /// Call the GetMonitoredItems method on the server.
        /// </summary>
        /// <param name="ct"></param>
        private async ValueTask<MonitoredItemsHandles> GetMonitoredItemsAsync(
            CancellationToken ct)
        {
            try
            {
                var requests = new ArrayOf<CallMethodRequest>(new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_GetMonitoredItems,
                        InputArguments = [new Variant(m_context.Id)]
                    }
                });

                CallResponse response = await m_context.MethodServiceSet.CallAsync(null, requests,
                    ct).ConfigureAwait(false);
                ArrayOf<CallMethodResult> results = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;
                ClientBase.ValidateResponse(results, requests);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create(results[0].StatusCode,
                        0, diagnosticInfos, response.ResponseHeader.StringTable);
                }

                ArrayOf<Variant> outputArguments = results[0].OutputArguments;
                if (outputArguments.Count != 2 ||
                    !outputArguments[0].TryGetValue(out ArrayOf<uint> serverHandles) ||
                    !outputArguments[1].TryGetValue(out ArrayOf<uint> clientHandles) ||
                    serverHandles.IsNull ||
                    clientHandles.IsNull ||
                    serverHandles.Count != clientHandles.Count)
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "Output arguments incorrect");
                }
                return new MonitoredItemsHandles(
                    true,
                    serverHandles.ToList().Zip(clientHandles.ToList()).ToList());
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogError(sre,
                    "{Subscription}: Failed to call GetMonitoredItems on server", this);
                return new MonitoredItemsHandles(false, []);
            }
        }

        /// <summary>
        /// A pending triggering operation queued for batched apply in
        /// <see cref="ApplyMonitoredItemChangesAsync"/>.
        /// Operations are produced by both the imperative
        /// <c>Subscription.SetTriggeringAsync(IMonitoredItem, …)</c>
        /// API and by options-change diffs that touch
        /// <see cref="MonitoredItemOptions.TriggeredByNames"/>.
        /// </summary>
        /// <param name="TriggeringItem">
        /// The triggering item. Must be a current member of this
        /// subscription (verified by reference identity).
        /// </param>
        /// <param name="Add">Triggered items to be linked.</param>
        /// <param name="Remove">Triggered items whose link should be removed.</param>
        /// <param name="Completion">
        /// Optional completion source signaled when the operation has
        /// been applied (success or final error). The imperative API
        /// supplies one; declarative options-driven enqueues do not.
        /// </param>
        internal sealed record TriggeringOperation(
            IMonitoredItem TriggeringItem,
            IReadOnlyList<IMonitoredItem> Add,
            IReadOnlyList<IMonitoredItem> Remove,
            TaskCompletionSource<SetTriggeringResult>? Completion = null)
        {
            internal int RetryCount;

            /// <summary>
            /// Cancellation flag set by the imperative API's
            /// <see cref="CancellationToken"/> registration when the
            /// caller cancels. Read by
            /// <see cref="ApplyTriggeringOperationsAsync"/> after
            /// draining the queue to skip the op (and its contribution
            /// to per-edge folding) on a best-effort basis. Set via
            /// <see cref="Interlocked.Exchange(ref int, int)"/> so the
            /// read in the apply pass is memory-ordered without
            /// requiring an external lock. Once the apply pass has
            /// already dispatched the RPC for this op's group, setting
            /// this flag has no effect — the server-side state
            /// mutation cannot be cancelled.
            /// </summary>
            internal int Cancelled;

            internal bool IsCancelled
                => Volatile.Read(ref Cancelled) != 0;

            internal void MarkCancelled()
            {
                Interlocked.Exchange(ref Cancelled, 1);
            }
        }

        /// <summary>
        /// Eager validation under the manager lock that the supplied
        /// triggering and triggered items all belong to this
        /// subscription (per Part 4 §5.13.5.1). Validation uses
        /// reference identity against the current items dictionary;
        /// items removed before this call is made are rejected. When
        /// successful, the runtime <c>DesiredTriggeredByNames</c> of
        /// each affected triggered item is updated immediately so that
        /// <see cref="IMonitoredItem.TriggeringItems"/>,
        /// <see cref="IMonitoredItem.TriggeredItems"/>, and
        /// <see cref="MonitoredItem.Snapshot"/> reflect the requested
        /// intent before the batched apply pass runs.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="triggeringItem"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        internal void ValidateBelongsAndUpdateDesired(
            IMonitoredItem triggeringItem,
            IReadOnlyList<IMonitoredItem> add,
            IReadOnlyList<IMonitoredItem> remove)
        {
            if (triggeringItem == null)
            {
                throw new ArgumentNullException(nameof(triggeringItem));
            }
            if (string.IsNullOrEmpty(triggeringItem.Name))
            {
                throw new ArgumentException(
                    "Triggering item must have a name.",
                    nameof(triggeringItem));
            }
            string trigName = triggeringItem.Name;
            lock (m_monitoredItemsLock)
            {
                if (!m_monitoredItems.TryGetValue(triggeringItem.ClientHandle,
                        out MonitoredItem? trigInThisSub) ||
                    !ReferenceEquals(trigInThisSub, triggeringItem))
                {
                    throw new ArgumentException(
                        $"Triggering item '{triggeringItem.Name}' is not a " +
                        "current member of this subscription.",
                        nameof(triggeringItem));
                }
                for (int i = 0; i < add.Count; i++)
                {
                    IMonitoredItem target = add[i] ??
                        throw new ArgumentNullException(nameof(add),
                            $"linksToAdd[{i}] is null.");
                    if (!m_monitoredItems.TryGetValue(target.ClientHandle,
                            out MonitoredItem? inSub) ||
                        !ReferenceEquals(inSub, target))
                    {
                        throw new ArgumentException(
                            $"Triggered item '{target.Name}' is not a " +
                            "current member of this subscription.",
                            nameof(add));
                    }
                }
                for (int i = 0; i < remove.Count; i++)
                {
                    IMonitoredItem target = remove[i] ??
                        throw new ArgumentNullException(nameof(remove),
                            $"linksToRemove[{i}] is null.");
                    if (!m_monitoredItems.TryGetValue(target.ClientHandle,
                            out MonitoredItem? inSub) ||
                        !ReferenceEquals(inSub, target))
                    {
                        throw new ArgumentException(
                            $"Triggered item '{target.Name}' is not a " +
                            "current member of this subscription.",
                            nameof(remove));
                    }
                }

                // Mutate runtime desired state under the same lock so
                // that snapshot/navigation observe the intent
                // immediately, while options.TriggeredByNames remains
                // the *initial* declarative input (never mutated).
                for (int i = 0; i < add.Count; i++)
                {
                    if (add[i] is MonitoredItem mi)
                    {
                        mi.AddDesiredTriggeredBy(trigName);
                    }
                }
                for (int i = 0; i < remove.Count; i++)
                {
                    if (remove[i] is MonitoredItem mi)
                    {
                        mi.RemoveDesiredTriggeredBy(trigName);
                    }
                }
            }
        }

        /// <summary>
        /// Enqueue a triggering operation for batched apply during
        /// <see cref="ApplyMonitoredItemChangesAsync"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        internal void EnqueueTriggeringOperation(TriggeringOperation op)
        {
            m_triggeringOps.Enqueue(op ?? throw new ArgumentNullException(nameof(op)));
        }

        /// <summary>
        /// Test-only snapshot of the number of folded
        /// <c>(triggeringName, triggeredItem)</c> pairs currently
        /// tracked in <c>m_pendingByTriggeringName</c>. Used to verify
        /// that <see cref="MaxPendingTriggeringEntries"/> is enforced
        /// across all insertion paths (including inserts under an
        /// existing outer key).
        /// </summary>
        internal int PendingTriggeringEntryCount => m_pendingTriggeringCount;

        /// <inheritdoc/>
        public void EnqueueTriggeringDelta(
            IMonitoredItem triggeredItem,
            IReadOnlyList<string> addedTriggeringNames,
            IReadOnlyList<string> removedTriggeringNames)
        {
            if (triggeredItem == null)
            {
                throw new ArgumentNullException(nameof(triggeredItem));
            }
            if (addedTriggeringNames == null)
            {
                throw new ArgumentNullException(nameof(addedTriggeringNames));
            }
            if (removedTriggeringNames == null)
            {
                throw new ArgumentNullException(nameof(removedTriggeringNames));
            }
            if (addedTriggeringNames.Count == 0 &&
                removedTriggeringNames.Count == 0)
            {
                return;
            }
            // Resolve each name to an IMonitoredItem and enqueue one
            // operation per (triggering item, single triggered item)
            // pair. The batched apply merges per-triggering-item across
            // operations so we never make the queue lopsided just by
            // calling the helper many times.
            //
            // Names that don't currently resolve are persisted in
            // m_pendingByTriggeringName (folded per edge so later wins).
            // When the missing triggering item is later added (via
            // TryAdd/Update/AddLoaded), the pending entries are
            // materialized into real triggering operations. This makes
            // the declarative add order order-independent: a triggered
            // item that references a not-yet-present trigger by name is
            // wired up automatically once the trigger appears.
            lock (m_monitoredItemsLock)
            {
                for (int i = 0; i < removedTriggeringNames.Count; i++)
                {
                    if (m_monitoredItemsByName.TryGetValue(
                            removedTriggeringNames[i],
                            out MonitoredItem? trig) &&
                        trig != null)
                    {
                        m_triggeringOps.Enqueue(new TriggeringOperation(
                            trig,
                            [],
                            [triggeredItem],
                            null));
                    }
                    else
                    {
                        AddPendingTriggeringEntry(
                            removedTriggeringNames[i], triggeredItem, isAdd: false);
                    }
                }
                for (int i = 0; i < addedTriggeringNames.Count; i++)
                {
                    if (m_monitoredItemsByName.TryGetValue(
                            addedTriggeringNames[i],
                            out MonitoredItem? trig) &&
                        trig != null)
                    {
                        m_triggeringOps.Enqueue(new TriggeringOperation(
                            trig,
                            [triggeredItem],
                            [],
                            null));
                    }
                    else
                    {
                        AddPendingTriggeringEntry(
                            addedTriggeringNames[i], triggeredItem, isAdd: true);
                    }
                }
            }
        }

        /// <summary>
        /// Add an unresolved triggering-name delta to the pending
        /// dictionary, folded per <c>(triggeringName, triggeredItem)</c>
        /// so a later opposite isAdd value overrides an earlier one
        /// (last-intent-wins). Bounded by
        /// <see cref="MaxPendingTriggeringEntries"/>: when the cap is
        /// hit, a warning is logged and the new entry is dropped.
        /// Caller must hold <c>m_monitoredItemsLock</c>.
        /// </summary>
        private void AddPendingTriggeringEntry(
            string triggeringName, IMonitoredItem triggeredItem, bool isAdd)
        {
            // Determine whether this call would introduce a brand-new
            // (triggeringName, triggeredItem) pair BEFORE touching any
            // state. Three shapes count as new:
            //   1. The outer name key is missing.
            //   2. The outer key exists but the inner dict does not
            //      yet contain the triggered item.
            // Only the new-pair shape consumes a slot against the cap
            // and bumps m_pendingTriggeringCount; an existing pair gets
            // its isAdd value overwritten (last-intent-wins fold) with
            // no cap check and no counter bump — preserving the
            // intentional fold semantic for already-tracked edges.
            bool hadOuter = m_pendingByTriggeringName.TryGetValue(
                triggeringName,
                out Dictionary<IMonitoredItem, bool>? entries);
            bool isNew = !hadOuter || !entries!.ContainsKey(triggeredItem);
            if (isNew &&
                m_pendingTriggeringCount >= MaxPendingTriggeringEntries)
            {
                m_logger.LogWarning(
                    "Pending triggering-name dictionary is full " +
                    "({Cap} entries); dropping unresolved {Op} of " +
                    "triggered '{Triggered}' under triggering " +
                    "'{Triggering}'. Add the triggering item, remove " +
                    "stale triggered items, or call " +
                    "SetTriggeringAsync(IMonitoredItem,...) explicitly.",
                    MaxPendingTriggeringEntries,
                    isAdd ? "add" : "remove",
                    triggeredItem.Name, triggeringName);
                return;
            }
            if (!hadOuter)
            {
                entries = [];
                m_pendingByTriggeringName.Add(triggeringName, entries);
            }
            entries![triggeredItem] = isAdd;
            if (isNew)
            {
                m_pendingTriggeringCount++;
            }
        }

        /// <summary>
        /// Drain pending unresolved triggering-name deltas for a
        /// just-added triggering item, materializing each (still-valid)
        /// entry into a real <see cref="TriggeringOperation"/> appended
        /// to <c>m_triggeringOps</c>. Validates that the triggered item
        /// is still registered in this subscription AND (for adds) that
        /// it still desires the link before materializing. Caller must
        /// hold <c>m_monitoredItemsLock</c>.
        /// </summary>
        private void DrainPendingForTriggeringItem(MonitoredItem triggeringItem)
        {
            if (!m_pendingByTriggeringName.Remove(
                    triggeringItem.Name,
                    out Dictionary<IMonitoredItem, bool>? entries))
            {
                return;
            }
            m_pendingTriggeringCount -= entries.Count;
            foreach (KeyValuePair<IMonitoredItem, bool> kv in entries)
            {
                IMonitoredItem triggered = kv.Key;
                // Item may have been removed after the pending entry
                // was queued; reference-identity check is enough since
                // m_monitoredItemsByName always points at the live item.
                if (!m_monitoredItemsByName.TryGetValue(
                        triggered.Name, out MonitoredItem? current) ||
                    !ReferenceEquals(current, triggered))
                {
                    continue;
                }
                // For add intents, only enqueue if the triggered item
                // still desires this triggering name — guards against a
                // stale add that was already revoked by an options
                // change before the triggering item appeared.
                if (kv.Value &&
                    !ContainsOrdinal(
                        current.DesiredTriggeredByNames,
                        triggeringItem.Name))
                {
                    continue;
                }
                m_triggeringOps.Enqueue(new TriggeringOperation(
                    triggeringItem,
                    kv.Value
                        ? [triggered]
                        : Array.Empty<IMonitoredItem>(),
                    kv.Value
                        ? Array.Empty<IMonitoredItem>()
                        : [triggered],
                    null));
            }
        }

        /// <summary>
        /// Remove all pending triggering-name deltas that reference the
        /// supplied (just-removed) monitored item, both as the
        /// triggering side (its name as a key) and as the triggered
        /// side (entries within other keys' inner dicts). Caller must
        /// hold <c>m_monitoredItemsLock</c>.
        /// </summary>
        private void DropPendingForRemovedItem(MonitoredItem removed)
        {
            if (m_pendingByTriggeringName.Remove(
                    removed.Name,
                    out Dictionary<IMonitoredItem, bool>? own))
            {
                m_pendingTriggeringCount -= own.Count;
            }
            List<string>? emptyKeys = null;
            foreach (KeyValuePair<string, Dictionary<IMonitoredItem, bool>> kv
                in m_pendingByTriggeringName)
            {
                if (kv.Value.Remove(removed))
                {
                    m_pendingTriggeringCount--;
                }
                if (kv.Value.Count == 0)
                {
                    emptyKeys ??= [];
                    emptyKeys.Add(kv.Key);
                }
            }
            if (emptyKeys != null)
            {
                foreach (string key in emptyKeys)
                {
                    m_pendingByTriggeringName.Remove(key);
                }
            }
        }

        private static bool ContainsOrdinal(IReadOnlyList<string> list, string value)
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
        /// Batched apply step of
        /// <see cref="ApplyMonitoredItemChangesAsync"/> —
        /// drain the triggering operations queue, group by triggering
        /// item, fold per-edge (last-intent wins) so that an "add A"
        /// followed by a "remove A" in the same batch resolves to a
        /// single net-zero per-edge no-op, and issue one
        /// <c>SetTriggering</c> RPC per triggering item carrying both
        /// the merged add and remove lists (per Part 4 §5.13.5.2 the
        /// server processes <c>linksToRemove</c> before
        /// <c>linksToAdd</c>, which is the canonical order for this
        /// batch shape).
        /// </summary>
        /// <returns>
        /// <c>true</c> when any RPCs were issued (caller may want to
        /// invoke another ApplyChangesAsync iteration).
        /// </returns>
        internal async ValueTask<bool> ApplyTriggeringOperationsAsync(
            CancellationToken ct)
        {
            var drained = new List<TriggeringOperation>();
            while (m_triggeringOps.TryDequeue(out TriggeringOperation? op))
            {
                drained.Add(op);
            }
            if (drained.Count == 0)
            {
                return false;
            }

            // Best-effort cancellation: drop ops whose imperative
            // caller has cancelled their CancellationToken via
            // Subscription.SetTriggeringAsync. The op's TCS is already
            // in the cancelled state (the registration callback set it
            // before MarkCancelled returned), so we don't have to
            // notify anyone here — just exclude them from per-edge
            // folding and from the merged RPC. Once an op is filtered
            // out, the desired-state mutations performed synchronously
            // by ValidateBelongsAndUpdateDesired stay (matches the
            // documented semantic that local intent stands across
            // cancellation; the caller reverts via an explicit
            // opposing call). Race window: an op whose group has
            // already begun its RPC dispatch cannot be cancelled here
            // — by that point we are inside the awaited
            // SetTriggeringAsync call below.
            drained.RemoveAll(o => o.IsCancelled);
            if (drained.Count == 0)
            {
                return false;
            }

            var toRequeue = new List<TriggeringOperation>();
            bool anyApplied = false;

            // Group operations by triggering item (reference identity).
            // Each group becomes at most one SetTriggering RPC.
            foreach (IGrouping<IMonitoredItem, TriggeringOperation> group in
                drained.GroupBy(o => o.TriggeringItem))
            {
                IMonitoredItem trig = group.Key;
                List<TriggeringOperation> ops = [.. group];

                // The triggering item must be Created on the server
                // before SetTriggering can reference it. If not yet
                // created — and not terminally failed — re-queue and
                // wait for the next pass. We bound retries to avoid an
                // infinite loop when create has permanently failed
                // (e.g. BadFilterNotAllowed).
                if (!trig.Created)
                {
                    foreach (TriggeringOperation o in ops)
                    {
                        o.RetryCount++;
                        if (o.RetryCount > MaxTriggeringRetryCount)
                        {
                            // Terminal failure: propagate the
                            // triggering item's Error if available;
                            // otherwise default to
                            // BadMonitoredItemIdInvalid.
                            StatusCode propagated = trig.Error == null ||
                                trig.Error.StatusCode == StatusCodes.Good
                                    ? StatusCodes.BadMonitoredItemIdInvalid
                                    : trig.Error.StatusCode;
                            FailOperation(o, trig, propagated);
                        }
                        else
                        {
                            toRequeue.Add(o);
                        }
                    }
                    continue;
                }

                // Defer the entire operation group if any per-edge Add
                // targets a triggered item that isn't yet Created on
                // the server. Same retry pattern as the
                // triggering-item-not-Created case above so add-order
                // doesn't matter and a slow create doesn't terminally
                // drop the link.
                bool anyAddPending = false;
                foreach (TriggeringOperation o in ops)
                {
                    foreach (IMonitoredItem item in o.Add)
                    {
                        if (!item.Created)
                        {
                            anyAddPending = true;
                            break;
                        }
                    }
                    if (anyAddPending)
                    {
                        break;
                    }
                }
                if (anyAddPending)
                {
                    foreach (TriggeringOperation o in ops)
                    {
                        o.RetryCount++;
                        if (o.RetryCount > MaxTriggeringRetryCount)
                        {
                            // Retry budget exhausted: roll back desired
                            // state for only the non-Created add edges
                            // (Created ones get failed with the same
                            // status but the link request never reached
                            // the server, so there's no desired state
                            // to reverse on those).
                            lock (m_monitoredItemsLock)
                            {
                                foreach (IMonitoredItem addItem in o.Add)
                                {
                                    if (!addItem.Created &&
                                        addItem is MonitoredItem mi)
                                    {
                                        mi.RemoveDesiredTriggeredBy(trig.Name);
                                    }
                                }
                            }
                            FailOperation(o, trig,
                                StatusCodes.BadMonitoredItemIdInvalid);
                        }
                        else
                        {
                            toRequeue.Add(o);
                        }
                    }
                    continue;
                }

                // Fold per-edge: each triggered item contributes its
                // last-recorded intent (add or remove). Operations are
                // applied in enqueue order — later wins.
                var perEdge = new Dictionary<IMonitoredItem, bool>(); // true=add
                foreach (TriggeringOperation o in ops)
                {
                    foreach (IMonitoredItem item in o.Remove)
                    {
                        perEdge[item] = false;
                    }
                    foreach (IMonitoredItem item in o.Add)
                    {
                        perEdge[item] = true;
                    }
                }

                // Build per-edge applied-status map; we'll fill it
                // after the RPC returns. The non-Created Add case is
                // already handled by the deferral above; here we only
                // need to handle remove-of-not-Created items (server
                // auto-removes the link when the item is deleted,
                // §5.13.1.6, so model as Good no-op).
                var edgeApplied =
                    new Dictionary<IMonitoredItem, StatusCode>();
                var addList = new List<IMonitoredItem>();
                var removeList = new List<IMonitoredItem>();
                foreach (KeyValuePair<IMonitoredItem, bool> kv in perEdge)
                {
                    if (kv.Value)
                    {
                        addList.Add(kv.Key);
                    }
                    else if (!kv.Key.Created)
                    {
                        // Server auto-removed the link when the
                        // item was deleted (§5.13.1.6) — model as
                        // Good no-op on the client side.
                        edgeApplied[kv.Key] = StatusCodes.Good;
                    }
                    else
                    {
                        removeList.Add(kv.Key);
                    }
                }

                StatusCode serviceResult = StatusCodes.Good;
                if (addList.Count == 0 && removeList.Count == 0)
                {
                    // Nothing to send (everything pre-resolved); fall
                    // through to per-op completion.
                }
                else
                {
                    try
                    {
                        uint[] addIds = new uint[addList.Count];
                        for (int i = 0; i < addList.Count; i++)
                        {
                            addIds[i] = addList[i].ServerId;
                        }
                        uint[] removeIds = new uint[removeList.Count];
                        for (int i = 0; i < removeList.Count; i++)
                        {
                            removeIds[i] = removeList[i].ServerId;
                        }

                        SetTriggeringResponse response = await m_context
                            .MonitoredItemServiceSet
                            .SetTriggeringAsync(
                                null,
                                m_context.Id,
                                trig.ServerId,
                                addIds.ToArrayOf(),
                                removeIds.ToArrayOf(),
                                ct).ConfigureAwait(false);
                        anyApplied = true;

                        if (response.ResponseHeader != null)
                        {
                            serviceResult = response.ResponseHeader
                                .ServiceResult;
                        }

                        if (!StatusCode.IsBad(serviceResult))
                        {
                            ArrayOf<StatusCode> addResults =
                                response.AddResults;
                            ArrayOf<StatusCode> remResults =
                                response.RemoveResults;
                            lock (m_monitoredItemsLock)
                            {
                                for (int i = 0; i < addList.Count; i++)
                                {
                                    StatusCode s = addResults.Count > i
                                        ? addResults[i]
                                        : StatusCodes.Bad;
                                    edgeApplied[addList[i]] = s;
                                    // Roll back the desired-state on
                                    // per-link failure so the caller's
                                    // intent matches the server's
                                    // reality.
                                    if (StatusCode.IsBad(s) &&
                                        addList[i] is MonitoredItem mi)
                                    {
                                        mi.RemoveDesiredTriggeredBy(trig.Name);
                                    }
                                }
                                for (int i = 0; i < removeList.Count; i++)
                                {
                                    StatusCode s = remResults.Count > i
                                        ? remResults[i]
                                        : StatusCodes.Bad;
                                    edgeApplied[removeList[i]] = s;
                                    // A failed remove means the server
                                    // still has the link; preserve the
                                    // desired state to match (don't
                                    // undo the optimistic remove we
                                    // did during validation).
                                    if (StatusCode.IsBad(s) &&
                                        removeList[i] is MonitoredItem mi)
                                    {
                                        mi.AddDesiredTriggeredBy(trig.Name);
                                    }
                                }
                            }
                        }
                        else if (serviceResult ==
                            StatusCodes.BadSubscriptionIdInvalid)
                        {
                            // Recoverable via subscription recreate:
                            // KEEP the optimistic desired-state and
                            // re-queue. Rolling back here would leave
                            // snapshots/navigation showing no link
                            // during recovery; the retry path will
                            // re-issue against the recreated
                            // subscription with the correct intent.
                            toRequeue.AddRange(ops);
                            continue;
                        }
                        else
                        {
                            // Service-level terminal error: roll back
                            // the optimistic desired-state changes for
                            // every affected item.
                            RollbackDesired(addList, removeList, trig.Name);
                            foreach (IMonitoredItem item in addList)
                            {
                                edgeApplied[item] = serviceResult;
                            }
                            foreach (IMonitoredItem item in removeList)
                            {
                                edgeApplied[item] = serviceResult;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex,
                            "{Subscription}: SetTriggering for " +
                            "triggering item {Name} failed.",
                            this, trig.Name);
                        StatusCode failStatus = ex is ServiceResultException sre
                            ? sre.StatusCode
                            : StatusCodes.BadCommunicationError;
                        if (failStatus ==
                            StatusCodes.BadSubscriptionIdInvalid)
                        {
                            // Recoverable: KEEP desired-state and
                            // re-queue. Same rationale as the
                            // service-result path above.
                            toRequeue.AddRange(ops);
                            continue;
                        }
                        // Terminal: rollback the optimistic desired
                        // state so the local view matches what the
                        // server didn't apply.
                        RollbackDesired(addList, removeList, trig.Name);
                        foreach (IMonitoredItem item in addList)
                        {
                            edgeApplied[item] = failStatus;
                        }
                        foreach (IMonitoredItem item in removeList)
                        {
                            edgeApplied[item] = failStatus;
                        }
                        serviceResult = failStatus;
                    }
                }

                // Complete every contributing operation's TCS with the
                // per-link statuses for its own inputs (looked up by
                // reference identity in the edgeApplied map).
                foreach (TriggeringOperation o in ops)
                {
                    if (o.Completion == null)
                    {
                        continue;
                    }
                    var addResultsOut =
                        new (IMonitoredItem Item, StatusCode Status)[o.Add.Count];
                    for (int i = 0; i < o.Add.Count; i++)
                    {
                        addResultsOut[i] = (o.Add[i],
                            edgeApplied.TryGetValue(o.Add[i], out StatusCode s)
                                ? s
                                : StatusCodes.Good);
                    }
                    var removeResultsOut =
                        new (IMonitoredItem Item, StatusCode Status)[o.Remove.Count];
                    for (int i = 0; i < o.Remove.Count; i++)
                    {
                        removeResultsOut[i] = (o.Remove[i],
                            edgeApplied.TryGetValue(o.Remove[i], out StatusCode s)
                                ? s
                                : StatusCodes.Good);
                    }
                    o.Completion.TrySetResult(new SetTriggeringResult(
                        trig, addResultsOut, removeResultsOut, serviceResult));
                }
            }

            foreach (TriggeringOperation o in toRequeue)
            {
                m_triggeringOps.Enqueue(o);
            }
            return anyApplied;
        }

        private static void FailOperation(
            TriggeringOperation op, IMonitoredItem trig, StatusCode status)
        {
            if (op.Completion == null)
            {
                return;
            }
            var addOut = new (IMonitoredItem Item, StatusCode Status)[op.Add.Count];
            for (int i = 0; i < op.Add.Count; i++)
            {
                addOut[i] = (op.Add[i], status);
            }
            var remOut = new (IMonitoredItem Item, StatusCode Status)[op.Remove.Count];
            for (int i = 0; i < op.Remove.Count; i++)
            {
                remOut[i] = (op.Remove[i], status);
            }
            op.Completion.TrySetResult(new SetTriggeringResult(
                trig, addOut, remOut, status));
        }

        private void RollbackDesired(
            List<IMonitoredItem> add,
            List<IMonitoredItem> remove,
            string trigName)
        {
            lock (m_monitoredItemsLock)
            {
                for (int i = 0; i < add.Count; i++)
                {
                    if (add[i] is MonitoredItem mi)
                    {
                        mi.RemoveDesiredTriggeredBy(trigName);
                    }
                }
                for (int i = 0; i < remove.Count; i++)
                {
                    if (remove[i] is MonitoredItem mi)
                    {
                        mi.AddDesiredTriggeredBy(trigName);
                    }
                }
            }
        }

        /// <summary>
        /// Hard cap on triggering re-queues so a permanently failed
        /// triggering or triggered item cannot starve the queue
        /// indefinitely. Operations that exceed this budget are
        /// completed with the triggering item's last error (or
        /// <c>Bad_MonitoredItemIdInvalid</c> as a fallback).
        /// </summary>
        private const int MaxTriggeringRetryCount = 10;

        /// <summary>
        /// Internal (not private) so the regression test in
        /// MonitoredItemManagerTriggeringTests can compute "cap + N"
        /// without hard-coding 256 alongside the production constant.
        /// </summary>
        internal const int MaxPendingTriggeringEntries = 256;
        private readonly ConcurrentQueue<TriggeringOperation> m_triggeringOps = new();

        /// <summary>
        /// Pending unresolved declarative-path triggering deltas keyed by
        /// triggering-item name; inner dictionary folds per triggered
        /// item (last-intent wins). Protected by m_monitoredItemsLock.
        /// </summary>
        private readonly Dictionary<string, Dictionary<IMonitoredItem, bool>>
            m_pendingByTriggeringName =
                new(StringComparer.Ordinal);
        private int m_pendingTriggeringCount;

        private readonly IMonitoredItemManagerContext m_context;
        private readonly List<MonitoredItem> m_deletedItems = [];
        private readonly Lock m_monitoredItemsLock = new();
        private readonly Dictionary<uint, MonitoredItem> m_monitoredItems = [];
        private readonly Dictionary<string, MonitoredItem> m_monitoredItemsByName = [];
        private readonly ILogger m_logger;
    }
}
