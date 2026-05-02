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
    public sealed class DefaultSubscriptionEngine : ISubscriptionEngine
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="DefaultSubscriptionEngine"/> class.
        /// </summary>
        /// <param name="context">The session context that provides
        /// access to session state and services.</param>
        public DefaultSubscriptionEngine(
            ISubscriptionEngineContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            m_manager = new SubscriptionManager(
                new EngineContextAdapter(context),
                context.Telemetry.LoggerFactory,
                context.ReturnDiagnostics);

            // The V2 SubscriptionManager starts paused. Resume it so
            // publish workers begin processing as soon as a
            // subscription is added. Reconnect logic toggles
            // pause/resume around connection transitions.
            m_manager.Resume();
        }

        /// <inheritdoc/>
        public int GoodPublishRequestCount => m_manager.GoodPublishRequestCount;

        /// <inheritdoc/>
        public int BadPublishRequestCount => m_manager.BadPublishRequestCount;

        /// <inheritdoc/>
        public int PublishWorkerCount => m_manager.PublishWorkerCount;

        /// <summary>
        /// The underlying <see cref="ISubscriptionManager"/> that drives the
        /// new options-based subscription API. Exposed so callers can access
        /// the V2 manager via the engine.
        /// </summary>
        public Subscriptions.ISubscriptionManager SubscriptionManager => m_manager;

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
            ThrowIfDisposed();
            m_manager.Resume();
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
            ThrowIfDisposed();
            m_manager.Pause();
        }

        /// <inheritdoc/>
        public void ResumePublishing()
        {
            ThrowIfDisposed();
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

        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(m_disposed, this);
#else
            if (m_disposed)
            {
                throw new ObjectDisposedException(
                    typeof(DefaultSubscriptionEngine).FullName);
            }
#endif
        }

        /// <summary>
        /// Adapts <see cref="ISubscriptionEngineContext"/> to
        /// <see cref="ISubscriptionManagerContext"/> so the V2
        /// <see cref="SubscriptionManager"/> can operate against
        /// a classic session.
        /// </summary>
        private sealed class EngineContextAdapter : ISubscriptionManagerContext
        {
            public EngineContextAdapter(ISubscriptionEngineContext context)
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
                ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
                CancellationToken ct)
            {
                return m_context.PublishAsync(
                    requestHeader ?? new RequestHeader(),
                    subscriptionAcknowledgements, ct);
            }

            /// <inheritdoc/>
            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
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
            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct)
            {
                return m_context.DeleteSubscriptionsAsync(
                    requestHeader, subscriptionIds, ct);
            }

            private readonly ISubscriptionEngineContext m_context;
        }

        /// <summary>
        /// Adapts the classic session services exposed by
        /// <see cref="ISubscriptionEngineContext"/> into the
        /// <see cref="ISubscriptionContext"/> needed by the modern
        /// <see cref="Subscription"/> base class.
        /// </summary>
        private sealed class SubscriptionContextAdapter : ISubscriptionContext
        {
            public SubscriptionContextAdapter(ISubscriptionEngineContext context)
            {
                m_context = context;
                m_services = new ServiceSetAdapter(context);
            }

            /// <inheritdoc/>
            public TimeSpan SessionTimeout
                => TimeSpan.FromMilliseconds(m_context.OperationTimeout);

            /// <inheritdoc/>
            public ISubscriptionServiceSetClientMethods SubscriptionServiceSet => m_services;

            /// <inheritdoc/>
            public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet => m_services;

            /// <inheritdoc/>
            public IMethodServiceSetClientMethods MethodServiceSet => m_services;

            /// <inheritdoc/>
            public override string ToString()
            {
                return m_context.SessionId.ToString();
            }

            private readonly ISubscriptionEngineContext m_context;
            private readonly ServiceSetAdapter m_services;
        }

        /// <summary>
        /// Routes OPC UA service calls from subscriptions
        /// back through the classic
        /// <see cref="ISubscriptionEngineContext"/>. Operations
        /// that the subscriptions invoke for lifecycle
        /// management are delegated to the underlying session.
        /// </summary>
        private sealed class ServiceSetAdapter
            : ISubscriptionServiceSetClientMethods,
              IMonitoredItemServiceSetClientMethods,
              IMethodServiceSetClientMethods
        {
            public ServiceSetAdapter(ISubscriptionEngineContext context)
            {
                m_context = context;
            }

            /// <inheritdoc/>
            public ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
                RequestHeader? requestHeader,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                bool publishingEnabled,
                byte priority,
                CancellationToken ct)
            {
                return m_context.SubscriptionServiceSet.CreateSubscriptionAsync(
                    requestHeader,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    publishingEnabled,
                    priority,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
                RequestHeader? requestHeader,
                uint subscriptionId,
                double requestedPublishingInterval,
                uint requestedLifetimeCount,
                uint requestedMaxKeepAliveCount,
                uint maxNotificationsPerPublish,
                byte priority,
                CancellationToken ct)
            {
                return m_context.SubscriptionServiceSet.ModifySubscriptionAsync(
                    requestHeader,
                    subscriptionId,
                    requestedPublishingInterval,
                    requestedLifetimeCount,
                    requestedMaxKeepAliveCount,
                    maxNotificationsPerPublish,
                    priority,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
                RequestHeader? requestHeader,
                bool publishingEnabled,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct)
            {
                return m_context.SubscriptionServiceSet.SetPublishingModeAsync(
                    requestHeader,
                    publishingEnabled,
                    subscriptionIds,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<RepublishResponse> RepublishAsync(
                RequestHeader? requestHeader,
                uint subscriptionId,
                uint retransmitSequenceNumber,
                CancellationToken ct)
            {
                return m_context.SubscriptionServiceSet.RepublishAsync(
                    requestHeader,
                    subscriptionId,
                    retransmitSequenceNumber,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct)
            {
                return m_context.DeleteSubscriptionsAsync(
                    requestHeader, subscriptionIds, ct);
            }

            /// <inheritdoc/>
            public ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
                RequestHeader? requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
                CancellationToken ct)
            {
                return m_context.MonitoredItemServiceSet.CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToCreate,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
                RequestHeader? requestHeader,
                uint subscriptionId,
                TimestampsToReturn timestampsToReturn,
                ArrayOf<MonitoredItemModifyRequest> itemsToModify,
                CancellationToken ct)
            {
                return m_context.MonitoredItemServiceSet.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    timestampsToReturn,
                    itemsToModify,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
                RequestHeader? requestHeader,
                uint subscriptionId,
                ArrayOf<uint> monitoredItemIds,
                CancellationToken ct)
            {
                return m_context.MonitoredItemServiceSet.DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    monitoredItemIds,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
                RequestHeader? requestHeader,
                uint subscriptionId,
                MonitoringMode monitoringMode,
                ArrayOf<uint> monitoredItemIds,
                CancellationToken ct)
            {
                return m_context.MonitoredItemServiceSet.SetMonitoringModeAsync(
                    requestHeader,
                    subscriptionId,
                    monitoringMode,
                    monitoredItemIds,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<CallResponse> CallAsync(
                RequestHeader? requestHeader,
                ArrayOf<CallMethodRequest> methodsToCall,
                CancellationToken ct)
            {
                return m_context.MethodServiceSet.CallAsync(
                    requestHeader,
                    methodsToCall,
                    ct);
            }

            /// <inheritdoc/>
            /// <inheritdoc/>
            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader? requestHeader,
                ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
                CancellationToken ct)
            {
                return m_context.SubscriptionServiceSet.PublishAsync(
                    requestHeader,
                    subscriptionAcknowledgements,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                    RequestHeader? requestHeader,
                    ArrayOf<uint> subscriptionIds,
                    bool sendInitialValues,
                    CancellationToken ct)
            {
                return m_context.SubscriptionServiceSet.TransferSubscriptionsAsync(
                    requestHeader,
                    subscriptionIds,
                    sendInitialValues,
                    ct);
            }

            /// <inheritdoc/>
            public ValueTask<SetTriggeringResponse> SetTriggeringAsync(
                    RequestHeader? requestHeader,
                    uint subscriptionId,
                    uint triggeringItemId,
                    ArrayOf<uint> linksToAdd,
                    ArrayOf<uint> linksToRemove,
                    CancellationToken ct)
            {
                return m_context.MonitoredItemServiceSet.SetTriggeringAsync(
                    requestHeader,
                    subscriptionId,
                    triggeringItemId,
                    linksToAdd,
                    linksToRemove,
                    ct);
            }

            private readonly ISubscriptionEngineContext m_context;
        }

        /// <summary>
        /// A concrete <see cref="Subscription"/> that can
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
            protected override Subscriptions.MonitoredItems.MonitoredItem CreateMonitoredItem(
                string name,
                IOptionsMonitor<Subscriptions.MonitoredItems.MonitoredItemOptions> options,
                IMonitoredItemContext context,
                ITelemetryContext telemetry)
            {
                return new DefaultMonitoredItem(
                    context,
                    name,
                    options,
                    telemetry.LoggerFactory.CreateLogger<DefaultMonitoredItem>());
            }
        }

        /// <summary>
        /// A concrete <see cref="MonitoredItem"/> that can
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

        private readonly SubscriptionManager m_manager;
        private bool m_disposed;
    }
}
