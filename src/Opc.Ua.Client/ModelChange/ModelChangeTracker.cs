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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.ModelChange
{
    /// <summary>
    /// Default <see cref="IModelChangeTracker"/> backed by a
    /// streaming subscription on the Server object's
    /// <c>GeneralModelChangeEventType</c> notifier.
    /// </summary>
    public sealed class ModelChangeTracker : IModelChangeTracker
    {
        private readonly IStreamingSubscription m_streaming;
        private readonly INodeCache? m_nodeCache;
        private readonly INamespaceTableRefresher? m_namespaceTables;
        private readonly ILogger m_logger;
        private CancellationTokenSource? m_cts;
        private Task? m_pumpTask;
        private bool m_disposed;

        /// <inheritdoc/>
        public event EventHandler<ModelChangedEventArgs>? ModelChanged;

        /// <inheritdoc/>
        public bool IsTracking { get; private set; }

        /// <summary>
        /// Initializes a new model change tracker.
        /// </summary>
        /// <param name="streaming">Streaming subscription used to
        /// receive the server's model change events.</param>
        /// <param name="nodeCache">Optional node cache to invalidate
        /// when changes arrive.</param>
        /// <param name="logger">Optional logger.</param>
        /// <param name="namespaceTables">Optional namespace table to
        /// refresh when a change indicates that the server namespace
        /// array may have changed. Pass <c>null</c> to never refresh.
        /// <see cref="Session"/> and <see cref="ManagedSession"/> both
        /// implement <see cref="INamespaceTableRefresher"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="streaming"/>
        /// is <c>null</c>.</exception>
        public ModelChangeTracker(
            IStreamingSubscription streaming,
            INodeCache? nodeCache = null,
            ILogger? logger = null,
            INamespaceTableRefresher? namespaceTables = null)
        {
            m_streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
            m_nodeCache = nodeCache;
            m_namespaceTables = namespaceTables;
            m_logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        /// <inheritdoc/>
        public ValueTask StartTrackingAsync(CancellationToken ct = default)
        {
            if (IsTracking)
            {
                return default;
            }

            m_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Capture the token before launching the pump task so a racing
            // StopTrackingAsync (which nulls m_cts) cannot NRE the lambda.
            CancellationToken pumpToken = m_cts.Token;
            m_pumpTask = Task.Run(() => PumpAsync(pumpToken), pumpToken);
            IsTracking = true;

            return default;
        }

        /// <inheritdoc/>
        public async ValueTask StopTrackingAsync(CancellationToken ct = default)
        {
            if (!IsTracking)
            {
                return;
            }

            IsTracking = false;
            CancellationTokenSource? cts = m_cts;
            m_cts = null;

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // already disposed
                }
            }

            if (m_pumpTask != null)
            {
                try
                {
                    await m_pumpTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
                m_pumpTask = null;
            }

            cts?.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            await StopTrackingAsync().ConfigureAwait(false);
        }

        private async Task PumpAsync(CancellationToken ct)
        {
            try
            {
                EventFilter filter = BuildModelChangeFilter();
                var options = new MonitoringOptions
                {
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    QueueSize = 50
                };

                IAsyncEnumerable<EventNotification> source =
                    m_streaming.SubscribeEventsAsync(ObjectIds.Server, filter, options, ct);
                await foreach (EventNotification notification in source.ConfigureAwait(false))
                {
                    await HandleNotificationAsync(notification, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                m_logger.ModelChangeTrackerPumpFailed(ex);
            }
        }

        private static EventFilter BuildModelChangeFilter()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventId));
            filter.AddSelectClause(ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            filter.AddSelectClause(ObjectTypeIds.GeneralModelChangeEventType,
                QualifiedName.From(BrowseNames.Changes));

            filter.WhereClause.Push(FilterOperator.OfType,
                Variant.From(ObjectTypeIds.BaseModelChangeEventType));

            return filter;
        }

        private async ValueTask HandleNotificationAsync(
            EventNotification notification,
            CancellationToken ct)
        {
            Variant[] fields = notification.Fields.ToArray() ?? [];
            if (fields.Length < 3)
            {
                return;
            }

            Variant changesVariant = fields[2];

            var changes = new List<ModelChange>();
            bool requiresFullInvalidation = false;

            if (changesVariant.TryGetValue(out ArrayOf<ExtensionObject> extObjs))
            {
                foreach (ExtensionObject ext in extObjs)
                {
                    if (ext.TryGetValue(out ModelChangeStructureDataType? change) &&
                        change != null)
                    {
                        changes.Add(new ModelChange(
                            (ModelChangeVerb)change.Verb,
                            change.Affected,
                            change.AffectedType));
                    }
                }
            }
            else
            {
                requiresFullInvalidation = true;
            }

            // Refresh the namespace table before anything else so
            // subscribers that re-browse from the ModelChanged handler —
            // and the per-node cache invalidation below — see NodeIds
            // from namespaces the server has just added.
            bool namespaceTableRefreshed = await TryRefreshNamespaceTablesAsync(
                changes, requiresFullInvalidation, ct).ConfigureAwait(false);

            try
            {
                if (requiresFullInvalidation)
                {
                    m_nodeCache?.Clear();
                }
                else
                {
                    foreach (ModelChange change in changes)
                    {
                        InvalidateCache(change);
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.ModelChangeTrackerFailedInvalidateCache(ex);
            }

            try
            {
                ModelChanged?.Invoke(this,
                    new ModelChangedEventArgs(changes, requiresFullInvalidation,
                        namespaceTableRefreshed));
            }
            catch (Exception ex)
            {
                m_logger.ModelChangeTrackerSubscriberThrew(ex);
            }
        }

        /// <summary>
        /// Re-reads the server namespace table when the reported change
        /// indicates that the server namespace array may have changed.
        /// A failure is logged and swallowed: the model change itself
        /// must still be surfaced and the pump must keep running.
        /// </summary>
        /// <returns><c>true</c> when the table was refreshed.</returns>
        private async ValueTask<bool> TryRefreshNamespaceTablesAsync(
            IReadOnlyList<ModelChange> changes,
            bool requiresFullInvalidation,
            CancellationToken ct)
        {
            INamespaceTableRefresher? namespaceTables = m_namespaceTables;
            if (namespaceTables == null ||
                !RequiresNamespaceTableRefresh(namespaceTables, changes,
                    requiresFullInvalidation))
            {
                return false;
            }
            try
            {
                await namespaceTables.FetchNamespaceTablesAsync(ct)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                m_logger.ModelChangeTrackerFailedRefreshNamespaceTables(ex);
                return false;
            }
        }

        /// <summary>
        /// Whether a reported change means the client's namespace table
        /// can no longer be trusted. A server that appends a namespace
        /// uri while running (for example when a NodeManager is
        /// registered live) updates <c>Server_NamespaceArray</c> and
        /// reports the change without per-node detail, so an
        /// unqualified change is treated as a namespace change too.
        /// </summary>
        private static bool RequiresNamespaceTableRefresh(
            INamespaceTableRefresher namespaceTables,
            IReadOnlyList<ModelChange> changes,
            bool requiresFullInvalidation)
        {
            if (requiresFullInvalidation)
            {
                return true;
            }
            int knownNamespaces = namespaceTables.NamespaceUris.Count;
            foreach (ModelChange change in changes)
            {
                if (change.AffectedNode == VariableIds.Server_NamespaceArray)
                {
                    return true;
                }
                if (IsUnresolvable(change.AffectedNode, knownNamespaces) ||
                    IsUnresolvable(change.TypeDefinition, knownNamespaces))
                {
                    return true;
                }
            }
            return false;

            static bool IsUnresolvable(NodeId nodeId, int knownNamespaces)
            {
                return !nodeId.IsNull && nodeId.NamespaceIndex >= knownNamespaces;
            }
        }

        private void InvalidateCache(ModelChange change)
        {
            if (m_nodeCache == null || change.Verb == ModelChangeVerb.None)
            {
                return;
            }

            // Targeted per-node invalidation. Falls back to Clear()
            // automatically through the INodeCache default impl when
            // an implementation doesn't override per-node eviction.
            m_nodeCache.InvalidateNode(change.AffectedNode);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ModelChangeTracker"/>.
    /// </summary>
    internal static partial class ModelChangeTrackerLog
    {
        [LoggerMessage(EventId = ClientEventIds.ModelChangeTracker + 0, Level = LogLevel.Error,
            Message = "ModelChangeTracker pump failed")]
        public static partial void ModelChangeTrackerPumpFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ModelChangeTracker + 1, Level = LogLevel.Warning,
            Message = "ModelChangeTracker failed to invalidate cache")]
        public static partial void ModelChangeTrackerFailedInvalidateCache(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ModelChangeTracker + 2, Level = LogLevel.Error,
            Message = "ModelChangeTracker subscriber threw")]
        public static partial void ModelChangeTrackerSubscriberThrew(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = ClientEventIds.ModelChangeTracker + 3, Level = LogLevel.Warning,
            Message = "ModelChangeTracker failed to refresh the namespace table")]
        public static partial void ModelChangeTrackerFailedRefreshNamespaceTables(
            this ILogger logger,
            Exception? exception);
    }

}
