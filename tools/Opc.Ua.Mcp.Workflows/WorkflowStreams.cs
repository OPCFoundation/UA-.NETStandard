// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
