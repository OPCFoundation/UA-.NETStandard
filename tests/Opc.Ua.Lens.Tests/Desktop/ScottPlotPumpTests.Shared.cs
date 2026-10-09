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

public sealed partial class ScottPlotPumpTests
{

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
