/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Borrows the host's streaming subscription; callers own only their returned observation enumerators.
    /// </summary>
    public interface IWorkflowStreams
    {
        /// <summary>
        /// Gets the selected session's borrowed streaming subscription.
        /// </summary>
        IStreamingSubscription GetStreaming(ISession session);

        /// <summary>
        /// Observes a finite caller-owned event item, optionally requesting a refresh after item creation.
        /// </summary>
        IAsyncEnumerable<EventNotification> Subscribe(
            IStreamingSubscription streaming, NodeId notifier, EventFilter filter,
            MonitoringOptions options, bool refresh, CancellationToken ct = default);
    }

    /// <summary>
    /// Default adapter to managed sessions and the SDK's event-stream lifecycle.
    /// </summary>
    public sealed class WorkflowStreams : IWorkflowStreams
    {
        /// <inheritdoc/>
        public IStreamingSubscription GetStreaming(ISession session)
        {
            return McpCompanionTools.GetStreaming(session);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<EventNotification> Subscribe(
            IStreamingSubscription streaming, NodeId notifier, EventFilter filter,
            MonitoringOptions options, bool refresh, CancellationToken ct = default)
        {
            return refresh
                ? streaming.SubscribeEventsWithRefreshAsync(notifier, filter, options, ct)
                : streaming.SubscribeEventsAsync(notifier, filter, options, ct);
        }
    }
}
