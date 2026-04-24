// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client.Services;

    /// <summary>
    /// Context for monitored item manager. The monitored item
    /// manager manages the state of the monitored items in the
    /// subscription.
    /// </summary>
    internal interface IMonitoredItemManagerContext
    {
        /// <summary>
        /// Subscription id the monitored items are managed for
        /// </summary>
        uint Id { get; }

        /// <summary>
        /// Monitored item services
        /// </summary>
        IMonitoredItemServiceSet MonitoredItemServiceSet { get; }

        /// <summary>
        /// Method call services
        /// </summary>
        IMethodServiceSet MethodServiceSet { get; }

        /// <summary>
        /// Create monitored item
        /// </summary>
        /// <param name="name"></param>
        /// <param name="options"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItemOptions> options,
            IMonitoredItemContext context);

        /// <summary>
        /// Update
        /// </summary>
        void Update();
    }
}
