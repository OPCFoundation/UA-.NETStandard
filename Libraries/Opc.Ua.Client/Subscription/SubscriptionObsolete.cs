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

#nullable disable

using System;
using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Subscription extensions that are obsolete.
    /// </summary>
    public static class SubscriptionObsolete
    {
        /// <summary>
        /// Creates a subscription on the server and adds all monitored items.
        /// </summary>
        [Obsolete("Use CreateAsync() instead.")]
        public static void Create(this Subscription subscription)
        {
            subscription.CreateAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Called after the subscription was transferred.
        /// </summary>
        [Obsolete("Use TransferAsync() instead.")]
        public static bool Transfer(
            this Subscription subscription,
            ISession session,
            uint id,
            UInt32Collection availableSequenceNumbers)
        {
            return subscription.TransferAsync(
                session,
                id,
                availableSequenceNumbers)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Deletes a subscription on the server.
        /// </summary>
        [Obsolete("Use DeleteAsync() instead.")]
        public static void Delete(this Subscription subscription, bool silent)
        {
            subscription.DeleteAsync(silent).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Modifies a subscription on the server.
        /// </summary>
        [Obsolete("Use ModifyAsync() instead.")]
        public static void Modify(this Subscription subscription)
        {
            subscription.ModifyAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Changes the publishing enabled state for the subscription.
        /// </summary>
        [Obsolete("Use SetPublishingModeAsync() instead.")]
        public static void SetPublishingMode(
            this Subscription subscription,
            bool enabled)
        {
            subscription.SetPublishingModeAsync(enabled)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Republishes the specified notification message.
        /// </summary>
        [Obsolete("Use RepublishAsync() instead.")]
        public static NotificationMessage Republish(
            this Subscription subscription,
            uint sequenceNumber)
        {
            return subscription.RepublishAsync(sequenceNumber)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Applies any changes to the subscription items.
        /// </summary>
        [Obsolete("Use ApplyChangesAsync() instead.")]
        public static void ApplyChanges(this Subscription subscription)
        {
            subscription.ApplyChangesAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Resolves all relative paths to nodes on the server.
        /// </summary>
        [Obsolete("Use ResolveItemNodeIdsAsync() instead.")]
        public static void ResolveItemNodeIds(this Subscription subscription)
        {
            subscription.ResolveItemNodeIdsAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates all items that have not already been created.
        /// </summary>
        [Obsolete("Use CreateItemsAsync() instead.")]
        public static IList<MonitoredItem> CreateItems(this Subscription subscription)
        {
            return subscription.CreateItemsAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Modifies all items that have been changed.
        /// </summary>
        [Obsolete("Use ModifyItemsAsync() instead.")]
        public static IList<MonitoredItem> ModifyItems(this Subscription subscription)
        {
            return subscription.ModifyItemsAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Deletes all items that have been marked for deletion.
        /// </summary>
        [Obsolete("Use DeleteItemsAsync() instead.")]
        public static IList<MonitoredItem> DeleteItems(this Subscription subscription)
        {
            return subscription.DeleteItemsAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Set monitoring mode of items.
        /// </summary>
        [Obsolete("Use SetMonitoringModeAsync() instead.")]
        public static List<ServiceResult> SetMonitoringMode(
            this Subscription subscription,
            MonitoringMode monitoringMode,
            IList<MonitoredItem> monitoredItems)
        {
            return subscription.SetMonitoringModeAsync(monitoringMode, monitoredItems)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Tells the server to refresh all conditions being
        /// monitored by the subscription.
        /// </summary>
        [Obsolete("Use ConditionRefreshAsync() instead.")]
        public static bool ConditionRefresh(this Subscription subscription)
        {
            return subscription.ConditionRefreshAsync()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Tells the server to refresh all conditions being monitored
        /// by the subscription for a specific monitoredItem for events.
        /// </summary>
        [Obsolete("Use ConditionRefresh2Async() instead.")]
        public static bool ConditionRefresh2(
            this Subscription subscription,
            uint monitoredItemId)
        {
            return subscription.ConditionRefresh2Async(monitoredItemId)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Call the ResendData method on the server for this subscription.
        /// </summary>
        [Obsolete("Use ResendDataAsync() instead.")]
        public static bool ResendData(this Subscription subscription)
        {
            return subscription.ResendDataAsync()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Call the GetMonitoredItems method on the server.
        /// </summary>
        [Obsolete("Use GetMonitoredItemsAsync() instead.")]
        public static bool GetMonitoredItems(
            this Subscription subscription,
            out UInt32Collection serverHandles,
            out UInt32Collection clientHandles)
        {
            (bool result, serverHandles, clientHandles) =
                subscription.GetMonitoredItemsAsync()
                .GetAwaiter()
                .GetResult();
            return result;
        }

        /// <summary>
        /// Set the subscription to durable.
        /// </summary>
        [Obsolete("Use SetSubscriptionDurableAsync() instead.")]
        public static bool SetSubscriptionDurable(
            this Subscription subscription,
            uint lifetimeInHours,
            out uint revisedLifetimeInHours)
        {
            (bool result, revisedLifetimeInHours) =
                subscription.SetSubscriptionDurableAsync(lifetimeInHours)
                .GetAwaiter()
                .GetResult();
            return result;
        }

        /// <summary>
        /// Get event type
        /// </summary>
        [Obsolete("Use GetEventTypeAsync() instead.")]
        public static INode GetEventType(
            this MonitoredItem monitoredItem,
            EventFieldList eventFields)
        {
            return monitoredItem.GetEventTypeAsync(eventFields)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }
}
