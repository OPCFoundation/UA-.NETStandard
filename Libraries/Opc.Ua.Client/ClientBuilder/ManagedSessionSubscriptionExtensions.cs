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
using NewMonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using NewSubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using V2 = Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Convenience extensions on <see cref="ManagedSession"/> for working
    /// with the new options-based subscription API. These wrap plain
    /// <see cref="V2.SubscriptionOptions"/> /
    /// <see cref="V2.MonitoredItems.MonitoredItemOptions"/> snapshots into
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
    /// adapters so callers don't need to set up a DI options pipeline for
    /// one-off use.
    /// </summary>
    public static class ManagedSessionSubscriptionExtensions
    {
        /// <summary>
        /// Add a new subscription to the session using the supplied options
        /// snapshot. The subscription is registered with
        /// <see cref="ManagedSession.SubscriptionManager"/> and starts
        /// asynchronously.
        /// </summary>
        public static V2.ISubscription AddSubscription(
            this ManagedSession session,
            V2.ISubscriptionNotificationHandler handler,
            NewSubscriptionOptions options)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var monitor = new OptionsMonitor<NewSubscriptionOptions>(options);
            return session.SubscriptionManager.Add(handler, monitor);
        }

        /// <summary>
        /// Add a new subscription to the session, configuring options via
        /// a callback over a fresh <see cref="V2.SubscriptionOptions"/>
        /// record.
        /// </summary>
        public static V2.ISubscription AddSubscription(
            this ManagedSession session,
            V2.ISubscriptionNotificationHandler handler,
            Func<NewSubscriptionOptions, NewSubscriptionOptions> configure)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return session.AddSubscription(handler, configure(new NewSubscriptionOptions()));
        }

        /// <summary>
        /// Add a monitored item to the subscription using the supplied
        /// options snapshot.
        /// </summary>
        public static bool TryAddMonitoredItem(
            this V2.ISubscription subscription,
            string name,
            NewMonitoredItemOptions options,
            out V2.MonitoredItems.IMonitoredItem? monitoredItem)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name is required", nameof(name));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var monitor = new OptionsMonitor<NewMonitoredItemOptions>(options);
            return subscription.MonitoredItems.TryAdd(name, monitor, out monitoredItem);
        }

        /// <summary>
        /// Add a monitored item to the subscription, configuring options
        /// via a callback over a fresh
        /// <see cref="V2.MonitoredItems.MonitoredItemOptions"/>
        /// record initialized with the supplied node id.
        /// </summary>
        public static bool TryAddMonitoredItem(
            this V2.ISubscription subscription,
            string name,
            NodeId nodeId,
            Func<NewMonitoredItemOptions, NewMonitoredItemOptions> configure,
            out V2.MonitoredItems.IMonitoredItem? monitoredItem)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("name is required", nameof(name));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return subscription.TryAddMonitoredItem(
                name,
                configure(new NewMonitoredItemOptions { StartNodeId = nodeId }),
                out monitoredItem);
        }
    }
}

