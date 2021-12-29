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
using Opc.Ua.Bindings;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The async interface for a subscription.
    /// </summary>
    public partial class Subscription
    {
        #region Public Async Methods (TPL)
        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        public async Task CreateAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(false);

            // create the subscription.
            uint revisedMaxKeepAliveCount = m_keepAliveCount;
            uint revisedLifetimeCount = m_lifetimeCount;

            AdjustCounts(ref revisedMaxKeepAliveCount, ref revisedLifetimeCount);

            ManageMessageWorkerSemaphore();

            var response = await m_session.CreateSubscriptionAsync(
                null,
                m_publishingInterval,
                revisedLifetimeCount,
                revisedMaxKeepAliveCount,
                m_maxNotificationsPerPublish,
                m_publishingEnabled,
                m_priority,
                ct).ConfigureAwait(false);

            CreateSubscription(
                response.SubscriptionId,
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount);

            await CreateItemsAsync(ct).ConfigureAwait(false);

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
            if (!this.Created)
            {
                return;
            }

            try
            {
                // stop the publish timer.
                if (m_publishTimer != null)
                {
                    m_publishTimer.Dispose();
                    m_publishTimer = null;
                }

                // delete the subscription.
                UInt32Collection subscriptionIds = new uint[] { m_id };

                var response = await m_session.DeleteSubscriptionsAsync(
                    null,
                    subscriptionIds,
                    ct).ConfigureAwait(false);

                // validate response.
                ClientBase.ValidateResponse(response.Results, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

                if (StatusCode.IsBad(response.Results[0]))
                {
                    throw new ServiceResultException(
                        ClientBase.GetResult(response.Results[0], 0, response.DiagnosticInfos, response.ResponseHeader));
                }
            }

            // supress exception if silent flag is set. 
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
            uint revisedKeepAliveCount = m_keepAliveCount;
            uint revisedLifetimeCounter = m_lifetimeCount;

            AdjustCounts(ref revisedKeepAliveCount, ref revisedLifetimeCounter);

            var response = await m_session.ModifySubscriptionAsync(
                null,
                m_id,
                m_publishingInterval,
                revisedLifetimeCounter,
                revisedKeepAliveCount,
                m_maxNotificationsPerPublish,
                m_priority,
                ct).ConfigureAwait(false);

            // update current state.
            ModifySubscription(
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount);

            ChangesCompleted();
        }

        /// <summary>
        /// Changes the publishing enabled state for the subscription.
        /// </summary>
        public async Task SetPublishingModeAsync(bool enabled, CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            // modify the subscription.
            UInt32Collection subscriptionIds = new uint[] { m_id };

            var response = await m_session.SetPublishingModeAsync(
                null,
                enabled,
                new uint[] { m_id },
                ct).ConfigureAwait(false);

            // validate response.
            ClientBase.ValidateResponse(response.Results, subscriptionIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, subscriptionIds);

            if (StatusCode.IsBad(response.Results[0]))
            {
                throw new ServiceResultException(
                    ClientBase.GetResult(response.Results[0], 0, response.DiagnosticInfos, response.ResponseHeader));
            }

            // update current state.
            m_currentPublishingEnabled = m_publishingEnabled = enabled;
            m_changeMask |= SubscriptionChangeMask.Modified;

            ChangesCompleted();
        }

        /// <summary>
        /// Republishes the specified notification message.
        /// </summary>
        public async Task<NotificationMessage> RepublishAsync(uint sequenceNumber, CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            var response = await m_session.RepublishAsync(
                null,
                m_id,
                sequenceNumber,
                ct).ConfigureAwait(false);

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
            BrowsePathCollection browsePaths = new BrowsePathCollection();
            List<MonitoredItem> itemsToBrowse = new List<MonitoredItem>();

            PrepareResolveItemNodeIds(browsePaths, itemsToBrowse);

            // nothing to do.
            if (browsePaths.Count == 0)
            {
                return;
            }

            // translate browse paths.
            var response = await m_session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                browsePaths,
                ct).ConfigureAwait(false);

            BrowsePathResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, browsePaths);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToBrowse[ii].SetResolvePathResult(results[ii], ii, response.DiagnosticInfos, response.ResponseHeader);
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
            var response = await m_session.CreateMonitoredItemsAsync(
                null,
                m_id,
                m_timestampsToReturn,
                requestItems,
                ct).ConfigureAwait(false);

            MonitoredItemCreateResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, itemsToCreate);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, itemsToCreate);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToCreate[ii].SetCreateResult(requestItems[ii], results[ii], ii, response.DiagnosticInfos, response.ResponseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsCreated;
            ChangesCompleted();

            // return the list of items affected by the change.
            return itemsToCreate;
        }

        /// <summary>
        /// Modies all items that have been changed.
        /// </summary>
        public async Task<IList<MonitoredItem>> ModifyItemsAsync(CancellationToken ct = default)
        {
            VerifySubscriptionState(true);

            MonitoredItemModifyRequestCollection requestItems = new MonitoredItemModifyRequestCollection();
            List<MonitoredItem> itemsToModify = new List<MonitoredItem>();

            PrepareItemsToModify(requestItems, itemsToModify);

            if (requestItems.Count == 0)
            {
                return itemsToModify;
            }

            // modify the subscription.
            var response = await m_session.ModifyMonitoredItemsAsync(
                null,
                m_id,
                m_timestampsToReturn,
                requestItems,
                ct).ConfigureAwait(false);

            MonitoredItemModifyResultCollection results = response.Results;
            ClientBase.ValidateResponse(results, itemsToModify);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, itemsToModify);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToModify[ii].SetModifyResult(requestItems[ii], results[ii], ii, response.DiagnosticInfos, response.ResponseHeader);
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
                return new List<MonitoredItem>();
            }

            List<MonitoredItem> itemsToDelete = m_deletedItems;
            m_deletedItems = new List<MonitoredItem>();

            UInt32Collection monitoredItemIds = new UInt32Collection();

            foreach (MonitoredItem monitoredItem in itemsToDelete)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            var response = await m_session.DeleteMonitoredItemsAsync(
                null,
                m_id,
                monitoredItemIds,
                ct).ConfigureAwait(false);

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
            CancellationToken ct = default)
        {
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));

            VerifySubscriptionState(true);

            if (monitoredItems.Count == 0)
            {
                return null;
            }

            // get list of items to update.
            UInt32Collection monitoredItemIds = new UInt32Collection();
            foreach (MonitoredItem monitoredItem in monitoredItems)
            {
                monitoredItemIds.Add(monitoredItem.Status.Id);
            }

            var response = await m_session.SetMonitoringModeAsync(
                null,
                m_id,
                monitoringMode,
                monitoredItemIds,
                ct).ConfigureAwait(false);

            StatusCodeCollection results = response.Results;
            ClientBase.ValidateResponse(results, monitoredItemIds);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, monitoredItemIds);

            // update results.
            List<ServiceResult> errors = new List<ServiceResult>();
            bool noErrors = UpdateMonitoringMode(
                monitoredItems, errors, results,
                response.DiagnosticInfos, response.ResponseHeader,
                monitoringMode);

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
        public async Task ConditionRefreshAsync(CancellationToken ct = default(CancellationToken))
        {
            VerifySubscriptionState(true);

            var methodsToCall = new CallMethodRequestCollection();
            methodsToCall.Add(new CallMethodRequest() {
                MethodId = MethodIds.ConditionType_ConditionRefresh,
                InputArguments = new VariantCollection() { new Variant(m_id) }
            });

            var response = await m_session.CallAsync(
                null,
                methodsToCall,
                ct).ConfigureAwait(false);
        }

        #endregion
    }
}
#endif
