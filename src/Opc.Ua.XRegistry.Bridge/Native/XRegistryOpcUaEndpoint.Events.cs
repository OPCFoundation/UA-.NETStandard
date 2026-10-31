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

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.XRegistry.Protocol;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryOpcUaEndpoint
    {
        /// <inheritdoc/>
        public async IAsyncEnumerable<XRegistryChangeHint> WatchAsync(
            XRegistryCallContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            if (m_session is not ManagedSession managed ||
                !managed.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Native invalidation requires a ManagedSession with the V2 subscription manager.");
            }
            _ = await InspectAsync(context, cancellationToken).ConfigureAwait(false);
            var root = ExpandedNodeId.ToNodeId(m_root, m_session.NamespaceUris);
            EventFilter filter = XRegistryEventTypeRecord.EventFilters.Build(m_session.NamespaceUris,
                new EventRecordDecoderRegistry().RegisterxRegistryDecoders(m_session.NamespaceUris));
            var notifier = new InvalidationNotifier(filter,
            [
                ExpandedNodeId.ToNodeId(ObjectTypeIds.ModelUpdatedEventType, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(ObjectTypeIds.ModelSourceUpdatedEventType, m_session.NamespaceUris)
            ]);
            var subscriptionOptions = new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions
            {
                PublishingEnabled = true,
                PublishingInterval = TimeSpan.FromMilliseconds(100),
                DisableUnboundedItemMode = true
            });
            var itemOptions = new OptionsMonitor<MonitoringOptions>(new MonitoringOptions
            {
                StartNodeId = root,
                AttributeId = Attributes.EventNotifier,
                Filter = filter,
                QueueSize = 64,
                DiscardOldest = true
            });
            ISubscription subscription = manager.Add(notifier, subscriptionOptions);
            await using ConfiguredAsyncDisposable subscriptionLifetime = subscription.ConfigureAwait(false);
            if (!subscription.MonitoredItems.TryAdd("xregistry-events", itemOptions, out IMonitoredItem? item) ||
                item is null)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyMonitoredItems);
            }
            void ConnectionChanged(object? sender, ConnectionStateChangedEventArgs args)
            {
                if (args.NewState != ConnectionState.Connected)
                {
                    notifier.ConnectionLost();
                }
            }
            managed.ConnectionStateChanged += ConnectionChanged;
            try
            {
                var readyDeadline = new XRegistryOperationDeadline(
                    m_options.TimeProvider, m_options.PreparedOperationTimeout, cancellationToken);
                await using (readyDeadline.ConfigureAwait(false))
                {
                    while (!item.Created)
                    {
                        readyDeadline.Token.ThrowIfCancellationRequested();
                        if (ServiceResult.IsBad(item.Error))
                        {
                            throw new ServiceResultException(item.Error);
                        }
                        await XRegistryOperationDeadline.DelayAsync(
                            m_options.TimeProvider, TimeSpan.FromMilliseconds(10), readyDeadline.Token)
                            .ConfigureAwait(false);
                    }
                }
                yield return new XRegistryChangeHint("/", "subscribed");
                await foreach (XRegistryChangeHint hint in notifier.ReadAllAsync(cancellationToken).ConfigureAwait(
                    false))
                {
                    yield return hint;
                }
            }
            finally
            {
                managed.ConnectionStateChanged -= ConnectionChanged;
                subscription.MonitoredItems.TryRemove(item.ClientHandle);
                notifier.Complete();
            }
        }

        internal sealed class InvalidationNotifier(EventFilter filter, ArrayOf<NodeId> modelEventTypes = default)
            : ISubscriptionNotificationHandler
        {
            public IAsyncEnumerable<XRegistryChangeHint> ReadAllAsync(CancellationToken ct)
            {
                return m_hints.Reader.ReadAllAsync(ct);
            }

            public void Complete()
            {
                m_hints.Writer.TryComplete();
            }

            public void ConnectionLost(string reason = "connection-or-namespace-changed")
            {
                m_hints.Writer.TryWrite(new XRegistryChangeHint(null, reason));
                m_hints.Writer.TryComplete(new ServiceResultException(StatusCodes.BadSessionClosed,
                    "Re-inspect the reconnected session and recreate the native invalidation stream."));
            }

            public ValueTask OnDataChangeNotificationAsync(ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, ReadOnlyMemory<DataValueChange> notification, PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, ReadOnlyMemory<EventNotification> notification, PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                foreach (EventNotification value in notification.Span)
                {
                    string? path = null;
                    NodeId eventType = default;
                    for (int index = 0; index < filter.SelectClauses.Count && index < value.Fields.Count; index++)
                    {
                        ArrayOf<QualifiedName> browse = filter.SelectClauses[index].BrowsePath;
                        if (browse.Count != 0 &&
                            browse[^1].Name == BrowseNames.Subject &&
                            value.Fields[index].TryGetValue(out string subject))
                        {
                            path = subject;
                        }
                        if (browse.Count != 0 && browse[^1].Name == Ua.BrowseNames.EventType)
                        {
                            _ = value.Fields[index].TryGetValue(out eventType);
                        }
                    }
                    foreach (NodeId modelEvent in modelEventTypes)
                    {
                        if (eventType == modelEvent)
                        {
                            ConnectionLost("model-changed");
                            return default;
                        }
                    }
                    m_hints.Writer.TryWrite(new XRegistryChangeHint(path,
                        path is null ? "unclassified-event-or-overflow" : "registry-changed"));
                }
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                if ((publishStateMask & (PublishState.Republish | PublishState.Recovered)) != 0)
                {
                    m_hints.Writer.TryWrite(new XRegistryChangeHint(null, "publish-gap-repair"));
                }
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(ISubscription subscription, SubscriptionState state,
                PublishState publishStateMask, CancellationToken ct = default)
            {
                if (state == SubscriptionState.Error ||
                    (publishStateMask &
                        (PublishState.Transferred | PublishState.Stopped | PublishState.Timeout)) != 0)
                {
                    ConnectionLost();
                }
                else if ((publishStateMask & (PublishState.Republish | PublishState.Recovered)) != 0)
                {
                    m_hints.Writer.TryWrite(new XRegistryChangeHint(null, "publish-gap-repair"));
                }
                return default;
            }

            private readonly Channel<XRegistryChangeHint> m_hints =
                Channel.CreateBounded<XRegistryChangeHint>(new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    AllowSynchronousContinuations = false
                });
        }
    }
}
