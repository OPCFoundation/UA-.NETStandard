#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua.Client.Services;
    using System;

    /// <summary>
    /// Subscription context
    /// </summary>
    internal interface ISubscriptionContext
    {
        /// <summary>
        /// Current session timeout
        /// </summary>
        TimeSpan SessionTimeout { get; }

        /// <summary>
        /// Get subscription services
        /// </summary>
        ISubscriptionServiceSet SubscriptionServiceSet { get; }

        /// <summary>
        /// Get monitored item services
        /// </summary>
        IMonitoredItemServiceSet MonitoredItemServiceSet { get; }

        /// <summary>
        /// Call methods
        /// </summary>
        IMethodServiceSet MethodServiceSet { get; }
    }
}
#endif
