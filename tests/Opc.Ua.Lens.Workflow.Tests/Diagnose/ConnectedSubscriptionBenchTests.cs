// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client.Subscriptions;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Tests.Desktop;
using UaLens.Tests.Subscriptions;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Platform("Win,Linux")]
[Category("LensDesktopWorkflow")]
[NonParallelizable]
public sealed class ConnectedSubscriptionBenchTests
{
    [Test]
    public Task ConnectedBenchWaitsForExplicitSizeAndStopReleasesOnlyItsSubscriptions()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            var engine = new ChannelV2EngineAdapterTests.AdapterContext();
            ISubscriptionManager? manager = engine.Manager.Object;
            context.Session.Setup(session => session.TryGetSubscriptionManager(out manager)).Returns(true);
            await context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = new SubscriptionBenchPlugin(context.Host);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(plugin.IsConnected, Is.True);
            Assert.That(plugin.ItemsSliderMax, Is.EqualTo(1000));
            Assert.That(plugin.SubsSliderMax, Is.EqualTo(100));
            Assert.That(engine.Adds, Is.Zero);
            await plugin.SeedFromNodeAsync(s_first, NodeClass.Variable, "First").ConfigureAwait(true);
            await plugin.SeedFromNodeAsync(s_second, NodeClass.Variable, "Second").ConfigureAwait(true);
            Assert.That(plugin.PoolDescription, Is.EqualTo("2 variable(s) in pool."));
            Assert.That(engine.Adds, Is.Zero, "Populating the pool must not start subscription traffic.");
            var itemsCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.ItemAdded = () =>
            {
                if (engine.Monitored.Count == 3)
                {
                    itemsCreated.TrySetResult();
                }
            };
            plugin.ItemsSliderValue = 3;
            plugin.SubsSliderValue = 1;
            await itemsCreated.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.That(engine.Adds, Is.EqualTo(1));
            Assert.That(
                engine.ItemOptions.Select(options => options.CurrentValue.StartNodeId),
                Is.EqualTo(s_roundRobin));
            Assert.That(engine.ItemOptions.Select(options => options.CurrentValue.AttributeId),
                Is.All.EqualTo(Attributes.Value));
            await plugin.StopCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(plugin.ItemsSliderValue, Is.Zero);
            Assert.That(plugin.SubsSliderValue, Is.Zero);
            Assert.That(plugin.Status, Does.StartWith("Stopped"));
            engine.Subscription.Verify(subscription => subscription.DisposeAsync(), Times.Once);
            Assert.That(context.Desktop.Connection.IsConnected, Is.True);
            Assert.That(plugin.TotalValuesText, Is.EqualTo("Total values: 0"));
        });
    }

    [Test]
    public Task MissingV2EngineRefusesGrowthAndRestoresTheActualSize()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new ConnectedProtocolContext();
            await context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = new SubscriptionBenchPlugin(context.Host);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            await plugin.SeedFromNodeAsync(s_first, NodeClass.Variable, "First").ConfigureAwait(true);
            await DesktopInteraction.ModelChangedAsync(plugin,
                () => plugin.Status.Contains("requires the V2 engine", StringComparison.Ordinal), () =>
                {
                    plugin.ItemsSliderValue = 1;
                    plugin.SubsSliderValue = 1;
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
            Assert.That(plugin.SubsSliderValue, Is.Zero);
            Assert.That(context.Desktop.Connection.IsConnected, Is.True);
            await context.Desktop.Connection.DisconnectAsync().ConfigureAwait(true);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(plugin.IsConnected, Is.False);
            Assert.That(plugin.ItemsSliderValue, Is.Zero);
            Assert.That(plugin.Status, Does.Contain("Disconnected"));
        });
    }

    private static readonly NodeId s_first = new("First", 2);
    private static readonly NodeId s_second = new("Second", 2);
    private static readonly NodeId[] s_roundRobin = [s_first, s_second, s_first];
}
