/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
