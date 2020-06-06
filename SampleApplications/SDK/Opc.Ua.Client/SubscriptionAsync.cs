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
using System.Threading.Tasks;

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

            UpdateSubscription(subscriptionId, revisedPublishingInterval, revisedKeepAliveCount, revisedLifetimeCounter);
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
                // TODO: what happens in this case?
                return null;
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
        #endregion

        #region Public Async Methods (TPL)
        public async Task CreateAsync()
        {
            await Task.Factory.FromAsync(BeginCreate(EndCreate), EndCreate);
            await CreateMonitoredItems();
        }

        public async Task CreateMonitoredItems()
        {
            await Task.Factory.FromAsync(BeginCreateMonitoredItems(EndCreateMonitoredItems), EndCreateMonitoredItems);
        }
        #endregion
    }
}
