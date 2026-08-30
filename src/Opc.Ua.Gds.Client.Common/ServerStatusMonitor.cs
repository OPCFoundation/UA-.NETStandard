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
using Opc.Ua.Client;
using V2 = Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Monitors the <c>Server_ServerStatus</c> variable of a connected server
    /// and forwards every data change to the owning client so it can raise its
    /// <c>ServerStatusChanged</c> event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both subscription engines are supported, because which one a client gets
    /// depends on the <see cref="ISessionFactory"/> it was constructed with:
    /// <see cref="DefaultSessionFactory"/> produces classic-engine sessions,
    /// while a <c>ManagedSession</c> opts into the V2 engine. The V2 engine
    /// never invokes the classic per-item <c>Notification</c> event, and the
    /// classic engine has no <c>ISubscriptionManager</c>, so the monitor picks
    /// the path the live session actually supports and normalizes both onto
    /// <see cref="ServerStatusChangedEventArgs"/>.
    /// </para>
    /// <para>
    /// One instance per client. <see cref="StartAsync"/> and
    /// <see cref="StopAsync"/> are called by the owning client while it holds
    /// its session lock, so they are never interleaved for the same instance.
    /// Every failure is logged and swallowed: server status monitoring is a
    /// convenience on top of the session and must never fail a connect.
    /// </para>
    /// </remarks>
    internal sealed class ServerStatusMonitor : V2.ISubscriptionNotificationHandler
    {
        /// <summary>
        /// Name of the monitored item created for <c>Server_ServerStatus</c>.
        /// </summary>
        public const string ServerStatusItemName = "ServerStatus";

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerStatusMonitor"/>
        /// class.
        /// </summary>
        /// <param name="options">The options of the owning client.</param>
        /// <param name="logger">The logger of the owning client.</param>
        /// <param name="onServerStatusChanged">Invoked for every notification
        /// received for the monitored item.</param>
        public ServerStatusMonitor(
            GdsClientOptions options,
            ILogger logger,
            Action<ServerStatusChangedEventArgs> onServerStatusChanged)
        {
            m_options = options;
            m_logger = logger;
            m_onServerStatusChanged = onServerStatusChanged;
        }

        /// <summary>
        /// Creates the subscription and the <c>Server_ServerStatus</c>
        /// monitored item on <paramref name="session"/>. Does nothing when
        /// <see cref="GdsClientOptions.MonitorServerStatus"/> is disabled.
        /// </summary>
        /// <param name="session">The freshly connected session.</param>
        /// <param name="ct">The cancellation token.</param>
        public async ValueTask StartAsync(ISession session, CancellationToken ct = default)
        {
            if (!m_options.MonitorServerStatus ||
                m_v2Subscription != null ||
                m_classicSubscription != null)
            {
                return;
            }

            NodeId serverStatusId = ExpandedNodeId.ToNodeId(
                Ua.VariableIds.Server_ServerStatus,
                session.NamespaceUris);

            if (session.TryGetSubscriptionManager(out V2.ISubscriptionManager? manager))
            {
                await StartV2Async(manager, serverStatusId).ConfigureAwait(false);
                return;
            }

            await StartClassicAsync(session, serverStatusId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the subscription from the server if one was created. Safe to
        /// call when monitoring was never started or when the session has
        /// already gone away.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        public async ValueTask StopAsync(CancellationToken ct = default)
        {
            V2.ISubscription? v2 = Interlocked.Exchange(ref m_v2Subscription, null);
            if (v2 != null)
            {
                await DisposeV2SubscriptionAsync(v2).ConfigureAwait(false);
            }

            Subscription? classic = Interlocked.Exchange(ref m_classicSubscription, null);
            ISession? session = Interlocked.Exchange(ref m_classicSession, null);
            if (classic != null)
            {
                try
                {
                    if (session != null)
                    {
                        await session.RemoveSubscriptionAsync(classic, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.FailedToStopMonitoringServerStatus(ex);
                }
                finally
                {
                    classic.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask OnDataChangeNotificationAsync(
            V2.ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<V2.DataValueChange> notification,
            V2.PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            ReadOnlySpan<V2.DataValueChange> changes = notification.Span;
            for (int ii = 0; ii < changes.Length; ii++)
            {
                V2.DataValueChange change = changes[ii];

                // Dispatch only what this monitor created. A change without a
                // monitored item cannot be attributed to it, so it is dropped
                // rather than surfaced as a server status.
                if (change.MonitoredItem?.Name != ServerStatusItemName)
                {
                    continue;
                }

                Raise(change.Value);
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnEventDataNotificationAsync(
            V2.ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<V2.EventNotification> notification,
            V2.PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnKeepAliveNotificationAsync(
            V2.ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            V2.PublishState publishStateMask)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnSubscriptionStateChangedAsync(
            V2.ISubscription subscription,
            V2.SubscriptionState state,
            V2.PublishState publishStateMask,
            CancellationToken ct = default)
        {
            return default;
        }

        /// <summary>
        /// V2 engine: the subscription and its monitored item are created on
        /// the server asynchronously by the engine, so nothing is awaited here.
        /// </summary>
        private async ValueTask StartV2Async(
            V2.ISubscriptionManager manager,
            NodeId serverStatusId)
        {
            V2.ISubscription? subscription = null;
            try
            {
                subscription = manager.Add(
                    this,
                    new OptionsMonitor<V2.SubscriptionOptions>(
                        new V2.SubscriptionOptions
                        {
                            PublishingInterval = m_options.ServerStatusPublishingInterval,
                            PublishingEnabled = true,
                            KeepAliveCount = kKeepAliveCount,
                            LifetimeCount = kLifetimeCount
                        }));

                if (!subscription.TryAddMonitoredItem(
                    ServerStatusItemName,
                    serverStatusId,
                    o => o with
                    {
                        SamplingInterval = m_options.ServerStatusSamplingInterval,
                        QueueSize = 1,
                        DiscardOldest = true
                    },
                    out _))
                {
                    m_logger.FailedToMonitorServerStatus(null);
                    await DisposeV2SubscriptionAsync(subscription).ConfigureAwait(false);
                    return;
                }

                m_v2Subscription = subscription;
            }
            catch (Exception ex)
            {
                m_logger.FailedToMonitorServerStatus(ex);
                if (subscription != null)
                {
                    await DisposeV2SubscriptionAsync(subscription).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Classic engine: the subscription and the monitored item are created
        /// on the server by explicit service calls, and notifications arrive on
        /// the per-item <c>Notification</c> event.
        /// </summary>
        private async ValueTask StartClassicAsync(
            ISession session,
            NodeId serverStatusId,
            CancellationToken ct)
        {
            Subscription? subscription = null;
            try
            {
                subscription = new Subscription(session.DefaultSubscription)
                {
                    DisplayName = ServerStatusItemName,
                    PublishingEnabled = true,
                    PublishingInterval =
                        ToMilliseconds(m_options.ServerStatusPublishingInterval),
                    KeepAliveCount = kKeepAliveCount,
                    LifetimeCount = kLifetimeCount
                };

                if (!session.AddSubscription(subscription))
                {
                    m_logger.FailedToMonitorServerStatus(null);
                    subscription.Dispose();
                    return;
                }

                await subscription.CreateAsync(ct).ConfigureAwait(false);

                var item = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = serverStatusId,
                    AttributeId = Attributes.Value,
                    DisplayName = ServerStatusItemName,
                    SamplingInterval =
                        ToMilliseconds(m_options.ServerStatusSamplingInterval),
                    QueueSize = 1,
                    DiscardOldest = true
                };
                item.Notification += OnClassicNotification;

                subscription.AddItem(item);
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

                m_classicSession = session;
                m_classicSubscription = subscription;
            }
            catch (Exception ex)
            {
                m_logger.FailedToMonitorServerStatus(ex);
                if (subscription != null)
                {
                    try
                    {
                        await session.RemoveSubscriptionAsync(subscription, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception removeException)
                    {
                        m_logger.FailedToStopMonitoringServerStatus(removeException);
                    }
                    subscription.Dispose();
                }
            }
        }

        private void OnClassicNotification(
            MonitoredItem monitoredItem,
            MonitoredItemNotificationEventArgs e)
        {
            if (e.NotificationValue is MonitoredItemNotification notification)
            {
                Raise(notification.Value);
            }
        }

        /// <summary>
        /// The classic API takes its intervals as milliseconds in an
        /// <see cref="int"/>, so a caller-supplied <see cref="TimeSpan"/> has
        /// to be clamped: a very large one overflows the cast, and a negative
        /// one is a misconfiguration of an option documented as an interval
        /// rather than the <c>-1</c> sentinel of the service. Zero leaves the
        /// interval to the server, which revises it to what it supports.
        /// </summary>
        private static int ToMilliseconds(TimeSpan interval)
        {
            double milliseconds = interval.TotalMilliseconds;

            if (milliseconds <= 0)
            {
                return 0;
            }

            return milliseconds >= int.MaxValue ? int.MaxValue : (int)milliseconds;
        }

        /// <summary>
        /// A subscriber that throws must not tear down the publish pipeline of
        /// the session the client uses for everything else.
        /// </summary>
        private void Raise(DataValue value)
        {
            try
            {
                m_onServerStatusChanged(new ServerStatusChangedEventArgs(value));
            }
            catch (Exception ex)
            {
                m_logger.SubscriberThrewInServerStatusChangedHandler(ex);
            }
        }

        /// <summary>
        /// Disposing a V2 subscription deletes it on the server, which fails
        /// when the session is already gone - a routine situation on a bad
        /// keep-alive teardown, so failures are logged, not raised.
        /// </summary>
        private async ValueTask DisposeV2SubscriptionAsync(V2.ISubscription subscription)
        {
            try
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.FailedToStopMonitoringServerStatus(ex);
            }
        }

        private const uint kKeepAliveCount = 10;
        private const uint kLifetimeCount = 100;

        private readonly GdsClientOptions m_options;
        private readonly ILogger m_logger;
        private readonly Action<ServerStatusChangedEventArgs> m_onServerStatusChanged;
        private V2.ISubscription? m_v2Subscription;
        private Subscription? m_classicSubscription;
        private ISession? m_classicSession;
    }

    internal static partial class ServerStatusMonitorLog
    {
        [LoggerMessage(EventId = GdsClientCommonEventIds.ServerStatusMonitor + 0, Level = LogLevel.Error,
            Message = "Failed to monitor Server_ServerStatus. ServerStatusChanged will not be raised.")]
        public static partial void FailedToMonitorServerStatus(this ILogger logger, Exception? ex);

        [LoggerMessage(EventId = GdsClientCommonEventIds.ServerStatusMonitor + 1, Level = LogLevel.Debug,
            Message = "Failed to stop monitoring Server_ServerStatus.")]
        public static partial void FailedToStopMonitoringServerStatus(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = GdsClientCommonEventIds.ServerStatusMonitor + 2, Level = LogLevel.Error,
            Message = "Subscriber threw in ServerStatusChanged handler.")]
        public static partial void SubscriberThrewInServerStatusChangedHandler(
            this ILogger logger, Exception ex);
    }
}
