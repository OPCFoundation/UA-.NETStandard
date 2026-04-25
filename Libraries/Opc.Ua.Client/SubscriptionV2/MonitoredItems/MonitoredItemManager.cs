#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

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
                lock (_monitoredItemsLock)
                {
                    return new List<IMonitoredItem>(_monitoredItems.Values);
                }
            }
        }

        /// <inheritdoc/>
        public uint Count
        {
            get
            {
                lock (_monitoredItemsLock)
                {
                    return (uint)_monitoredItems.Count;
                }
            }
        }

        /// <summary>
        /// Create monitored item manager
        /// </summary>
        /// <param name="context"></param>
        /// <param name="telemetry"></param>
        public MonitoredItemManager(IMonitoredItemManagerContext context,
            IV2TelemetryContext telemetry)
        {
            _logger = telemetry.LoggerFactory.CreateLogger<MonitoredItemManager>();
            _context = context;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var monitoredItem in _monitoredItems.Values.ToList())
                {
                    await monitoredItem.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _monitoredItems.Clear();
                _monitoredItemsByName.Clear();
            }
        }

        /// <inheritdoc/>
        public bool TryAdd(string name, IOptionsMonitor<MonitoredItemOptions> options,
            out IMonitoredItem? monitoredItem)
        {
            lock (_monitoredItemsLock)
            {
                if (_monitoredItemsByName.TryGetValue(name, out var item))
                {
                    monitoredItem = item;
                    return false;
                }
                item = _context.CreateMonitoredItem(name, options, this);
                _monitoredItems.Add(item.ClientHandle, item);
                _monitoredItemsByName.Add(name, item);
                monitoredItem = item;
            }
            _context.Update();
            return true;
        }

        /// <inheritdoc/>
        public bool TryGetMonitoredItemByClientHandle(uint clientHandle,
            [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
        {
            lock (_monitoredItemsLock)
            {
                var result = _monitoredItems.TryGetValue(clientHandle, out var item);
                monitoredItem = item;
                return result;
            }
        }

        /// <inheritdoc/>
        public bool TryGetMonitoredItemByName(string name,
            [MaybeNullWhen(false)] out IMonitoredItem? monitoredItem)
        {
            lock (_monitoredItemsLock)
            {
                var result = _monitoredItemsByName.TryGetValue(name, out var item);
                monitoredItem = item;
                return result;
            }
        }

        /// <inheritdoc/>
        public bool TryRemove(uint clientHandle)
        {
            lock (_monitoredItemsLock)
            {
                if (!_monitoredItems.Remove(clientHandle, out var monitoredItem))
                {
                    return false;
                }
                _monitoredItemsByName.Remove(monitoredItem.Name);
                _deletedItems.Add(monitoredItem);
            }
            _context.Update();
            return true;
        }

        /// <inheritdoc/>
        public IReadOnlyList<IMonitoredItem> Update(IReadOnlyList<(
            string Name, IOptionsMonitor<MonitoredItemOptions> Options)> state)
        {
            var monitoredItems = new List<IMonitoredItem>(state.Count);
            lock (_monitoredItemsLock)
            {
                // Capture current state
                var remove = _monitoredItems.ToDictionary();

                // Generate items
                foreach (var (name, options) in state)
                {
                    if (!_monitoredItemsByName.TryGetValue(name, out var item))
                    {
                        // Create new item
                        item = _context.CreateMonitoredItem(name, options, this);
                        _monitoredItems.Add(item.ClientHandle, item);
                        _monitoredItemsByName.Add(name, item);
                    }
                    else
                    {
                        item.Options = options;
                        remove.Remove(item.ClientHandle);
                    }
                    monitoredItems.Add(item);
                }

                // Remove all items not in state
                foreach (var clientHandle in remove.Keys)
                {
                    if (_monitoredItems.Remove(clientHandle, out var monitoredItem))
                    {
                        _monitoredItemsByName.Remove(monitoredItem.Name);
                        _deletedItems.Add(monitoredItem);
                    }
                }
            }
            _context.Update();
            return monitoredItems;
        }

        /// <inheritdoc/>
        public void NotifyItemChange(MonitoredItem monitoredItem, bool itemDisposed)
        {
            Debug.Assert(monitoredItem != null);
            if (!itemDisposed)
            {
                _context.Update();
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
            lock (_monitoredItemsLock)
            {
                for (var i = 0; i < notification.MonitoredItems.Count; i++)
                {
                    var item = notification.MonitoredItems[i];
                    _monitoredItems.TryGetValue(item.ClientHandle, out var monitored);
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
            lock (_monitoredItemsLock)
            {
                for (var i = 0; i < notification.Events.Count; i++)
                {
                    var item = notification.Events[i];
                    _monitoredItems.TryGetValue(item.ClientHandle, out var monitored);
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
            var modified = false;
            while (!ct.IsCancellationRequested && TryGetMonitoredItemChanges(
                out var itemsToDelete, out var itemsToModify, resetAll))
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
            lock (_monitoredItemsLock)
            {
                foreach (var item in _monitoredItems.Values)
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
            lock (_monitoredItemsLock)
            {
                foreach (var item in _monitoredItems.Values)
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
                foreach (var dispose in itemsToDelete.Where(c => !c.Created))
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
                    var response = await _context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(null, _context.Id,
                        monitoredItemIds, ct).ConfigureAwait(false);
                    Ua.ClientBase.ValidateResponse(response.Results, monitoredItemIds);
                    Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos,
                        monitoredItemIds);

                    for (var index = 0; index < response.Results.Count; index++)
                    {
                        if (StatusCode.IsGood(response.Results[index]) ||
                            response.Results[index] == StatusCodes.BadMonitoredItemIdInvalid ||
                            !itemsToDelete[index].Created)
                        {
                            await itemsToDelete[index].DisposeAsync().ConfigureAwait(false);
                            continue;
                        }
                        _deletedItems.Add(itemsToDelete[index]); // Retry this
                        // TODO: Give up after a while
                    }
                }
                catch (Exception ex)
                {
                    _deletedItems.AddRange(itemsToDelete);
                    _logger.LogInformation(ex, "Failed to delete monitored items.");
                }
            }

            // To force recreate if item is created therefore, we need to delete the item first.
            var deletes = itemsToModify
                .Where(c => c.Item.Created && c.ForceRecreate)
                .ToList();
            if (deletes.Count > 0)
            {
                var monitoredItemIds = new ArrayOf<uint>(deletes.Select(c => c.Item.ServerId).ToArray());
                var response = await _context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(null, _context.Id,
                    monitoredItemIds, ct).ConfigureAwait(false);
                Ua.ClientBase.ValidateResponse(response.Results, monitoredItemIds);
                Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);
                for (var index = 0; index < response.Results.Count; index++)
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
            foreach (var group in modifications
                .Where(c => c.Modify != null)
                .GroupBy(c => c.Timestamps))
            {
                var monitoredItems = group.ToList();
                var requests = new ArrayOf<MonitoredItemModifyRequest>(group.Select(c => c.Modify!).ToArray());
                if (requests.Count > 0)
                {
                    var response = await _context.MonitoredItemServiceSet.ModifyMonitoredItemsAsync(null, _context.Id,
                        group.Key, requests, ct).ConfigureAwait(false);
                    Ua.ClientBase.ValidateResponse(response.Results, requests);
                    Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                    // update results.
                    for (var index = 0; index < response.Results.Count; index++)
                    {
                        monitoredItems[index].SetModifyResult(requests[index],
                            response.Results[index], index, response.DiagnosticInfos,
                            response.ResponseHeader);
                    }
                }
            }

            // Finalize by updating the monitoring mode where needed
            foreach (var mode in modifications
                .Where(c => c.MonitoringModeChange != null)
                .GroupBy(c => c.MonitoringModeChange!.Value))
            {
                var monitoredItems = mode
                    .Where(c => c.Item.Created && c.Item.CurrentMonitoringMode != mode.Key)
                    .ToList();
                var requests = new ArrayOf<uint>(monitoredItems.Select(c => c.Item.ServerId).ToArray());
                if (requests.Count > 0)
                {
                    var response = await _context.MonitoredItemServiceSet.SetMonitoringModeAsync(null, _context.Id,
                        mode.Key, requests, ct).ConfigureAwait(false);
                    Ua.ClientBase.ValidateResponse(response.Results, requests);
                    Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                    // update results.
                    for (var index = 0; index < response.Results.Count; index++)
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
            foreach (var group in creations
                .Where(c => c.Create != null)
                .GroupBy(c => c.Timestamps))
            {
                var monitoredItems = group.ToList();
                var requests = new ArrayOf<MonitoredItemCreateRequest>(group.Select(c => c.Create!).ToArray());
                if (requests.Count > 0)
                {
                    var response = await _context.MonitoredItemServiceSet.CreateMonitoredItemsAsync(null, _context.Id,
                        group.Key, requests, ct).ConfigureAwait(false);
                    Ua.ClientBase.ValidateResponse(response.Results, requests);
                    Ua.ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);
                    // update results.
                    for (var index = 0; index < response.Results.Count; index++)
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
            lock (_monitoredItemsLock)
            {
                itemsToDelete = [.. _deletedItems];
                _deletedItems.Clear();

                foreach (var monitoredItem in _monitoredItems.Values)
                {
                    if (resetAll)
                    {
                        monitoredItem.Reset();
                    }
                    // build item request.
                    if (monitoredItem.TryGetPendingChange(out var change))
                    {
                        itemsToModify.Add(change);
                    }
                }
            }
            if (itemsToDelete.Count == 0 &&
                itemsToModify.Count == 0)
            {
                return false;
            }
            return true;
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
            var (success, serverHandleStateMap) = await GetMonitoredItemsAsync(
                ct).ConfigureAwait(false);

            ArrayOf<uint> itemsToDelete;
            lock (_monitoredItemsLock)
            {
                if (!success)
                {
                    // Reset all items
                    foreach (var monitoredItem in _monitoredItems.Values)
                    {
                        monitoredItem.Reset();
                    }
                    return false;
                }

                var monitoredItems = _monitoredItems.ToDictionary();
                _monitoredItems.Clear();

                //
                // Assign the server id to the item with the matching client handle. This
                // handles the case where the CreateMonitoredItems call succeeded on the
                // server side, but the response was not provided back.
                //
                var clientServerHandleMap = serverHandleStateMap
                    .ToDictionary(m => m.clientHandle, m => m.serverHandle);
                foreach (var monitoredItem in monitoredItems.ToList())
                {
                    //
                    // Adjust any items where the server handles does not map to the
                    // handle the server has assigned.
                    //
                    var clientHandle = monitoredItem.Value.ClientHandle;
                    if (clientServerHandleMap.Remove(clientHandle, out var serverHandle))
                    {
                        monitoredItem.Value.SetTransferResult(clientHandle, serverHandle);
                        monitoredItems.Remove(monitoredItem.Key);
                        _monitoredItems.Add(clientHandle, monitoredItem.Value);
                    }
                }

                //
                // Assign the server side client handle to the item with the same server id
                // This handles the case where we are recreating the subscription from a
                // previously stored state.
                //
                var serverClientHandleMap = clientServerHandleMap
                    .ToDictionary(m => m.Value, m => m.Key);
                foreach (var monitoredItem in monitoredItems.ToList())
                {
                    var serverHandle = monitoredItem.Value.ServerId;
                    if (serverClientHandleMap.Remove(serverHandle, out var clientHandle))
                    {
                        //
                        // There should not be any more item with the same client handle
                        // in this subscription, they were updated before. TODO: Assert
                        //
                        monitoredItem.Value.SetTransferResult(clientHandle, serverHandle);
                        monitoredItems.Remove(monitoredItem.Key);
                        _monitoredItems.Add(clientHandle, monitoredItem.Value);
                    }
                }

                _deletedItems.Clear();
                itemsToDelete = new ArrayOf<uint>(serverClientHandleMap.Keys.ToArray());

                // Remaining items do not exist anymore on the server and need to be recreated
                foreach (var missingOnServer in monitoredItems.Values)
                {
                    if (missingOnServer.Created)
                    {
                        _logger.LogDebug("{Subscription}: Recreate client item {Item}.", this,
                            missingOnServer);
                        missingOnServer.Reset();
                    }
                    _monitoredItems.Add(missingOnServer.ClientHandle, missingOnServer);
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
                await _context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(null, _context.Id, itemsToDelete,
                    ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Subscription}: Failed to delete missing items.", this);
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
                        InputArguments = [new Variant(_context.Id)]
                    }
                });

                var response = await _context.MethodServiceSet.CallAsync(null, requests,
                    ct).ConfigureAwait(false);
                var results = response.Results;
                var diagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(results, requests);
                Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);
                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    throw ServiceResultException.Create(results[0].StatusCode,
                        0, diagnosticInfos, response.ResponseHeader.StringTable);
                }

                var outputArguments = results[0].OutputArguments;
                if (outputArguments.Count != 2 ||
                    outputArguments[0].AsBoxedObject() is not uint[] serverHandles ||
                    outputArguments[1].AsBoxedObject() is not uint[] clientHandles ||
                    clientHandles.Length != serverHandles.Length)
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "Output arguments incorrect");
                }
                return new MonitoredItemsHandles(true, serverHandles.Zip(clientHandles).ToList());
            }
            catch (ServiceResultException sre)
            {
                _logger.LogError(sre,
                    "{Subscription}: Failed to call GetMonitoredItems on server", this);
                return new MonitoredItemsHandles(false, []);
            }
        }

        private readonly IMonitoredItemManagerContext _context;
        private readonly List<MonitoredItem> _deletedItems = [];
        private readonly Lock _monitoredItemsLock = new();
        private readonly Dictionary<uint, MonitoredItem> _monitoredItems = [];
        private readonly Dictionary<string, MonitoredItem> _monitoredItemsByName = [];
        private readonly ILogger _logger;
    }
}
#endif
