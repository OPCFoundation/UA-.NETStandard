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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Abstracts one managed session participating in a redundant client.
    /// </summary>
    public interface IRedundantManagedClientSession : IAsyncDisposable
    {
        /// <summary>
        /// Raised when the session receives a notification.
        /// </summary>
        event EventHandler<RedundantManagedClientNotificationEventArgs>? NotificationReceived;

        /// <summary>
        /// Gets the endpoint for this session.
        /// </summary>
        ConfiguredEndpoint Endpoint { get; }

        /// <summary>
        /// Gets the underlying managed session, when available.
        /// </summary>
        ManagedSession? Session { get; }

        /// <summary>
        /// Gets a value indicating whether the session is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the last known service level.
        /// </summary>
        byte ServiceLevel { get; }

        /// <summary>
        /// Connects the session.
        /// </summary>
        ValueTask ConnectAsync(CancellationToken ct = default);

        /// <summary>
        /// Closes the session.
        /// </summary>
        ValueTask CloseAsync(CancellationToken ct = default);

        /// <summary>
        /// Activates a mirrored server-side session on this session's endpoint.
        /// </summary>
        ValueTask ActivateMirroredSessionAsync(
            IRedundantManagedClientSession source,
            CancellationToken ct = default);

        /// <summary>
        /// Reads and stores the current service level.
        /// </summary>
        ValueTask<byte> ReadServiceLevelAsync(CancellationToken ct = default);

        /// <summary>
        /// Reads redundancy information through the supplied handler.
        /// </summary>
        ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
            IServerRedundancyHandler handler,
            CancellationToken ct = default);

        /// <summary>
        /// Adds a replicated subscription.
        /// </summary>
        ValueTask AddSubscriptionAsync(
            string subscriptionKey,
            Subscription template,
            MonitoringMode monitoringMode,
            bool publishingEnabled,
            CancellationToken ct = default);

        /// <summary>
        /// Updates all replicated subscriptions on the session.
        /// </summary>
        ValueTask SetSubscriptionStateAsync(
            MonitoringMode monitoringMode,
            bool publishingEnabled,
            CancellationToken ct = default);
    }
}
