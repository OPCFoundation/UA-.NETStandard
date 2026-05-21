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

namespace Opc.Ua.Client.AliasNames.Refresh
{
    /// <summary>
    /// Refresh strategy that creates an OPC UA <c>Subscription</c> +
    /// <c>MonitoredItem</c> on the category's <c>LastChange</c>
    /// variable (Part 17 §6.3.1) and invalidates the resolver cache on
    /// every notification whose value differs from the cached one
    /// (wrap-safe — compares for inequality, not strict greater-than).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pushes notifications instead of polling — preferable to
    /// <see cref="PollingAliasNameRefreshStrategy"/> whenever the
    /// server supports subscriptions and exposes <c>LastChange</c>.
    /// </para>
    /// <para>
    /// By default the strategy creates and owns its own
    /// <see cref="Subscription"/>; to share one with other monitored
    /// items pass it via
    /// <see cref="MonitoredItemAliasNameRefreshStrategyOptions.SharedSubscription"/>.
    /// </para>
    /// </remarks>
    public sealed class MonitoredItemAliasNameRefreshStrategy : IAliasNameRefreshStrategy
    {
        /// <summary>
        /// Initializes a new monitored-item-based strategy.
        /// </summary>
        /// <param name="options">Strategy tunables; defaults are applied
        /// when <c>null</c>.</param>
        public MonitoredItemAliasNameRefreshStrategy(
            MonitoredItemAliasNameRefreshStrategyOptions? options = null)
        {
            Options = options ?? new MonitoredItemAliasNameRefreshStrategyOptions();
        }

        /// <summary>
        /// The (snapshot of) configured options.
        /// </summary>
        public MonitoredItemAliasNameRefreshStrategyOptions Options { get; }

        /// <inheritdoc/>
        public async ValueTask StartAsync(
            AliasNameClient client,
            Action onInvalidate,
            CancellationToken ct)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            m_onInvalidate = onInvalidate ?? throw new ArgumentNullException(nameof(onInvalidate));

            NodeId lastChangeId = await client
                .ResolveLastChangeNodeIdAsync(ct)
                .ConfigureAwait(false);
            if (lastChangeId.IsNull)
            {
                // Category does not expose LastChange — no auto-invalidation
                // possible. The strategy becomes a no-op equivalent to
                // ManualAliasNameRefreshStrategy.
                return;
            }

            Subscription subscription;
            bool ownsSubscription = false;
            if (Options.SharedSubscription != null)
            {
                subscription = Options.SharedSubscription;
            }
            else
            {
                subscription = new Subscription(client.Session.MessageContext.Telemetry)
                {
                    DisplayName = Options.SubscriptionDisplayName,
                    PublishingEnabled = true,
                    PublishingInterval = (int)Options.PublishingIntervalMs,
                };
                client.Session.AddSubscription(subscription);
                await subscription.CreateAsync(ct).ConfigureAwait(false);
                ownsSubscription = true;
            }

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = lastChangeId,
                AttributeId = Attributes.Value,
                DisplayName = "LastChange",
                SamplingInterval = (int)Options.SamplingIntervalMs,
                QueueSize = 1,
                DiscardOldest = true,
                MonitoringMode = MonitoringMode.Reporting,
            };
            item.Notification += OnNotification;
            subscription.AddItem(item);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

            m_subscription = subscription;
            m_item = item;
            m_ownsSubscription = ownsSubscription;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            MonitoredItem? item = m_item;
            Subscription? subscription = m_subscription;
            bool owns = m_ownsSubscription;
            m_item = null;
            m_subscription = null;

            if (item != null)
            {
                item.Notification -= OnNotification;
                if (subscription != null)
                {
                    try
                    {
                        subscription.RemoveItem(item);
                        await subscription.ApplyChangesAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort cleanup; the session may already be
                        // closed.
                    }
                }
            }

            if (owns && subscription != null)
            {
                AliasNameClient? client = m_client;
                try
                {
                    if (client != null)
                    {
                        await client.Session
                            .RemoveSubscriptionAsync(subscription)
                            .ConfigureAwait(false);
                    }
                    await subscription.DeleteAsync(silent: true).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup.
                }
                finally
                {
                    subscription.Dispose();
                }
            }

            m_client = null;
            m_onInvalidate = null;
        }

        private void OnNotification(
            MonitoredItem item,
            MonitoredItemNotificationEventArgs e)
        {
            // The data-change notification carries a DataValue in the
            // first value of NotificationValue. Extract the uint and
            // compare on inequality.
            if (item.LastValue is not MonitoredItemNotification notification)
            {
                return;
            }
            if (!notification.Value.WrappedValue.TryGetValue(out uint current))
            {
                return;
            }
            uint? last = m_lastSeen;
            if (last != current)
            {
                m_lastSeen = current;
                m_onInvalidate?.Invoke();
            }
        }

        private AliasNameClient? m_client;
        private Action? m_onInvalidate;
        private Subscription? m_subscription;
        private MonitoredItem? m_item;
        private bool m_ownsSubscription;
        private uint? m_lastSeen;
    }
}
