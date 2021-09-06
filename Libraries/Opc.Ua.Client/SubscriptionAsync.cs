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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

#if NET_STANDARD_ASYNC

namespace Opc.Ua.Client
{
    /// <summary>
    /// The async interface for a subscription.
    /// </summary>
    public partial class Subscription
    {
        #region Public Async Methods (APM)
        /// <summary>
        /// Begin an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        /// <remarks>
        /// When the Create API is replaced with the async version,
        /// an extra call to Begin/EndCreateMonitoredItems is necessary.
        /// </remarks>
        public IAsyncResult BeginCreate(AsyncCallback callback)
        {
            VerifySubscriptionState(false);

            // create the subscription.
            uint keepAliveCount = m_keepAliveCount;
            uint lifetimeCounter = m_lifetimeCount;

            AdjustCounts(ref keepAliveCount, ref lifetimeCounter);

            return m_session.BeginCreateSubscription(
                null,
                m_publishingInterval,
                lifetimeCounter,
                keepAliveCount,
                m_maxNotificationsPerPublish,
                m_publishingEnabled,
                m_priority,
                callback,
                null);
        }

        /// <summary>
        /// Finish an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        public void EndCreate(IAsyncResult asyncResult)
        {
            uint subscriptionId;
            double revisedPublishingInterval;
            uint revisedKeepAliveCount;
            uint revisedLifetimeCounter;

            ResponseHeader responseHeader = m_session.EndCreateSubscription(
                asyncResult,
                out subscriptionId,
                out revisedPublishingInterval,
                out revisedLifetimeCounter,
                out revisedKeepAliveCount);

            // TODO: error handling

            UpdateSubscription(true, subscriptionId, revisedPublishingInterval, revisedKeepAliveCount, revisedLifetimeCounter);
        }

        /// <summary>
        /// Begin to create all items that have not already been created.
        /// </summary>
        public IAsyncResult BeginCreateMonitoredItems(AsyncCallback callback)
        {
            List<MonitoredItem> itemsToCreate;
            MonitoredItemCreateRequestCollection requestItems = PrepareItemsToCreate(out itemsToCreate);

            if (requestItems.Count == 0)
            {
                // return idle result
                var result = new ChannelAsyncOperation<int>(Int32.MaxValue, callback, null);
                result.Complete(0);
                return result;
            }

            var asyncState = new object[] { itemsToCreate, requestItems };

            // modify the subscription.
            return m_session.BeginCreateMonitoredItems(
                null,
                m_id,
                m_timestampsToReturn,
                requestItems,
                callback,
                asyncState);
        }

        /// <summary>
        /// Finish to create all items that have not already been created.
        /// </summary>
        public void EndCreateMonitoredItems(IAsyncResult asyncResult)
        {
            MonitoredItemCreateResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            var state = (object[])asyncResult.AsyncState;
            if (state == null)
            {
                return;
            }
            var itemsToCreate = (List<MonitoredItem>)state[0];
            var requestItems = (MonitoredItemCreateRequestCollection)state[1];

            ResponseHeader responseHeader = m_session.EndCreateMonitoredItems(asyncResult, out results, out diagnosticInfos);

            ClientBase.ValidateResponse(results, itemsToCreate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToCreate);

            // update results.
            for (int ii = 0; ii < results.Count; ii++)
            {
                itemsToCreate[ii].SetCreateResult(requestItems[ii], results[ii], ii, diagnosticInfos, responseHeader);
            }

            m_changeMask |= SubscriptionChangeMask.ItemsCreated;

            ChangesCompleted();
        }

        // TODO: BeginDelete/EndDelete

        /// <summary>
        /// Modifies a subscription on the server.
        /// </summary>
        public IAsyncResult BeginModify(AsyncCallback callback)
        {
            VerifySubscriptionState(false);

            // create the subscription.
            uint revisedKeepAliveCount = m_keepAliveCount;
            uint revisedLifetimeCounter = m_lifetimeCount;

            AdjustCounts(ref revisedKeepAliveCount, ref revisedLifetimeCounter);

            return m_session.BeginModifySubscription(
                null,
                m_id,
                m_publishingInterval,
                revisedLifetimeCounter,
                revisedKeepAliveCount,
                m_maxNotificationsPerPublish,
                m_priority,
                callback,
                null);
        }

        /// <summary>
        /// Finish an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        public void EndModify(IAsyncResult asyncResult)
        {
            double revisedPublishingInterval;
            uint revisedMaxKeepAliveCount;
            uint revisedLifetimeCount;

            ResponseHeader responseHeader = m_session.EndModifySubscription(
                asyncResult,
                out revisedPublishingInterval,
                out revisedLifetimeCount,
                out revisedMaxKeepAliveCount);

            // TODO: error handling

            // update current state.
            UpdateSubscription(
                false, 0,
                revisedPublishingInterval,
                revisedMaxKeepAliveCount,
                revisedLifetimeCount);
        }

        // TODO: SetPublishingMode
        #endregion

        #region Public Async Methods (TPL)
        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        // TODO: ct token?
        public async Task CreateAsync()
        {
            await Task.Factory.FromAsync(BeginCreate(EndCreate), EndCreate).ConfigureAwait(false);
            await CreateItemsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Creates all items on the server that have not already been created.
        /// </summary>
        // TODO: How to add a ct token?
        public Task CreateItemsAsync()
        {
            return Task.Factory.FromAsync(BeginCreateMonitoredItems(EndCreateMonitoredItems), EndCreateMonitoredItems);
        }

        /// <summary>
        /// Deletes a subscription on the server.
        /// </summary>
        public async Task DeleteAsync(bool silent, CancellationToken ct = default(CancellationToken))
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
        public async Task ModifyAsync(CancellationToken ct = default(CancellationToken))
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
            UpdateSubscription(
                false, 0,
                response.RevisedPublishingInterval,
                response.RevisedMaxKeepAliveCount,
                response.RevisedLifetimeCount);

            ChangesCompleted();
        }

        /// <summary>
        /// Changes the publishing enabled state for the subscription.
        /// </summary>
        public async Task SetPublishingModeAsync(bool enabled, CancellationToken ct = default(CancellationToken))
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


#if TODO
        /// <summary>
        /// Applies any changes to the subscription items.
        /// </summary>
        public async Task ApplyChangesAsync()
        {
            await DeleteItemsAsync();
            await ModifyItemsAsync();
            return await CreateItemsAsync();
        }
#endif

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

            // TODO: check response
        }
        #endregion
    }
}

#endif
