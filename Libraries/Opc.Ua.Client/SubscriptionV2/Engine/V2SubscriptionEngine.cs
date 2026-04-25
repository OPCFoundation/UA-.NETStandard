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

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.Client
{
    /// <summary>
    /// A subscription engine that wraps the V2
    /// <see cref="SubscriptionManager"/> to provide the
    /// <see cref="ISubscriptionEngine"/> contract. This allows
    /// a classic session to use the channel-based V2
    /// publish pipeline as a drop-in replacement for the
    /// classic fire-and-forget engine.
    /// </summary>
    public sealed class V2SubscriptionEngine : ISubscriptionEngine
    {
        private readonly SubscriptionManager m_manager;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="V2SubscriptionEngine"/> class.
        /// </summary>
        /// <param name="context">The session context that provides
        /// access to session state and services.</param>
        public V2SubscriptionEngine(
            ISubscriptionEngineContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var loggerFactory = context.Telemetry.LoggerFactory;
            m_manager = new SubscriptionManager(
                new EngineContextAdapter(context),
                loggerFactory,
                context.ReturnDiagnostics);
        }

        /// <inheritdoc/>
        public int GoodPublishRequestCount
            => m_manager.GoodPublishRequestCount;

        /// <inheritdoc/>
        public int BadPublishRequestCount
            => m_manager.BadPublishRequestCount;

        /// <inheritdoc/>
        public int PublishWorkerCount
            => m_manager.PublishWorkerCount;

        /// <inheritdoc/>
        public int MinPublishRequestCount
        {
            get => m_manager.MinPublishWorkerCount;
            set => m_manager.MinPublishWorkerCount = value;
        }

        /// <inheritdoc/>
        public int MaxPublishRequestCount
        {
            get => m_manager.MaxPublishWorkerCount;
            set => m_manager.MaxPublishWorkerCount = value;
        }

        /// <inheritdoc/>
        public void StartPublishing(int timeout, bool fullQueue)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            m_manager.Update();
        }

        /// <inheritdoc/>
        public async ValueTask StopPublishingAsync(
            CancellationToken ct)
        {
            if (!m_disposed)
            {
                await m_manager.DisposeAsync()
                    .ConfigureAwait(false);
                m_disposed = true;
            }
        }

        /// <inheritdoc/>
        public void PausePublishing()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            m_manager.Pause();
        }

        /// <inheritdoc/>
        public void ResumePublishing()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            m_manager.Resume();
        }

        /// <inheritdoc/>
        public void NotifySubscriptionsChanged()
        {
            if (!m_disposed)
            {
                m_manager.Update();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_manager.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                m_disposed = true;
            }
        }

        #region Nested adapters

        /// <summary>
        /// Adapts <see cref="ISubscriptionEngineContext"/> to
        /// <see cref="ISubscriptionManagerContext"/> so the V2
        /// <see cref="SubscriptionManager"/> can operate against
        /// a classic session.
        /// </summary>
        private sealed class EngineContextAdapter
            : ISubscriptionManagerContext
        {
            private readonly ISubscriptionEngineContext
                m_context;

            public EngineContextAdapter(
                ISubscriptionEngineContext context)
            {
                m_context = context;
            }

            /// <inheritdoc/>
            public IManagedSubscription CreateSubscription(
                ISubscriptionNotificationHandler handler,
                IOptionsMonitor<Subscriptions.SubscriptionOptions> options,
                IMessageAckQueue queue)
            {
                var subscriptionContext =
                    new SubscriptionContextAdapter(m_context);
                return new DefaultSubscription(
                    subscriptionContext, handler, queue,
                    options, m_context.Telemetry);
            }

            /// <inheritdoc/>
            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader? requestHeader,
                ArrayOf<SubscriptionAcknowledgement>
                    subscriptionAcknowledgements,
                CancellationToken ct)
            {
                return m_context.PublishAsync(
                    requestHeader ?? new RequestHeader(),
                    subscriptionAcknowledgements, ct);
            }

            /// <inheritdoc/>
            public ValueTask<TransferSubscriptionsResponse>
                TransferSubscriptionsAsync(
                    RequestHeader? requestHeader,
                    ArrayOf<uint> subscriptionIds,
                    bool sendInitialValues,
                    CancellationToken ct)
            {
                return m_context.TransferSubscriptionsAsync(
                    requestHeader, subscriptionIds,
                    sendInitialValues, ct);
            }

            /// <inheritdoc/>
            public ValueTask<DeleteSubscriptionsResponse>
                DeleteSubscriptionsAsync(
                    RequestHeader? requestHeader,
                    ArrayOf<uint> subscriptionIds,
                    CancellationToken ct)
            {
                return m_context.DeleteSubscriptionsAsync(
                    requestHeader, subscriptionIds, ct);
            }
        }

        /// <summary>
        /// Adapts the classic session services exposed by
        /// <see cref="ISubscriptionEngineContext"/> into the
        /// <see cref="ISubscriptionContext"/> needed by the V2
        /// <see cref="Subscription"/> base class.
        /// </summary>
        private sealed class SubscriptionContextAdapter
            : ISubscriptionContext
        {
            private readonly ISubscriptionEngineContext
                m_context;
            private readonly ServiceSetAdapter m_services;

            public SubscriptionContextAdapter(
                ISubscriptionEngineContext context)
            {
                m_context = context;
                m_services = new ServiceSetAdapter(context);
            }

            /// <inheritdoc/>
            public TimeSpan SessionTimeout
                => TimeSpan.FromMilliseconds(
                    m_context.OperationTimeout);

            /// <inheritdoc/>
            public Services.ISubscriptionServiceSet
                SubscriptionServiceSet => m_services;

            /// <inheritdoc/>
            public Services.IMonitoredItemServiceSet
                MonitoredItemServiceSet => m_services;

            /// <inheritdoc/>
            public Services.IMethodServiceSet
                MethodServiceSet => m_services;

            /// <inheritdoc/>
            public override string ToString()
            {
                return m_context.SessionId.ToString();
            }
        }

        /// <summary>
        /// Routes OPC UA service calls from V2 subscriptions
        /// back through the classic
        /// <see cref="ISubscriptionEngineContext"/>. Operations
        /// that the V2 subscriptions invoke for lifecycle
        /// management are delegated to the underlying session.
        /// </summary>
        private sealed class ServiceSetAdapter
            : Services.ISubscriptionServiceSet,
              Services.IMonitoredItemServiceSet,
              Services.IMethodServiceSet
        {
            private readonly ISubscriptionEngineContext
                m_context;

            public ServiceSetAdapter(
                ISubscriptionEngineContext context)
            {
                m_context = context;
            }

            // --- ISubscriptionServiceSet ---

            /// <inheritdoc/>
            public ValueTask<CreateSubscriptionResponse>
                CreateSubscriptionAsync(
                    RequestHeader? requestHeader,
                    double requestedPublishingInterval,
                    uint requestedLifetimeCount,
                    uint requestedMaxKeepAliveCount,
                    uint maxNotificationsPerPublish,
                    bool publishingEnabled,
                    byte priority,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "CreateSubscription is not yet " +
                    "supported through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<ModifySubscriptionResponse>
                ModifySubscriptionAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    double requestedPublishingInterval,
                    uint requestedLifetimeCount,
                    uint requestedMaxKeepAliveCount,
                    uint maxNotificationsPerPublish,
                    byte priority,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "ModifySubscription is not yet " +
                    "supported through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<SetPublishingModeResponse>
                SetPublishingModeAsync(
                    RequestHeader? requestHeader,
                    bool publishingEnabled,
                    ArrayOf<uint> subscriptionIds,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "SetPublishingMode is not yet " +
                    "supported through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<RepublishResponse>
                RepublishAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    uint retransmitSequenceNumber,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "Republish is not yet supported " +
                    "through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<DeleteSubscriptionsResponse>
                DeleteSubscriptionsAsync(
                    RequestHeader? requestHeader,
                    ArrayOf<uint> subscriptionIds,
                    CancellationToken ct)
            {
                return m_context.DeleteSubscriptionsAsync(
                    requestHeader, subscriptionIds, ct);
            }

            // --- IMonitoredItemServiceSet ---

            /// <inheritdoc/>
            public ValueTask<CreateMonitoredItemsResponse>
                CreateMonitoredItemsAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    TimestampsToReturn timestampsToReturn,
                    ArrayOf<MonitoredItemCreateRequest>
                        itemsToCreate,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "CreateMonitoredItems is not yet " +
                    "supported through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<ModifyMonitoredItemsResponse>
                ModifyMonitoredItemsAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    TimestampsToReturn timestampsToReturn,
                    ArrayOf<MonitoredItemModifyRequest>
                        itemsToModify,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "ModifyMonitoredItems is not yet " +
                    "supported through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<DeleteMonitoredItemsResponse>
                DeleteMonitoredItemsAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    ArrayOf<uint> monitoredItemIds,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "DeleteMonitoredItems is not yet " +
                    "supported through the V2 engine.");
            }

            /// <inheritdoc/>
            public ValueTask<SetMonitoringModeResponse>
                SetMonitoringModeAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    MonitoringMode monitoringMode,
                    ArrayOf<uint> monitoredItemIds,
                    CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "SetMonitoringMode is not yet " +
                    "supported through the V2 engine.");
            }

            // --- IMethodServiceSet ---

            /// <inheritdoc/>
            public ValueTask<CallResponse> CallAsync(
                RequestHeader? requestHeader,
                ArrayOf<CallMethodRequest> methodsToCall,
                CancellationToken ct)
            {
                // TODO: Route through the session when the
                // V2 engine owns the full subscription
                // lifecycle.
                throw new NotSupportedException(
                    "Call is not yet supported through " +
                    "the V2 engine.");
            }
        }

        /// <summary>
        /// A concrete V2 <see cref="Subscription"/> that can
        /// be instantiated by the engine context adapter. The
        /// base class is abstract so this thin subclass exposes
        /// the constructor and provides a default monitored item
        /// factory.
        /// </summary>
        private sealed class DefaultSubscription : Subscriptions.Subscription
        {
            public DefaultSubscription(
                ISubscriptionContext context,
                ISubscriptionNotificationHandler handler,
                IMessageAckQueue completion,
                IOptionsMonitor<Subscriptions.SubscriptionOptions> options,
                ITelemetryContext telemetry)
                : base(context, handler, completion,
                       options, telemetry)
            {
            }

            /// <inheritdoc/>
            protected override Subscriptions.MonitoredItems.MonitoredItem
                CreateMonitoredItem(
                    string name,
                    IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions>
                        options,
                    IMonitoredItemContext context,
                    ITelemetryContext telemetry)
            {
                return new DefaultMonitoredItem(
                    context, name, options,
                    telemetry.LoggerFactory
                        .CreateLogger<DefaultMonitoredItem>());
            }
        }

        /// <summary>
        /// A concrete V2 <see cref="MonitoredItem"/> that can
        /// be instantiated by the default subscription. The
        /// base class is abstract so this thin subclass simply
        /// exposes the constructor.
        /// </summary>
        private sealed class DefaultMonitoredItem
            : Subscriptions.MonitoredItems.MonitoredItem
        {
            public DefaultMonitoredItem(
                IMonitoredItemContext context,
                string name,
                IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions> options,
                ILogger logger)
                : base(context, name, options, logger)
            {
            }
        }

        #endregion
    }
}

#endif
