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
public sealed partial class SubscriptionViewModelLifecycleTests
{
    [Test]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task UnboundItemsHaveDistinctDocumentIdsAndPreserveIntent()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var log = new RecordingLogger();
            await using var model = new SubscriptionViewModel("Offline boiler", null, log);
            var subscription = new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(350),
                KeepAliveCount = 7,
                LifetimeCount = 21,
                PublishingEnabled = false,
                Priority = 8
            };
            await model.ApplySubscriptionCommand.ExecuteAsync(subscription).ConfigureAwait(true);
            await model.AddItemCommand.ExecuteAsync(Item(90, "Temperature")).ConfigureAwait(true);
            await model.AddItemCommand.ExecuteAsync(Item(91, "Pressure")).ConfigureAwait(true);
            Assert.That(model.Items.Select(item => item.Id), Is.EqualTo(s_offlineItemIds));
            MonitoredItemConfig neighbor = model.Items[1];
            var settings = new MonitoredItemSettings
            {
                SamplingInterval = TimeSpan.FromMilliseconds(35),
                QueueSize = 9,
                DiscardOldest = false,
                MonitoringMode = MonitoringMode.Sampling
            };
            await model.ConfigureItemAsync(model.Items[0], settings).ConfigureAwait(true);
            await model.SetMonitoringModeAsync(model.ItemStatuses[0], MonitoringMode.Disabled).ConfigureAwait(true);

            Assert.That(model.Items[0].Id, Is.EqualTo(1));
            Assert.That(model.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(35)));
            Assert.That(model.Items[0].QueueSize, Is.EqualTo(9));
            Assert.That(model.Items[0].DiscardOldest, Is.False);
            Assert.That(model.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
            Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Disabled"));
            Assert.That(model.Items[1], Is.SameAs(neighbor));
            Assert.That(model.IsBound, Is.False);
            Assert.That(model.Subscription, Is.SameAs(subscription));
            Assert.That(log.Records, Is.Empty);
            await model.RemoveItemCommand.ExecuteAsync(neighbor).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(model.Items.Select(item => item.Id), Is.EqualTo(s_remainingOfflineItemIds));
            Assert.That(model.ItemStatuses.Select(row => row.Id), Is.EqualTo(s_remainingOfflineItemIds));
        });
    }

    [TestCase("add")]
    [TestCase("remove")]
    [TestCase("configure")]
    [TestCase("mode")]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task FailedItemMutationsRetainLocalIntentAndConfirmedAdapterStateAndReportOriginalFailure(string operation)
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
            MonitoredItemConfig[] expected;
            switch (operation)
            {
                case "add":
                    adapter.Mock.Setup(a => a.AddItemAsync(
                        It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                    await model.AddItemCommand.ExecuteAsync(Item(0, "Valve")).ConfigureAwait(true);
                    expected = [first, second, Item(103, "Valve")];
                    adapter.Mock.Verify(a => a.AddItemAsync(
                        expected[2], It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
                    Assert.That(model.ErrorText,
                        Is.EqualTo("Adding the monitored item failed: server rejected sampling"));
                    break;
                case "remove":
                    adapter.Mock.Setup(a => a.RemoveItemAsync(101, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                    await model.RemoveItemCommand.ExecuteAsync(first).ConfigureAwait(true);
                    expected = [second];
                    adapter.Mock.Verify(a => a.RemoveItemAsync(
                        first.Id, It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
                    Assert.That(model.ErrorText,
                        Is.EqualTo("Removing the monitored item failed: server rejected sampling"));
                    break;
                case "configure":
                    adapter.Mock.Setup(a => a.ConfigureItemAsync(
                        It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                    await Assert.ThatAsync(() => model.ConfigureItemAsync(first,
                        new MonitoredItemSettings
                        {
                            SamplingInterval = first.SamplingInterval,
                            QueueSize = 99,
                            DiscardOldest = first.DiscardOldest,
                            MonitoringMode = first.MonitoringMode
                        }),
                        Throws.Exception.SameAs(failure)).ConfigureAwait(true);
                    expected = [first with { QueueSize = 99 }, second];
                    adapter.Mock.Verify(a => a.ConfigureItemAsync(
                        expected[0], It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
                    Assert.That(model.ItemStatuses[0].Queue, Is.EqualTo("99"));
                    break;
                default:
                    adapter.Mock.Setup(a => a.SetMonitoringModeAsync(101, MonitoringMode.Disabled,
                        It.IsAny<CancellationToken>())).ThrowsAsync(failure);
                    await model.SetMonitoringModeAsync(model.ItemStatuses[0], MonitoringMode.Disabled)
                        .ConfigureAwait(true);
                    expected = [first with { MonitoringMode = MonitoringMode.Disabled }, second];
                    adapter.Mock.Verify(a => a.SetMonitoringModeAsync(first.Id, MonitoringMode.Disabled,
                        It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
                    Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Disabled"));
                    break;
            }
            await FlushAsync().ConfigureAwait(true);
            Assert.That(model.Items, Is.EqualTo(expected));
            Assert.That(model.Items.Single(item => item.Id == second.Id), Is.SameAs(second));
            Assert.That(adapter.Items, Is.EqualTo(new[] { first, second }));
            Assert.That(model.ItemStatuses.Select(row => row.Id), Is.EqualTo(expected.Select(item => item.Id)));
            string operationName = operation switch
            {
                "add" => "Adding the monitored item",
                "remove" => "Removing the monitored item",
                "configure" => "Item settings",
                _ => "Monitoring mode"
            };
            Assert.That(model.ErrorText, Is.EqualTo($"{operationName} failed: server rejected sampling"));
            LogRecord record = log.Records.Single();
            Assert.That(record.Exception, Is.SameAs(failure));
            Assert.That(record.State["Title"], Is.EqualTo("Mutation boiler"));
            Assert.That(record.State["Operation"], Is.EqualTo(operationName));
            Assert.That(record.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(record.Event.Id, Is.EqualTo(539));
            Assert.That(record.Event.Name, Is.EqualTo("SubscriptionMutationFailed"));
        });
    }

    [TestCase("creating", "Creating item")]
    [TestCase("pending", "Applying settings")]
    [TestCase("bad", "BadOutOfRange")]
    [TestCase("created", "Uncertain")]
    [TestCase("unknown", "Uncertain")]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task StatusShowsRevisedSettingsItemResultAndExactSample(string resultKind, string expectedStatus)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel("Status boiler", adapter.Object, NullLogger.Instance);
            MonitoredItemConfig requested = Item(101, "Temperature");
            MonitoredItemConfig revised = requested with
            {
                SamplingInterval = TimeSpan.FromMilliseconds(275),
                QueueSize = 23,
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
    [Category("LensDesktopWorkflow")]
    public Task SuccessfulItemChangesForwardExactIdentityAndKeepNeighbors()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel(
                "Confirmed mutations", adapter.Object, NullLogger.Instance);
            await model.AddItemCommand.ExecuteAsync(Item(0, "Temperature")).ConfigureAwait(true);
            await model.AddItemCommand.ExecuteAsync(Item(0, "Pressure")).ConfigureAwait(true);
            MonitoredItemConfig first = model.Items[0];
            MonitoredItemConfig neighbor = model.Items[1];
            Assert.That(first.Id, Is.EqualTo(1));
            Assert.That(neighbor.Id, Is.EqualTo(2));
            Assert.That(adapter.Added, Is.EqualTo(new[] { first, neighbor }));
            using var cancellation = new CancellationTokenSource();
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValueTimestamp,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 0.75
            };
            await model.ConfigureItemAsync(model.Items[0], new MonitoredItemSettings
            {
                SamplingInterval = TimeSpan.FromMilliseconds(27),
                QueueSize = 15,
                DiscardOldest = false,
                MonitoringMode = MonitoringMode.Sampling,
                DataChangeFilter = filter
            }, cancellation.Token).ConfigureAwait(true);
            await model.SetMonitoringModeDisabledCommand.ExecuteAsync(model.ItemStatuses[0]).ConfigureAwait(true);

            MonitoredItemConfig sent = adapter.Configured.Single();
            Assert.That(sent.Id, Is.EqualTo(first.Id));
            Assert.That(sent.NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
            Assert.That(sent.AttributeId, Is.EqualTo(Attributes.Value));
            Assert.That(sent.QueueSize, Is.EqualTo(15));
            Assert.That(sent.DataChangeFilter, Is.SameAs(filter));
            Assert.That(sent.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(27)));
            Assert.That(sent.DiscardOldest, Is.False);
            Assert.That(model.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
            Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Disabled"));
            Assert.That(model.Items[1], Is.SameAs(neighbor));
            Assert.That(adapter.Items[0], Is.EqualTo(model.Items[0]));
            adapter.Mock.Verify(a => a.ConfigureItemAsync(
                sent, It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
            adapter.Mock.Verify(a => a.SetMonitoringModeAsync(
                first.Id, MonitoringMode.Disabled, It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
            await model.RemoveItemCommand.ExecuteAsync(model.Items[0]).ConfigureAwait(true);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(adapter.Removed, Is.EqualTo(new[] { first.Id }));
            Assert.That(model.Items.Single(), Is.SameAs(neighbor));
            Assert.That(model.ItemStatuses.Single().Id, Is.EqualTo(neighbor.Id));
            Assert.That(adapter.Items.Single(), Is.EqualTo(neighbor));
        });
    }

    [Test]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task FailedPublishingApplyKeepsRequestedIntentAndReportsStructuredFailure()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            var log = new RecordingLogger();
            await using var model = new SubscriptionViewModel("Publishing boiler", adapter.Object, log);
            var failure = new ServiceResultException(StatusCodes.BadOutOfRange, "publishing rejected");
            adapter.Mock.Setup(a => a.ApplySubscriptionAsync(
                It.IsAny<SubscriptionConfig>(), It.IsAny<CancellationToken>())).ThrowsAsync(failure);
            var requested = new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(175),
                LifetimeCount = 31,
                KeepAliveCount = 9
            };

            await model.ApplySubscriptionCommand.ExecuteAsync(requested).ConfigureAwait(true);

            Assert.That(model.Subscription, Is.SameAs(requested));
            Assert.That(adapter.Configuration.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(1000)));
            Assert.That(model.ErrorText, Is.EqualTo("Subscription settings failed: publishing rejected"));
            LogRecord record = log.Records.Single();
            Assert.That(record.Exception, Is.SameAs(failure));
            Assert.That(record.Event.Id, Is.EqualTo(539));
            Assert.That(record.Event.Name, Is.EqualTo("SubscriptionMutationFailed"));
            Assert.That(record.State["Title"], Is.EqualTo("Publishing boiler"));
            Assert.That(record.State["Operation"], Is.EqualTo("Subscription settings"));
            Assert.That(record.Level, Is.EqualTo(LogLevel.Error));
            adapter.Mock.Verify(a => a.ApplySubscriptionAsync(
                requested, It.Is<CancellationToken>(token => token.CanBeCanceled)), Times.Once);
        });
    }

    private static readonly int[] s_offlineItemIds = [1, 2];
    private static readonly int[] s_remainingOfflineItemIds = [1];

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
}
