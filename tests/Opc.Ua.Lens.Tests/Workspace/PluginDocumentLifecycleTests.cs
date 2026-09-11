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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
[NonParallelizable]
public sealed class PluginDocumentLifecycleTests
{
    [TestCase("success")]
    [TestCase("failure")]
    [TestCase("canceled")]
    public async Task SynchronizeConnectionPassesExactTokenAwaitsExistingPluginAndDoesNotDisposeIt(string outcome)
    {
        await using var context = new DesktopConnectionContext();
        var operations = new PluginDocumentOperations(context.Connection);
        var document = new Mock<IPlugin>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("document connection failed");
        document.Setup(plugin => plugin.OnConnectionStateChangedAsync(cancellation.Token)).Returns(reply.Task);

        Task operation = operations.SynchronizeConnectionAsync(document.Object, cancellation.Token);
        Assert.That(operation.IsCompleted, Is.False);
        if (outcome == "failure")
        {
            reply.SetException(failure);
            await Assert.ThatAsync(() => operation, Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        }
        else
        {
            if (outcome == "canceled")
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            reply.SetResult();
            if (outcome == "canceled")
            {
                await Assert.ThatAsync(() => operation, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
            }
            else
            {
                await operation.ConfigureAwait(false);
            }
        }
        document.Verify(plugin => plugin.OnConnectionStateChangedAsync(cancellation.Token), Times.Once);
        document.Verify(plugin => plugin.DisposeAsync(), Times.Never);
        document.VerifyNoOtherCalls();
        Assert.That(context.Connection.CurrentSession, Is.Null);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    [Test]
    public async Task DisconnectedSubscriptionSynchronizationAwaitsDetachAndPreservesItemIntent()
    {
        await using var context = new DesktopConnectionContext();
        var operations = new PluginDocumentOperations(context.Connection);
        var adapter = new ControlledSubscriptionAdapter();
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Mock.Setup(value => value.DisposeAsync()).Returns(() =>
        {
            entered.SetResult();
            return new ValueTask(disposal.Task);
        });
        await using var document = new SubscriptionViewModel("Retained monitor", adapter.Object, NullLogger.Instance);
        MonitoredItemConfig item = Item(91, "Temperature");
        document.Items.Add(item);
        Task synchronization = operations.SynchronizeConnectionAsync(document, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.That(synchronization.IsCompleted, Is.False);
            Assert.That(document.Adapter, Is.Null);
            Assert.That(document.Items.Single(), Is.SameAs(item));
            disposal.SetResult();
            await synchronization.ConfigureAwait(false);

            Assert.That(document.SubscriptionStatus, Is.EqualTo("● Disconnected — items preserved for reconnect."));
            Assert.That(document.Items.Single().QueueSize, Is.EqualTo(13));
            Assert.That(document.ItemStatuses.Single().Id, Is.EqualTo(91));
            await operations.SynchronizeConnectionAsync(document, default).ConfigureAwait(false);
            adapter.Mock.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(context.ConfigurationsCreated, Is.Zero);
        }
        finally
        {
            disposal.TrySetResult();
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [Platform("Win,Linux")]
    public Task SubscriptionStateApplyAndCapturePreserveSettingsItemsAndModesWithoutServerIds(int count)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var document = new SubscriptionViewModel("Before", null, NullLogger.Instance);
            document.Items.Add(Item(999, "Old"));
            MonitoredItemConfig[] items = new[]
            {
                Item(21, "Temperature"),
                Item(22, "Pressure") with { MonitoringMode = MonitoringMode.Disabled },
                Item(23, "Events") with { AttributeId = Attributes.EventNotifier, IsEvent = true }
            }.Take(count).ToArray();
            var subscription = new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(123.5), LifetimeCount = 37, KeepAliveCount = 9,
                Priority = 6, PublishingEnabled = false, MaxNotificationsPerPublish = 43,
                MinPublishRequestCount = 3, MaxPublishRequestCount = 17
            };
            var state = new SubscriptionDocumentState(
                "Restored monitor", subscription, items, AnimationMode.Histogram, 2, true,
                ShowItemStatusGrid: false, ShowLegend: true, ShowXAxis: true, ShowYAxis: false, DisplayModeIndex: 5);

            await state.ApplyToAsync(document).ConfigureAwait(true);
            SubscriptionDocumentState captured = SubscriptionDocumentState.Capture(document);
            UaLens.Connection.SessionFile.TabSnapshot exported = captured.Export();

            Assert.That(document.Title, Is.EqualTo("Restored monitor"));
            Assert.That(document.IsBound, Is.False);
            Assert.That(document.Items.Select(item => item.Id),
                Is.EqualTo(Enumerable.Range(1, count).Select(id => -id)));
            Assert.That(document.ItemStatuses.Select(row => row.NodeId),
                Is.EqualTo(s_subscriptionStateApplyAndCapturePreserveSettingsItemsAndModesExpected.Take(count)));
            Assert.That(exported.Items.Select(item => item.NodeId),
                Is.EqualTo(s_subscriptionStateApplyAndCapturePreserveSettingsItemsAndModesExpected.Take(count)));
            Assert.That(exported.Items.Select(item => item.QueueSize), Is.All.EqualTo(13));
            Assert.That(exported.Items.Select(item => item.MonitoringMode),
                Is.EqualTo(new byte[] { 2, 0, 2 }.Take(count)));
            Assert.That(exported.PublishingInterval.Milliseconds, Is.EqualTo(123.5));
            Assert.That(exported.KeepAliveCount, Is.EqualTo(9));
            Assert.That(exported.LifetimeCount, Is.EqualTo(37));
            Assert.That(exported.Priority, Is.EqualTo(6));
            Assert.That(exported.MaxNotificationsPerPublish, Is.EqualTo(43));
            Assert.That(exported.PublishingEnabled, Is.False);
            Assert.That(exported.MinPublishRequestCount, Is.EqualTo(3));
            Assert.That(exported.MaxPublishRequestCount, Is.EqualTo(17));
            Assert.That(exported.AnimationMode, Is.EqualTo("Histogram"));
            Assert.That(exported.DisplayModeIndex, Is.EqualTo(5));
            Assert.That(exported.AnimationTimeScale, Is.EqualTo(2));
            Assert.That(exported.ShowResourceOverlay, Is.True);
            Assert.That(exported.ShowItemStatusGrid, Is.False);
            Assert.That(exported.ShowLegend, Is.True);
            Assert.That(exported.ShowXAxis, Is.True);
            Assert.That(exported.ShowYAxis, Is.False);
            Assert.That(captured.Items.Count, Is.EqualTo(count));
            document.Items.Clear();
            Assert.That(captured.Items.Count, Is.EqualTo(count));
            Assert.That(document.ItemStatuses, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BoundOrPreCanceledStateApplyDoesNotOverwriteExistingDocument(bool bound)
    {
        var adapter = new ControlledSubscriptionAdapter();
        await using var document = new SubscriptionViewModel(
            "Original", bound ? adapter.Object : null, NullLogger.Instance);
        MonitoredItemConfig item = Item(-1, "Temperature");
        document.Items.Add(item);
        var state = new SubscriptionDocumentState(
            "Replacement", new SubscriptionConfig(), [Item(27, "Pressure")], AnimationMode.Lines, 3, false);
        using var cancellation = new CancellationTokenSource();
        if (!bound)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        await Assert.ThatAsync(() => state.ApplyToAsync(document, cancellation.Token), bound
            ? Throws.InvalidOperationException.With.Message.EqualTo(
                "Local configuration must be applied before binding a subscription.")
            : Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(document.Title, Is.EqualTo("Original"));
        Assert.That(document.Items.Single(), Is.SameAs(item));
        Assert.That(document.AnimationMode, Is.EqualTo(AnimationMode.Dots));
        Assert.That(adapter.Added, Is.Empty);
        Assert.That(adapter.Applied, Is.Empty);
    }

    [Test]
    public async Task SeedExistingBenchAddsDeduplicatedVariableContextWithoutOpeningAnotherDocument()
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = HistorianWorkflowTests.Host(context);
        await using var bench = new SubscriptionBenchPlugin(host);
        var operations = new PluginDocumentOperations(context.Connection);
        var selected = new NodeViewModel(context.Browser, new NodeId("Boiler", 2),
            new NodeId("Temperature", 2), "Boiler / Temperature", NodeClass.Variable);
        var second = new NodeViewModel(context.Browser, NodeId.Null, new NodeId("Pressure", 2),
            "Pressure", NodeClass.Variable);

        await operations.SeedExistingAsync(bench, new ToolSeed(BenchNode: selected)).ConfigureAwait(false);
        await operations.SeedExistingAsync(bench, new ToolSeed(BenchNode: selected)).ConfigureAwait(false);
        await operations.SeedExistingAsync(bench, new ToolSeed(BenchNode: second)).ConfigureAwait(false);

        System.Text.Json.JsonElement state = bench.CaptureState();
        Assert.That(state.GetProperty("pool").EnumerateArray().Select(item => item.GetProperty("nodeId").GetString()),
            Is.EqualTo(s_seedExistingBenchAddsDeduplicatedVariableContextWithoutOpeninExpected));
        Assert.That(state.GetProperty("pool").EnumerateArray()
            .Select(item => item.GetProperty("displayName").GetString()),
            Is.EqualTo(s_seedExistingBenchAddsDeduplicatedVariableContextWithoutOpeninExpected2));
        Assert.That(bench.PoolDescription, Is.EqualTo("2 variable(s) in pool."));
        Assert.That(bench.SubsSliderValue, Is.Zero);
        Assert.That(bench.ItemsSliderValue, Is.Zero);
        Assert.That(bench.IsConnected, Is.False);
        Mock.Get(host.Workspace).Verify(workspace => workspace.OpenToolAsync(
            It.IsAny<PluginKind>(), It.IsAny<EndpointDescription?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static MonitoredItemConfig Item(int id, string name)
    {
        return new MonitoredItemConfig
        {
            Id = id, NodeId = new NodeId(name, 2), DisplayName = name, QueueSize = 13,
            SamplingInterval = TimeSpan.FromMilliseconds(25), DiscardOldest = false
        };
    }

    private static readonly string[] s_subscriptionStateApplyAndCapturePreserveSettingsItemsAndModesExpected =
    [
        "ns=2;s=Temperature",
        "ns=2;s=Pressure",
        "ns=2;s=Events",
    ];
    private static readonly string[] s_seedExistingBenchAddsDeduplicatedVariableContextWithoutOpeninExpected =
    [
        "ns=2;s=Temperature",
        "ns=2;s=Pressure",
    ];
    private static readonly string[] s_seedExistingBenchAddsDeduplicatedVariableContextWithoutOpeninExpected2 =
    [
        "Boiler / Temperature",
        "Pressure",
    ];
}
