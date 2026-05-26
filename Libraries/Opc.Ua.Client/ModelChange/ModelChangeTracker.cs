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
using MItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

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
        public ModelChangeTracker(
            IStreamingSubscription streaming,
            INodeCache? nodeCache = null,
            ILogger? logger = null)
        {
            m_streaming = streaming ?? throw new ArgumentNullException(nameof(streaming));
            m_nodeCache = nodeCache;
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
            m_pumpTask = Task.Run(() => PumpAsync(m_cts.Token), m_cts.Token);
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
                var options = new MItemOptions
                {
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    QueueSize = 50
                };

                IAsyncEnumerable<EventNotification> source =
                    m_streaming.SubscribeEventsAsync(ObjectIds.Server, filter, options, ct);
                await foreach (EventNotification notification in source.ConfigureAwait(false))
                {
                    HandleNotification(notification);
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "ModelChangeTracker pump failed");
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
                Variant.From((NodeId)ObjectTypeIds.BaseModelChangeEventType));

            return filter;
        }

        private void HandleNotification(EventNotification notification)
        {
            Variant[] fields = notification.Fields.ToArray() ?? Array.Empty<Variant>();
            if (fields.Length < 3)
            {
                return;
            }

            Variant changesVariant = fields[2];

            var changes = new List<ModelChange>();
            bool requiresFullInvalidation = false;

            object? boxed = changesVariant.IsNull ? null : changesVariant.AsBoxedObject();
            if (boxed is ExtensionObject[] extObjs)
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
                m_logger.LogWarning(ex, "ModelChangeTracker failed to invalidate cache");
            }

            try
            {
                ModelChanged?.Invoke(this,
                    new ModelChangedEventArgs(changes, requiresFullInvalidation));
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "ModelChangeTracker subscriber threw");
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
}
