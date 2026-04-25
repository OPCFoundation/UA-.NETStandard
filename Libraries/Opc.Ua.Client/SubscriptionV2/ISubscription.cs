#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Subscription services
    /// </summary>
    public interface ISubscription : IAsyncDisposable
    {
        /// <summary>
        /// Created subscription
        /// </summary>
        bool Created { get; }

        /// <summary>
        /// The current publishing interval on the server
        /// </summary>
        TimeSpan CurrentPublishingInterval { get; }

        /// <summary>
        /// The current priority of the subscription
        /// </summary>
        byte CurrentPriority { get; }

        /// <summary>
        /// The current lifetime count on the server
        /// </summary>
        uint CurrentLifetimeCount { get; }

        /// <summary>
        /// The current keep alive count on the server
        /// </summary>
        uint CurrentKeepAliveCount { get; }

        /// <summary>
        /// The current publishing enabled state
        /// </summary>
        bool CurrentPublishingEnabled { get; }

        /// <summary>
        /// Current max notifications per publish
        /// </summary>
        uint CurrentMaxNotificationsPerPublish { get; }

        /// <summary>
        /// Monitored items
        /// </summary>
        IMonitoredItemCollection MonitoredItems { get; }

        /// <summary>
        /// Tells the server to refresh all conditions being
        /// monitored by the subscription.
        /// </summary>
        /// <param name="ct"></param>
        ValueTask ConditionRefreshAsync(
            CancellationToken ct = default);
    }
}
#endif
