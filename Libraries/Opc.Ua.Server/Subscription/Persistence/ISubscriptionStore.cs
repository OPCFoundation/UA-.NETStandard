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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Interface for storing subscriptions on server shutdown and restoring on startup
    /// </summary>
    public interface ISubscriptionStore
    {
        /// <summary>
        /// Restore subscriptions from storage, called on server startup
        /// </summary>
        /// <returns>the templates to restore the susbscriptions from</returns>
        IEnumerable<IStoredSubscription> RestoreSubscriptions();
        /// <summary>
        /// Store subscriptions in storage, called on server shutdown
        /// </summary>
        /// <param name="subscriptions">the subscription templates to sture</param>
        /// <returns>true if storing was successful</returns>
        bool StoreSubscriptions(IEnumerable<IStoredSubscription> subscriptions);

        /// <summary>
        /// Restore a Monitored Item Queue from storage
        /// </summary>
        /// <param name="monitoredItemId">Id of monitored item owning the the queue</param>
        /// <returns>the queue</returns>
        IDataChangeMonitoredItemQueue RestoreDataChangeMonitoredItemQueue(uint monitoredItemId);

        /// <summary>
        /// Restore an event Monitored Item Queue from storage
        /// </summary>
        /// <param name="monitoredItemId">Id of the Monitored Item owning the queue</param>
        /// <returns>the queue</returns>
        IEventMonitoredItemQueue RestoreEventMonitoredItemQueue(uint monitoredItemId);

        /// <summary>
        /// Provide created Subscriptiosn incl MonitoredItems to SubscriptionStore, to signal cleanup can take place
        /// The store shall clean all stored subscriptions, monitoredItems, and only keep the persitent queues for the monitoredItemIds provided
        /// <param name="createdSubscriptions"> key = subscription id, value = monitoredItems </param>
        /// </summary>
        void OnSubscriptionRestoreComplete(Dictionary<uint, uint[]> createdSubscriptions);
    }
}
