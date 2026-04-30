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
            }
        }

        /// <inheritdoc/>
        public bool TryAdd(string name, IOptionsMonitor<MonitoredItemOptions> options,
            out IMonitoredItem? monitoredItem)
        {
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
            }
            m_context.Update();
            return true;
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
            lock (m_monitoredItemsLock)
            {
                // Capture current state
                var remove = m_monitoredItems.ToDictionary();

                // Generate items
                foreach ((string? name, IOptionsMonitor<MonitoredItemOptions>? options) in state)
                {
                    if (!m_monitoredItemsByName.TryGetValue(name, out MonitoredItem? item))
                    {
                        // Create new item
                        item = m_context.CreateMonitoredItem(name, options, this);
                        m_monitoredItems.Add(item.ClientHandle, item);
                        m_monitoredItemsByName.Add(name, item);
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
                        m_deletedItems.Add(monitoredItem);
                    }
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
        public bool NotifyItemChangeResult(MonitoredItem monitoredItem,
            int retryCount, MonitoredItemOptions source, ServiceResult serviceResult,
            bool final, MonitoringFilterResult? filterResult)
        {
            return final || retryCount > 5; // TODO: Resiliency policy
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
            lock (m_monitoredItemsLock)
            {
                for (int i = 0; i < notification.MonitoredItems.Count; i++)
                {
                    MonitoredItemNotification item = notification.MonitoredItems[i];
                    m_monitoredItems.TryGetValue(item.ClientHandle, out MonitoredItem? monitored);
                    memory[i] = new DataValueChange(monitored,
                        item.Value, item.DiagnosticInfo);
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
            lock (m_monitoredItemsLock)
            {
                for (int i = 0; i < notification.Events.Count; i++)
                {
                    EventFieldList item = notification.Events[i];
                    m_monitoredItems.TryGetValue(item.ClientHandle, out MonitoredItem? monitored);
                    memory[i] = new EventNotification(monitored, item.EventFields);
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

                var monitoredItems = m_monitoredItems.ToDictionary();
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
                    outputArguments[0].AsBoxedObject(Variant.BoxingBehavior.Legacy) is not uint[] serverHandles ||
                    outputArguments[1].AsBoxedObject(Variant.BoxingBehavior.Legacy) is not uint[] clientHandles ||
                    clientHandles.Length != serverHandles.Length)
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "Output arguments incorrect");
                }
                return new MonitoredItemsHandles(true, serverHandles.Zip(clientHandles).ToList());
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogError(sre,
                    "{Subscription}: Failed to call GetMonitoredItems on server", this);
                return new MonitoredItemsHandles(false, []);
            }
        }

        private readonly IMonitoredItemManagerContext m_context;
        private readonly List<MonitoredItem> m_deletedItems = [];
        private readonly Lock m_monitoredItemsLock = new();
        private readonly Dictionary<uint, MonitoredItem> m_monitoredItems = [];
        private readonly Dictionary<string, MonitoredItem> m_monitoredItemsByName = [];
        private readonly ILogger m_logger;
    }
}
