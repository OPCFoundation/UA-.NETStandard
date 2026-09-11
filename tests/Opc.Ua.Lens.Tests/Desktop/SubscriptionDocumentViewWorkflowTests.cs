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
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class SubscriptionDocumentViewWorkflowTests
{
    [TestCase(0, "Dots", false, false)]
    [TestCase(1, "Signal", true, true)]
    [TestCase(2, "Dots", true, false)]
    [TestCase(3, "Bars", true, false)]
    [TestCase(4, "Lines", true, false)]
    [TestCase(5, "Histogram", true, true)]
    [TestCase(6, "Heatmap", true, true)]
    public Task ModeAxisAndLegendControlsUpdateRealPlotsAndModel(
        int index, string mode, bool visible, bool plotted)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel(
                "Controlled monitor", adapter.Object, NullLogger.Instance);
            MonitoredItemConfig item = ScottPlotPumpTests.Item(4, "Temperature");
            model.Items.Add(item);
            adapter.Items.Add(item);
            using var view = new SubscriptionDocumentView(model);
            DesktopInteraction.Owner.Content = view;
            try
            {
                DesktopInteraction.Control<ComboBox>(view, "DocumentViewMode").SelectedIndex = index;
                Assert.That(model.DisplayModeIndex, Is.EqualTo(index));
                Assert.That(model.AnimationMode.ToString(), Is.EqualTo(mode));
                Assert.That(DesktopInteraction.Control<Grid>(view, "DocumentChart").IsVisible, Is.EqualTo(visible));
                ScottPlotView plotView = DesktopInteraction.Control<ScottPlotView>(view, "DocumentPlot");
                AnimationCanvas animation = DesktopInteraction.Control<AnimationCanvas>(view, "DocumentAnimation");
                Assert.That(plotView.IsVisible, Is.EqualTo(visible && plotted));
                Assert.That(animation.IsVisible, Is.EqualTo(visible && !plotted));
                Plot plot = DesktopInteraction.Control<AvaPlot>(plotView, "Plot").Plot;
                model.ShowLegend = true;
                model.ShowXAxis = true;
                model.ShowYAxis = false;
                Assert.That(plot.Legend.IsVisible, Is.True);
                Assert.That(plot.Axes.Bottom.IsVisible, Is.True);
                Assert.That(plot.Axes.Left.IsVisible, Is.False);
                model.ShowResourceOverlay = true;
                Assert.That(animation.ShowResourceOverlay, Is.True);
                if (plotted)
                {
                    Assert.That(plot.GetPlottables().Single().GetType().Name, Is.EqualTo(mode switch
                    {
                        "Signal" => nameof(DataStreamer),
                        "Histogram" => nameof(BarPlot),
                        _ => nameof(Heatmap)
                    }));
                }
                Assert.That(DesktopInteraction.Control<TextBlock>(view, "DocumentDeliverySummary").Text,
                    Is.EqualTo("0 values | 0 publishes | 0 keep-alives | gaps 0 | republished 0 | dropped 0"));
                for (int i = 0; i < 5; i++)
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "ChartZoomInButton"));
                }
                Assert.That(model.AnimationTimeScale, Is.EqualTo(8));
                for (int i = 0; i < 8; i++)
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "ChartZoomOutButton"));
                }
                Assert.That(model.AnimationTimeScale, Is.EqualTo(0.125));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "ChartZoomResetButton"));
                Assert.That(model.AnimationTimeScale, Is.EqualTo(1));
                Assert.That(animation.TimeScale, Is.EqualTo(1));
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task PublishingAndItemControlsSendExactRequestsAndPreserveNeighbor()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel(
                "Controlled monitor", adapter.Object, NullLogger.Instance);
            using var view = new SubscriptionDocumentView(model);
            DesktopInteraction.Owner.Content = view;
            try
            {
                TextBox input = DesktopInteraction.Control<TextBox>(view, "MonitoredNodeIdInput");
                Button add = DesktopInteraction.Control<Button>(view, "AddNodeIdButton");
                input.Text = "not a node id";
                DesktopInteraction.Click(add);
                Assert.That(model.ErrorText, Does.StartWith("Enter a valid node id"));
                Assert.That(adapter.Added, Is.Empty);
                input.Text = "ns=2;s=Temperature";
                await DesktopInteraction.ChangedAsync(input, () => string.IsNullOrEmpty(input.Text), () =>
                {
                    DesktopInteraction.Click(add);
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(model.Items[0].Id, Is.EqualTo(101));
                Assert.That(adapter.Added[0].NodeId, Is.EqualTo(new NodeId("Temperature", 2)));
                Assert.That(input.Text, Is.Empty);
                input.Text = "ns=2;s=Pressure";
                await DesktopInteraction.ChangedAsync(input, () => string.IsNullOrEmpty(input.Text), () =>
                {
                    DesktopInteraction.Click(add);
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                MonitoredItemConfig neighbor = model.Items[1];
                DesktopInteraction.Control<NumericUpDown>(view, "PublishingIntervalInput").Value = 750;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "ApplyPublishingButton"));
                Assert.That(adapter.Applied.Single().PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(750)));
                Assert.That(adapter.Applied[0].PublishingEnabled, Is.True);
                Assert.That(model.Subscription.PublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(750)));

                ListBox values = DesktopInteraction.Control<ListBox>(view, "MonitorValuesList");
                values.SelectedItem = model.ItemStatuses[0];
                MonitoredItemSettingsDialog settings = await DesktopInteraction
                    .OpenedAsync<MonitoredItemSettingsDialog>(
                        () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "ItemSettingsButton")))
                    .ConfigureAwait(true);
                DesktopInteraction.Control<TextBox>(settings, "SamplingMs").Text = "25";
                DesktopInteraction.Control<TextBox>(settings, "QueueSizeBox").Text = "19";
                DesktopInteraction.Control<ComboBox>(settings, "MonitoringModeCombo").SelectedIndex = 1;
                DesktopInteraction.Control<CheckBox>(settings, "DiscardOldestBox").IsChecked = false;
                await DesktopInteraction.CollectionChangedAsync(model.Items,
                    () => model.Items[0].QueueSize == 19,
                    () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(settings, "OkButton")))
                    .ConfigureAwait(true);
                Assert.That(adapter.Configured.Single().Id, Is.EqualTo(101));
                Assert.That(adapter.Configured[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(25)));
                Assert.That(adapter.Configured[0].MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                Assert.That(adapter.Configured[0].DiscardOldest, Is.False);
                Assert.That(model.Items[1], Is.SameAs(neighbor));
                Assert.That(model.ItemStatuses[0].Mode, Is.EqualTo("Sampling"));
                Assert.That(model.ItemStatuses[0].Sampling, Is.EqualTo("25ms"));
                await DesktopInteraction.CollectionChangedAsync(model.Items, () => model.Items.Count == 1,
                    () => DesktopInteraction.Click(
                        DesktopInteraction.Control<Button>(view, "RemoveSelectedItemButton")))
                    .ConfigureAwait(true);
                Assert.That(adapter.Removed, Is.EqualTo(s_publishingAndItemControlsSendExactRequestsAndPreserveNeighborExpected));
                Assert.That(model.Items.Single(), Is.SameAs(neighbor));
                Assert.That(model.ItemStatuses.Single().Id, Is.EqualTo(102));
            }
            finally
            {
                foreach (Window child in DesktopInteraction.Owner.OwnedWindows.ToArray())
                {
                    child.Close();
                }
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task FailedItemSettingsLeaveConfirmedConfigurationUnchanged()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel(
                "Controlled monitor", adapter.Object, NullLogger.Instance);
            MonitoredItemConfig original = ScottPlotPumpTests.Item(4, "Temperature");
            adapter.Items.Add(original);
            model.Items.Add(original);
            adapter.Mock.Setup(a => a.ConfigureItemAsync(
                It.IsAny<MonitoredItemConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadOutOfRange, "sampling rejected"));
            using var view = new SubscriptionDocumentView(model);
            DesktopInteraction.Owner.Content = view;
            try
            {
                DesktopInteraction.Control<ListBox>(view, "MonitorValuesList").SelectedItem = model.ItemStatuses[0];
                MonitoredItemSettingsDialog settings = await DesktopInteraction
                    .OpenedAsync<MonitoredItemSettingsDialog>(
                        () => DesktopInteraction.Click(DesktopInteraction.Control<Button>(view, "ItemSettingsButton")))
                    .ConfigureAwait(true);
                DesktopInteraction.Control<TextBox>(settings, "QueueSizeBox").Text = "19";
                await DesktopInteraction.ModelChangedAsync(model,
                    () => model.ErrorText.StartsWith("Item settings failed:", StringComparison.Ordinal), () =>
                    {
                        DesktopInteraction.Click(DesktopInteraction.Control<Button>(settings, "OkButton"));
                        return Task.CompletedTask;
                    }).ConfigureAwait(true);
                Assert.That(model.ErrorText, Does.Contain("sampling rejected"));
                Assert.That(model.Items.Single(), Is.SameAs(original));
                Assert.That(adapter.Items.Single().QueueSize, Is.EqualTo(1));
                adapter.Mock.Verify(a => a.ConfigureItemAsync(
                    It.Is<MonitoredItemConfig>(item => item.Id == 4 && item.QueueSize == 19),
                    It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                foreach (Window child in DesktopInteraction.Owner.OwnedWindows.ToArray())
                {
                    child.Close();
                }
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task RecorderConsumersHaveIndependentCursorsAcrossViewChanges()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var adapter = new ControlledSubscriptionAdapter();
            await using var model = new SubscriptionViewModel("Retained", adapter.Object, NullLogger.Instance);
            MonitoredItemConfig item = ScottPlotPumpTests.Item(4, "Temperature");
            model.Items.Add(item);
            adapter.Items.Add(item);
            using var view = new SubscriptionDocumentView(model);
            DesktopInteraction.Owner.Content = view;
            try
            {
                ChannelReader<NotificationEvent> export = model.Recorder.CreateReader();
                ChannelReader<NotificationEvent> other = model.Recorder.CreateReader();
                DateTime time = DateTime.UtcNow.AddDays(1);
                NotificationEvent first = ScottPlotPumpTests.Event(4, 11, -12) with { ReceivedAtUtc = time };
                NotificationEvent second = ScottPlotPumpTests.Event(4, 12, 28) with { ReceivedAtUtc = time };
                adapter.Input.Writer.TryWrite(first);
                adapter.Input.Writer.TryWrite(second);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Assert.That(await export.ReadAsync(timeout.Token).ConfigureAwait(true), Is.EqualTo(first));
                Assert.That(await export.ReadAsync(timeout.Token).ConfigureAwait(true), Is.EqualTo(second));
                ComboBox mode = DesktopInteraction.Control<ComboBox>(view, "DocumentViewMode");
                mode.SelectedIndex = 2;
                AnimationCanvas animation = DesktopInteraction.Control<AnimationCanvas>(view, "DocumentAnimation");
                animation.Drain();
                Assert.That(animation.DotSnapshot.Select(dot => dot.SequenceNumber), Is.EqualTo(new uint[] { 11, 12 }));
                mode.SelectedIndex = 3;
                animation.Drain();
                Assert.That(animation.LineRangeFor(4), Is.EqualTo((-12.0, 28.0)));
                Assert.That(await other.ReadAsync(timeout.Token).ConfigureAwait(true), Is.EqualTo(first));
                Assert.That(await other.ReadAsync(timeout.Token).ConfigureAwait(true), Is.EqualTo(second));
                mode.SelectedIndex = 0;
                Assert.That(model.Recorder.Snapshot().ToArray(), Is.EqualTo(new[] { first, second }));
                Assert.That(adapter.DisposeCount, Is.Zero);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
                view.Dispose();
                await model.DisposeAsync().ConfigureAwait(true);
            }
            Assert.That(adapter.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public Task ScottPlotViewDrainsNotificationsIntoExactSeriesAndRecorder()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            Channel<NotificationEvent> input = Channel.CreateUnbounded<NotificationEvent>();
            var observed = new ObservingReader(input.Reader, 4);
            var recorder = new NotificationRecorder();
            using var view = new ScottPlotView();
            view.Bind(observed, [ScottPlotPumpTests.Item(4, "Temperature"), ScottPlotPumpTests.Item(9, "Pressure")],
                AnimationMode.Signal, recorder: recorder);
            NotificationEvent[] events =
            [
                ScottPlotPumpTests.Event(4, 1, -12),
                ScottPlotPumpTests.Event(9, 2, 9.5),
                ScottPlotPumpTests.Event(4, 3, 31),
                ScottPlotPumpTests.Event(4, 4, 99) with { Kind = NotificationKind.Event }
            ];
            foreach (NotificationEvent notification in events)
            {
                input.Writer.TryWrite(notification);
            }
            DesktopInteraction.Owner.Content = view;
            try
            {
                await observed.Consumed.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
                Plot plot = DesktopInteraction.Control<AvaPlot>(view, "Plot").Plot;
                DataStreamer temperature = plot.GetPlottables<DataStreamer>()
                    .Single(p => p.LegendText == "Temperature");
                DataStreamer pressure = plot.GetPlottables<DataStreamer>().Single(p => p.LegendText == "Pressure");
                Assert.That(temperature.Data.CountTotal, Is.EqualTo(2));
                Assert.That(temperature.Data.Data.Take(2), Is.EqualTo(new double[] { -12, 31 }));
                Assert.That(pressure.Data.CountTotal, Is.EqualTo(1));
                Assert.That(pressure.Data.NewestPoint, Is.EqualTo(9.5));
                Assert.That(recorder.Snapshot().ToArray(), Is.EqualTo(events));
                view.OnItemsChanged([ScottPlotPumpTests.Item(9, "Pressure")]);
                Assert.That(plot.GetPlottables<DataStreamer>().Single(), Is.SameAs(pressure));
                view.Dispose();
                Assert.That(plot.GetPlottables(), Is.Empty);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task RebindRestoresProvidedLimitsVisibilityAndReleasesColorBars()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            using var view = new ScottPlotView();
            var items = new[] { ScottPlotPumpTests.Item(4, "Temperature"), ScottPlotPumpTests.Item(9, "Pressure") };
            Plot plot = DesktopInteraction.Control<AvaPlot>(view, "Plot").Plot;
            view.SetChartElementsVisible(false, true, false);
            view.Bind(null, items, AnimationMode.Heatmap, new AxisLimits(10, 90, 2, 8));
            IPlottable heatmap = plot.GetPlottables().Single();
            Assert.That(plot.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>().Count(), Is.EqualTo(1));
            view.ApplyXZoom(0.5);
            AxisLimits heatmapLimits = Limits(view);
            Assert.That(heatmapLimits.Left, Is.EqualTo(30));
            Assert.That(heatmapLimits.Right, Is.EqualTo(70));
            view.Bind(null, items, AnimationMode.Histogram, new AxisLimits(0, 40, 0, 12));
            Assert.That(plot.GetPlottables(), Does.Not.Contain(heatmap));
            Assert.That(plot.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>(), Is.Empty);
            view.ResetXZoom();
            Assert.That(Limits(view).Left, Is.Zero);
            Assert.That(Limits(view).Right, Is.EqualTo(200));
            view.Bind(null, items, AnimationMode.Heatmap, heatmapLimits);
            Assert.That(Limits(view).Left, Is.EqualTo(30));
            Assert.That(Limits(view).Right, Is.EqualTo(70));
            Assert.That(plot.Legend.IsVisible, Is.False);
            Assert.That(plot.Axes.Bottom.IsVisible, Is.True);
            Assert.That(plot.Axes.Left.IsVisible, Is.False);
            view.Bind(null, null, AnimationMode.Dots);
            Assert.That(plot.GetPlottables(), Is.Empty);
            Assert.That(plot.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>(), Is.Empty);
            view.Dispose();
            view.Dispose();
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task RebindAndDetachReleaseTheOldReaderAfterThe1024EventBudget()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            Channel<NotificationEvent> first = Channel.CreateUnbounded<NotificationEvent>();
            Channel<NotificationEvent> second = Channel.CreateUnbounded<NotificationEvent>();
            var firstReader = new ObservingReader(first.Reader, 1024);
            var secondReader = new ObservingReader(second.Reader, 2);
            var recorder = new NotificationRecorder();
            for (int i = 1; i <= 1025; i++)
            {
                first.Writer.TryWrite(ScottPlotPumpTests.Event(4, (uint)i, i));
            }
            second.Writer.TryWrite(ScottPlotPumpTests.Event(4, 2001, -4));
            second.Writer.TryWrite(ScottPlotPumpTests.Event(4, 2002, 9));
            using var view = new ScottPlotView();
            var items = new[] { ScottPlotPumpTests.Item(4, "Temperature") };
            view.Bind(firstReader, items, AnimationMode.Signal, recorder: recorder);
            DesktopInteraction.Owner.Content = view;
            try
            {
                await firstReader.Consumed.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
                Plot plot = DesktopInteraction.Control<AvaPlot>(view, "Plot").Plot;
                DataStreamer original = plot.GetPlottables<DataStreamer>().Single();
                Assert.That(original.Data.CountTotal, Is.EqualTo(1024));
                Assert.That(original.Data.NewestPoint, Is.EqualTo(1024));
                Assert.That(first.Reader.Count, Is.EqualTo(1));
                Assert.That(recorder.Snapshot()[1023].SequenceNumber, Is.EqualTo(1024));
                view.Bind(secondReader, items, AnimationMode.Signal);
                DesktopInteraction.Owner.Content = view;
                await secondReader.Consumed.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
                DataStreamer replacement = plot.GetPlottables<DataStreamer>().Single();
                Assert.That(replacement, Is.Not.SameAs(original));
                Assert.That(replacement.Data.CountTotal, Is.EqualTo(2));
                Assert.That(replacement.Data.Data.Take(2), Is.EqualTo(new double[] { -4, 9 }));
                Assert.That(first.Reader.TryRead(out NotificationEvent remaining), Is.True);
                Assert.That(remaining.SequenceNumber, Is.EqualTo(1025));
                Assert.That(recorder.TotalWritten, Is.EqualTo(1024));
                view.Dispose();
                Assert.That(plot.GetPlottables(), Is.Empty);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    private static AxisLimits Limits(ScottPlotView view)
    {
        return view.CurrentLimits ?? throw new InvalidOperationException("The plot has no axes.");
    }

    private sealed class ObservingReader : ChannelReader<NotificationEvent>
    {
        public ObservingReader(ChannelReader<NotificationEvent> source, int expected)
        {
            m_source = source;
            m_expected = expected;
        }

        public Task Consumed => m_consumed.Task;

        public override bool TryRead(out NotificationEvent item)
        {
            if (!m_source.TryRead(out item))
            {
                return false;
            }
            if (++m_count == m_expected)
            {
                m_consumed.TrySetResult();
            }
            return true;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            return m_source.WaitToReadAsync(cancellationToken);
        }

        private readonly ChannelReader<NotificationEvent> m_source;
        private readonly int m_expected;
        private readonly TaskCompletionSource m_consumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_count;
    }

    private static readonly int[] s_publishingAndItemControlsSendExactRequestsAndPreserveNeighborExpected =
    [
        101,
    ];
}
