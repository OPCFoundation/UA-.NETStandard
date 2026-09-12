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

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using SkiaSharp;
using UaLens.Plugins.Performance;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class LiveChartViewWorkflowTests
{
    [Test]
    public Task PerformanceHistogramConfiguresFiniteOverflowSlotAndDistinctPercentileMarkers()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var view = new PerformanceView();
            Plot throughput = DesktopInteraction.Control<AvaPlot>(view, "ThroughputPlot").Plot;
            Plot histogram = DesktopInteraction.Control<AvaPlot>(view, "HistogramPlot").Plot;

            Assert.That(throughput.Axes.Title.Label.Text, Is.EqualTo("Throughput (ops/sec)"));
            Assert.That(throughput.Axes.Bottom.Label.Text, Is.EqualTo("seconds since run start"));
            Assert.That(throughput.Axes.Left.Label.Text, Is.EqualTo("ops/sec"));
            Assert.That(throughput.GetPlottables<DataLogger>().Single().LineWidth, Is.EqualTo(1.5));
            Bar[] bars = histogram.GetPlottables<BarPlot>().Single().Bars.ToArray();
            Assert.That(bars, Has.Length.EqualTo(71));
            Assert.That(bars.Select(bar => bar.Value), Is.All.Zero);
            Assert.That(bars.Select(bar => bar.Size), Is.All.EqualTo(0.09).Within(0.0000001));
            Assert.That(bars[0].Position, Is.InRange(-3, -2.9));
            Assert.That(bars[^1].Position, Is.InRange(4, 4.1));
            Assert.That(bars[^1].Position - bars[0].Position, Is.EqualTo(7).Within(0.0000001));
            VerticalLine[] markers = histogram.GetPlottables<VerticalLine>().ToArray();
            Assert.That(
                markers.Select(marker => marker.LabelText),
                Is.EqualTo(s_performanceHistogramConfiguresFiniteOverflowSlotAndDistinctPeExpected));
            Assert.That(markers.Select(marker => marker.LinePattern), Is.All.EqualTo(LinePattern.Dashed));
            Assert.That(markers.Select(marker => marker.LabelOppositeAxis), Is.All.True);
            AxisLimits limits = histogram.Axes.GetLimits();
            Assert.That(limits.Left, Is.EqualTo(-3).Within(0.000001));
            Assert.That(limits.Right, Is.EqualTo(4.1).Within(0.000001));
            histogram.Axes.SetLimitsX(-3.1, 4.1);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(900, 400));
            histogram.Render(surface.Canvas, 900, 400);
            Assert.That(histogram.Axes.Bottom.TickGenerator.Ticks.Select(tick => tick.Label),
                Is.EqualTo(s_performanceHistogramConfiguresFiniteOverflowSlotAndDistinctPeExpected2));
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task BenchChartPlacesOnlyCpuAndMemoryOnResourceAxisAndKeepsAllThroughputWindows()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var view = new SubscriptionBenchView();
            Plot plot = DesktopInteraction.Control<AvaPlot>(view, "Chart").Plot;
            DataLogger[] series = plot.GetPlottables<DataLogger>().ToArray();

            Assert.That(series.Select(logger => logger.LegendText),
                Is.EqualTo(s_benchChartPlacesOnlyCpuAndMemoryOnResourceAxisAndKeepsAllThroExpected));
            Assert.That(series.Take(4).Select(logger => logger.Axes.YAxis), Is.All.SameAs(plot.Axes.Left));
            Assert.That(series[4].Axes.YAxis, Is.SameAs(series[5].Axes.YAxis));
            Assert.That(series[4].Axes.YAxis, Is.Not.SameAs(plot.Axes.Left));
            Assert.That(series[4].Axes.YAxis.Label.Text, Is.EqualTo("CPU % / Mem MB"));
            Assert.That(plot.Axes.Bottom.Label.Text, Is.EqualTo("seconds"));
            Assert.That(plot.Axes.Left.Label.Text, Is.EqualTo("values/sec"));
            Assert.That(plot.Axes.Title.Label.Text, Is.EqualTo("Subscription throughput"));
            Assert.That(plot.Legend.IsVisible, Is.True);
            Assert.That(series.Take(4).Select(logger => logger.LineWidth), Is.All.EqualTo(1.5));
            Assert.That(series.Skip(4).Select(logger => logger.LineWidth), Is.All.EqualTo(1.2f));
            return Task.CompletedTask;
        });
    }

    private static readonly string[] s_performanceHistogramConfiguresFiniteOverflowSlotAndDistinctPeExpected =
    [
        "p50",
        "p95",
        "p99",
    ];
    private static readonly string[] s_performanceHistogramConfiguresFiniteOverflowSlotAndDistinctPeExpected2 =
    [
        "1µs",
        "10µs",
        "100µs",
        "1ms",
        "10ms",
        "100ms",
        "1s",
        "10s+",
    ];
    private static readonly string[] s_benchChartPlacesOnlyCpuAndMemoryOnResourceAxisAndKeepsAllThroExpected =
    [
        "1s rate",
        "10s rate",
        "30s rate",
        "60s rate",
        "CPU %",
        "Mem MB",
    ];
}
