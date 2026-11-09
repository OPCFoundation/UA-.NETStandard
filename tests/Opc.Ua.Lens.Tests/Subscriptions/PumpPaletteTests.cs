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

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class PumpPaletteTests
{
    [Test]
    public void SignalThemeCycleRecolorsExistingSeriesAndLegendWithoutChangingSamplesOrZoom()
    {
        using var plot = new Plot();
        using var pump = new SignalPump();
        pump.Bind(plot, static () => { });
        pump.OnItemsChanged([Item()]);
        pump.OnEvent(Notification(1));
        pump.OnEvent(Notification(2));
        pump.OnEvent(Notification(3));
        DataStreamer streamer = plot.GetPlottables().OfType<DataStreamer>().Single();
        double[] samples = [.. streamer.Data.Data];
        plot.Axes.SetLimits(0, 20, -1, 5);
        pump.ApplyXZoom(0.5);
        AxisLimits limits = plot.Axes.GetLimits();

        foreach (bool isLight in s_paletteCycle)
        {
            pump.ApplyPalette(isLight);

            Color expected = ItemColors.ScottPlotForItemId(ItemId, isLight);
            Assert.That(plot.GetPlottables().Single(), Is.SameAs(streamer));
            Assert.That(streamer.Color, Is.EqualTo(expected));
            Assert.That(streamer.LineWidth, Is.EqualTo(2));
            Assert.That(streamer.LegendItems.Single().LineColor, Is.EqualTo(expected));
            Assert.That(streamer.LegendItems.Single().LabelText, Is.EqualTo("Stable item"));
            Assert.That(streamer.Data.Data, Is.EqualTo(samples));
            Assert.That(streamer.CountTotal, Is.EqualTo(3));
            Assert.That(streamer.ManageAxisLimits, Is.False);
            Assert.That(plot.Axes.GetLimits(), Is.EqualTo(limits));
        }
    }

    [Test]
    public void HistogramThemeCycleRecolorsExistingBarsAndLegendWithoutChangingSamplesOrZoom()
    {
        using var plot = new Plot();
        using var pump = new HistogramPump();
        pump.Bind(plot, static () => { });
        pump.OnItemsChanged([Item()]);
        pump.OnEvent(Notification(1));
        pump.OnEvent(Notification(2));
        pump.OnEvent(Notification(3));
        pump.Refresh();
        BarPlot histogram = plot.GetPlottables().OfType<BarPlot>().Single();
        Bar[] bars = [.. histogram.Bars];
        double[] samples = bars.Select(bar => bar.Value).ToArray();
        plot.Axes.SetLimits(10, 90, -1, 5);
        AxisLimits limits = plot.Axes.GetLimits();

        foreach (bool isLight in s_paletteCycle)
        {
            pump.ApplyPalette(isLight);

            Color expected = ItemColors.ScottPlotForItemId(ItemId, isLight);
            Assert.That(plot.GetPlottables().Single(), Is.SameAs(histogram));
            Assert.That(histogram.Bars, Is.EqualTo(bars));
            Assert.That(histogram.Bars.Select(bar => bar.FillColor), Is.All.EqualTo(expected));
            Assert.That(histogram.LegendItems.Single().FillColor, Is.EqualTo(expected));
            Assert.That(histogram.LegendItems.Single().LabelText, Is.EqualTo("Stable item"));
            Assert.That(histogram.Bars.Select(bar => bar.Value), Is.EqualTo(samples));
            Assert.That(pump.SampleCountFor(ItemId), Is.EqualTo(2));
            Assert.That(plot.Axes.GetLimits(), Is.EqualTo(limits));
        }
    }

    private static MonitoredItemConfig Item()
    {
        return new MonitoredItemConfig
        {
            Id = ItemId,
            NodeId = new NodeId("stable", 0),
            DisplayName = "Stable item"
        };
    }

    private static NotificationEvent Notification(uint sequence)
    {
        return new NotificationEvent(NotificationKind.DataChange, ItemId, 1, sequence,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(sequence * 25), sequence);
    }

    private const int ItemId = 17;
    private static readonly bool[] s_paletteCycle = [false, true, false];
}
