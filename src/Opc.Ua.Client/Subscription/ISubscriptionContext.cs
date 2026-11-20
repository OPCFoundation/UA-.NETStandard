// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.Subscriptions
{
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
        ISubscriptionServiceSetClientMethods SubscriptionServiceSet { get; }

        /// <summary>
        /// Get monitored item services
        /// </summary>
        IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet { get; }

        /// <summary>
        /// Call methods
        /// </summary>
        IMethodServiceSetClientMethods MethodServiceSet { get; }
    }
}
