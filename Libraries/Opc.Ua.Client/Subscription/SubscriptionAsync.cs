/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#if CLIENT_ASYNC

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The async interface for a subscription.
    /// </summary>
    public partial class Subscription
    {
        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        public async Task CreateAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(false);

            // create the subscription.
            uint revisedMaxKeepAliveCount = KeepAliveCount;
            uint revisedLifetimeCount = LifetimeCount;

            AdjustCounts(ref revisedMaxKeepAliveCount, ref revisedLifetimeCount);

            CreateSubscriptionResponse response = await Session
                .CreateSubscriptionAsync(
                    null,
                    PublishingInterval,
                    revisedLifetimeCount,
                    revisedMaxKeepAliveCount,
                    MaxNotificationsPerPublish,
                    false,
                    Priority,
                    ct
                )
                .ConfigureAwait(false);

            CreateSubscription(
                response.SubscriptionId,
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount
            );

            await CreateItemsAsync(ct).ConfigureAwait(false);

            // only enable publishing afer CreateSubscription is called to avoid race conditions with subscription cleanup.
            if (PublishingEnabled)
            {
                await SetPublishingModeAsync(PublishingEnabled, ct).ConfigureAwait(false);
            }

            ChangesCompleted();
        }

        /// <summary>
        /// Deletes a subscription on the server.
        /// </summary>
        public async Task DeleteAsync(bool silent, CancellationToken ct = default)
        {
            if (!silent)
            {
                VerifySubscriptionState(true);
            }

            // nothing to do if not created.
            if (!Created)
            {
                return;
            }

            await ResetPublishTimerAndWorkerStateAsync().ConfigureAwait(false);

            try
            {
                // delete the subscription.
                UInt32Collection subscriptionIds = new uint[] { Id };

                DeleteSubscriptionsResponse response = await Session
                    .DeleteSubscriptionsAsync(null, subscriptionIds, ct)
                    .ConfigureAwait(false);

                // validate response.
                ClientBase.ValidateResponse(response.Results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(
                        ClientBase.GetResult(response.Results[0], 0, response.DiagnosticInfos, response.ResponseHeader)
                    );
                }
            }
            // suppress exception if silent flag is set.
            catch (Exception e)
            {
                if (!silent)
                {
                    throw new ServiceResultException(e, StatusCodes.BadUnexpectedError);
                }
            }
            // always put object in disconnected state even if an error occurs.
            finally
            {
                DeleteSubscription();
            }

            ChangesCompleted();
        }

        /// <summary>
        /// Modifies a subscription on the server.
        /// </summary>
        public async Task ModifyAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            // modify the subscription.
            uint revisedKeepAliveCount = KeepAliveCount;
            uint revisedLifetimeCounter = LifetimeCount;

            AdjustCounts(ref revisedKeepAliveCount, ref revisedLifetimeCounter);

            ModifySubscriptionResponse response = await Session
                .ModifySubscriptionAsync(
                    null,
                    Id,
                    PublishingInterval,
                    revisedLifetimeCounter,
                    revisedKeepAliveCount,
                    MaxNotificationsPerPublish,
                    Priority,
                    ct
                )
                .ConfigureAwait(false);

            // update current state.
            ModifySubscription(
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount
            );

            ChangesCompleted();
        }

        /// <summary>
        /// Changes the publishing enabled state for the subscription.
        /// </summary>
        public async Task SetPublishingModeAsync(bool enabled, CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            // modify the subscription.
            UInt32Collection subscriptionIds = new uint[] { Id };

            SetPublishingModeResponse response = await Session
                .SetPublishingModeAsync(null, enabled, new uint[] { Id }, ct)
                .ConfigureAwait(false);

            // validate response.
            ClientBase.ValidateResponse(response.Results, subscriptionIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

            if (StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(
                    ClientBase.GetResult(response.Results[0], 0, response.DiagnosticInfos, response.ResponseHeader)
                );
            }

            // update current state.
            CurrentPublishingEnabled = PublishingEnabled = enabled;
            m_changeMask |= SubscriptionChangeMask.Modified;

            ChangesCompleted();
        }

        /// <summary>
        /// Republishes the specified notification message.
        /// </summary>
        public async Task<NotificationMessage> RepublishAsync(uint sequenceNumber, CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            RepublishResponse response = await Session
                .RepublishAsync(null, Id, sequenceNumber, ct)
                .ConfigureAwait(false);

            return response.NotificationMessage;
        }

        /// <summary>
        /// Applies any changes to the subscription items.
        /// </summary>
        public async Task ApplyChangesAsync(CancellationToken ct = default)
        {
            await DeleteItemsAsync(ct).ConfigureAwait(false);
            await ModifyItemsAsync(ct).ConfigureAwait(false);
            await CreateItemsAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves all relative paths to nodes on the server.
        /// </summary>
        public async Task ResolveItemNodeIdsAsync(CancellationToken ct)
        {
            VerifySubscriptionState(true);

            // collect list of browse paths.
            var browsePaths = new BrowsePathCollection();
            var itemsToBrowse = new List<MonitoredItem>();

            PrepareResolveItemNodeIds(browsePaths, itemsToBrowse);

            // nothing to do.
            if (browsePaths.Count == 0)
            {
                return;
            }

            // translate browse paths.
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);

            BrowsePathResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, browsePaths);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToBrowse[ii]
                    .SetResolvePathResult(results[ii], ii, response.DiagnosticInfos, response.ResponseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsModified;
        }

        /// <summary>
        /// Creates all items on the server that have not already been created.
        /// </summary>
        public async Task<IList<MonitoredItem>> CreateItemsAsync(CancellationToken ct = default)
        {
            List<MonitoredItem> itemsToCreate;
            MonitoredItemCreateRequestCollection requestItems = PrepareItemsToCreate(out itemsToCreate);

            if (requestItems.Count == 0)
            {
                return itemsToCreate;
            }

            // create monitored items.
            CreateMonitoredItemsResponse response = await Session
                .CreateMonitoredItemsAsync(null, Id, TimestampsToReturn, requestItems, ct)
                .ConfigureAwait(false);

            MonitoredItemCreateResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, itemsToCreate);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, itemsToCreate);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToCreate[ii]
                    .SetCreateResult(
                        requestItems[ii],
                        results[ii],
                        ii,
                        response.DiagnosticInfos,
                        response.ResponseHeader
                    );
            }

            m_changeMask |= SubscriptionChangeMask.ItemsCreated;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToCreate;
        }

        /// <summary>
        /// Modifies all items that have been changed.
        /// </summary>
        public async Task<IList<MonitoredItem>> ModifyItemsAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            var requestItems = new MonitoredItemModifyRequestCollection();
            var itemsToModify = new List<MonitoredItem>();

            PrepareItemsToModify(requestItems, itemsToModify);

            if (requestItems.Count == 0)
            {
                return itemsToModify;
            }

            // modify the subscription.
            ModifyMonitoredItemsResponse response = await Session
                .ModifyMonitoredItemsAsync(null, Id, TimestampsToReturn, requestItems, ct)
                .ConfigureAwait(false);

            MonitoredItemModifyResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, itemsToModify);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, itemsToModify);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToModify[ii]
                    .SetModifyResult(
                        requestItems[ii],
                        results[ii],
                        ii,
                        response.DiagnosticInfos,
                        response.ResponseHeader
                    );
            }

            m_changeMask |= SubscriptionChangeMask.ItemsModified;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToModify;
        }

        /// <summary>
        /// Deletes all items that have been marked for deletion.
        /// </summary>
        public async Task<IList<MonitoredItem>> DeleteItemsAsync(CancellationToken ct)
        {
            VerifySubscriptionState(true);

            if (m_deletedItems.Count == 0)
            {
                return [];
            }

            List<MonitoredItem> itemsToDelete = m_deletedItems;
            m_deletedItems = [];

            var monitoredItemIds = new UInt32Collection();

            foreach (MonitoredItem monitoredItem in itemsToDelete)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            DeleteMonitoredItemsResponse response = await Session
                .DeleteMonitoredItemsAsync(null, Id, monitoredItemIds, ct)
                .ConfigureAwait(false);

            StatusCodeCollection results = response.Results;
            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToDelete[ii].SetDeleteResult(results[ii], ii, response.DiagnosticInfos, response.ResponseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsDeleted;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToDelete;
        }

        /// <summary>
        /// Set monitoring mode of items.
        /// </summary>
        public async Task<List<ServiceResult>> SetMonitoringModeAsync(
            MonitoringMode monitoringMode,
            IList<MonitoredItem> monitoredItems,
            CancellationToken ct = default
        )
        {
            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            VerifySubscriptionState(true);

            if (monitoredItems.Count == 0)
            {
                return null;
            }

            // get list of items to update.
            var monitoredItemIds = new UInt32Collection();
            foreach (MonitoredItem monitoredItem in monitoredItems)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            SetMonitoringModeResponse response = await Session
                .SetMonitoringModeAsync(null, Id, monitoringMode, monitoredItemIds, ct)
                .ConfigureAwait(false);

            StatusCodeCollection results = response.Results;
            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);

            // update results.
            var errors = new List<ServiceResult>();
            bool noErrors = UpdateMonitoringMode(
                monitoredItems,
                errors,
                results,
                response.DiagnosticInfos,
                response.ResponseHeader,
                monitoringMode
            );

            // raise state changed event.
            m_changeMask |= SubscriptionChangeMask.ItemsModified;
            ChangesCompleted();

            // return null list if no errors occurred.
            if (noErrors)
            {
                return null;
            }

            return errors;
        }

        /// <summary>
        /// Tells the server to refresh all conditions being monitored by the subscription.
        /// </summary>
        public async Task ConditionRefreshAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            var methodsToCall = new CallMethodRequestCollection
            {
                new CallMethodRequest()
                {
                    ObjectId = ObjectTypeIds.ConditionType,
                    MethodId = MethodIds.ConditionType_ConditionRefresh,
                    InputArguments = [new Variant(Id)],
                },
            };

            CallResponse response = await Session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Tells the server to refresh all conditions being monitored by the subscription for a specific
        /// monitoredItem for events.
        /// </summary>
        public async Task ConditionRefresh2Async(uint monitoredItemId, CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            var methodsToCall = new CallMethodRequestCollection
            {
                new CallMethodRequest()
                {
                    ObjectId = ObjectTypeIds.ConditionType,
                    MethodId = MethodIds.ConditionType_ConditionRefresh2,
                    InputArguments = [new Variant(Id), new Variant(monitoredItemId)],
                },
            };

            CallResponse response = await Session.CallAsync(null, methodsToCall, ct).ConfigureAwait(false);
        }
    }
}
#endif
