/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using ScottPlot;
using ScottPlot.Plottables;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Centralised dark-theme styling for all three ScottPlot pumps.  Sets
/// background colors, axis frame, tick labels, title, grid, and legend
/// to match the rest of the UaLens UI; switches the font
/// to the same monospace stack used by the AnimationCanvas.
/// </summary>
internal static class ScottPlotStyling
{
    private static readonly ScottPlot.Color s_text = ScottPlot.Color.FromHex("#E2E8F0");
    private static readonly ScottPlot.Color s_dim = ScottPlot.Color.FromHex("#94A3B8");
    private static readonly ScottPlot.Color s_grid = ScottPlot.Color.FromHex("#1E293B");
    private static readonly ScottPlot.Color s_figBg = ScottPlot.Color.FromHex("#0F172A");
    private static readonly ScottPlot.Color s_dataBg = ScottPlot.Color.FromHex("#0B1220");
    private static readonly ScottPlot.Color s_legBg = ScottPlot.Color.FromHex("#1E293B");

    private const string FontName = "Cascadia Mono";

    public static void Apply(Plot plot, string title, string xLabel, string yLabel)
    {
        plot.FigureBackground.Color = s_figBg;
        plot.DataBackground.Color = s_dataBg;
        plot.Axes.Color(s_text);
        plot.Grid.MajorLineColor = s_grid;
        plot.Grid.MinorLineColor = s_grid;

        plot.Axes.Title.Label.Text = title;
        plot.Axes.Title.Label.ForeColor = s_text;
        plot.Axes.Title.Label.FontName = FontName;
        plot.Axes.Title.Label.FontSize = 14;

        plot.XLabel(xLabel);
        plot.YLabel(yLabel);
        plot.Axes.Bottom.Label.ForeColor = s_dim;
        plot.Axes.Bottom.Label.FontName = FontName;
        plot.Axes.Bottom.TickLabelStyle.ForeColor = s_dim;
        plot.Axes.Bottom.TickLabelStyle.FontName = FontName;
        plot.Axes.Left.Label.ForeColor = s_dim;
        plot.Axes.Left.Label.FontName = FontName;
        plot.Axes.Left.TickLabelStyle.ForeColor = s_dim;
        plot.Axes.Left.TickLabelStyle.FontName = FontName;

        plot.Legend.BackgroundColor = s_legBg;
        plot.Legend.FontColor = s_text;
        plot.Legend.FontName = FontName;
        plot.Legend.OutlineColor = s_dim;
    }

    /// <summary>
    /// Multiply the current X-axis range of <paramref name="plot"/> by
    /// <paramref name="factor"/> around the existing center.  Used by
    /// the +/- buttons (and Ctrl+/Ctrl-) when in a ScottPlot view mode.
    /// </summary>
    public static void ScalePlotXAxis(Plot plot, double factor)
    {
        AxisLimits limits = plot.Axes.GetLimits();
        double mid = (limits.Left + limits.Right) / 2.0;
        double half = (limits.Right - limits.Left) / 2.0 * factor;
        if (double.IsNaN(half) || half <= 0)
        {
            return;
        }
        plot.Axes.SetLimitsX(mid - half, mid + half);
    }
}

/// <summary>
/// Pluggable adapter that owns the ScottPlot plottables for one of the
/// new view modes (Signal / Histogram / Heatmap).Each pump is bound to
/// a single <see cref="Plot"/> + refresh callback so it works equally
/// well with an Avalonia <c>AvaPlot</c> control or a headless probe.
/// <see cref="OnEvent"/> is invoked on every <see cref="NotificationEvent"/>
/// drained from the subscription channel; <see cref="ScottPlotView"/>
/// drives <see cref="Refresh"/> from a 15 fps <c>DispatcherTimer</c>.
/// </summary>
internal interface IScottPlotPump : IDisposable
{
    /// <summary>Configure the plot for this mode and prepare per-item state.</summary>
    void Bind(Plot plot, Action refresh);

    /// <summary>Called whenever the active tab's monitored-item list changes.</summary>
    void OnItemsChanged(IReadOnlyList<MonitoredItemConfig> items);

    /// <summary>Called once per drained channel event.</summary>
    void OnEvent(in NotificationEvent ev);

    /// <summary>Push any pending mutations to the plot and invoke the refresh callback.</summary>
    void Refresh();

    /// <summary>
    /// Multiply the visible X-axis range by <paramref name="factor"/> around the current
    /// center.  factor &lt; 1 zooms in; factor &gt; 1 zooms out.
    /// </summary>
    void ApplyXZoom(double factor);

    /// <summary>Reset the X-axis to the SDK auto-fit so live data fills the view.</summary>
    void ResetXZoom();
}

/// <summary>
/// Signal-mode pump — one <see cref="DataStreamer"/> per monitored item.
/// Y values are read from <see cref="NotificationEvent.Value"/>; events /
/// KAs are ignored.
/// </summary>
internal sealed class SignalPump : IScottPlotPump
{
    private const int kCapacity = 1024;

    private Plot? m_plot;
    private Action? m_refresh;
    private readonly Dictionary<int, DataStreamer> m_streamers = new();

    /// <summary>Test hook — how many streamers are currently active.</summary>
    internal int StreamerCount => m_streamers.Count;

    public void Bind(Plot plot, Action refresh)
    {
        m_plot = plot;
        m_refresh = refresh;
        plot.Clear();
        m_streamers.Clear();
        ScottPlotStyling.Apply(plot,
            title: "Signal — value over time per item",
            xLabel: "Sample # (newest at right)",
            yLabel: "Value");
        plot.ShowLegend();
        // Use the full chart area on AutoScale — no axis margins.
        plot.Axes.Margins(0, 0);
        m_refresh();
    }

    public void OnItemsChanged(IReadOnlyList<MonitoredItemConfig> items)
    {
        if (m_plot is null)
        {
            return;
        }

        // Remove streamers for items no longer present.
        var present = new HashSet<int>();
        foreach (MonitoredItemConfig it in items)
        {
            present.Add(it.Id);
        }

        var stale = new List<int>();
        foreach (int id in m_streamers.Keys)
        {
            if (!present.Contains(id))
            {
                stale.Add(id);
            }
        }
        foreach (int id in stale)
        {
            m_plot.Remove(m_streamers[id]);
            m_streamers.Remove(id);
        }

        // Add streamers for new items.
        foreach (MonitoredItemConfig it in items)
        {
            if (m_streamers.ContainsKey(it.Id))
            {
                continue;
            }

            DataStreamer streamer = m_plot.Add.DataStreamer(kCapacity);
            streamer.LegendText = it.DisplayName ?? $"#{it.Id}";
            streamer.Color = ItemColors.ScottPlotForItemId(it.Id);
            streamer.ViewScrollLeft();
            m_streamers[it.Id] = streamer;
        }
    }

    public void OnEvent(in NotificationEvent ev)
    {
        if (ev.Kind != NotificationKind.DataChange)
        {
            return;
        }

        if (!ev.Value.HasValue)
        {
            return;
        }

        if (!m_streamers.TryGetValue(ev.ItemId, out DataStreamer? streamer))
        {
            return;
        }

        streamer.Add(ev.Value.Value);
    }

    public void Refresh()
    {
        // DataStreamer manages its own axes via ManageAxisLimits = true
        // (the default).  We deliberately do NOT call Plot.Axes.AutoScale()
        // here — that would override user-initiated pan/zoom every tick.
        m_refresh?.Invoke();
    }

    public void ApplyXZoom(double factor)
    {
        if (m_plot is null)
        {
            return;
        }
        // First time the user adjusts the X zoom, disable each DataStreamer's
        // built-in axis management so subsequent live data doesn't override
        // the user's framing.
        foreach (DataStreamer s in m_streamers.Values)
        {
            s.ManageAxisLimits = false;
        }
        ScottPlotStyling.ScalePlotXAxis(m_plot, factor);
        m_refresh?.Invoke();
    }

    public void ResetXZoom()
    {
        if (m_plot is null)
        {
            return;
        }

        foreach (DataStreamer s in m_streamers.Values)
        {
            s.ManageAxisLimits = true;
        }
        m_plot.Axes.AutoScale();
        m_refresh?.Invoke();
    }

    public void Dispose()
    {
        if (m_plot is null)
        {
            return;
        }

        foreach (DataStreamer s in m_streamers.Values)
        {
            m_plot.Remove(s);
        }
        m_streamers.Clear();
    }
}

/// <summary>
/// Histogram-mode pump — for each monitored item, computes the
/// distribution of inter-arrival times (milliseconds between consecutive
/// notifications).  Bins are 0..200 ms, 20 bins.  All items share a
/// single plot with one <see cref="ScottPlot.Plottables.BarPlot"/> per
/// item, colored from the item palette.
/// </summary>
internal sealed class HistogramPump : IScottPlotPump
{
    private const int kBinCount = 20;
    private const double kBinMaxMs = 200;
    private const double kBinSizeMs = kBinMaxMs / kBinCount;
    private const int kMaxSamples = 4096;

    private Plot? m_plot;
    private Action? m_refresh;
    private readonly Dictionary<int, double[]> m_bins = new();
    private readonly Dictionary<int, DateTime> m_lastAt = new();
    private readonly Dictionary<int, int> m_sampleCount = new();
    private readonly Dictionary<int, string> m_displayName = new();
    // Stable per-item BarPlot — created on items change, bars[] mutated in
    // place on each Refresh.  This is what lets the user zoom in / pan
    // without their axis-state being wiped each tick.
    private readonly Dictionary<int, BarPlot> m_barPlots = new();
    private readonly Dictionary<int, List<Bar>> m_barLists = new();
    private bool m_dirty;
    private bool m_initialFit;

    /// <summary>Test hook — number of items tracked.</summary>
    internal int ItemCount => m_bins.Count;

    /// <summary>Test hook — total sample count for a given item.</summary>
    internal int SampleCountFor(int itemId)
        => m_sampleCount.TryGetValue(itemId, out int n) ? n : 0;

    public void Bind(Plot plot, Action refresh)
    {
        m_plot = plot;
        m_refresh = refresh;
        plot.Clear();
        m_bins.Clear();
        m_lastAt.Clear();
        m_sampleCount.Clear();
        m_displayName.Clear();
        m_barPlots.Clear();
        m_barLists.Clear();
        m_initialFit = false;
        ScottPlotStyling.Apply(plot,
            title: "Histogram — inter-arrival times (ms)",
            xLabel: "ms since previous notification",
            yLabel: "Count");
        plot.ShowLegend();
        plot.Axes.SetLimitsX(0, kBinMaxMs);
        // Fill the entire chart on AutoScale — no margins (so bars are
        // grounded at y=0 and span the full vertical space).
        plot.Axes.Margins(0, 0);
        m_refresh();
    }

    public void OnItemsChanged(IReadOnlyList<MonitoredItemConfig> items)
    {
        if (m_plot is null)
        {
            return;
        }

        var present = new HashSet<int>();
        foreach (MonitoredItemConfig it in items)
        {
            present.Add(it.Id);
            m_bins.TryAdd(it.Id, new double[kBinCount]);
            m_sampleCount.TryAdd(it.Id, 0);
            m_displayName[it.Id] = it.DisplayName ?? $"#{it.Id}";
        }
        var stale = new List<int>();
        foreach (int id in m_bins.Keys)
        {
            if (!present.Contains(id))
            {
                stale.Add(id);
            }
        }
        foreach (int id in stale)
        {
            m_bins.Remove(id);
            m_lastAt.Remove(id);
            m_sampleCount.Remove(id);
            m_displayName.Remove(id);
        }

        // Item set changed — rebuild BarPlots to match.  Items keep their
        // accumulated bins (we just reattach a fresh plottable around the
        // same data).
        RebuildBarPlots();
        m_dirty = true;
    }

    private void RebuildBarPlots()
    {
        if (m_plot is null)
        {
            return;
        }

        // Remove all existing BarPlots.
        foreach (BarPlot bp in m_barPlots.Values)
        {
            m_plot.Remove(bp);
        }
        m_barPlots.Clear();
        m_barLists.Clear();

        // Create one BarPlot per item with kBinCount bars at fixed X positions.
        int itemCount = m_bins.Count;
        int idx = 0;
        foreach ((int id, _) in m_bins)
        {
            double xOffset = (idx - (itemCount - 1) / 2.0) * (kBinSizeMs / Math.Max(1, itemCount + 1));
            double barSize = kBinSizeMs / Math.Max(1.5, itemCount + 1);
            var bars = new List<Bar>(kBinCount);
            for (int i = 0; i < kBinCount; i++)
            {
                bars.Add(new Bar
                {
                    Position = (i + 0.5) * kBinSizeMs + xOffset,
                    Value = 0,
                    Size = barSize,
                    FillColor = ItemColors.ScottPlotForItemId(id)
                });
            }
            BarPlot bp = m_plot.Add.Bars(bars);
            bp.LegendText = m_displayName.TryGetValue(id, out string? n) ? n : $"#{id}";
            m_barPlots[id] = bp;
            m_barLists[id] = bars;
            idx++;
        }
    }

    public void OnEvent(in NotificationEvent ev)
    {
        if (ev.Kind != NotificationKind.DataChange)
        {
            return;
        }

        if (!m_bins.TryGetValue(ev.ItemId, out double[]? bins))
        {
            return;
        }

        if (m_lastAt.TryGetValue(ev.ItemId, out DateTime last))
        {
            double deltaMs = (ev.ReceivedAtUtc - last).TotalMilliseconds;
            if (deltaMs >= 0 && deltaMs < kBinMaxMs)
            {
                int idx = Math.Min(kBinCount - 1, (int)(deltaMs / kBinSizeMs));
                if (m_sampleCount[ev.ItemId] >= kMaxSamples)
                {
                    for (int i = 0; i < kBinCount; i++)
                    {
                        bins[i] *= 0.5;
                    }

                    m_sampleCount[ev.ItemId] = kMaxSamples / 2;
                }
                bins[idx] += 1;
                m_sampleCount[ev.ItemId]++;
                m_dirty = true;
            }
        }
        m_lastAt[ev.ItemId] = ev.ReceivedAtUtc;
    }

    public void Refresh()
    {
        if (m_plot is null || !m_dirty)
        {
            return;
        }

        m_dirty = false;

        // Mutate bar values in place — DO NOT clear/re-add so the user's
        // pan/zoom state is preserved.
        foreach ((int id, double[] bins) in m_bins)
        {
            if (!m_barLists.TryGetValue(id, out List<Bar>? bars))
            {
                continue;
            }

            for (int i = 0; i < kBinCount && i < bars.Count; i++)
            {
                bars[i].Value = bins[i];
            }
        }

        // Auto-fit only once per Bind so initial frame is correct, then
        // let the user own the axes.
        if (!m_initialFit)
        {
            m_initialFit = true;
            m_plot.Axes.AutoScale();
            m_plot.Axes.SetLimitsX(0, kBinMaxMs);
        }
        m_refresh?.Invoke();
    }

    public void ApplyXZoom(double factor)
    {
        if (m_plot is null)
        {
            return;
        }

        ScottPlotStyling.ScalePlotXAxis(m_plot, factor);
        m_refresh?.Invoke();
    }

    public void ResetXZoom()
    {
        if (m_plot is null)
        {
            return;
        }

        m_plot.Axes.AutoScale();
        m_plot.Axes.SetLimitsX(0, kBinMaxMs);
        m_refresh?.Invoke();
    }

    public void Dispose()
    {
        if (m_plot is not null)
        {
            foreach (BarPlot bp in m_barPlots.Values)
            {
                m_plot.Remove(bp);
            }
        }
        m_barPlots.Clear();
        m_barLists.Clear();
        m_bins.Clear();
        m_lastAt.Clear();
        m_sampleCount.Clear();
        m_displayName.Clear();
    }
}

/// <summary>
/// Heatmap-mode pump — single 2D array (rows = items, cols = time-buckets
/// of 100 ms each, 200 buckets wide = 20 s window).  Each cell holds the
/// notification count in that bucket.  The Heatmap and its ColorBar are
/// added to the plot once on <see cref="OnItemsChanged"/> and mutated in
/// place per tick; the user can pan/zoom because the plottables and axes
/// are not torn down between refreshes.
/// </summary>
internal sealed class HeatmapPump : IScottPlotPump
{
    private const int kCols = 200;
    private const int kBucketMs = 100;

    private Plot? m_plot;
    private Action? m_refresh;
    private Heatmap? m_heatmap;
    private ScottPlot.Panels.ColorBar? m_colorBar;
    private double[,] m_grid = new double[0, kCols];
    /// <summary>Display-ordered intensities (oldest at col 0, newest at right).</summary>
    private double[,] m_ordered = new double[0, kCols];
    private List<int> m_rowOrder = new();
    private List<string> m_rowNames = new();
    private int m_head;
    private DateTime m_columnStartUtc = DateTime.UtcNow;
    private bool m_initialFit;

    /// <summary>Test hook — number of rows in the heatmap grid.</summary>
    internal int RowCount => m_grid.GetLength(0);

    public void Bind(Plot plot, Action refresh)
    {
        m_plot = plot;
        m_refresh = refresh;
        RemovePlottables();
        plot.Clear();
        m_grid = new double[0, kCols];
        m_ordered = new double[0, kCols];
        m_rowOrder = new List<int>();
        m_rowNames = new List<string>();
        m_head = 0;
        m_columnStartUtc = DateTime.UtcNow;
        m_initialFit = false;
        ScottPlotStyling.Apply(plot,
            title: "Heatmap — notification density (items × time)",
            xLabel: "Time bucket (newest at right)",
            yLabel: "Item");
        // Fill the entire chart area on AutoScale.
        plot.Axes.Margins(0, 0);
        m_refresh();
    }

    public void OnItemsChanged(IReadOnlyList<MonitoredItemConfig> items)
    {
        if (m_plot is null)
        {
            return;
        }

        var newOrder = new List<int>(items.Count);
        var newNames = new List<string>(items.Count);
        foreach (MonitoredItemConfig it in items)
        {
            newOrder.Add(it.Id);
            newNames.Add(it.DisplayName ?? $"#{it.Id}");
        }
        var newGrid = new double[newOrder.Count, kCols];
        for (int newRow = 0; newRow < newOrder.Count; newRow++)
        {
            int id = newOrder[newRow];
            int oldRow = m_rowOrder.IndexOf(id);
            if (oldRow < 0)
            {
                continue;
            }

            for (int c = 0; c < kCols; c++)
            {
                newGrid[newRow, c] = m_grid[oldRow, c];
            }
        }
        m_grid = newGrid;
        m_rowOrder = newOrder;
        m_rowNames = newNames;

        // (Re)create heatmap + colorbar to match the new row count.  This
        // is the ONLY place that adds plottables; Refresh() only mutates
        // the existing Intensities array in place.
        RemovePlottables();
        if (m_rowOrder.Count > 0)
        {
            m_ordered = new double[m_rowOrder.Count, kCols];
            m_heatmap = m_plot.Add.Heatmap(m_ordered);
            m_heatmap.Colormap = new ScottPlot.Colormaps.Viridis();
            m_colorBar = m_plot.Add.ColorBar(m_heatmap);
            m_initialFit = false;
        }
        else
        {
            m_ordered = new double[0, kCols];
        }
    }

    public void OnEvent(in NotificationEvent ev)
    {
        if (ev.Kind != NotificationKind.DataChange)
        {
            return;
        }

        AdvanceColumn(ev.ReceivedAtUtc);
        int row = m_rowOrder.IndexOf(ev.ItemId);
        if (row < 0)
        {
            return;
        }

        m_grid[row, m_head] += 1;
    }

    private void AdvanceColumn(DateTime nowUtc)
    {
        int advance = (int)((nowUtc - m_columnStartUtc).TotalMilliseconds / kBucketMs);
        if (advance <= 0)
        {
            return;
        }

        if (advance > kCols)
        {
            advance = kCols;
        }

        for (int i = 0; i < advance; i++)
        {
            m_head = (m_head + 1) % kCols;
            for (int r = 0; r < m_grid.GetLength(0); r++)
            {
                m_grid[r, m_head] = 0;
            }
        }
        m_columnStartUtc = m_columnStartUtc.AddMilliseconds(advance * kBucketMs);
    }

    public void Refresh()
    {
        if (m_plot is null)
        {
            return;
        }

        AdvanceColumn(DateTime.UtcNow);

        int rows = m_grid.GetLength(0);
        if (rows == 0 || m_heatmap is null)
        {
            m_refresh?.Invoke();
            return;
        }

        // Copy the ring-buffered grid into the contiguous display array
        // (oldest at col 0, newest at col kCols-1).  The heatmap already
        // holds a reference to m_ordered so mutating in place updates the
        // displayed image after Heatmap.Update() / plot.Refresh().
        for (int c = 0; c < kCols; c++)
        {
            int src = (m_head + 1 + c) % kCols;
            for (int r = 0; r < rows; r++)
            {
                m_ordered[r, c] = m_grid[r, src];
            }
        }
        m_heatmap.Update();
        if (!m_initialFit)
        {
            m_initialFit = true;
            m_plot.Axes.AutoScale();
        }
        m_refresh?.Invoke();
    }

    public void ApplyXZoom(double factor)
    {
        if (m_plot is null)
        {
            return;
        }

        ScottPlotStyling.ScalePlotXAxis(m_plot, factor);
        m_refresh?.Invoke();
    }

    public void ResetXZoom()
    {
        if (m_plot is null)
        {
            return;
        }

        m_plot.Axes.AutoScale();
        m_refresh?.Invoke();
    }

    private void RemovePlottables()
    {
        if (m_plot is null)
        {
            return;
        }

        if (m_heatmap is not null)
        {
            m_plot.Remove(m_heatmap);
        }
        // ColorBar is a Panel, not a Plottable — remove via Axes.Remove.
        if (m_colorBar is not null)
        {
            m_plot.Axes.Remove((IPanel)m_colorBar);
        }

        m_heatmap = null;
        m_colorBar = null;
    }

    public void Dispose()
    {
        // Critical: explicitly remove the heatmap AND the colorbar.
        // Plot.Clear() in the next pump's Bind() does NOT remove color
        // bars, so they would otherwise linger across mode switches.
        RemovePlottables();
        m_grid = new double[0, kCols];
        m_ordered = new double[0, kCols];
        m_rowOrder.Clear();
        m_rowNames.Clear();
    }
}

