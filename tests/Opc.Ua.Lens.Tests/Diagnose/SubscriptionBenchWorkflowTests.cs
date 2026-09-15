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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;
using V2ItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using V2SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[NonParallelizable]
public sealed class SubscriptionBenchWorkflowTests
{
    [TestCase(null, "ns=2;s=Temperature")]
    [TestCase("", "ns=2;s=Temperature")]
    [TestCase("  ", "ns=2;s=Temperature")]
    [TestCase("Boiler / Temperature", "Boiler / Temperature")]
    public async Task VariablePoolSeedDeduplicatesAndNeverAutoStarts(string? label, string expected)
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = HistorianWorkflowTests.Host(context);
        await using var plugin = new SubscriptionBenchPlugin(host);
        NodeId node = new("Temperature", 2);

        await plugin.SeedFromNodeAsync(node, NodeClass.Variable, label).ConfigureAwait(false);
        await plugin.SeedFromNodeAsync(node, NodeClass.Variable, "Should not replace first name").ConfigureAwait(false);
        await plugin.SeedFromNodeAsync(NodeId.Null, NodeClass.Variable, "Ignored").ConfigureAwait(false);
        await plugin.SeedFromNodeAsync(new NodeId("Pressure", 2), NodeClass.Variable, "Pressure").ConfigureAwait(false);

        JsonElement pool = plugin.CaptureState().GetProperty("pool");
        Assert.That(pool.EnumerateArray().Select(row => row.GetProperty("nodeId").GetString()),
            Is.EqualTo(s_variablePoolSeedDeduplicatesAndNeverAutoStartsExpected));
        Assert.That(pool.EnumerateArray().Select(row => row.GetProperty("displayName").GetString()),
            Is.EqualTo(new[] { expected, "Pressure" }));
        Assert.That(plugin.PoolDescription, Is.EqualTo("2 variable(s) in pool."));
        Assert.That(plugin.Status, Is.EqualTo("Added 1 variable(s) to the pool (skipped duplicates)."));
        Assert.That(plugin.SubsSliderValue, Is.Zero);
        Assert.That(plugin.ItemsSliderValue, Is.Zero);
        Assert.That(plugin.CanResize, Is.False);
        Assert.That(plugin.TryDequeueChartSample(out _), Is.False);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    [Test]
    public async Task ClearCountersPreservesPoolWhileClearPoolRemovesOnlyPoolIntent()
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = HistorianWorkflowTests.Host(context);
        await using var plugin = new SubscriptionBenchPlugin(host);
        await plugin.SeedFromNodeAsync(new NodeId("Temperature", 2), NodeClass.Variable, "Temperature")
            .ConfigureAwait(false);
        await plugin.SeedFromNodeAsync(new NodeId("Pressure", 2), NodeClass.Variable, "Pressure").ConfigureAwait(false);
        plugin.SubsSliderValue = 3;
        plugin.ItemsSliderValue = 17;
        plugin.WasReset = false;

        plugin.ClearCommand.Execute(null);

        Assert.That(plugin.WasReset, Is.True);
        Assert.That(plugin.PoolDescription, Is.EqualTo("2 variable(s) in pool."));
        Assert.That(plugin.CaptureState().GetProperty("pool").GetArrayLength(), Is.EqualTo(2));
        Assert.That(plugin.TotalValuesText, Is.EqualTo("Total values: 0"));
        Assert.That(plugin.TotalErrorsText, Is.EqualTo("Errors      : 0"));
        plugin.ClearPoolCommand.Execute(null);
        Assert.That(plugin.PoolDescription, Is.EqualTo("0 variables in pool."));
        Assert.That(plugin.Status, Is.EqualTo("Pool cleared."));
        Assert.That(plugin.CaptureState().GetProperty("pool").GetArrayLength(), Is.Zero);
        Assert.That(plugin.SubsSliderValue, Is.EqualTo(3));
        Assert.That(plugin.ItemsSliderValue, Is.EqualTo(17));
        await plugin.OnConnectionStateChangedAsync(default).ConfigureAwait(false);
        Assert.That(plugin.SubsSliderValue, Is.Zero);
        Assert.That(plugin.ItemsSliderValue, Is.Zero);
        Assert.That(plugin.Status, Is.EqualTo("Disconnected — connect to resume."));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task SubscriptionSettingsCommandCommitsOnlyAcceptedIntentWithoutStartingTopology(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new SubscriptionBenchPlugin(host);
            DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
            Task command = Task.CompletedTask;
            SubscriptionSettingsDialog dialog = await DesktopInteraction.OpenedAsync<SubscriptionSettingsDialog>(
                () => command = plugin.EditSubscriptionCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "PubMs").Text = "375";
                DesktopInteraction.Control<TextBox>(dialog, "KeepAlive").Text = "7";
                DesktopInteraction.Control<TextBox>(dialog, "Lifetime").Text = "31";
                DesktopInteraction.Control<TextBox>(dialog, "MaxNotifs").Text = "47";
                DesktopInteraction.Control<TextBox>(dialog, "Priority").Text = "19";
                DesktopInteraction.Control<CheckBox>(dialog, "PublishingEnabled").IsChecked = false;
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                await command.ConfigureAwait(true);

                JsonElement config = plugin.CaptureState().GetProperty("subscription");
                Assert.That(config.GetProperty("publishingIntervalMs").GetDouble(), Is.EqualTo(accept ? 375 : 1000));
                Assert.That(config.GetProperty("keepAliveCount").GetUInt32(), Is.EqualTo(accept ? 7 : 10));
                Assert.That(config.GetProperty("lifetimeCount").GetUInt32(), Is.EqualTo(accept ? 31 : 1000));
                Assert.That(config.GetProperty("maxNotificationsPerPublish").GetUInt32(), Is.EqualTo(accept ? 47 : 0));
                Assert.That(config.GetProperty("priority").GetByte(), Is.EqualTo(accept ? 19 : 0));
                Assert.That(config.GetProperty("publishingEnabled").GetBoolean(), Is.EqualTo(!accept));
                Assert.That(plugin.Status, Is.EqualTo(accept
                    ? "Subscription parameters saved — they apply when connected." : "Not connected."));
                Assert.That(plugin.SubsSliderValue, Is.Zero);
                Assert.That(plugin.ItemsSliderValue, Is.Zero);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await command.ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task ItemSettingsCommandCommitsAcceptedSamplingQueueModeAndFilterWithoutStartingTopology(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new SubscriptionBenchPlugin(host);
            DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
            Task command = Task.CompletedTask;
            MonitoredItemSettingsDialog dialog = await DesktopInteraction.OpenedAsync<MonitoredItemSettingsDialog>(
                () => command = plugin.EditItemSettingsCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "SamplingMs").Text = "45";
                DesktopInteraction.Control<TextBox>(dialog, "QueueSizeBox").Text = "13";
                DesktopInteraction.Control<CheckBox>(dialog, "DiscardOldestBox").IsChecked = false;
                DesktopInteraction.Control<ComboBox>(dialog, "MonitoringModeCombo").SelectedIndex = 1;
                DesktopInteraction.Control<ComboBox>(dialog, "TriggerCombo").SelectedIndex = 2;
                DesktopInteraction.Control<ComboBox>(dialog, "DeadbandTypeCombo").SelectedIndex = 1;
                DesktopInteraction.Control<TextBox>(dialog, "DeadbandValueBox").Text = "2";
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                await command.ConfigureAwait(true);

                JsonElement item = plugin.CaptureState().GetProperty("item");
                Assert.That(item.GetProperty("samplingIntervalMs").GetDouble(), Is.EqualTo(accept ? 45 : 0));
                Assert.That(item.GetProperty("queueSize").GetUInt32(), Is.EqualTo(accept ? 13 : 1));
                Assert.That(item.GetProperty("discardOldest").GetBoolean(), Is.EqualTo(!accept));
                Assert.That(item.GetProperty("monitoringMode").GetInt32(), Is.EqualTo(accept ? 1 : 2));
                if (accept)
                {
                    Assert.That(item.GetProperty("filter").GetProperty("trigger").GetInt32(), Is.EqualTo(2));
                    Assert.That(item.GetProperty("filter").GetProperty("deadbandType").GetUInt32(), Is.EqualTo(1));
                    Assert.That(item.GetProperty("filter").GetProperty("deadbandValue").GetDouble(), Is.EqualTo(2));
                    Assert.That(plugin.Status, Is.EqualTo("Item settings saved — they apply when connected."));
                }
                Assert.That(plugin.SubsSliderValue, Is.Zero);
                Assert.That(plugin.ItemsSliderValue, Is.Zero);
                Assert.That(plugin.TryDequeueChartSample(out _), Is.False);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await command.ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public async Task V2ResourcesShareOptionsAndHandlerButReleaseTheirDistinctOwners()
    {
        var protocol = new BenchProtocol();
        var configuration = new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(250), KeepAliveCount = 7, LifetimeCount = 31,
            MaxNotificationsPerPublish = 53, Priority = 9, PublishingEnabled = false
        };
        var factory = new V2BenchResourceFactory(protocol.Session.Object, protocol.Counters, configuration);
        Assert.That(factory.IsReady, Is.True);
        IBenchSubscription first = factory.CreateSubscription();
        IBenchSubscription second = factory.CreateSubscription();
        try
        {
            Assert.That(protocol.Options, Has.Count.EqualTo(2));
            Assert.That(protocol.Options[1], Is.SameAs(protocol.Options[0]));
            Assert.That(protocol.Handlers[1], Is.SameAs(protocol.Handlers[0]));
            V2SubscriptionOptions initial = protocol.Options[0].CurrentValue;
            Assert.That(initial.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
            Assert.That(initial.KeepAliveCount, Is.EqualTo(7));
            Assert.That(initial.LifetimeCount, Is.EqualTo(31));
            Assert.That(initial.MaxNotificationsPerPublish, Is.EqualTo(53));
            Assert.That(initial.Priority, Is.EqualTo(9));
            Assert.That(initial.PublishingEnabled, Is.False);
            Assert.That(initial.Disabled, Is.False);
            Assert.That(initial.MinLifetimeInterval, Is.EqualTo(TimeSpan.FromMinutes(1)));
            var observed = new List<TimeSpan>();
            using IDisposable? registration = protocol.Options[0]
                .OnChange((value, _) => observed.Add(value.PublishingInterval));

            second.ApplySubscriptionConfig(configuration with
            {
                PublishingInterval = TimeSpan.FromMilliseconds(375), PublishingEnabled = true, Priority = 4
            });

            Assert.That(
                protocol.Options[0].CurrentValue.PublishingInterval,
                Is.EqualTo(TimeSpan.FromMilliseconds(375)));
            Assert.That(protocol.Options[1].CurrentValue.PublishingEnabled, Is.True);
            Assert.That(protocol.Options[0].CurrentValue.Priority, Is.EqualTo(4));
            Assert.That(observed, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(375) }));
            Assert.That(first.RevisedPublishingIntervalMs, Is.EqualTo(425));
            Assert.That(second.RevisedPublishingIntervalMs, Is.EqualTo(650));
            protocol.Session.VerifySet(session => session.MinPublishRequestCount = It.IsAny<int>(), Times.Never);
            protocol.Session.VerifySet(session => session.MaxPublishRequestCount = It.IsAny<int>(), Times.Never);
        }
        finally
        {
            await first.DisposeAsync().ConfigureAwait(false);
            await second.DisposeAsync().ConfigureAwait(false);
        }
        Assert.That(
            protocol.Released,
            Is.EqualTo(s_v2ResourcesShareOptionsAndHandlerButReleaseTheirDistinctOwnerExpected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task V2ItemsForwardExactOptionsAndPreserveIdentityAcrossConfigurationChanges(bool removeResult)
    {
        var protocol = new BenchProtocol();
        var factory = new V2BenchResourceFactory(protocol.Session.Object, protocol.Counters, new SubscriptionConfig());
        await using IBenchSubscription subscription = factory.CreateSubscription();
        var item = new Mock<IMonitoredItem>(MockBehavior.Strict);
        item.SetupGet(value => value.ClientHandle).Returns(117);
        item.SetupGet(value => value.Error).Returns(ServiceResult.Good);
        IMonitoredItem? created = item.Object;
        IOptionsMonitor<V2ItemOptions>? received = null;
        var collection = new Mock<IMonitoredItemCollection>(MockBehavior.Strict);
        collection.Setup(
            values => values.TryAdd("boiler-temperature", It.IsAny<IOptionsMonitor<V2ItemOptions>>(), out created))
            .Callback(
                new InvocationAction(invocation => received = (IOptionsMonitor<V2ItemOptions>)invocation.Arguments[1]))
            .Returns(true);
        collection.Setup(values => values.TryRemove(117)).Returns(removeResult);
        protocol.Subscriptions[0].SetupGet(value => value.MonitoredItems).Returns(collection.Object);
        var filter = new DataChangeFilter
        {
            Trigger = DataChangeTrigger.StatusValue, DeadbandType = (uint)DeadbandType.Absolute, DeadbandValue = 2
        };
        var settings = new MonitoredItemSettings
        {
            SamplingInterval = TimeSpan.FromMilliseconds(45), QueueSize = 19, DiscardOldest = false,
            MonitoringMode = MonitoringMode.Sampling, DataChangeFilter = filter
        };

        IBenchItem? wrapper = subscription.TryAddItem("boiler-temperature", new NodeId("Temperature", 2), settings);

        Assert.That(wrapper, Is.Not.Null);
        Assert.That(received!.CurrentValue.StartNodeId, Is.EqualTo(new NodeId("Temperature", 2)));
        Assert.That(received.CurrentValue.AttributeId, Is.EqualTo(Attributes.Value));
        Assert.That(received.CurrentValue.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(45)));
        Assert.That(received.CurrentValue.QueueSize, Is.EqualTo(19));
        Assert.That(received.CurrentValue.DiscardOldest, Is.False);
        Assert.That(received.CurrentValue.MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        Assert.That(received.CurrentValue.Filter, Is.SameAs(filter));
        Assert.That(wrapper!.IsBad, Is.False);
        wrapper.ApplySettings(settings with
        {
            SamplingInterval = TimeSpan.FromMilliseconds(80), QueueSize = 7, DiscardOldest = true,
            MonitoringMode = MonitoringMode.Disabled, DataChangeFilter = null
        });
        Assert.That(received.CurrentValue.StartNodeId, Is.EqualTo(new NodeId("Temperature", 2)));
        Assert.That(received.CurrentValue.AttributeId, Is.EqualTo(Attributes.Value));
        Assert.That(received.CurrentValue.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(80)));
        Assert.That(received.CurrentValue.QueueSize, Is.EqualTo(7));
        Assert.That(received.CurrentValue.DiscardOldest, Is.True);
        Assert.That(received.CurrentValue.MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
        Assert.That(received.CurrentValue.Filter, Is.Null);
        item.SetupGet(value => value.Error).Returns(new ServiceResult(StatusCodes.BadOutOfRange));
        Assert.That(wrapper.IsBad, Is.True);
        Assert.That(wrapper.Remove(), Is.EqualTo(removeResult));
        collection.Verify(values => values.TryRemove(117), Times.Once);
        collection.Verify(values => values.TryAdd("boiler-temperature", It.IsAny<IOptionsMonitor<V2ItemOptions>>(),
            out created), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task V2FailedItemAddReturnsNoWrapperWithoutRemovingAnUnownedItem(bool successWithoutItem)
    {
        var protocol = new BenchProtocol();
        var factory = new V2BenchResourceFactory(protocol.Session.Object, protocol.Counters, new SubscriptionConfig());
        await using IBenchSubscription subscription = factory.CreateSubscription();
        var collection = new Mock<IMonitoredItemCollection>(MockBehavior.Strict);
        IMonitoredItem? absent = null;
        collection.Setup(values => values.TryAdd("denied", It.IsAny<IOptionsMonitor<V2ItemOptions>>(), out absent))
            .Returns(successWithoutItem);
        protocol.Subscriptions[0].SetupGet(value => value.MonitoredItems).Returns(collection.Object);

        IBenchItem? result = subscription.TryAddItem("denied", new NodeId("Denied", 2), new MonitoredItemSettings());

        Assert.That(result, Is.Null);
        collection.Verify(values => values.TryAdd("denied", It.IsAny<IOptionsMonitor<V2ItemOptions>>(), out absent),
            Times.Once);
        collection.Verify(values => values.TryRemove(It.IsAny<uint>()), Times.Never);
        Assert.That(protocol.Released, Is.Empty);
    }

    [Test]
    public async Task CapturedV2ResourceHandlerCountsOnlyDataValuesAndOnlyBadStatusesAsErrors()
    {
        var protocol = new BenchProtocol();
        var factory = new V2BenchResourceFactory(protocol.Session.Object, protocol.Counters, new SubscriptionConfig());
        await using IBenchSubscription subscription = factory.CreateSubscription();
        ISubscriptionNotificationHandler handler = protocol.Handlers.Single();
        ISubscription live = protocol.Subscriptions.Single().Object;
        var item = new Mock<IMonitoredItem>().Object;
        DateTime time = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        DataValueChange[] changes =
        [
            new(item, new DataValue(new Variant(12), StatusCodes.Good, time, time), null),
            new(item, new DataValue(new Variant(13), StatusCodes.Uncertain, time, time), null),
            new(item, new DataValue(new Variant(14), StatusCodes.BadOutOfRange, time, time), null),
            new(item, new DataValue(new Variant(15), StatusCodes.BadNoData, time, time), null)
        ];

        await handler.OnDataChangeNotificationAsync(live, 71, time, changes, PublishState.None, [])
            .ConfigureAwait(false);
        await handler.OnDataChangeNotificationAsync(live, 72, time, ReadOnlyMemory<DataValueChange>.Empty,
            PublishState.Republish, []).ConfigureAwait(false);
        await handler.OnEventDataNotificationAsync(live, 73, time,
            new EventNotification[] { new(item, [new Variant("not a value notification")]) },
            PublishState.None, []).ConfigureAwait(false);
        await handler.OnKeepAliveNotificationAsync(live, 74, time, PublishState.KeepAlive).ConfigureAwait(false);
        await handler.OnSubscriptionStateChangedAsync(live, V2SubscriptionState.Modified, PublishState.Recovered)
            .ConfigureAwait(false);

        Assert.That(protocol.Counters.TotalValues, Is.EqualTo(4));
        Assert.That(protocol.Counters.TotalErrors, Is.EqualTo(2));
        BenchThroughputCounters.Sample sample = protocol.Counters.Rotate();
        Assert.That(sample.Last1s, Is.EqualTo(4));
        Assert.That(sample.TotalValues, Is.EqualTo(4));
        Assert.That(sample.TotalErrors, Is.EqualTo(2));
        Assert.That(protocol.Options, Has.Count.EqualTo(1));
        Assert.That(protocol.Released, Is.Empty);
    }

    [Test]
    public void MissingV2ManagerRefusesResourceCreationWithoutAllocatingAnything()
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        ISubscriptionManager? manager = null;
        session.Setup(value => value.TryGetSubscriptionManager(out manager)).Returns(false);
        var counters = new BenchThroughputCounters();
        var factory = new V2BenchResourceFactory(session.Object, counters, new SubscriptionConfig());

        Assert.That(factory.IsReady, Is.False);
        Assert.That(() => factory.CreateSubscription(), Throws.InvalidOperationException.With.Message.EqualTo(
            "The V2 (channel) subscription engine is required for the Subscription Bench."));

        Assert.That(counters.TotalValues, Is.Zero);
        Assert.That(counters.TotalErrors, Is.Zero);
        session.Verify(value => value.TryGetSubscriptionManager(out manager), Times.Exactly(2));
        session.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task V2ResourceDisposalAwaitsAndPropagatesTheOwnedSubscriptionResult(bool failure)
    {
        var protocol = new BenchProtocol();
        var factory = new V2BenchResourceFactory(protocol.Session.Object, protocol.Counters, new SubscriptionConfig());
        IBenchSubscription subscription = factory.CreateSubscription();
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("server release rejected");
        protocol.Subscriptions[0].Setup(value => value.DisposeAsync()).Returns(new ValueTask(reply.Task));

        Task disposal = subscription.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        if (failure)
        {
            reply.SetException(error);
            await Assert.ThatAsync(() => disposal, Throws.Exception.SameAs(error)).ConfigureAwait(false);
        }
        else
        {
            reply.SetResult();
            await disposal.ConfigureAwait(false);
        }
        protocol.Subscriptions[0].Verify(value => value.DisposeAsync(), Times.Once);
        Assert.That(protocol.Counters.TotalValues, Is.Zero);
        Assert.That(protocol.Handlers, Has.Count.EqualTo(1));
    }

    private sealed class BenchProtocol
    {
        public BenchProtocol()
        {
            ISubscriptionManager? manager = Manager.Object;
            Session.Setup(session => session.TryGetSubscriptionManager(out manager)).Returns(true);
            Manager.Setup(value => value.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                It.IsAny<IOptionsMonitor<V2SubscriptionOptions>>()))
                .Returns((ISubscriptionNotificationHandler handler, IOptionsMonitor<V2SubscriptionOptions> options) =>
                {
                    int index = Subscriptions.Count;
                    var subscription = new Mock<ISubscription>(MockBehavior.Strict);
                    subscription.SetupGet(value => value.CurrentPublishingInterval)
                        .Returns(TimeSpan.FromMilliseconds(index == 0 ? 425 : 650));
                    subscription.Setup(value => value.DisposeAsync()).Returns(() =>
                    {
                        Released.Add(index);
                        return ValueTask.CompletedTask;
                    });
                    Subscriptions.Add(subscription);
                    Handlers.Add(handler);
                    Options.Add(options);
                    return subscription.Object;
                });
        }

        public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
        public Mock<ISubscriptionManager> Manager { get; } = new(MockBehavior.Strict);
        public BenchThroughputCounters Counters { get; } = new();
        public List<Mock<ISubscription>> Subscriptions { get; } = [];
        public List<ISubscriptionNotificationHandler> Handlers { get; } = [];
        public List<IOptionsMonitor<V2SubscriptionOptions>> Options { get; } = [];
        public List<int> Released { get; } = [];
    }

    private static readonly string[] s_variablePoolSeedDeduplicatesAndNeverAutoStartsExpected =
    [
        "ns=2;s=Temperature",
        "ns=2;s=Pressure",
    ];
    private static readonly int[] s_v2ResourcesShareOptionsAndHandlerButReleaseTheirDistinctOwnerExpected =
    [
        0,
        1,
    ];
}
