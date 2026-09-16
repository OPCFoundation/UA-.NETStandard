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
using NUnit.Framework;
using Opc.Ua;
using ScottPlot;
using ScottPlot.Plottables;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
public sealed class ScottPlotPumpTests
{
    [Test]
    public void StylingAppliesExpectedPaletteAxesAndLegend()
    {
        var plot = new Plot();
        plot.Axes.SetLimits(-4, 12, -8, 24);

        ScottPlotStyling.Apply(plot, "Arrival distribution", "Milliseconds", "Samples");

        Assert.That(plot.FigureBackground.Color, Is.EqualTo(Color.FromHex("#0F172A")));
        Assert.That(plot.DataBackground.Color, Is.EqualTo(Color.FromHex("#0B1220")));
        Assert.That(plot.Grid.MajorLineColor, Is.EqualTo(Color.FromHex("#1E293B")));
        Assert.That(plot.Axes.Title.Label.Text, Is.EqualTo("Arrival distribution"));
        Assert.That(plot.Axes.Bottom.Label.Text, Is.EqualTo("Milliseconds"));
        Assert.That(plot.Axes.Left.Label.Text, Is.EqualTo("Samples"));
        Assert.That(plot.Legend.BackgroundColor, Is.EqualTo(Color.FromHex("#1E293B")));
        Assert.That(plot.Legend.FontColor, Is.EqualTo(Color.FromHex("#E2E8F0")));
        Assert.That(plot.Legend.FontName, Is.EqualTo("Cascadia Mono"));
        AssertLimits(plot, -4, 12, -8, 24);
    }

    [Test]
    public void SignalAcceptsOnlyKnownNumericDataAndPreservesSurvivingItems()
    {
        var plot = new Plot();
        int refreshes = 0;
        using var pump = new SignalPump();
        pump.Bind(plot, () => refreshes++);
        MonitoredItemConfig temperature = Item(4, "Temperature");
        MonitoredItemConfig pressure = Item(9, "Pressure");
        pump.OnItemsChanged([temperature, pressure]);
        DataStreamer original = plot.GetPlottables<DataStreamer>().Single(p => p.LegendText == "Temperature");
        pump.OnEvent(Event(4, 1, -12.5));
        pump.OnEvent(Event(4, 2, 38.25));
        pump.OnEvent(Event(9, 3, 7));
        pump.OnEvent(Event(4, 4, null));
        pump.OnEvent(Event(81, 5, 999));
        pump.OnEvent(Event(4, 6, 999) with { Kind = NotificationKind.Event });
        pump.OnEvent(Event(4, 7, 999) with { Kind = NotificationKind.KeepAlive });
        pump.OnItemsChanged([pressure, temperature]);
        pump.Refresh();

        Assert.That(plot.GetPlottables<DataStreamer>().Single(p => p.LegendText == "Temperature"),
            Is.SameAs(original));
        Assert.That(original.Data.CountTotal, Is.EqualTo(2));
        Assert.That(original.Data.Data.Take(2), Is.EqualTo(new[] { -12.5, 38.25 }));
        Assert.That(original.Data.NewestPoint, Is.EqualTo(38.25));
        Assert.That(original.Color, Is.EqualTo(ItemColors.ScottPlotForItemId(4)));
        Assert.That(refreshes, Is.EqualTo(2));
        pump.OnItemsChanged([temperature]);
        pump.OnEvent(Event(9, 8, 300));
        Assert.That(pump.StreamerCount, Is.EqualTo(1));
        Assert.That(plot.GetPlottables<DataStreamer>(), Is.EqualTo(new[] { original }));
        pump.OnItemsChanged([]);
        Assert.That(plot.GetPlottables(), Is.Empty);
    }

    [TestCase(-0.1, -1)]
    [TestCase(0, 0)]
    [TestCase(9.9999, 0)]
    [TestCase(10, 1)]
    [TestCase(10.0001, 1)]
    [TestCase(199, 19)]
    [TestCase(199.9999, 19)]
    [TestCase(200, -1)]
    public void HistogramPlacesBoundaryIntervalsInExactBins(double interval, int expectedBin)
    {
        var plot = new Plot();
        using var pump = new HistogramPump();
        pump.Bind(plot, () => { });
        pump.OnItemsChanged([Item(4, "Temperature"), Item(9, "Pressure")]);
        DateTime start = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        pump.OnEvent(Event(4, 1, null) with { ReceivedAtUtc = start });
        pump.OnEvent(Event(4, 2, null) with { ReceivedAtUtc = start.AddMilliseconds(interval) });
        pump.OnEvent(Event(9, 3, null) with { ReceivedAtUtc = start });
        pump.OnEvent(Event(9, 4, null) with { ReceivedAtUtc = start.AddMilliseconds(35) });
        pump.OnEvent(Event(4, 5, null) with { Kind = NotificationKind.Event });
        pump.OnEvent(Event(99, 6, null));
        pump.Refresh();

        BarPlot temperature = plot.GetPlottables<BarPlot>().Single(p => p.LegendText == "Temperature");
        BarPlot pressure = plot.GetPlottables<BarPlot>().Single(p => p.LegendText == "Pressure");
        double[] expected = new double[20];
        if (expectedBin >= 0)
        {
            expected[expectedBin] = 1;
        }
        Assert.That(temperature.Bars.Select(bar => bar.Value), Is.EqualTo(expected));
        Assert.That(pressure.Bars.Select(bar => bar.Value),
            Is.EqualTo(new double[] { 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(pump.SampleCountFor(4), Is.EqualTo(expectedBin < 0 ? 0 : 1));
        Assert.That(pump.SampleCountFor(9), Is.EqualTo(1));
        Assert.That(temperature.Bars[0].Position, Is.EqualTo(5 - 5.0 / 3).Within(0.000001));
        Assert.That(temperature.Bars[0].Size, Is.EqualTo(10.0 / 3).Within(0.000001));
    }

    [TestCase(4095, 4095)]
    [TestCase(4096, 4096)]
    [TestCase(4097, 2049)]
    public void HistogramDecaysAtSampleCapacityWithoutReplacingBars(int samples, int retained)
    {
        var plot = new Plot();
        int refreshes = 0;
        using var pump = new HistogramPump();
        pump.Bind(plot, () => refreshes++);
        pump.OnItemsChanged([Item(4, "Temperature")]);
        DateTime time = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        pump.OnEvent(Event(4, 0, null) with { ReceivedAtUtc = time });
        for (int i = 0; i < samples; i++)
        {
            time = time.AddMilliseconds(i < 2048 ? 5 : 15);
            pump.OnEvent(Event(4, (uint)(i + 1), null) with { ReceivedAtUtc = time });
        }
        pump.Refresh();
        BarPlot bars = plot.GetPlottables<BarPlot>().Single();
        Assert.That(pump.SampleCountFor(4), Is.EqualTo(retained));
        Assert.That(bars.Bars.Take(2).Select(bar => bar.Value),
            Is.EqualTo(samples > 4096 ? new double[] { 1024, 1025 } : new double[] { 2048, samples - 2048 }));
        Assert.That(bars.Bars.Skip(2).Select(bar => bar.Value), Is.All.EqualTo(0d));
        plot.Axes.SetLimits(20, 60, 3, 80);
        pump.Refresh();
        Assert.That(refreshes, Is.EqualTo(2));
        Assert.That(plot.GetPlottables<BarPlot>().Single(), Is.SameAs(bars));
        AssertLimits(plot, 20, 60, 3, 80);
        pump.OnItemsChanged([Item(4, "Renamed")]);
        pump.Refresh();
        Assert.That(plot.GetPlottables<BarPlot>().Single().LegendText, Is.EqualTo("Renamed"));
        Assert.That(plot.GetPlottables<BarPlot>().Single().Bars.Sum(bar => bar.Value), Is.EqualTo(retained));
        pump.OnItemsChanged([]);
        Assert.That(plot.GetPlottables(), Is.Empty);
        Assert.That(pump.SampleCountFor(4), Is.Zero);
    }

    [Test]
    public void HeatmapReordersRowsByIdentityAndPreservesCounts()
    {
        var plot = new Plot();
        using var pump = new HeatmapPump();
        pump.Bind(plot, () => { });
        pump.OnItemsChanged([Item(4, "Temperature"), Item(9, "Pressure")]);
        pump.OnEvent(Event(4, 1, null));
        pump.OnEvent(Event(9, 2, null));
        pump.OnEvent(Event(9, 3, null));
        pump.OnEvent(Event(4, 4, null) with { Kind = NotificationKind.Event });
        pump.OnEvent(Event(99, 5, null));
        pump.Refresh();
        Heatmap first = plot.GetPlottables<Heatmap>().Single();
        Assert.That(RowTotals(first), Is.EqualTo(new double[] { 1, 2 }));
        Assert.That(first.Intensities.GetLength(1), Is.EqualTo(200));
        pump.OnItemsChanged([Item(9, "Pressure"), Item(4, "Temperature"), Item(12, "Flow")]);
        pump.Refresh();
        Assert.That(RowTotals(plot.GetPlottables<Heatmap>().Single()), Is.EqualTo(new double[] { 2, 1, 0 }));
        Assert.That(plot.GetPlottables(), Does.Not.Contain(first));
        Assert.That(plot.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>().Count(), Is.EqualTo(1));
        pump.OnItemsChanged([Item(4, "Temperature")]);
        pump.Refresh();
        Assert.That(RowTotals(plot.GetPlottables<Heatmap>().Single()), Is.EqualTo(new double[] { 1 }));
        Assert.That(pump.RowCount, Is.EqualTo(1));
        pump.OnItemsChanged([]);
        pump.Refresh();
        Assert.That(plot.GetPlottables(), Is.Empty);
        Assert.That(plot.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>(), Is.Empty);
    }

    [TestCase("Signal")]
    [TestCase("Histogram")]
    [TestCase("Heatmap")]
    public void PumpsRespectManualFramingAndCenteredZoom(string kind)
    {
        using IScottPlotPump pump = Create(kind);
        pump.OnItemsChanged([Item(4, "Temperature")]);
        pump.OnEvent(Event(4, 1, 12));
        pump.Refresh();
        pump.ApplyXZoom(0.5);
        pump.ResetXZoom();
        var plot = new Plot();
        int refreshes = 0;
        pump.Bind(plot, () => refreshes++);
        pump.OnItemsChanged([Item(4, "Temperature")]);
        pump.OnEvent(Event(4, 2, 25));
        pump.Refresh();
        plot.Axes.SetLimits(10, 90, -10, 50);
        pump.ApplyXZoom(0.5);
        AssertLimits(plot, 30, 70, -10, 50);
        pump.Refresh();
        AssertLimits(plot, 30, 70, -10, 50);
        foreach (double invalid in new[] { 0, -1, double.NaN })
        {
            pump.ApplyXZoom(invalid);
            AssertLimits(plot, 30, 70, -10, 50);
        }
        if (kind == "Signal")
        {
            Assert.That(plot.GetPlottables<DataStreamer>().Single().ManageAxisLimits, Is.False);
        }
        int beforeReset = refreshes;
        pump.ResetXZoom();
        Assert.That(refreshes, Is.EqualTo(beforeReset + 1));
        if (kind == "Signal")
        {
            Assert.That(plot.GetPlottables<DataStreamer>().Single().ManageAxisLimits, Is.True);
        }
        if (kind == "Histogram")
        {
            Assert.That(plot.Axes.GetLimits().Left, Is.Zero);
            Assert.That(plot.Axes.GetLimits().Right, Is.EqualTo(200));
        }
    }

    [TestCase("Signal")]
    [TestCase("Histogram")]
    [TestCase("Heatmap")]
    public void RebindAndDisposeRemoveOwnedPlotsAndColorBar(string kind)
    {
        var plot = new Plot();
        using IScottPlotPump pump = Create(kind);
        int oldRefreshes = 0;
        int newRefreshes = 0;
        pump.Bind(plot, () => oldRefreshes++);
        pump.OnItemsChanged([Item(4, "Temperature")]);
        pump.OnEvent(Event(4, 1, 12));
        pump.Refresh();
        IPlottable owned = plot.GetPlottables().Single();
        pump.Bind(plot, () => newRefreshes++);
        Assert.That(plot.GetPlottables(), Does.Not.Contain(owned));
        int oldCount = oldRefreshes;
        pump.OnItemsChanged([Item(9, "Pressure")]);
        pump.OnEvent(Event(9, 2, 36));
        pump.Refresh();
        Assert.That(oldRefreshes, Is.EqualTo(oldCount));
        Assert.That(newRefreshes, Is.EqualTo(2));
        pump.Dispose();
        pump.Dispose();
        Assert.That(plot.GetPlottables(), Is.Empty);
        Assert.That(plot.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>(), Is.Empty);
    }

    private static IScottPlotPump Create(string kind)
    {
        return kind switch
        {
            "Signal" => new SignalPump(),
            "Histogram" => new HistogramPump(),
            "Heatmap" => new HeatmapPump(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    internal static MonitoredItemConfig Item(int id, string name)
    {
        return new MonitoredItemConfig { Id = id, DisplayName = name, NodeId = new NodeId((uint)id, 2) };
    }

    internal static NotificationEvent Event(int id, uint sequence, double? value)
    {
        return new NotificationEvent(NotificationKind.DataChange, id, 1, sequence, DateTime.MinValue, value);
    }

    private static double[] RowTotals(Heatmap heatmap)
    {
        double[,] values = heatmap.Intensities;
        var totals = new double[values.GetLength(0)];
        for (int row = 0; row < totals.Length; row++)
        {
            for (int column = 0; column < values.GetLength(1); column++)
            {
                totals[row] += values[row, column];
            }
        }
        return totals;
    }

    private static void AssertLimits(Plot plot, double left, double right, double bottom, double top)
    {
        AxisLimits actual = plot.Axes.GetLimits();
        Assert.That(actual.Left, Is.EqualTo(left));
        Assert.That(actual.Right, Is.EqualTo(right));
        Assert.That(actual.Bottom, Is.EqualTo(bottom));
        Assert.That(actual.Top, Is.EqualTo(top));
    }
}
