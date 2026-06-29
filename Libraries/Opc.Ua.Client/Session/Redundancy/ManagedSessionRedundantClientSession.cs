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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Adapts a <see cref="ManagedSession"/> for use by <see cref="RedundantManagedClient"/>.
    /// </summary>
    public sealed class ManagedSessionRedundantClientSession : IRedundantManagedClientSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedSessionRedundantClientSession"/> class.
        /// </summary>
        public ManagedSessionRedundantClientSession(
            ConfiguredEndpoint endpoint,
            Func<ConfiguredEndpoint, CancellationToken, ValueTask<ManagedSession>> sessionFactory)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        }

        /// <inheritdoc/>
        public event EventHandler<RedundantManagedClientNotificationEventArgs>? NotificationReceived;

        /// <inheritdoc/>
        public ConfiguredEndpoint Endpoint { get; }

        /// <inheritdoc/>
        public ManagedSession? Session { get; private set; }

        /// <inheritdoc/>
        public bool IsConnected => Session != null;

        /// <inheritdoc/>
        public byte ServiceLevel { get; private set; }

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (Session != null)
            {
                return;
            }

            Session = await m_sessionFactory(Endpoint, ct).ConfigureAwait(false);
            Session.Notification += OnNotification;
            await ReadServiceLevelAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken ct = default)
        {
            ManagedSession? session = Session;
            if (session == null)
            {
                return;
            }

            session.Notification -= OnNotification;
            await session.CloseAsync(0, closeChannel: true, ct).ConfigureAwait(false);
            Session = null;
        }

        /// <inheritdoc/>
        public async ValueTask ActivateMirroredSessionAsync(
            IRedundantManagedClientSession source,
            CancellationToken ct = default)
        {
            if (source is not ManagedSessionRedundantClientSession sourceSession ||
                sourceSession.Session == null)
            {
                throw new InvalidOperationException("A connected mirrored source session is required.");
            }

            ManagedSession mirroredSession = sourceSession.Session;
            if (Session != null && !ReferenceEquals(Session, mirroredSession))
            {
                await CloseAsync(ct).ConfigureAwait(false);
            }

            await mirroredSession
                .ReactivateMirroredSessionAsync(Endpoint, ct)
                .ConfigureAwait(false);

            sourceSession.DetachMirroredSession();
            Session = mirroredSession;
            Session.Notification += OnNotification;
            await ReadServiceLevelAsync(ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<byte> ReadServiceLevelAsync(CancellationToken ct = default)
        {
            if (Session == null)
            {
                return ServiceLevel;
            }

            DataValue value = await Session.ReadValueAsync(
                VariableIds.Server_ServiceLevel,
                ct).ConfigureAwait(false);
            if (StatusCode.IsGood(value.StatusCode) &&
                value.WrappedValue.TryGetValue(out byte serviceLevel))
            {
                ServiceLevel = serviceLevel;
            }

            return ServiceLevel;
        }

        /// <inheritdoc/>
        public ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
            IServerRedundancyHandler handler,
            CancellationToken ct = default)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (Session == null)
            {
                throw new InvalidOperationException("The redundant session is not connected.");
            }

            return handler.FetchRedundancyInfoAsync(Session, ct);
        }

        /// <inheritdoc/>
        public async ValueTask AddSubscriptionAsync(
            string subscriptionKey,
            Subscription template,
            MonitoringMode monitoringMode,
            bool publishingEnabled,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(subscriptionKey))
            {
                throw new ArgumentException("Subscription key must not be empty.", nameof(subscriptionKey));
            }

            if (template is null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (Session == null)
            {
                throw new InvalidOperationException("The redundant session is not connected.");
            }

            Subscription subscription = new(template, copyEventHandlers: true)
            {
                PublishingEnabled = publishingEnabled,
                Handle = subscriptionKey
            };
            SetTemplateMonitoringMode(subscription, monitoringMode);
            Session.AddSubscription(subscription);
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            m_subscriptions[subscriptionKey] = subscription;
        }

        /// <inheritdoc/>
        public async ValueTask SetSubscriptionStateAsync(
            MonitoringMode monitoringMode,
            bool publishingEnabled,
            CancellationToken ct = default)
        {
            foreach (Subscription subscription in m_subscriptions.Values)
            {
                await subscription.SetPublishingModeAsync(publishingEnabled, ct).ConfigureAwait(false);
                ArrayOf<MonitoredItem> monitoredItems = new(subscription.MonitoredItems.ToArray());
                await subscription.SetMonitoringModeAsync(
                    monitoringMode,
                    monitoredItems,
                    ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
        }

        private static void SetTemplateMonitoringMode(
            Subscription subscription,
            MonitoringMode monitoringMode)
        {
            foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
            {
                monitoredItem.MonitoringMode = monitoringMode;
            }
        }

        private void OnNotification(ISession session, NotificationEventArgs e)
        {
            if (e.NotificationMessage.NotificationData.IsEmpty)
            {
                return;
            }

            string serverUri = Endpoint.Description.Server?.ApplicationUri ?? string.Empty;
            foreach (ExtensionObject notificationData in e.NotificationMessage.NotificationData)
            {
                if (!notificationData.TryGetValue(out DataChangeNotification? dataChange))
                {
                    continue;
                }

                for (int ii = 0; ii < dataChange.MonitoredItems.Count; ii++)
                {
                    MonitoredItemNotification notification = dataChange.MonitoredItems[ii];
                    NotificationReceived?.Invoke(
                        this,
                        new RedundantManagedClientNotificationEventArgs(
                            serverUri,
                            e.Subscription?.Handle as string ?? string.Empty,
                            notification.ClientHandle,
                            e.NotificationMessage.SequenceNumber,
                            notification.Value));
                }
            }
        }

        private void DetachMirroredSession()
        {
            ManagedSession? session = Session;
            if (session != null)
            {
                session.Notification -= OnNotification;
                Session = null;
            }
        }

        private readonly Func<ConfiguredEndpoint, CancellationToken, ValueTask<ManagedSession>> m_sessionFactory;
        private readonly Dictionary<string, Subscription> m_subscriptions = new(StringComparer.Ordinal);
    }
}
