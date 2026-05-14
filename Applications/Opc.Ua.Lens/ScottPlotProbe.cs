/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using ScottPlot;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens;

/// <summary>
/// Headless validator for the three ScottPlot pumps (Signal / Histogram /
/// Heatmap).  Constructs a bare <see cref="Plot"/>, binds each pump to it,
/// feeds 1000 synthetic <see cref="NotificationEvent"/>s for two items,
/// and asserts that:
/// <list type="bullet">
/// <item>no exceptions are thrown,</item>
/// <item>each pump tracks the expected number of items,</item>
/// <item>the Histogram pump accumulates samples,</item>
/// <item>each pump's <see cref="IScottPlotPump.Refresh"/> completes without throwing.</item>
/// </list>
/// </summary>
internal static class ScottPlotProbe
{
    public static int Run()
    {
        Console.WriteLine("== ScottPlot probe ==");
        int rc = 0;

        var items = new List<MonitoredItemConfig>
        {
            new() { Id = 1, DisplayName = "item-1", NodeId = Opc.Ua.NodeId.Parse("ns=2;s=Sim_A") },
            new() { Id = 2, DisplayName = "item-2", NodeId = Opc.Ua.NodeId.Parse("ns=2;s=Sim_B") }
        };

        // Synthetic events: 500 per item, 10 ms apart, alternating values.
        var events = new List<NotificationEvent>(1000);
        DateTime t0 = DateTime.UtcNow;
        for (int i = 0; i < 500; i++)
        {
            DateTime ts = t0.AddMilliseconds(i * 10);
            events.Add(new NotificationEvent(NotificationKind.DataChange, 1, 1, (uint)(i + 1), ts, Value: Math.Sin(i * 0.1)));
            events.Add(new NotificationEvent(NotificationKind.DataChange, 2, 1, (uint)(i + 1), ts, Value: Math.Cos(i * 0.1)));
        }

        rc |= ExerciseSignalPump(items, events);
        rc |= ExerciseHistogramPump(items, events);
        rc |= ExerciseHeatmapPump(items, events);

        Console.WriteLine(rc == 0 ? "SCOTTPLOT PROBE PASS" : "SCOTTPLOT PROBE FAIL");
        return rc;
    }

    private static int ExerciseSignalPump(List<MonitoredItemConfig> items, IReadOnlyList<NotificationEvent> events)
    {
#pragma warning disable CA2000 // Plot is a transient ScottPlot fixture for the probe; no managed resources to leak.
        var plot = new Plot();
#pragma warning restore CA2000
        int refreshes = 0;
        var pump = new SignalPump();
        try
        {
            pump.Bind(plot, () => refreshes++);
            pump.OnItemsChanged(items);
            if (pump.StreamerCount != items.Count)
            {
                return Fail($"SignalPump.StreamerCount expected {items.Count}, got {pump.StreamerCount}");
            }
            foreach (NotificationEvent ev in events)
            {
                pump.OnEvent(ev);
            }
            pump.Refresh();
            if (refreshes < 2)
            {
                return Fail($"SignalPump expected ≥2 refreshes, got {refreshes}");
            }
            Console.WriteLine($"  signal: {pump.StreamerCount} streamers, {refreshes} refresh calls — PASS");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("SignalPump threw: " + ex.Message);
        }
        finally
        {
            pump.Dispose();
        }
    }

    private static int ExerciseHistogramPump(List<MonitoredItemConfig> items, IReadOnlyList<NotificationEvent> events)
    {
#pragma warning disable CA2000 // Plot is a transient ScottPlot fixture for the probe; no managed resources to leak.
        var plot = new Plot();
#pragma warning restore CA2000
        int refreshes = 0;
        var pump = new HistogramPump();
        try
        {
            pump.Bind(plot, () => refreshes++);
            pump.OnItemsChanged(items);
            if (pump.ItemCount != items.Count)
            {
                return Fail($"HistogramPump.ItemCount expected {items.Count}, got {pump.ItemCount}");
            }
            foreach (NotificationEvent ev in events)
            {
                pump.OnEvent(ev);
            }
            pump.Refresh();
            int s1 = pump.SampleCountFor(1);
            int s2 = pump.SampleCountFor(2);
            if (s1 < 100 || s2 < 100)
            {
                return Fail($"HistogramPump samples too low: item1={s1}, item2={s2}");
            }
            Console.WriteLine($"  histogram: {pump.ItemCount} items, item1={s1} samples, item2={s2} samples — PASS");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("HistogramPump threw: " + ex.Message);
        }
        finally
        {
            pump.Dispose();
        }
    }

    private static int ExerciseHeatmapPump(List<MonitoredItemConfig> items, IReadOnlyList<NotificationEvent> events)
    {
#pragma warning disable CA2000 // Plot is a transient ScottPlot fixture for the probe; no managed resources to leak.
        var plot = new Plot();
#pragma warning restore CA2000
        int refreshes = 0;
        var pump = new HeatmapPump();
        try
        {
            pump.Bind(plot, () => refreshes++);
            pump.OnItemsChanged(items);
            if (pump.RowCount != items.Count)
            {
                return Fail($"HeatmapPump.RowCount expected {items.Count}, got {pump.RowCount}");
            }
            foreach (NotificationEvent ev in events)
            {
                pump.OnEvent(ev);
            }
            pump.Refresh();
            if (refreshes < 2)
            {
                return Fail($"HeatmapPump expected ≥2 refreshes, got {refreshes}");
            }
            Console.WriteLine($"  heatmap: {pump.RowCount} rows, {refreshes} refresh calls — PASS");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("HeatmapPump threw: " + ex.Message);
        }
        finally
        {
            pump.Dispose();
        }
    }

    private static int Fail(string msg)
    {
        Console.WriteLine("FAIL: " + msg);
        return 1;
    }
}
