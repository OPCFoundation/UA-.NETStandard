// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client.Subscriptions;
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using System.Collections.Generic;

    /// <summary>
    /// Subscription client options
    /// </summary>
    public record class SubscriptionClientOptions
    {
        /// <summary>
        /// The monitored items to subscribe to
        /// </summary>
        public Dictionary<
            string,
            IOptionsMonitor<MonitoredItemOptions>
            > MonitoredItems
        { get; init; } = [];

        /// <summary>
        /// Subscription options
        /// </summary>
        public SubscriptionOptions? Options { get; init; }
    }
}
