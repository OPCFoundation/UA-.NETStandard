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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Diagnostics;
using UaLens.Subscriptions;
using UaLens.Telemetry;
using V2ItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2Options = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class ChannelV2EngineAdapterTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MetricsReflectTheSessionAndOptionalSubscriptionManager(bool supported)
    {
        var context = new AdapterContext(supported);
        await using ChannelV2EngineAdapter adapter = context.CreateAdapter();

        Assert.That(adapter.PublishWorkerCount, Is.EqualTo(supported ? 3 : 0));
        Assert.That(adapter.BadPublishRequestCount, Is.EqualTo(supported ? 4 : 0));
        Assert.That(adapter.MissingMessageCount, Is.EqualTo(supported ? 5 : 0));
        Assert.That(adapter.RepublishMessageCount, Is.EqualTo(supported ? 6 : 0));
        Assert.That(adapter.MinPublishWorkerCount, Is.EqualTo(supported ? 2 : 0));
        Assert.That(adapter.MaxPublishWorkerCount, Is.EqualTo(supported ? 9 : 0));
        Assert.That(adapter.GoodPublishRequestCount, Is.EqualTo(7));
        Assert.That(adapter.MinPublishRequestCount, Is.EqualTo(8));
        Assert.That(adapter.MaxPublishRequestCount, Is.EqualTo(19));
        Assert.That(adapter.HasWorkerPool, Is.True);
        Assert.That(adapter.CurrentPublishingInterval, Is.EqualTo(TimeSpan.Zero));
        Assert.That(adapter.CurrentKeepAliveCount, Is.Zero);
        Assert.That(adapter.CurrentLifetimeCount, Is.Zero);
        if (!supported)
        {
            Assert.That(() => adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("not available"));
            Assert.That(context.Adds, Is.Zero);
        }
    }

    [Test]
    public async Task SubscriptionIntentUpdatesTheSameMonitorWithoutReplacingServerRevisions()
    {
        var context = new AdapterContext();
        await using ChannelV2EngineAdapter adapter = context.CreateAdapter();
        var requested = new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(10),
            KeepAliveCount = 5,
            LifetimeCount = 150,
            MaxNotificationsPerPublish = 21,
            Priority = 11,
            PublishingEnabled = false
        };
        await adapter.ApplySubscriptionAsync(requested, CancellationToken.None).ConfigureAwait(false);
        IOptionsMonitor<V2Options> monitor = context.Options!;
        Assert.That(monitor.CurrentValue.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
        Assert.That(monitor.CurrentValue.KeepAliveCount, Is.EqualTo(5));
        Assert.That(monitor.CurrentValue.LifetimeCount, Is.EqualTo(150));
        Assert.That(monitor.CurrentValue.MaxNotificationsPerPublish, Is.EqualTo(21));
        Assert.That(monitor.CurrentValue.Priority, Is.EqualTo(11));
        Assert.That(monitor.CurrentValue.PublishingEnabled, Is.False);
        Assert.That(monitor.CurrentValue.Disabled, Is.False);
        Assert.That(monitor.CurrentValue.MinLifetimeInterval, Is.EqualTo(TimeSpan.FromMinutes(1)));
        await adapter.ApplySubscriptionAsync(requested with
        {
            PublishingInterval = TimeSpan.FromMilliseconds(1), PublishingEnabled = true
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.That(context.Adds, Is.EqualTo(1));
        Assert.That(context.Options, Is.SameAs(monitor));
        Assert.That(monitor.CurrentValue.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
        Assert.That(monitor.CurrentValue.PublishingEnabled, Is.True);
        Assert.That(adapter.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(12)));
        Assert.That(adapter.CurrentKeepAliveCount, Is.EqualTo(10));
        Assert.That(adapter.CurrentLifetimeCount, Is.EqualTo(300));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ItemSettingsSeparateRequestedValuesFromServerConfirmedState(bool events)
    {
        var context = new AdapterContext();
        await using ChannelV2EngineAdapter adapter = context.CreateAdapter();
        var filter = new DataChangeFilter
        {
            Trigger = DataChangeTrigger.StatusValueTimestamp, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 3
        };
        MonitoredItemConfig requested = Item(0, events) with { DataChangeFilter = filter };
        await Assert.ThatAsync(() => adapter.AddItemAsync(requested, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.Contains("Apply a subscription")).ConfigureAwait(false);
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None).ConfigureAwait(false);
        int id = await adapter.AddItemAsync(requested, CancellationToken.None).ConfigureAwait(false);
        V2ItemOptions options = context.ItemOptions[0].CurrentValue;
        Assert.That(id, Is.EqualTo(1));
        Assert.That(context.ItemNames.Single(), Is.EqualTo("item-1"));
        Assert.That(options.StartNodeId, Is.EqualTo(requested.NodeId));
        Assert.That(options.AttributeId, Is.EqualTo(requested.AttributeId));
        Assert.That(options.SamplingInterval, Is.EqualTo(requested.SamplingInterval));
        Assert.That(options.QueueSize, Is.EqualTo(4));
        Assert.That(options.DiscardOldest, Is.False);
        Assert.That(options.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
        if (events)
        {
            Assert.That(options.Filter, Is.TypeOf<EventFilter>());
            var eventFilter = (EventFilter)options.Filter!;
            Assert.That(eventFilter.SelectClauses.Count, Is.EqualTo(s_eventFields.Length));
            for (int i = 0; i < s_eventFields.Length; i++)
            {
                Assert.That(eventFilter.SelectClauses[i].TypeDefinitionId, Is.EqualTo(ObjectTypeIds.BaseEventType));
                Assert.That(eventFilter.SelectClauses[i].AttributeId, Is.EqualTo(Attributes.Value));
                Assert.That(eventFilter.SelectClauses[i].BrowsePath.Count, Is.EqualTo(1));
                Assert.That(eventFilter.SelectClauses[i].BrowsePath[0].Name, Is.EqualTo(s_eventFields[i]));
            }
        }
        else
        {
            Assert.That(options.Filter, Is.SameAs(filter));
        }
        Assert.That(adapter.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(27)));
        Assert.That(adapter.Items[0].QueueSize, Is.EqualTo(7));
        Assert.That(adapter.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        Assert.That(adapter.TryGetItemResult(id, out MonitoredItemResult result), Is.True);
        Assert.That(result, Is.EqualTo(new MonitoredItemResult(true, true, StatusCodes.Good)));

        await adapter.ConfigureItemAsync(requested with
        {
            Id = id, QueueSize = 12, DiscardOldest = true, DisplayName = "Updated"
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.That(context.ItemOptions[0].CurrentValue.QueueSize, Is.EqualTo(12));
        Assert.That(context.ItemOptions[0].CurrentValue.DiscardOldest, Is.True);
        Assert.That(adapter.Items[0].DisplayName, Is.EqualTo("Updated"));
        await adapter.SetMonitoringModeAsync(id, MonitoringMode.Disabled, CancellationToken.None).ConfigureAwait(false);
        Assert.That(context.ItemOptions[0].CurrentValue.MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
        Assert.That(adapter.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        await adapter.RemoveItemAsync(id, CancellationToken.None).ConfigureAwait(false);
        Assert.That(adapter.Items.IsEmpty, Is.True);
        Assert.That(adapter.TryGetItemStats(id, out _), Is.False);
        Assert.That(adapter.TryGetItemResult(id, out _), Is.False);
        context.Items.Verify(collection => collection.TryRemove(101), Times.Once);
        await adapter.RemoveItemAsync(id, CancellationToken.None).ConfigureAwait(false);
        context.Items.Verify(collection => collection.TryRemove(101), Times.Once);
    }

    [TestCase("target")]
    [TestCase("attribute")]
    [TestCase("kind")]
    [TestCase("missing")]
    [TestCase("canceled")]
    public async Task InvalidItemChangesCannotRedirectOrOverwriteAnExistingMonitor(string change)
    {
        var context = new AdapterContext();
        await using ChannelV2EngineAdapter adapter = context.CreateAdapter();
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None).ConfigureAwait(false);
        int id = await adapter.AddItemAsync(Item(0), CancellationToken.None).ConfigureAwait(false);
        V2ItemOptions original = context.ItemOptions[0].CurrentValue;
        MonitoredItemConfig updated = Item(id) with { QueueSize = 99 };
        updated = change switch
        {
            "target" => updated with { NodeId = new NodeId("Other", 2) },
            "attribute" => updated with { AttributeId = Attributes.DisplayName },
            "kind" => updated with { IsEvent = true },
            "missing" => updated with { Id = 700 },
            _ => updated
        };
        using var cancellation = new CancellationTokenSource();
        if (change == "canceled")
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        Type expected = change switch
        {
            "missing" => typeof(ServiceResultException),
            "canceled" => typeof(OperationCanceledException),
            _ => typeof(ArgumentException)
        };
        await Assert.ThatAsync(() => adapter.ConfigureItemAsync(updated, cancellation.Token),
            Throws.TypeOf(expected)).ConfigureAwait(false);
        Assert.That(context.ItemOptions[0].CurrentValue, Is.SameAs(original));
        Assert.That(adapter.Items[0].NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
        await adapter.SetMonitoringModeAsync(700, MonitoringMode.Disabled, CancellationToken.None).ConfigureAwait(false);
        Assert.That(context.ItemOptions[0].CurrentValue, Is.SameAs(original));
    }

    [TestCase(1, 500u)]
    [TestCase(2, 0u)]
    public async Task NotificationsPreserveKindsCountsValuesAndLogicalItemIdentity(int partitions, uint serverId)
    {
        var context = new AdapterContext();
        context.Subscription.As<IPartitionedSubscription>().SetupGet(subscription => subscription.PartitionIds)
            .Returns(partitions == 1 ? s_singlePartition : s_twoPartitions);
        var log = new PublishLogObserver(action => action());
        await using ChannelV2EngineAdapter adapter = context.CreateAdapter(log);
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None).ConfigureAwait(false);
        int id = await adapter.AddItemAsync(Item(0), CancellationToken.None).ConfigureAwait(false);
        DataValueChange[] values =
        [
            new(context.Monitored[0].Object, new DataValue(Variant.From(42)), null),
            new(null, new DataValue(Variant.From("text")), null),
            new(new Mock<IMonitoredItem>().Object, new DataValue(Variant.From(-5)), null)
        ];
        DateTime published = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await context.Handler!.OnDataChangeNotificationAsync(
            context.Subscription.Object, 71, published, values, PublishState.None, []).ConfigureAwait(false);
        EventNotification[] events = [new(context.Monitored[0].Object, []), new(null, [])];
        await context.Handler.OnEventDataNotificationAsync(
            context.Subscription.Object, 72, published, events, PublishState.None, []).ConfigureAwait(false);
        await context.Handler.OnKeepAliveNotificationAsync(
            context.Subscription.Object, 73, published, PublishState.None).ConfigureAwait(false);
        await context.Handler.OnSubscriptionStateChangedAsync(context.Subscription.Object,
            Opc.Ua.Client.Subscriptions.SubscriptionState.Created, PublishState.None).ConfigureAwait(false);

        var received = new List<NotificationEvent>();
        while (adapter.Events.TryRead(out NotificationEvent notification))
        {
            received.Add(notification);
        }
        Assert.That(received.Select(value => value.ItemId), Is.EqualTo(new[] { id, 0, 0, id, 0, 0 }));
        Assert.That(received.Select(value => value.SequenceNumber), Is.EqualTo(s_sequences));
        Assert.That(received.Select(value => value.Kind), Is.EqualTo(s_kinds));
        Assert.That(received[0].Value, Is.EqualTo(42));
        Assert.That(received[1].Value, Is.Null);
        Assert.That(received[2].Value, Is.EqualTo(-5));
        Assert.That(adapter.Counters.DataMessages, Is.EqualTo(1));
        Assert.That(adapter.Counters.DataValues, Is.EqualTo(3));
        Assert.That(adapter.Counters.EventMessages, Is.EqualTo(1));
        Assert.That(adapter.Counters.EventValues, Is.EqualTo(2));
        Assert.That(adapter.Counters.KeepAlives, Is.EqualTo(1));
        Assert.That(adapter.TryGetItemStats(id, out MonitoredItemLiveStats? stats), Is.True);
        Assert.That(stats!.Samples, Is.EqualTo(2));
        Assert.That(stats.LastValueText, Is.EqualTo("42"));
        Assert.That(stats.HasValue, Is.True);
        ArrayOf<PublishLogEntry> captured = log.CaptureSnapshot();
        Assert.That(captured.Count, Is.EqualTo(3));
        Assert.That(captured[0].SubscriptionId, Is.EqualTo(serverId));
        Assert.That(captured[0].SequenceNumber, Is.EqualTo(71));
        Assert.That(captured[0].PublishTimeUtc, Is.EqualTo(published));
        Assert.That(captured[0].NotifCount, Is.EqualTo(3));
        Assert.That(captured[0].ClientSubscriptionId, Is.EqualTo(captured[2].ClientSubscriptionId));
    }

    [TestCase(8191, 0)]
    [TestCase(8192, 0)]
    [TestCase(8193, 1)]
    public async Task BoundedDisplayQueueDropsOnlyTheOldestNotification(int count, int dropped)
    {
        var context = new AdapterContext();
        await using ChannelV2EngineAdapter adapter = context.CreateAdapter();
        for (int i = 0; i < count; i++)
        {
            adapter.WriteEventOrCount(new NotificationEvent(
                NotificationKind.DataChange, 1, 1, (uint)i, DateTime.UnixEpoch, i));
        }
        Assert.That(adapter.DroppedNotificationCount, Is.EqualTo(dropped));
        Assert.That(adapter.Events.Count, Is.EqualTo(Math.Min(count, 8192)));
        Assert.That(adapter.Events.TryRead(out NotificationEvent first), Is.True);
        Assert.That(first.SequenceNumber, Is.EqualTo(dropped));
        NotificationEvent last = first;
        while (adapter.Events.TryRead(out NotificationEvent next))
        {
            last = next;
        }
        Assert.That(last.SequenceNumber, Is.EqualTo(count - 1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposalCompletesTheNotificationReaderEvenWhenRemoteCleanupFails(bool fail)
    {
        var context = new AdapterContext();
        var failure = new InvalidOperationException("subscription release failed");
        context.Subscription.Setup(subscription => subscription.DisposeAsync())
            .Returns(() => fail ? ValueTask.FromException(failure) : ValueTask.CompletedTask);
        ChannelV2EngineAdapter adapter = context.CreateAdapter();
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None).ConfigureAwait(false);
        if (fail)
        {
            await Assert.ThatAsync(async () => await adapter.DisposeAsync().ConfigureAwait(false),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        }
        else
        {
            await adapter.DisposeAsync().ConfigureAwait(false);
        }
        Assert.That(await adapter.Events.WaitToReadAsync().ConfigureAwait(false), Is.False);
        await adapter.DisposeAsync().ConfigureAwait(false);
        context.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Once);
        context.Session.Verify(session => session.Dispose(), Times.Never);
    }

    private static MonitoredItemConfig Item(int id, bool events = false)
    {
        return new MonitoredItemConfig
        {
            Id = id, NodeId = new NodeId("Temperature", 2), DisplayName = "Temperature",
            SamplingInterval = TimeSpan.FromMilliseconds(13), QueueSize = 4, DiscardOldest = false,
            IsEvent = events, AttributeId = events ? Attributes.EventNotifier : Attributes.Value
        };
    }

    private sealed class AdapterContext
    {
        public AdapterContext(bool supported = true)
        {
            Subscription.As<IPartitionedSubscription>().SetupGet(subscription => subscription.PartitionIds)
                .Returns(s_singlePartition);
            Subscription.SetupGet(subscription => subscription.MonitoredItems).Returns(Items.Object);
            Subscription.SetupGet(subscription => subscription.CurrentPublishingInterval)
                .Returns(TimeSpan.FromMilliseconds(12));
            Subscription.SetupGet(subscription => subscription.CurrentKeepAliveCount).Returns(10u);
            Subscription.SetupGet(subscription => subscription.CurrentLifetimeCount).Returns(300u);
            Subscription.Setup(subscription => subscription.DisposeAsync()).Returns(ValueTask.CompletedTask);
            ISubscriptionManager? manager = supported ? Manager.Object : null;
            Session.Setup(session => session.TryGetSubscriptionManager(out manager)).Returns(supported);
            Session.SetupGet(session => session.GoodPublishRequestCount).Returns(7);
            Session.SetupGet(session => session.MinPublishRequestCount).Returns(8);
            Session.SetupGet(session => session.MaxPublishRequestCount).Returns(19);
            Manager.SetupGet(value => value.PublishWorkerCount).Returns(3);
            Manager.SetupGet(value => value.BadPublishRequestCount).Returns(4);
            Manager.SetupGet(value => value.MissingMessageCount).Returns(5L);
            Manager.SetupGet(value => value.RepublishMessageCount).Returns(6L);
            Manager.SetupGet(value => value.MinPublishWorkerCount).Returns(2);
            Manager.SetupGet(value => value.MaxPublishWorkerCount).Returns(9);
            Manager.Setup(value => value.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                It.IsAny<IOptionsMonitor<V2Options>>()))
                .Callback((ISubscriptionNotificationHandler handler, IOptionsMonitor<V2Options> options) =>
                {
                    Adds++;
                    Handler = handler;
                    Options = options;
                }).Returns(Subscription.Object);
            Items.Setup(value => value.TryAdd(It.IsAny<string>(), It.IsAny<IOptionsMonitor<V2ItemOptions>>(),
                out It.Ref<IMonitoredItem?>.IsAny))
                .Callback(new CaptureItem((string name, IOptionsMonitor<V2ItemOptions> options, out IMonitoredItem? item) =>
                {
                    ItemNames.Add(name);
                    ItemOptions.Add(options);
                    var created = new Mock<IMonitoredItem>();
                    created.As<IMonitoredItemApplyState>().SetupGet(value => value.HasPendingChanges).Returns(true);
                    created.SetupGet(value => value.ClientHandle).Returns((uint)(101 + Monitored.Count));
                    created.SetupGet(value => value.Created).Returns(true);
                    created.SetupGet(value => value.Error).Returns(new ServiceResult(StatusCodes.Good));
                    created.SetupGet(value => value.CurrentSamplingInterval).Returns(TimeSpan.FromMilliseconds(27));
                    created.SetupGet(value => value.CurrentQueueSize).Returns(7u);
                    created.SetupGet(value => value.CurrentMonitoringMode).Returns(MonitoringMode.Sampling);
                    Monitored.Add(created);
                    item = created.Object;
                })).Returns(true);
            Items.Setup(value => value.TryRemove(It.IsAny<uint>())).Returns(true);
        }

        public Mock<ISession> Session { get; } = new();
        public Mock<ISubscriptionManager> Manager { get; } = new();
        public Mock<ISubscription> Subscription { get; } = new();
        public Mock<IMonitoredItemCollection> Items { get; } = new();
        public List<string> ItemNames { get; } = [];
        public List<IOptionsMonitor<V2ItemOptions>> ItemOptions { get; } = [];
        public List<Mock<IMonitoredItem>> Monitored { get; } = [];
        public IOptionsMonitor<V2Options>? Options { get; private set; }
        public ISubscriptionNotificationHandler? Handler { get; private set; }
        public int Adds { get; private set; }

        public ChannelV2EngineAdapter CreateAdapter(PublishLogObserver? log = null)
        {
            return new ChannelV2EngineAdapter(Session.Object, new AppTelemetryContext(new LogRingBuffer(32)), log);
        }
    }

    private delegate void CaptureItem(string name, IOptionsMonitor<V2ItemOptions> options, out IMonitoredItem? item);

    private static readonly uint[] s_singlePartition = [500];
    private static readonly uint[] s_twoPartitions = [500, 501];
    private static readonly uint[] s_sequences = [71, 71, 71, 72, 72, 73];
    private static readonly string[] s_eventFields = ["EventId", "EventType", "SourceName", "Time", "Message", "Severity"];
    private static readonly NotificationKind[] s_kinds =
    [
        NotificationKind.DataChange, NotificationKind.DataChange, NotificationKind.DataChange,
        NotificationKind.Event, NotificationKind.Event, NotificationKind.KeepAlive
    ];
}
