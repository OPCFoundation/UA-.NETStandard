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
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Di;
using IMonitoredItem = Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        /// <summary>
        /// Reads the <c>DeviceHealth</c> of an asset (OPC 10000-110 §9.2).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The health, or null when the asset publishes none.</returns>
        public async ValueTask<DeviceHealthEnumeration?> ReadDeviceHealthAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            Variant[] values = await ReadChildValuesAsync(
                asset,
                [[Di(Opc.Ua.Di.BrowseNames.DeviceHealth)]],
                cancellationToken).ConfigureAwait(false);
            return values[0].TryGetValue(out int health) ? (DeviceHealthEnumeration)health : null;
        }

        /// <summary>
        /// Reads the health alarms of an asset: the conditions it lists in its
        /// <c>2:DeviceHealthAlarms</c> folder (§9.3), or - without the folder -
        /// those a condition refresh reports for it, which are the retained
        /// ones.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The current state of each alarm.</returns>
        public async ValueTask<ArrayOf<AssetAlarmRecord>> ReadHealthAlarmsAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            var alarms = new List<AssetAlarmRecord>();
            HashSet<NodeId> maintenanceClasses = await MaintenanceClassesAsync(cancellationToken).ConfigureAwait(false);
            foreach (Variant[] values in await ReadConditionsAsync(asset, cancellationToken).ConfigureAwait(false))
            {
                if (!AmbConditionFields.IsMaintenance(values, maintenanceClasses))
                {
                    alarms.Add(AmbConditionFields.ToAlarm(values, Session.MessageContext));
                }
            }
            return alarms.ToArrayOf();
        }

        /// <summary>
        /// Reads the maintenance activities of an asset: the conditions with a
        /// maintenance condition class it lists in its
        /// <c>2:DeviceHealthAlarms</c> folder (§12.1), or - without the folder -
        /// those a condition refresh reports for it.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The current state of each activity.</returns>
        public async ValueTask<ArrayOf<MaintenanceActivityRecord>> ReadMaintenanceActivitiesAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            var activities = new List<MaintenanceActivityRecord>();
            HashSet<NodeId> maintenanceClasses = await MaintenanceClassesAsync(cancellationToken).ConfigureAwait(false);
            foreach (Variant[] values in await ReadConditionsAsync(asset, cancellationToken).ConfigureAwait(false))
            {
                if (AmbConditionFields.IsMaintenance(values, maintenanceClasses))
                {
                    activities.Add(AmbConditionFields.ToMaintenance(values, Session.MessageContext));
                }
            }
            return activities.ToArrayOf();
        }

        /// <summary>
        /// Follows the health alarms of assets as they are raised, updated,
        /// cleared and acknowledged: the conditions whose class is no
        /// maintenance condition class.
        /// </summary>
        /// <remarks>
        /// The stream carries every such condition of the notifier; filter on
        /// <see cref="AssetAlarmRecord.SourceNode"/> for one asset, or
        /// subscribe to the asset as notifier.
        /// </remarks>
        /// <param name="notifier">The notifier to subscribe to; the Server object when null.</param>
        /// <param name="streaming">
        /// The streaming subscription; the default one of a
        /// <see cref="ManagedSession"/> when null.
        /// </param>
        /// <param name="options">The monitored item options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        /// <returns>One record per event.</returns>
        public async IAsyncEnumerable<AssetAlarmRecord> ObserveHealthAlarmsAsync(
            NodeId notifier = default,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (ArrayOf<Variant> fields in ObserveConditionsAsync(
                    notifier,
                    streaming,
                    options,
                    maintenance: false,
                    cancellationToken).ConfigureAwait(false))
            {
                Variant[] values = fields.ToArray()!;
                if (values.Length >= AmbConditionFields.Count)
                {
                    yield return AmbConditionFields.ToAlarm(values, Session.MessageContext);
                }
            }
        }

        /// <summary>
        /// Follows the maintenance activities of assets through every
        /// transition and update.
        /// </summary>
        /// <param name="notifier">The notifier to subscribe to; the Server object when null.</param>
        /// <param name="streaming">
        /// The streaming subscription; the default one of a
        /// <see cref="ManagedSession"/> when null.
        /// </param>
        /// <param name="options">The monitored item options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        /// <returns>One record per event.</returns>
        public async IAsyncEnumerable<MaintenanceActivityRecord> ObserveMaintenanceAsync(
            NodeId notifier = default,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (ArrayOf<Variant> fields in ObserveConditionsAsync(
                    notifier,
                    streaming,
                    options,
                    maintenance: true,
                    cancellationToken).ConfigureAwait(false))
            {
                Variant[] values = fields.ToArray()!;
                if (values.Length >= AmbConditionFields.Count)
                {
                    yield return AmbConditionFields.ToMaintenance(values, Session.MessageContext);
                }
            }
        }

        /// <summary>
        /// Acknowledges a health alarm or a finished maintenance activity.
        /// </summary>
        /// <param name="conditionId">The condition.</param>
        /// <param name="eventId">The EventId of the event being acknowledged.</param>
        /// <param name="comment">The comment.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="ServiceResultException">The server refused the call.</exception>
        public ValueTask AcknowledgeAsync(
            NodeId conditionId,
            ByteString eventId,
            LocalizedText comment,
            CancellationToken cancellationToken = default)
        {
            return new AlarmClient(Session, Telemetry)
                .AcknowledgeAsync(conditionId, eventId, comment, cancellationToken);
        }

        private async IAsyncEnumerable<ArrayOf<Variant>> ObserveConditionsAsync(
            NodeId notifier,
            IStreamingSubscription? streaming,
            MonitoringOptions? options,
            bool maintenance,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IStreamingSubscription subscription = StreamingOf(streaming);
            HashSet<NodeId> maintenanceClasses = await MaintenanceClassesAsync(cancellationToken).ConfigureAwait(false);
            EventFilter filter = AmbConditionFields.Filter(Session.NamespaceUris, maintenanceClasses, maintenance);
            await foreach (EventNotification notification in subscription
                .SubscribeEventsAsync(
                    notifier.IsNull ? Ua.ObjectIds.Server : notifier,
                    filter,
                    options,
                    cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return notification.Fields;
            }
        }

        /// <summary>
        /// Gets the maintenance condition classes of the server:
        /// <c>MaintenanceConditionClassType</c> and its subtypes, read once
        /// per client; the classes the client knows when browsing fails.
        /// </summary>
        internal async ValueTask<HashSet<NodeId>> MaintenanceClassesAsync(CancellationToken cancellationToken)
        {
            HashSet<NodeId>? known = Volatile.Read(ref m_maintenanceClasses);
            if (known != null)
            {
                return known;
            }

            HashSet<NodeId> classes = AmbConditionFields.KnownMaintenanceClasses(Session.NamespaceUris);
            try
            {
                var pending = new Queue<(NodeId Type, int Depth)>();
                pending.Enqueue((Ua.ObjectTypeIds.MaintenanceConditionClassType, 0));
                while (pending.Count > 0)
                {
                    (NodeId type, int depth) = pending.Dequeue();
                    if (depth >= MaxDepth)
                    {
                        continue;
                    }
                    foreach (ReferenceDescription subtype in await BrowseAsync(
                        type,
                        Ua.ReferenceTypeIds.HasSubtype,
                        BrowseDirection.Forward,
                        cancellationToken,
                        NodeClass.ObjectType).ConfigureAwait(false))
                    {
                        NodeId subtypeId = ToNodeId(subtype.NodeId);
                        if (!subtypeId.IsNull && classes.Add(subtypeId))
                        {
                            pending.Enqueue((subtypeId, depth + 1));
                        }
                    }
                }
            }
            catch (ServiceResultException)
            {
                // The classes the client knows are a good enough answer;
                // they are not remembered, so the next call asks again.
                return classes;
            }
            Volatile.Write(ref m_maintenanceClasses, classes);
            return classes;
        }

        /// <summary>
        /// Asks the server for the conditions of an asset with a condition
        /// refresh (Part 9 §5.5.7) on a monitored item of its own: the server
        /// reports the retained conditions, bracketed by a RefreshStartEvent and
        /// a RefreshEndEvent.
        /// </summary>
        /// <remarks>
        /// Needs the subscription engine of a <see cref="ManagedSession"/>; on
        /// a classic session, or when the server does not answer in time, the
        /// conditions reported so far are returned.
        /// </remarks>
        internal async ValueTask<List<Variant[]>> RefreshConditionsAsync(
            NodeId asset,
            CancellationToken cancellationToken)
        {
            var conditions = new Dictionary<NodeId, Variant[]>();
            if (!Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                return [];
            }

            var collector = new RefreshCollector();
            ISubscription subscription = manager.Add(
                collector,
                new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 10,
                    LifetimeCount = 100
                }));
            await using (subscription.ConfigureAwait(false))
            {
                var itemOptions = new MonitoringOptions
                {
                    StartNodeId = Ua.ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    Filter = AmbConditionFields.RefreshFilter(Session.NamespaceUris, asset),
                    QueueSize = 1000
                };
                if (!subscription.MonitoredItems.TryAdd(
                        "amb_refresh_" + asset,
                        new OptionsMonitor<MonitoringOptions>(itemOptions),
                        out IMonitoredItem? item) ||
                    item == null)
                {
                    return [];
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RefreshTimeout);
                try
                {
                    while (!item.Created)
                    {
                        await Task.Delay(20, timeout.Token).ConfigureAwait(false);
                    }
                    await item.ConditionRefreshAsync(timeout.Token).ConfigureAwait(false);
                    using (timeout.Token.Register(() => collector.Ended.TrySetResult(false)))
                    {
                        await collector.Ended.Task.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // No answer in time: what arrived is what there is.
                }
                catch (ServiceResultException)
                {
                    // The server refused the refresh.
                }

                foreach (Variant[] values in collector.Conditions)
                {
                    NodeId condition = values[AmbConditionFields.ConditionId].TryGetValue(out NodeId id)
                        ? id
                        : NodeId.Null;
                    conditions[condition] = values;
                }
            }
            return [.. conditions.Values];
        }

        /// <summary>
        /// How long a condition refresh may take before the conditions
        /// reported so far are returned.
        /// </summary>
        private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(10);

        private HashSet<NodeId>? m_maintenanceClasses;

        /// <summary>
        /// Collects the events of a condition refresh until it ends.
        /// </summary>
        private sealed class RefreshCollector : ISubscriptionNotificationHandler
        {
            public TaskCompletionSource<bool> Ended { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<Variant[]> Conditions
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_conditions];
                    }
                }
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                foreach (EventNotification received in notification.Span)
                {
                    Variant[] values = received.Fields.ToArray() ?? [];
                    if (AmbConditionFields.IsRefreshEnd(values))
                    {
                        Ended.TrySetResult(true);
                    }
                    else if (values.Length >= AmbConditionFields.Count && !AmbConditionFields.IsRefreshBracket(values))
                    {
                        lock (m_lock)
                        {
                            m_conditions.Add(values);
                        }
                    }
                }
                return default;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }

            private readonly Lock m_lock = new();
            private readonly List<Variant[]> m_conditions = [];
        }

        /// <summary>
        /// Reads the fields of the conditions listed in the
        /// <c>2:DeviceHealthAlarms</c> folder of an asset.
        /// </summary>
        private async ValueTask<List<Variant[]>> ReadConditionsAsync(NodeId asset, CancellationToken cancellationToken)
        {
            var conditions = new List<Variant[]>();
            NodeId folder = await ResolveAsync(asset, cancellationToken, Di(Opc.Ua.Di.BrowseNames.DeviceHealthAlarms))
                .ConfigureAwait(false);
            if (folder.IsNull)
            {
                // The folder is what OPC 10000-100 recommends, not what every
                // asset has.
                return await RefreshConditionsAsync(asset, cancellationToken).ConfigureAwait(false);
            }

            AmbConditionFields.Field[] fields = AmbConditionFields.Build(Session.NamespaceUris);
            var paths = new QualifiedName[fields.Length - 1][];
            for (int ii = 1; ii < fields.Length; ii++)
            {
                paths[ii - 1] = fields[ii].Path;
            }

            var seen = new HashSet<NodeId>();
            foreach (ReferenceDescription reference in await BrowseAsync(
                folder,
                Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward,
                cancellationToken,
                NodeClass.Object).ConfigureAwait(false))
            {
                NodeId condition = ToNodeId(reference.NodeId);
                if (condition.IsNull || !seen.Add(condition))
                {
                    continue;
                }
                Variant[] read = await ReadChildValuesAsync(condition, paths, cancellationToken).ConfigureAwait(false);
                var values = new Variant[fields.Length];
                values[AmbConditionFields.ConditionId] = Variant.From(condition);
                for (int ii = 0; ii < read.Length; ii++)
                {
                    values[ii + 1] = read[ii];
                }
                if (values[AmbConditionFields.EventType].IsNull)
                {
                    values[AmbConditionFields.EventType] = Variant.From(ToNodeId(reference.TypeDefinition));
                }
                conditions.Add(values);
            }
            return conditions;
        }
    }
}
