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
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Subscription manager manages all subscription in a session
    /// </summary>
    public interface ISubscriptionManager
    {
        /// <summary>
        /// Return diagnostics mask to use when sending publish requests.
        /// </summary>
        DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <summary>
        /// Gets and sets the maximum number of publish requests to
        /// be used in the session.
        /// </summary>
        int MaxPublishWorkerCount { get; set; }

        /// <summary>
        /// Gets and sets the minimum number of publish requests to be
        /// used in the session.
        /// </summary>
        int MinPublishWorkerCount { get; set; }

        /// <summary>
        /// Get the number of current publishing workers
        /// </summary>
        int PublishWorkerCount { get; }

        /// <summary>
        /// Good publish requests
        /// </summary>
        int GoodPublishRequestCount { get; }

        /// <summary>
        /// Bad publish requests
        /// </summary>
        int BadPublishRequestCount { get; }

        /// <summary>
        /// Total number of missing notification messages detected across
        /// all subscriptions managed by this manager (sum of
        /// <see cref="ISubscription.MissingMessageCount"/>).
        /// </summary>
        long MissingMessageCount { get; }

        /// <summary>
        /// Total number of republish requests issued across all
        /// subscriptions managed by this manager (sum of
        /// <see cref="ISubscription.RepublishMessageCount"/>).
        /// </summary>
        long RepublishMessageCount { get; }

        /// <summary>
        /// Number of subscriptions
        /// </summary>
        int Count { get; }

        /// <summary>
        /// <para>
        /// When <c>true</c>, the V2 subscription dispatcher calls
        /// <see cref="IPooledEncodeable.Reuse"/> on notification payload
        /// instances (such as <c>MonitoredItemNotification</c>) after
        /// the handler returns, releasing them back to their
        /// activator-level pools for reuse.
        /// </para>
        /// <para>
        /// When <c>false</c> (default), instances are simply released
        /// to the GC as before.
        /// </para>
        /// <para>
        /// Setting this property takes effect on the next publish
        /// dispatch. Handlers that retain notification values past the
        /// dispatch call must not enable this option, or must copy the
        /// retained values before returning from the handler.
        /// </para>
        /// </summary>
        bool PoolNotifications { get; set; }

        /// <summary>
        /// Subscriptions
        /// </summary>
        IEnumerable<ISubscription> Items { get; }

        /// <summary>
        /// Create new subscriptions inside the session.
        /// The subscription will not be created but will
        /// be created asynchronously. Disposing the session
        /// will remove it from the session but not from
        /// the server. The subscription must also explicitly
        /// be deleted using DeleteAsync to remove it from
        /// the server.
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        ISubscription Add(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options);
    }
}
