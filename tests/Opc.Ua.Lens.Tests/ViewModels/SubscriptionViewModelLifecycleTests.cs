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
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[NonParallelizable]
public sealed class SubscriptionViewModelLifecycleTests
{
    [Test]
    [Platform("Win,Linux")]
    public Task UnboundItemsHaveDistinctNegativeIdsAndPreserveIntent()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var log = new RecordingLogger();
            await using var model = new SubscriptionViewModel("Offline boiler", null, log);
            var subscription = new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(350),
                KeepAliveCount = 7, LifetimeCount = 21, PublishingEnabled = false, Priority = 8
            };
            await model.ApplySubscriptionCommand.ExecuteAsync(subscription).ConfigureAwait(true);
            await model.AddItemCommand.ExecuteAsync(Item(90, "Temperature")).ConfigureAwait(true);
            await model.AddItemCommand.ExecuteAsync(Item(91, "Pressure")).ConfigureAwait(true);
            Assert.That(model.Items.Select(item => item.Id), Is.EqualTo(new[] { -1, -2 }));
            MonitoredItemConfig neighbor = model.Items[1];
            var settings = new MonitoredItemSettings
            {
                SamplingInterval = TimeSpan.FromMilliseconds(35), QueueSize = 9,
                DiscardOldest = false, MonitoringMode = MonitoringMode.Sampling
            };
            await model.ConfigureItemAsync(model.Items[0], settings).ConfigureAwait(true);
            await model.SetMonitoringModeAsync(model.ItemStatuses[0], MonitoringMode.Disabled).ConfigureAwait(true);

            Assert.That(model.Items[0].Id, Is.EqualTo(-1));
            Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(35)));
            Assert.That(model.Items[0].QueueSize, Is.EqualTo(9));
            Assert.That(model.Items[0].DiscardOldest, Is.False);
            Assert.That(model.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
            Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Disabled"));
            Assert.That(model.Items[1], Is.SameAs(neighbor));
            Assert.That(model.IsBound, Is.False);
            Assert.That(model.Subscription, Is.SameAs(subscription));
            LogRecord stored = log.Records.Single();
            Assert.That(stored.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(stored.Event.Id, Is.EqualTo(530));
            Assert.That(stored.State["Title"], Is.EqualTo("Offline boiler"));
            Assert.That(stored.State["Pub"], Is.EqualTo(350d));
            Assert.That(stored.Exception, Is.Null);
            await model.RemoveItemCommand.ExecuteAsync(neighbor).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(model.Items.Select(item => item.Id), Is.EqualTo(new[] { -1 }));
            Assert.That(model.ItemStatuses.Select(row => row.Id), Is.EqualTo(new[] { -1 }));
        });
    }

    [Test]
    public async Task AttachAppliesConfigurationBeforeReplayingItemsAndAdoptsIdsAfterSuccess()
    {
        var adapter = new ControlledSubscriptionAdapter();
        await using var model = new SubscriptionViewModel("Boiler", null, NullLogger.Instance);
        MonitoredItemConfig first = Item(-1, "Temperature") with { QueueSize = 17, DiscardOldest = false };
        MonitoredItemConfig second = Item(-2, "Pressure") with { MonitoringMode = MonitoringMode.Sampling };
        model.Items.Add(first);
        model.Items.Add(second);
        model.Subscription = new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(750), KeepAliveCount = 4, LifetimeCount = 12
        };
        var order = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Mock.Setup(a => a.ApplySubscriptionAsync(model.Subscription, cancellation.Token)).Returns(() =>
        {
            order.Add("apply");
            return Task.CompletedTask;
        });
        adapter.Mock.Setup(a => a.AddItemAsync(It.IsAny<MonitoredItemConfig>(), cancellation.Token))
            .Returns((MonitoredItemConfig item, CancellationToken _) =>
            {
                order.Add(item.DisplayName);
                Assert.That(item.Id, Is.Zero);
                if (item.DisplayName == "Temperature")
                {
                    Assert.That(item.QueueSize, Is.EqualTo(17));
                    Assert.That(item.DiscardOldest, Is.False);
                    return Task.FromResult(501);
                }
                Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                entered.SetResult();
                return reply.Task;
            });

        Task attach = model.AttachAdapterAsync(adapter.Object, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        Assert.That(attach.IsCompleted, Is.False);
        Assert.That(model.Items, Is.EqualTo(new[] { first, second }));
        Assert.That(model.ItemStatuses.Select(row => row.Id), Is.EqualTo(new[] { -1, -2 }));
        reply.SetResult(702);
        await attach.ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(s_attachAppliesConfigurationBeforeReplayingItemsAndAdoptsIdsAftExpected));
        Assert.That(model.Items.Select(item => item.Id), Is.EqualTo(s_attachAppliesConfigurationBeforeReplayingItemsAndAdoptsIdsAftExpected2));
        Assert.That(model.Items[0], Is.EqualTo(first with { Id = 501 }));
        Assert.That(model.Items[1], Is.EqualTo(second with { Id = 702 }));
        Assert.That(model.ItemStatuses.Select(row => row.Id), Is.EqualTo(s_attachAppliesConfigurationBeforeReplayingItemsAndAdoptsIdsAftExpected2));
        Assert.That(model.Adapter, Is.SameAs(adapter.Object));
        Assert.That(adapter.DisposeCount, Is.Zero);
    }

    [TestCase("apply")]
    [TestCase("replay")]
    [TestCase("preCanceled")]
    [TestCase("midCanceled")]
    [TestCase("disposed")]
    public async Task FailedCanceledOrDisposedAttachDisposesIncomingAdapterAndKeepsIntent(string failureStage)
    {
        var adapter = new ControlledSubscriptionAdapter();
        var model = new SubscriptionViewModel("Retained intent", null, NullLogger.Instance);
        model.Items.Add(Item(-1, "Temperature"));
        model.Items.Add(Item(-2, "Pressure"));
        MonitoredItemConfig[] original = model.Items.ToArray();
        using var cancellation = new CancellationTokenSource();
        var failure = new ServiceResultException(StatusCodes.BadTooManyMonitoredItems, "replay rejected");
        if (failureStage == "apply")
        {
            adapter.Mock.Setup(a => a.ApplySubscriptionAsync(
                It.IsAny<SubscriptionConfig>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        }
        if (failureStage is "replay" or "midCanceled")
        {
            adapter.Mock.Setup(a => a.AddItemAsync(
                It.Is<MonitoredItemConfig>(item => item.DisplayName == "Pressure"),
                It.IsAny<CancellationToken>())).Returns(() =>
            {
                if (failureStage == "midCanceled")
                {
                    cancellation.Cancel();
                    return Task.FromResult(888);
                }
                return Task.FromException<int>(failure);
            });
        }
        if (failureStage == "preCanceled")
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        if (failureStage == "disposed")
        {
            await model.DisposeAsync().ConfigureAwait(false);
        }
        try
        {
            Task attach = model.AttachAdapterAsync(adapter.Object, cancellation.Token);
            if (failureStage == "disposed")
            {
                await Assert.ThatAsync(() => attach, Throws.InstanceOf<ObjectDisposedException>())
                    .ConfigureAwait(false);
            }
            else if (failureStage is "preCanceled" or "midCanceled")
            {
                await Assert.ThatAsync(() => attach, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => attach, Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            }
            Assert.That(model.Items, Is.EqualTo(original));
            Assert.That(model.Adapter, Is.Null);
            Assert.That(model.IsBound, Is.False);
            Assert.That(adapter.DisposeCount, Is.EqualTo(1));
            Assert.That(adapter.Added, Has.Count.EqualTo(failureStage is "replay" or "midCanceled" ? 1 : 0));
            await adapter.Input.Reader.Completion.ConfigureAwait(false);
        }
        finally
        {
            await model.DisposeAsync().ConfigureAwait(false);
        }
        Assert.That(adapter.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ReattachDrainsOldCaptureBeforeDisposingAdapterOrApplyingReplacement()
    {
        var order = new List<string>();
        var source = new DrainControlledReader(order);
        var old = new ControlledSubscriptionAdapter();
        old.Mock.SetupGet(a => a.Events).Returns(source);
        old.Mock.Setup(a => a.DisposeAsync()).Returns(() =>
        {
            order.Add("old adapter disposed");
            return ValueTask.CompletedTask;
        });
        var replacement = new ControlledSubscriptionAdapter();
        replacement.Mock.Setup(a => a.ApplySubscriptionAsync(
            It.IsAny<SubscriptionConfig>(), It.IsAny<CancellationToken>())).Returns(() =>
        {
            order.Add("new adapter applied");
            return Task.CompletedTask;
        });
        await using var model = new SubscriptionViewModel("Drain order", old.Object, NullLogger.Instance);
        await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);

        Task attach = model.AttachAdapterAsync(replacement.Object);
        try
        {
            await source.Draining.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            Assert.That(attach.IsCompleted, Is.False);
            Assert.That(order, Is.Empty);
            Assert.That(model.Adapter, Is.Null);
            source.Release.SetResult();
            await attach.ConfigureAwait(false);
        }
        finally
        {
            source.Release.TrySetResult();
            await attach.ConfigureAwait(false);
        }

        Assert.That(order, Is.EqualTo(s_reattachDrainsOldCaptureBeforeDisposingAdapterOrApplyingReplaExpected));
        Assert.That(model.Adapter, Is.SameAs(replacement.Object));
        old.Mock.Verify(a => a.DisposeAsync(), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HiddenViewStillRecordsOrderedChannelNotifications(bool detachAndReattach)
    {
        var old = new ControlledSubscriptionAdapter();
        var replacement = new ControlledSubscriptionAdapter();
        old.Mock.Setup(a => a.DisposeAsync()).Returns(ValueTask.CompletedTask);
        await using var model = new SubscriptionViewModel("Hidden", old.Object, NullLogger.Instance);
        ChannelReader<NotificationEvent> playback = model.Recorder.CreateReader();
        DateTime time = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var first = new NotificationEvent(NotificationKind.DataChange, 101, 2, 51, time, -12.25);
        var second = new NotificationEvent(NotificationKind.Event, 202, 4, 52, time.AddSeconds(1));
        var third = new NotificationEvent(NotificationKind.KeepAlive, 0, 0, 53, time.AddSeconds(2));
        Assert.That(old.Input.Writer.TryWrite(first), Is.True);
        Assert.That(await ReadAsync(playback).ConfigureAwait(false), Is.EqualTo(first));
        if (detachAndReattach)
        {
            await model.DetachAdapterAsync().ConfigureAwait(false);
            Assert.That(old.Input.Writer.TryWrite(first with { SequenceNumber = 999 }), Is.True);
            await model.AttachAdapterAsync(replacement.Object).ConfigureAwait(false);
        }
        ControlledSubscriptionAdapter active = detachAndReattach ? replacement : old;
        Assert.That(active.Input.Writer.TryWrite(second), Is.True);
        Assert.That(active.Input.Writer.TryWrite(third), Is.True);
        Assert.That(await ReadAsync(playback).ConfigureAwait(false), Is.EqualTo(second));
        Assert.That(await ReadAsync(playback).ConfigureAwait(false), Is.EqualTo(third));

        List<NotificationEvent> retained = model.Recorder.Snapshot().ToList();
        Assert.That(retained.Select(value => value.SequenceNumber), Is.EqualTo(new uint[] { 51, 52, 53 }));
        Assert.That(retained.Select(value => value.Kind), Is.EqualTo(new[]
        {
            NotificationKind.DataChange, NotificationKind.Event, NotificationKind.KeepAlive
        }));
        Assert.That(retained[0].Value, Is.EqualTo(-12.25));
        Assert.That(retained[1].ItemId, Is.EqualTo(202));
        Assert.That(retained[1].ValueCount, Is.EqualTo(4));
        Assert.That(retained[2].ReceivedAtUtc, Is.EqualTo(time.AddSeconds(2)));
        Assert.That(model.Recorder.TotalWritten, Is.EqualTo(3));
        Assert.That(playback.TryRead(out _), Is.False);
        old.Input.Writer.TryComplete();
        await model.DisposeAsync().ConfigureAwait(false);
        Assert.That(await playback.WaitToReadAsync().ConfigureAwait(false), Is.False);
        await playback.Completion.ConfigureAwait(false);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DetachPreservesItemsAndDisposeCompletesRecorderEvenWhenAdapterCleanupFails(bool failCleanup)
    {
        var adapter = new ControlledSubscriptionAdapter();
        var failure = new InvalidOperationException("subscription deletion failed");
        int disposals = 0;
        adapter.Mock.Setup(a => a.DisposeAsync()).Returns(() =>
        {
            disposals++;
            adapter.Input.Writer.TryComplete();
            return failCleanup ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        });
        var model = new SubscriptionViewModel("Retain settings", adapter.Object, NullLogger.Instance);
        MonitoredItemConfig intent = Item(17, "Temperature") with { QueueSize = 11 };
        model.Items.Add(intent);
        ChannelReader<NotificationEvent> playback = model.Recorder.CreateReader();

        if (failCleanup)
        {
            await Assert.ThatAsync(async () => await model.DisposeAsync().ConfigureAwait(false),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await model.DisposeAsync().ConfigureAwait(false),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        }
        else
        {
            await model.DetachAdapterAsync().ConfigureAwait(false);
            Assert.That(model.SubscriptionStatus, Is.EqualTo("● Disconnected — items preserved for reconnect."));
            Assert.That(playback.Completion.IsCompleted, Is.False);
            await model.DisposeAsync().ConfigureAwait(false);
            await model.DisposeAsync().ConfigureAwait(false);
        }

        Assert.That(model.Adapter, Is.Null);
        Assert.That(model.Items.Single(), Is.SameAs(intent));
        Assert.That(disposals, Is.EqualTo(1));
        Assert.That(await playback.WaitToReadAsync().ConfigureAwait(false), Is.False);
        await playback.Completion.ConfigureAwait(false);
        Assert.That(() => model.Recorder.Record(default), Throws.InvalidOperationException);
    }

    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("configure")]
    [TestCase("mode")]
    [Platform("Win,Linux")]
    public Task FailedItemMutationsPreserveConfirmedIntentAndReportOriginalFailure(string operation)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            var log = new RecordingLogger();
            await using var model = new SubscriptionViewModel("Mutation boiler", adapter.Object, log);
            MonitoredItemConfig first = Item(101, "Temperature");
            MonitoredItemConfig second = Item(102, "Pressure");
            model.Items.Add(first);
            model.Items.Add(second);
            adapter.Items.Add(first);
            adapter.Items.Add(second);
            var failure = new ServiceResultException(StatusCodes.BadOutOfRange, "server rejected sampling");
            switch (operation)
            {
                case "add":
                    adapter.Mock.Setup(a => a.AddItemAsync(
                        It.IsAny<MonitoredItemConfig>(), CancellationToken.None)).ThrowsAsync(failure);
                    await model.AddItemCommand.ExecuteAsync(Item(0, "Valve")).ConfigureAwait(true);
                    Assert.That(model.ErrorText,
                        Is.EqualTo("Adding the monitored item failed: server rejected sampling"));
                    break;
                case "remove":
                    adapter.Mock.Setup(a => a.RemoveItemAsync(101, CancellationToken.None)).ThrowsAsync(failure);
                    await model.RemoveItemCommand.ExecuteAsync(first).ConfigureAwait(true);
                    Assert.That(model.ErrorText,
                        Is.EqualTo("Removing the monitored item failed: server rejected sampling"));
                    break;
                case "configure":
                    adapter.Mock.Setup(a => a.ConfigureItemAsync(
                        It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                    await Assert.ThatAsync(() => model.ConfigureItemAsync(first,
                        new MonitoredItemSettings { QueueSize = 99 }),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(true);
                    break;
                default:
                    adapter.Mock.Setup(a => a.SetMonitoringModeAsync(101, MonitoringMode.Disabled,
                        CancellationToken.None)).ThrowsAsync(failure);
                    await model.SetMonitoringModeAsync(model.ItemStatuses[0], MonitoringMode.Disabled)
                        .ConfigureAwait(true);
                    Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Reporting"));
                    break;
            }
            await FlushAsync().ConfigureAwait(true);
            Assert.That(model.Items, Is.EqualTo(new[] { first, second }));
            Assert.That(adapter.Items, Is.EqualTo(new[] { first, second }));
            Assert.That(model.ItemStatuses.Select(row => row.Id), Is.EqualTo(s_failedItemMutationsPreserveConfirmedIntentAndReportOriginalFaExpected));
            if (operation != "configure")
            {
                LogRecord record = log.Records.Single();
                Assert.That(record.Exception, Is.SameAs(failure));
                Assert.That(record.State["Title"], Is.EqualTo("Mutation boiler"));
                Assert.That(record.Level, Is.EqualTo(LogLevel.Error));
                Assert.That(record.Event.Id, Is.EqualTo(operation switch
                {
                    "add" => 534, "remove" => 536, _ => 538
                }));
                Assert.That(record.Event.Name, Is.EqualTo(operation switch
                {
                    "add" => "SubscriptionTabAddItemFailed",
                    "remove" => "SubscriptionTabRemoveItemFailed",
                    _ => "SubscriptionTabSetMonitoringModeFailed"
                }));
            }
        });
    }

    [TestCase("missing")]
    [TestCase("canceled")]
    public async Task InvalidConfigurationDoesNotMutateItemsOrContactUnexpectedItem(string kind)
    {
        var adapter = new ControlledSubscriptionAdapter();
        await using var model = new SubscriptionViewModel("Configuration", adapter.Object, NullLogger.Instance);
        MonitoredItemConfig first = Item(101, "Temperature");
        model.Items.Add(first);
        adapter.Items.Add(first);
        using var cancellation = new CancellationTokenSource();
        if (kind == "canceled")
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        Task configure = model.ConfigureItemAsync(kind == "missing" ? Item(999, "Other") : first,
            new MonitoredItemSettings { SamplingInterval = TimeSpan.FromMilliseconds(33) }, cancellation.Token);
        await Assert.ThatAsync(() => configure, kind == "missing"
            ? Throws.InvalidOperationException.With.Message.EqualTo("The monitored item is no longer in this document.")
            : Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        Assert.That(model.Items.Single(), Is.SameAs(first));
        Assert.That(adapter.Configured, Is.Empty);
        adapter.Mock.Verify(a => a.ConfigureItemAsync(It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()),
            kind == "missing" ? Times.Never() : Times.Once());
    }

    [TestCase("creating", "Creating item")]
    [TestCase("pending", "Applying settings")]
    [TestCase("bad", "BadOutOfRange")]
    [TestCase("created", "Uncertain")]
    [TestCase("unknown", "Uncertain")]
    [Platform("Win,Linux")]
    public Task StatusShowsRevisedSettingsItemResultAndExactSample(string resultKind, string expectedStatus)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel("Status boiler", adapter.Object, NullLogger.Instance);
            MonitoredItemConfig requested = Item(101, "Temperature");
            MonitoredItemConfig revised = requested with
            {
                SamplingInterval = TimeSpan.FromMilliseconds(275), QueueSize = 23,
                MonitoringMode = MonitoringMode.Sampling
            };
            model.Items.Add(requested);
            adapter.Items.Add(revised);
            MonitoredItemLiveStats? stats = new();
            DateTime time = new(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc);
            stats.RecordValue(new DataValue(new Variant(-9.5), StatusCodes.Uncertain, time, time.AddMilliseconds(800)));
            stats.RecordEvent();
            adapter.Mock.Setup(a => a.TryGetItemStats(101, out stats)).Returns(true);
            var result = new MonitoredItemResult(resultKind != "creating", resultKind == "pending",
                resultKind == "bad" ? StatusCodes.BadOutOfRange : StatusCodes.Good);
            adapter.Mock.Setup(a => a.TryGetItemResult(101, out result)).Returns(resultKind != "unknown");
            adapter.Mock.SetupGet(a => a.CurrentPublishingInterval).Returns(TimeSpan.FromMilliseconds(625));
            adapter.Mock.SetupGet(a => a.CurrentKeepAliveCount).Returns(8);
            adapter.Mock.SetupGet(a => a.CurrentLifetimeCount).Returns(27);
            model.ShowItemStatusGrid = false;
            model.ShowItemStatusGrid = true;
            model.RefreshStatus();

            MonitoredItemStatusRow row = model.ItemStatuses.Single();
            Assert.That(row.Id, Is.EqualTo(101));
            Assert.That(row.Sampling, Is.EqualTo("275ms"));
            Assert.That(row.Queue, Is.EqualTo("23"));
            Assert.That(row.Mode, Is.EqualTo("Sampling"));
            Assert.That(row.Samples, Is.EqualTo("2"));
            Assert.That(row.LastValue, Is.EqualTo("-9.5"));
            Assert.That(row.LastStatus, Is.EqualTo(expectedStatus));
            Assert.That(row.SourceTimestamp, Is.EqualTo("05:06:07.123"));
            Assert.That(row.ServerTimestamp, Is.EqualTo("05:06:07.923"));
            Assert.That(model.SubscriptionStatus,
                Is.EqualTo("Publishing 625 ms / keep-alive 8 / lifetime 27 / history retired 0"));
            Assert.That(model.Items.Single(), Is.SameAs(requested));
            model.OnDeactivated();
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task SuccessfulItemChangesForwardExactIdentityAndKeepNeighbors()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel(
                "Confirmed mutations", adapter.Object, NullLogger.Instance);
            await model.AddItemCommand.ExecuteAsync(Item(0, "Temperature")).ConfigureAwait(true);
            await model.AddItemCommand.ExecuteAsync(Item(0, "Pressure")).ConfigureAwait(true);
            MonitoredItemConfig neighbor = model.Items[1];
            using var cancellation = new CancellationTokenSource();
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp, DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 0.75
            };
            await model.ConfigureItemAsync(model.Items[0], new MonitoredItemSettings
            {
                SamplingInterval = TimeSpan.FromMilliseconds(27), QueueSize = 15,
                DiscardOldest = false, MonitoringMode = MonitoringMode.Sampling, DataChangeFilter = filter
            }, cancellation.Token).ConfigureAwait(true);
            adapter.Mock.Setup(a => a.SetMonitoringModeAsync(101, MonitoringMode.Disabled, CancellationToken.None))
                .Returns(Task.CompletedTask);
            await model.SetMonitoringModeDisabledCommand.ExecuteAsync(model.ItemStatuses[0]).ConfigureAwait(true);

            MonitoredItemConfig sent = adapter.Configured.Single();
            Assert.That(sent.Id, Is.EqualTo(101));
            Assert.That(sent.NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
            Assert.That(sent.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(sent.QueueSize, Is.EqualTo(15));
            Assert.That(sent.DataChangeFilter, Is.SameAs(filter));
            Assert.That(sent.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(27)));
            Assert.That(sent.DiscardOldest, Is.False);
            Assert.That(model.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
            Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Disabled"));
            Assert.That(model.Items[1], Is.SameAs(neighbor));
            adapter.Mock.Verify(a => a.ConfigureItemAsync(sent, cancellation.Token), Times.Once);
            adapter.Mock.Verify(a => a.SetMonitoringModeAsync(101, MonitoringMode.Disabled, CancellationToken.None),
                Times.Once);
            await model.RemoveItemCommand.ExecuteAsync(model.Items[0]).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(adapter.Removed, Is.EqualTo(s_successfulItemChangesForwardExactIdentityAndKeepNeighborsExpected));
            Assert.That(model.Items.Single(), Is.SameAs(neighbor));
            Assert.That(model.ItemStatuses.Single().Id, Is.EqualTo(102));
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task FailedPublishingApplyKeepsRequestedIntentAndReportsStructuredFailure()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            var log = new RecordingLogger();
            await using var model = new SubscriptionViewModel("Publishing boiler", adapter.Object, log);
            var failure = new ServiceResultException(StatusCodes.BadOutOfRange, "publishing rejected");
            adapter.Mock.Setup(a => a.ApplySubscriptionAsync(
                It.IsAny<SubscriptionConfig>(), CancellationToken.None)).ThrowsAsync(failure);
            var requested = new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(175), LifetimeCount = 31, KeepAliveCount = 9
            };

            await model.ApplySubscriptionCommand.ExecuteAsync(requested).ConfigureAwait(true);

            Assert.That(model.Subscription, Is.SameAs(requested));
            Assert.That(adapter.Configuration.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(1000)));
            Assert.That(model.ErrorText, Is.EqualTo("Subscription settings failed: publishing rejected"));
            LogRecord record = log.Records.Single();
            Assert.That(record.Exception, Is.SameAs(failure));
            Assert.That(record.Event.Id, Is.EqualTo(532));
            Assert.That(record.Event.Name, Is.EqualTo("SubscriptionTabSettingsFailed"));
            Assert.That(record.State["Title"], Is.EqualTo("Publishing boiler"));
            adapter.Mock.Verify(a => a.ApplySubscriptionAsync(requested, CancellationToken.None), Times.Once);
        });
    }

    [Test]
    public async Task FaultedCaptureReportsOriginalFailureAndStillCompletesOwnedRecorderOnDisposal()
    {
        var adapter = new ControlledSubscriptionAdapter();
        var failure = new InvalidOperationException("notification source failed");
        adapter.Input.Writer.TryComplete(failure);
        var model = new SubscriptionViewModel("Faulted capture", adapter.Object, NullLogger.Instance);
        ChannelReader<NotificationEvent> playback = model.Recorder.CreateReader();

        model.RefreshStatus();

        Assert.That(model.SubscriptionStatus, Is.EqualTo("Notification capture failed: notification source failed"));
        Assert.That(model.Recorder.TotalWritten, Is.Zero);
        await Assert.ThatAsync(async () => await model.DisposeAsync().ConfigureAwait(false),
            Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        Assert.That(model.Adapter, Is.Null);
        Assert.That(adapter.DisposeCount, Is.EqualTo(1));
        Assert.That(await playback.WaitToReadAsync().ConfigureAwait(false), Is.False);
        await playback.Completion.ConfigureAwait(false);
    }

    private static MonitoredItemConfig Item(int id, string name)
    {
        return new MonitoredItemConfig
        {
            Id = id, DisplayName = name, NodeId = new NodeId(name, 2),
            SamplingInterval = TimeSpan.FromMilliseconds(100), QueueSize = 3
        };
    }

    private static Task<NotificationEvent> ReadAsync(ChannelReader<NotificationEvent> reader)
    {
        return reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private sealed class DrainControlledReader(List<string> order) : ChannelReader<NotificationEvent>
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Draining { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool TryRead(out NotificationEvent item)
        {
            item = default;
            return false;
        }

        public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => canceled.TrySetResult());
            Waiting.TrySetResult();
            await canceled.Task.ConfigureAwait(false);
            Draining.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            order.Add("capture drained");
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private sealed record LogRecord(
        LogLevel Level, EventId Event, Exception? Exception, IReadOnlyDictionary<string, object?> State);

    private sealed class RecordingLogger : ILogger
    {
        public List<LogRecord> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Records.Add(new LogRecord(logLevel, eventId, exception, fields));
        }
    }

    private static readonly string[] s_attachAppliesConfigurationBeforeReplayingItemsAndAdoptsIdsAftExpected =
    [
        "apply",
        "Temperature",
        "Pressure",
    ];
    private static readonly int[] s_attachAppliesConfigurationBeforeReplayingItemsAndAdoptsIdsAftExpected2 =
    [
        501,
        702,
    ];
    private static readonly string[] s_reattachDrainsOldCaptureBeforeDisposingAdapterOrApplyingReplaExpected =
    [
        "capture drained",
        "old adapter disposed",
        "new adapter applied",
    ];
    private static readonly int[] s_failedItemMutationsPreserveConfirmedIntentAndReportOriginalFaExpected =
    [
        101,
        102,
    ];
    private static readonly int[] s_successfulItemChangesForwardExactIdentityAndKeepNeighborsExpected =
    [
        101,
    ];
}
