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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.Alarms;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class AlarmV2ObservationTests
{
    [Test]
    public async Task OwnsOneV2HandlerAndUsesLogicalSubscriptionRefresh()
    {
        var context = new V2Context();
        await using var lifetime = context.ConfigureAwait(false);

        await context.Observation.InitializeAsync(
            ObjectIds.Server, TimeSpan.FromMilliseconds(500), CancellationToken.None)
            .ConfigureAwait(false);
        await context.Observation.RefreshAsync(CancellationToken.None).ConfigureAwait(false);

        context.Manager.Verify(manager => manager.Add(
            context.Observation, It.IsAny<IOptionsMonitor<V2SubscriptionOptions>>()), Times.Once);
        context.Subscription.Verify(
            subscription => subscription.ConditionRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.ItemOptions!.CurrentValue.StartNodeId, Is.EqualTo(ObjectIds.Server));
        Assert.That(context.ItemOptions.CurrentValue.AttributeId, Is.EqualTo(Attributes.EventNotifier));
        Assert.That(context.ItemOptions.CurrentValue.QueueSize, Is.EqualTo(AlarmLimits.PendingUpdates));
        Assert.That(context.ItemOptions.CurrentValue.Filter, Is.TypeOf<EventFilter>());
        Assert.That(context.Observation.Health.PartitionIds, Is.EqualTo(new uint[] { 11, 22 }));
        context.Session.Verify(session => session.CallAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await context.Observation.DisposeAsync().ConfigureAwait(false);
        await context.Observation.DisposeAsync().ConfigureAwait(false);
        context.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task HandlerPreservesRefreshMarkersAndConditionInOneQueue()
    {
        var context = new V2Context();
        await using var lifetime = context.ConfigureAwait(false);
        await context.Observation.InitializeAsync(
            ObjectIds.Server, TimeSpan.FromMilliseconds(250), CancellationToken.None)
            .ConfigureAwait(false);
        var projection = new AlarmEventProjection();
        EventNotification[] notifications =
        [
            new(context.Item.Object, AlarmTestData.Fields(projection, ObjectTypeIds.RefreshStartEventType))
            {
                PartitionServerId = 11
            },
            new(context.Item.Object, AlarmTestData.Fields(projection, ObjectTypeIds.AlarmConditionType))
            {
                PartitionServerId = 11
            },
            new(context.Item.Object, AlarmTestData.Fields(projection, ObjectTypeIds.RefreshEndEventType))
            {
                PartitionServerId = 11
            }
        ];

        await context.Observation.OnEventDataNotificationAsync(
            context.Subscription.Object, 1, AlarmTestData.Now.UtcDateTime, notifications, PublishState.None, [])
            .ConfigureAwait(false);

        Assert.That(context.Observation.Updates.TryRead(out AlarmUpdate? start), Is.True);
        Assert.That(start!.Kind, Is.EqualTo(AlarmUpdateKind.RefreshStart));
        Assert.That(context.Observation.Updates.TryRead(out AlarmUpdate? condition), Is.True);
        Assert.That(condition!.Condition!.Key.ConditionId, Is.EqualTo(new NodeId(500u, 2)));
        Assert.That(condition.PartitionId, Is.EqualTo(11));
        Assert.That(context.Observation.Updates.TryRead(out AlarmUpdate? end), Is.True);
        Assert.That(end!.Kind, Is.EqualTo(AlarmUpdateKind.RefreshEnd));
        Assert.That(context.Observation.Updates.TryRead(out _), Is.False);
    }

    [Test]
    public async Task QueueIsBoundedAndDroppedMarkersHaveOutOfBandLossEvidence()
    {
        var context = new V2Context();
        await using var lifetime = context.ConfigureAwait(false);
        await context.Observation.InitializeAsync(
            ObjectIds.Server, TimeSpan.FromMilliseconds(250), CancellationToken.None)
            .ConfigureAwait(false);
        var projection = new AlarmEventProjection();
        ArrayOf<Variant> fields = AlarmTestData.Fields(projection, ObjectTypeIds.RefreshEndEventType);
        var notifications = new EventNotification[AlarmLimits.PendingUpdates + 7];
        for (int i = 0; i < notifications.Length; i++)
        {
            notifications[i] = new EventNotification(context.Item.Object, fields) { PartitionServerId = 11 };
        }

        await context.Observation.OnEventDataNotificationAsync(
            context.Subscription.Object, 1, AlarmTestData.Now.UtcDateTime, notifications, PublishState.None, [])
            .ConfigureAwait(false);

        Assert.That(context.Observation.Updates.Count, Is.EqualTo(AlarmLimits.PendingUpdates));
        Assert.That(context.Observation.Health.DroppedUpdates, Is.EqualTo(7));
    }

    [Test]
    public async Task BadMonitoredItemStatusIsNotReportedAsSuccessfulObservation()
    {
        var context = new V2Context();
        await using var lifetime = context.ConfigureAwait(false);
        context.Item.SetupGet(item => item.Error).Returns(new ServiceResult(StatusCodes.BadUserAccessDenied));

        await Assert.ThatAsync(
            () => context.Observation.InitializeAsync(
                ObjectIds.Server, TimeSpan.FromMilliseconds(250), CancellationToken.None),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);

        Assert.That(context.Observation.Health.IsReady, Is.False);
        Assert.That(context.Observation.Health.Detail, Does.Contain("BadUserAccessDenied"));
    }

    private sealed class V2Context : IAsyncDisposable
    {
        public V2Context()
        {
            Session.SetupGet(session => session.NamespaceUris).Returns(new NamespaceTable());
            Session.SetupGet(session => session.Connected).Returns(true);
            Session.SetupGet(session => session.MessageContext).Returns(Host.MessageContext);
            Item.SetupGet(item => item.Created).Returns(true);
            Item.SetupGet(item => item.Error).Returns(ServiceResult.Good);
            IMonitoredItem? monitored = Item.Object;
            Items.Setup(items => items.TryAdd(
                    It.IsAny<string>(), It.IsAny<IOptionsMonitor<V2MonitoredItemOptions>>(), out monitored))
                .Callback(new TryAddCallback((string _, IOptionsMonitor<V2MonitoredItemOptions> options,
                    out IMonitoredItem? result) =>
                {
                    ItemOptions = options;
                    result = Item.Object;
                }))
                .Returns(true);
            Subscription.SetupGet(subscription => subscription.MonitoredItems).Returns(Items.Object);
            Subscription.SetupGet(subscription => subscription.Created).Returns(true);
            Subscription.SetupGet(subscription => subscription.PartitionIds).Returns(new uint[] { 11, 22 });
            Subscription.SetupGet(subscription => subscription.CurrentPublishingInterval)
                .Returns(TimeSpan.FromMilliseconds(500));
            Subscription.Setup(subscription => subscription.ConditionRefreshAsync(It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            Subscription.Setup(subscription => subscription.DisposeAsync()).Returns(ValueTask.CompletedTask);
            Manager.Setup(manager => manager.Add(
                    It.IsAny<ISubscriptionNotificationHandler>(), It.IsAny<IOptionsMonitor<V2SubscriptionOptions>>()))
                .Returns(Subscription.Object);
            Observation = new StackAlarmObservation(Session.Object, Manager.Object, Host.Telemetry);
        }

        public ObserveTestHost Host { get; } = new();

        public Mock<ISession> Session { get; } = new(MockBehavior.Loose);

        public Mock<ISubscriptionManager> Manager { get; } = new(MockBehavior.Strict);

        public Mock<IPartitionedSubscription> Subscription { get; } = new(MockBehavior.Loose);

        public Mock<IMonitoredItemCollection> Items { get; } = new(MockBehavior.Strict);

        public Mock<IMonitoredItem> Item { get; } = new(MockBehavior.Loose);

        public IOptionsMonitor<V2MonitoredItemOptions>? ItemOptions { get; private set; }

        public StackAlarmObservation Observation { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Observation.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await Host.DisposeAsync().ConfigureAwait(false);
            }
        }

        private delegate void TryAddCallback(
            string name,
            IOptionsMonitor<V2MonitoredItemOptions> options,
            out IMonitoredItem? item);
    }
}
