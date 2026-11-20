/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

            Subscription? subscription = null;
            bool ownsSubscription = false;
            MonitoredItem? item = null;
            try
            {
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
                        PublishingInterval = (int)Options.PublishingIntervalMs
                    };
                    client.Session.AddSubscription(subscription);

                    // Claim ownership before the first await that can fail:
                    // CreateAsync throwing must still tear the registration
                    // down, otherwise the subscription this cleanup exists to
                    // prevent is exactly what a failed start leaves behind.
                    ownsSubscription = true;
                    await subscription.CreateAsync(ct).ConfigureAwait(false);
                }

                item = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = lastChangeId,
                    AttributeId = Attributes.Value,
                    DisplayName = "LastChange",
                    SamplingInterval = (int)Options.SamplingIntervalMs,
                    QueueSize = 1,
                    DiscardOldest = true,
                    MonitoringMode = MonitoringMode.Reporting
                };
                item.Notification += OnNotification;
                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Do not leave a half-built subscription behind: the caller
                // retries StartAsync on every resolve, and each failed attempt
                // would otherwise add another subscription to the session.
                if (item != null && subscription != null)
                {
                    item.Notification -= OnNotification;
                    try
                    {
                        subscription.RemoveItem(item);
                    }
                    catch (Exception cleanupException)
                        when (cleanupException is not OutOfMemoryException)
                    {
                        // Best-effort cleanup.
                    }
                }
                if (ownsSubscription && subscription != null)
                {
                    try
                    {
                        // Cleanup must run to completion even when the caller's
                        // token is what aborted the start.
                        await client.Session
                            .RemoveSubscriptionAsync(subscription, CancellationToken.None)
                            .ConfigureAwait(false);
                        await subscription.DeleteAsync(silent: true, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupException)
                        when (cleanupException is not OutOfMemoryException)
                    {
                        // Best-effort cleanup; the session may already be gone.
                    }
                    finally
                    {
                        subscription.Dispose();
                    }
                }
                throw;
            }

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
