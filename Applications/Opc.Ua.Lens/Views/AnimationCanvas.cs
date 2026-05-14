/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using UaLens.Subscriptions;

namespace UaLens.Views;

internal enum AnimationMode
{
    Dots,
    Bars,
    Lines,
    /// <summary>ScottPlot DataStreamer per item — proper axes + pan/zoom.</summary>
    Signal,
    /// <summary>ScottPlot histogram of inter-arrival times per item.</summary>
    Histogram,
    /// <summary>ScottPlot heatmap: items × time, color = notification count.</summary>
    Heatmap
}

/// <summary>
/// Per-item line style for the Lines animation mode.  Cycled by
/// clicking the lane label; default is <see cref="Interpolated"/>.
/// </summary>
internal enum LineStyle
{
    Interpolated,
    Wave,
    Zigzag
}

/// <summary>
/// Live renderer for OPC UA subscription notifications, with two view modes:
/// </summary>
/// <remarks>
/// <para>
/// <b>Dots</b> (default) — one dot per notification on a scrolling time
/// axis (newest at the left edge, scrolling right).  Each currently-
/// monitored item gets its own colour-coded horizontal lane, with a
/// right-aligned label showing the item's display name.  A single
/// shared keep-alive lane sits at the bottom (violet).  Filled dots
/// represent data-change notifications; hollow rings represent events.
/// </para>
/// <para>
/// <b>Bars</b> — historical waterfall: 200 columns × 50 ms each.  Each
/// column is a stacked bar with the keep-alive count at the baseline
/// and one segment per item above it (same per-item palette as Dots
/// mode, log-scaled).  A small legend overlayed in the bottom-right
/// of the chart maps colour → item display name (KA at the bottom).
/// </para>
/// </remarks>
internal sealed class AnimationCanvas : Control
{
    private const int HistoryColumns = 200;

    // Bars-mode state — KA still uses a fixed array; per-item counts
    // live in a dictionary keyed by ItemId so we don't grow lanes
    // unboundedly when items are added/removed across a long session.
    private readonly Dictionary<int, int[]> m_perItem = new();
    private readonly int[] m_ka = new int[HistoryColumns];
    private int m_head;
    private DateTime m_columnStartUtc = DateTime.UtcNow;
    private const int ColumnIntervalMs = 50;

    // Dots-mode state — bounded queue of individual notifications, age-pruned
    // to DotsWindowSeconds with a hard ceiling so a runaway publisher can't
    // blow up the heap.
    private readonly Queue<DotEvent> m_dots = new();
    private const double DotsWindowSeconds = 10.0;
    private const int DotsCapacity = 8000;

    // Lines-mode state — per-item buffer of (timestamp, double) samples,
    // expand-only min/max envelope per item, and per-item line style.
    // Buffers are age-pruned to the same retention window as the dots
    // queue so the time axes stay in sync.
    private readonly Dictionary<int, Queue<LineSample>> m_perItemSamples = new();
    private readonly Dictionary<int, (double Min, double Max)> m_perItemRange = new();
    /// <summary>
    /// Cache of the oldest timestamp in each per-item sample queue.
    /// Updated on enqueue/prune; lets <see cref="Tick"/> skip the
    /// per-queue prune walk when nothing has yet aged out (avoiding
    /// O(N×M) cost across many items × deep buffers).
    /// </summary>
    private readonly Dictionary<int, DateTime> m_perItemOldest = new();
    private readonly Dictionary<int, LineStyle> m_perItemLineStyle = new();
    private const int LinesPerItemCapacity = 4096;
    // Hit-rectangles for lane labels recorded during the last RenderLines
    // call; used by OnPointerPressed to cycle a clicked lane's LineStyle.
    private readonly List<(Rect Rect, int ItemId)> m_laneLabelHits = new();

    // Resource overlay (CPU / Mem) — sampled at most once per second from the
    // optional <see cref="GetResourceSample"/> callback.  Same time window
    // as the dots so the overlay is naturally aligned with notifications.
    private readonly Queue<ResSample> m_res = new();
    private DateTime m_lastResSampleUtc;

    private ChannelReader<NotificationEvent>? m_events;
    private SubscriptionCounters? m_counters;
    private UaLens.Connection.NotificationRecorder? m_recorder;
    private DispatcherTimer? m_timer;

    private uint m_lastDataSeq;
    private uint m_lastEvtSeq;
    private uint m_lastKaSeq;

    private AnimationMode m_mode = AnimationMode.Dots;

    /// <summary>Selected view mode. Triggers an immediate redraw.</summary>
    public AnimationMode Mode
    {
        get => m_mode;
        set
        {
            if (m_mode == value)
            {
                return;
            }
            m_mode = value;
            InvalidateVisual();
        }
    }

    // Palette — same hues as the Terminal.Gui version for muscle-memory continuity.
    private static readonly IBrush s_bgBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x1A, 0x2C));
    private static readonly IBrush s_axisBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
    private static readonly IBrush s_textBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
    private static readonly IBrush s_dimBrush = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly IBrush s_gridBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x47, 0x55, 0x69));
    private static readonly IBrush s_kaBrush = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
    private static readonly IBrush s_cpuBrush = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4));   // cyan
    private static readonly IBrush s_memBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0x49, 0x99));   // pink

    private static readonly Typeface s_mono =
        new Typeface(new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"));

    public Func<string>? GetHeaderText { get; set; }

    /// <summary>
    /// Optional callback returning (missingCount, republishCount, droppedCount)
    /// for the gap-tracking line.  Surfaced from
    /// <c>ISubscriptionAdapter.MissingMessageCount</c> / <c>RepublishMessageCount</c>
    /// / <c>DroppedNotificationCount</c>.  When null, the seq line skips them.
    /// </summary>
    public Func<(long Missing, long Republish, long Dropped)>? GetGapMetrics { get; set; }

    /// <summary>Optional callback returning (cpuPercent, memMiB) for the overlay.</summary>
    public Func<(double Cpu, double MemMiB)>? GetResourceSample { get; set; }

    /// <summary>
    /// Optional callback returning the live list of monitored items.
    /// Used by both Dots (per-item lane layout) and Bars (segmented
    /// columns + legend).  Snapshotted at the start of each render to
    /// avoid mutation while drawing.
    /// </summary>
    public Func<IReadOnlyList<MonitoredItemConfig>>? GetItems { get; set; }

    /// <summary>
    /// Time-axis stretch factor.  Default 1.0.  Values &gt; 1 spread dots/bars
    /// horizontally (zoom in — smaller visible time window); values &lt; 1
    /// condense them (zoom out — wider visible window).  The pixel grid does
    /// NOT scale, providing a stable reference at every zoom level.
    /// </summary>
    public double TimeScale { get; set; } = 1.0;

    /// <summary>Vertical grid spacing in pixels — independent of TimeScale.</summary>
    private const double kGridStepPx = 50;

    /// <summary>When true, render the CPU+Mem overlay on top of the chart.</summary>
    public bool ShowResourceOverlay { get; set; }

    public AnimationCanvas()
    {
        ClipToBounds = true;
        // Was IsHitTestVisible=false to be transparent to clicks; flip on
        // so Lines mode can capture clicks on lane labels to cycle the
        // per-item LineStyle.  The other panes don't subscribe pointer
        // input so this is benign.
        IsHitTestVisible = true;
        Focusable = false;
    }

    public void Bind(ChannelReader<NotificationEvent>? events, SubscriptionCounters? counters,
                     UaLens.Connection.NotificationRecorder? recorder = null)
    {
        m_events = events;
        m_counters = counters;
        m_recorder = recorder;
        m_perItem.Clear();
        Array.Clear(m_ka);
        m_head = 0;
        m_lastDataSeq = m_lastEvtSeq = m_lastKaSeq = 0;
        m_columnStartUtc = DateTime.UtcNow;
        m_dots.Clear();
        m_res.Clear();
        m_lastResSampleUtc = default;
        m_perItemSamples.Clear();
        m_perItemRange.Clear();
        m_perItemOldest.Clear();
        // m_perItemLineStyle intentionally NOT cleared — user style choices
        // survive a re-bind so a reconnect doesn't reset the lane styles.
        InvalidateVisual();
    }

    /// <summary>Snapshot of the dot buffer (for headless tests / probes).</summary>
    internal IReadOnlyCollection<DotEvent> DotSnapshot => m_dots.ToArray();

    /// <summary>Sample count for a given item (for headless tests / probes).</summary>
    internal int LineSampleCountFor(int itemId)
        => m_perItemSamples.TryGetValue(itemId, out Queue<LineSample>? q) ? q.Count : 0;

    /// <summary>Min/Max envelope for a given item (for headless tests / probes).</summary>
    internal (double Min, double Max)? LineRangeFor(int itemId)
        => m_perItemRange.TryGetValue(itemId, out (double Min, double Max) r) ? r : null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        m_timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(ColumnIntervalMs / 2.0),
                                        DispatcherPriority.Render,
                                        (_, _) => Tick());
        m_timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        m_timer?.Stop();
    }

    private void Tick()
    {
        Drain();

        // Advance the bars-mode active column on wall-clock so silent intervals
        // become visible gaps instead of collapsing onto a single column.
        DateTime now = DateTime.UtcNow;
        int advance = (int)((now - m_columnStartUtc).TotalMilliseconds / ColumnIntervalMs);
        if (advance > 0)
        {
            for (int i = 0; i < advance; i++)
            {
                m_head = (m_head + 1) % HistoryColumns;
                m_ka[m_head] = 0;
                foreach (int[] perItem in m_perItem.Values)
                {
                    perItem[m_head] = 0;
                }
            }
            m_columnStartUtc = m_columnStartUtc.AddMilliseconds(advance * ColumnIntervalMs);
        }

        // Drop per-item buckets for items that are no longer in the active
        // subscription so the dictionary doesn't grow unboundedly across
        // many add/remove cycles.
        if (GetItems is { } gi)
        {
            IReadOnlyList<MonitoredItemConfig> live = gi();
            if (m_perItem.Count > 0)
            {
                var liveIds = new HashSet<int>();
                foreach (MonitoredItemConfig it in live)
                {
                    liveIds.Add(it.Id);
                }

                if (m_perItem.Keys.Any(k => !liveIds.Contains(k)))
                {
                    var removed = new List<int>();
                    foreach (int k in m_perItem.Keys)
                    {
                        if (!liveIds.Contains(k))
                        {
                            removed.Add(k);
                        }
                    }
                    foreach (int k in removed)
                    {
                        m_perItem.Remove(k);
                    }
                }
            }
        }

        // Age-prune the dots buffer.  When TimeScale < 1 the visible window
        // grows beyond the default DotsWindowSeconds (we zoom out in time),
        // so retention has to grow with it — otherwise the leftmost portion
        // of the chart is empty at zoom-out.  DotsCapacity still caps memory.
        double retentionSeconds = DotsWindowSeconds / Math.Min(1.0, TimeScale);
        DateTime cutoff = now - TimeSpan.FromSeconds(retentionSeconds);
        while (m_dots.Count > 0 && m_dots.Peek().TimestampUtc < cutoff)
        {
            m_dots.Dequeue();
        }

        // Mirror the prune for Lines-mode per-item sample buffers.  Walk
        // only buckets whose tracked oldest timestamp is already past the
        // cutoff so the loop is O(active items) instead of O(items×depth).
        if (m_perItemSamples.Count > 0)
        {
            foreach (KeyValuePair<int, Queue<LineSample>> kv in m_perItemSamples)
            {
                if (!m_perItemOldest.TryGetValue(kv.Key, out DateTime oldest) || oldest >= cutoff)
                {
                    continue;
                }
                Queue<LineSample> q = kv.Value;
                while (q.Count > 0 && q.Peek().TimestampUtc < cutoff)
                {
                    q.Dequeue();
                }
                if (q.Count > 0)
                {
                    m_perItemOldest[kv.Key] = q.Peek().TimestampUtc;
                }
                else
                {
                    m_perItemOldest.Remove(kv.Key);
                }
            }
        }

        // Resource overlay: sample at most once per second so the line is
        // smooth without burning CPU on every render tick.
        if (GetResourceSample is { } get
            && (now - m_lastResSampleUtc).TotalMilliseconds >= 1000)
        {
            (double cpu, double mem) = get();
            m_res.Enqueue(new ResSample(cpu, mem, now));
            m_lastResSampleUtc = now;
            while (m_res.Count > 0 && m_res.Peek().Ts < cutoff)
            {
                m_res.Dequeue();
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Drain the channel reader, populating BOTH the bars-mode counters AND
    /// the dots-mode buffer.  Switching modes is therefore instantaneous —
    /// no information is lost when the user flips the dropdown.
    /// </summary>
    internal void Drain()
    {
        if (m_events is null)
        {
            return;
        }
        while (m_events.TryRead(out NotificationEvent ne))
        {
            m_recorder?.Record(ne);
            // Bars-mode counters.
            switch (ne.Kind)
            {
                case NotificationKind.DataChange:
                case NotificationKind.Event:
                {
                    if (!m_perItem.TryGetValue(ne.ItemId, out int[]? buf))
                    {
                        buf = new int[HistoryColumns];
                        m_perItem[ne.ItemId] = buf;
                    }
                    buf[m_head] += Math.Max(1, ne.ValueCount);
                    if (ne.Kind == NotificationKind.DataChange)
                    {
                        m_lastDataSeq = ne.SequenceNumber;
                    }
                    else
                    {
                        m_lastEvtSeq = ne.SequenceNumber;
                    }
                }
                break;
                case NotificationKind.KeepAlive:
                    m_ka[m_head] += 1;
                    m_lastKaSeq = ne.SequenceNumber;
                    break;
            }

            // Dots-mode buffer — keep one dot per ValueCount (or just one for
            // keep-alives) so a publish carrying 5 values shows as 5 dots.
            int dots = ne.Kind == NotificationKind.KeepAlive
                ? 1
                : Math.Max(1, ne.ValueCount);
            for (int i = 0; i < dots; i++)
            {
                m_dots.Enqueue(new DotEvent(ne.Kind, ne.ItemId, ne.SequenceNumber, ne.ReceivedAtUtc));
                if (m_dots.Count > DotsCapacity)
                {
                    m_dots.Dequeue();
                }
            }

            // Lines-mode buffer — only data-change events that carry a
            // numeric value.  The Variant→double conversion happened in
            // the engine adapter (VariantNumeric.TryToDouble); here we
            // just append to the per-item sample queue and update the
            // expand-only min/max envelope.
            if (ne.Kind == NotificationKind.DataChange && ne.Value is double v)
            {
                if (!m_perItemSamples.TryGetValue(ne.ItemId, out Queue<LineSample>? q))
                {
                    q = new Queue<LineSample>();
                    m_perItemSamples[ne.ItemId] = q;
                }
                q.Enqueue(new LineSample(ne.ReceivedAtUtc, v));
                while (q.Count > LinesPerItemCapacity)
                {
                    q.Dequeue();
                }
                // Track the oldest sample timestamp so Tick() can skip the
                // prune walk for this item when nothing has aged out yet.
                m_perItemOldest[ne.ItemId] = q.Peek().TimestampUtc;
                if (m_perItemRange.TryGetValue(ne.ItemId, out (double Min, double Max) range))
                {
                    if (v < range.Min)
                    {
                        range.Min = v;
                    }

                    if (v > range.Max)
                    {
                        range.Max = v;
                    }

                    m_perItemRange[ne.ItemId] = range;
                }
                else
                {
                    m_perItemRange[ne.ItemId] = (v, v);
                }
            }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        Rect bounds = new(default, Bounds.Size);
        ctx.FillRectangle(s_bgBrush, bounds);

        // ----- Header (shared by both modes) -----
        // The header carries three rows: title (engine + counters context),
        // sequence-number ticker (data / event / KA last seen), and the
        // running totals. Bumped to two physically distinct lines for
        // readability — the seq line was hard to read at the previous 11px
        // single-row layout.
        const double headerH = 52;
        // Tracks how tall the header actually ends up being.  When the
        // right-aligned counters line would collide with the seq line
        // we drop it to its own row, expanding the header to 70px.
        double dynamicHeaderH = headerH;
        string header = GetHeaderText?.Invoke() ?? "(animation)";
        var headerText = new FormattedText(header, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 12, s_textBrush);
        ctx.DrawText(headerText, new Point(8, 4));

        string seqs = string.Format(CultureInfo.InvariantCulture,
            "seq  data:{0,-8}  evt:{1,-8}  ka:{2,-8}",
            m_lastDataSeq, m_lastEvtSeq, m_lastKaSeq);
        if (GetGapMetrics is { } gm)
        {
            (long missing, long republish, long dropped) = gm();
            seqs += string.Format(CultureInfo.InvariantCulture,
                "  missing:{0,-6}  republish:{1,-6}  dropped:{2,-6}",
                missing, republish, dropped);
        }
        var seqText = new FormattedText(seqs, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 13, s_dimBrush);
        ctx.DrawText(seqText, new Point(8, 22));

        if (m_counters is { } c)
        {
            string counters = string.Format(CultureInfo.InvariantCulture,
                "Σ data:{0}  evt:{1}  ka:{2}   values data:{3}  evt:{4}",
                c.DataMessages, c.EventMessages, c.KeepAlives, c.DataValues, c.EventValues);
            var counterText = new FormattedText(counters, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, s_mono, 12, s_textBrush);
            // Default: right-aligned on the seq line.  If that would
            // overlap the seq text (seq ends at 8 + seqText.Width plus
            // a 12px gap), drop counters to a third row.
            double seqRight = 8 + seqText.Width + 12;
            double counterX = Bounds.Width - counterText.Width - 8;
            if (counterX < seqRight)
            {
                ctx.DrawText(counterText, new Point(8, 40));
                dynamicHeaderH = 64;
            }
            else
            {
                ctx.DrawText(counterText, new Point(counterX, 22));
            }
        }

        Rect chart = new(0, dynamicHeaderH, Bounds.Width, Math.Max(0, Bounds.Height - dynamicHeaderH - 4));
        if (chart.Height < 8 || chart.Width < 16)
        {
            return;
        }

        // Grid is drawn first so dots/bars/overlay sit on top.
        RenderGrid(ctx, chart);

        if (m_mode == AnimationMode.Dots)
        {
            RenderDots(ctx, chart);
        }
        else if (m_mode == AnimationMode.Lines)
        {
            RenderLines(ctx, chart);
        }
        else
        {
            RenderBars(ctx, chart);
        }
        // Note: the per-bar legend is drawn inside RenderBars (Bars-mode
        // only).  Dots and Lines modes use right-aligned per-lane labels.
        if (ShowResourceOverlay)
        {
            RenderResourceOverlay(ctx, chart);
        }
    }

    /// <summary>
    /// Pixel-pitch grid drawn underneath everything.  Vertical lines every
    /// <see cref="kGridStepPx"/> from the left edge; horizontal quartile
    /// lines.  Lines stay at fixed pixel positions regardless of
    /// <see cref="TimeScale"/>; their time-meaning is shown by tick labels
    /// at the bottom of the chart.
    /// </summary>
    private void RenderGrid(DrawingContext ctx, Rect chart)
    {
        var gridPen = new Pen(s_gridBrush, 1.0);

        // Vertical lines — every kGridStepPx, with a slightly stronger pen
        // every other line so labels under them are easy to spot.
        for (double x = chart.X + kGridStepPx; x < chart.Right; x += kGridStepPx)
        {
            ctx.DrawLine(gridPen, new Point(x, chart.Y), new Point(x, chart.Bottom));
        }
        // Horizontal quartile lines.
        for (int q = 1; q < 4; q++)
        {
            double y = chart.Y + chart.Height * q / 4.0;
            ctx.DrawLine(gridPen, new Point(chart.X, y), new Point(chart.Right, y));
        }

        // Bottom-edge time-tick labels — every other vertical grid line.
        // Time per pixel is the inverse of pxPerSec used by the dots/bars
        // renderers, so the labels match what the data shows.
        double pxPerSec = chart.Width / DotsWindowSeconds * TimeScale;
        if (pxPerSec <= 0)
        {
            return;
        }
        int labelEvery = 2;
        int idx = 0;
        for (double x = chart.X; x < chart.Right; x += kGridStepPx, idx++)
        {
            if (idx % labelEvery != 0)
            {
                continue;
            }
            double seconds = (x - chart.X) / pxPerSec;
            string label = seconds < 1.0
                ? $"+{seconds * 1000:0}ms"
                : $"+{seconds:0.0}s";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, s_mono, 9, s_dimBrush);
            ctx.DrawText(ft, new Point(x + 2, chart.Bottom - ft.Height - 2));
        }
    }

    private void RenderDots(DrawingContext ctx, Rect chart)
    {
        // Snapshot the items list (stable per render); KA always gets its own
        // lane at the bottom.  Lane height = chart.Height / (items.Count + 1).
        IReadOnlyList<MonitoredItemConfig> items = GetItems?.Invoke()
            ?? (IReadOnlyList<MonitoredItemConfig>)Array.Empty<MonitoredItemConfig>();
        int laneCount = items.Count + 1;
        double laneH = chart.Height / Math.Max(1, laneCount);
        double jitter = Math.Min(laneH * 0.30, 14);

        var axisPen = new Pen(s_axisBrush, 0.5);
        // Faint lane separators between every lane.
        for (int i = 1; i < laneCount; i++)
        {
            double sy = chart.Y + laneH * i;
            ctx.DrawLine(axisPen, new Point(chart.X, sy), new Point(chart.Right, sy));
        }

        // Map ItemId → lane index (0..items.Count-1); KA = laneCount - 1.
        var itemLane = new Dictionary<int, int>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            itemLane[items[i].Id] = i;
        }
        int kaLane = laneCount - 1;
        double kaY = chart.Y + (kaLane + 0.5) * laneH;

        // Right-aligned per-lane label.  KA is purple; items use the palette.
        for (int i = 0; i < items.Count; i++)
        {
            double cy = chart.Y + (i + 0.5) * laneH;
            DrawLaneLabelRight(ctx, TruncateLabel(items[i].DisplayName, 28),
                chart.Right - 6, cy, ItemColors.ForItemId(items[i].Id));
        }
        DrawLaneLabelRight(ctx, "KA", chart.Right - 6, kaY, s_kaBrush);

        DateTime now = DateTime.UtcNow;
        double pxPerSec = chart.Width / DotsWindowSeconds * TimeScale;
        const double dotR = 2.5;

        foreach (DotEvent d in m_dots)
        {
            double age = (now - d.TimestampUtc).TotalSeconds;
            if (age < 0)
            {
                age = 0;
            }
            double x = chart.X + age * pxPerSec;
            if (x > chart.Right)
            {
                continue;
            }

            double y;
            IBrush brush;
            bool hollow = false;
            if (d.Kind == NotificationKind.KeepAlive)
            {
                y = kaY;
                brush = s_kaBrush;
            }
            else
            {
                if (!itemLane.TryGetValue(d.ItemId, out int lane))
                {
                    continue;          // item already removed — drop dot
                }
                y = chart.Y + (lane + 0.5) * laneH;
                brush = ItemColors.ForItemId(d.ItemId);
                hollow = d.Kind == NotificationKind.Event;
            }
            // Deterministic jitter from a hash of (kind, item, seq, tick) so the
            // same dot always lands on the same row.
            int h = HashCode.Combine((int)d.Kind, d.ItemId, (int)d.SequenceNumber, d.TimestampUtc.Ticks);
            double offset = ((h & 0xFF) / 255.0 * 2.0 - 1.0) * jitter;
            var p = new Point(x, y + offset);
            if (hollow)
            {
                ctx.DrawEllipse(null, new Pen(brush, 1.5), p, dotR, dotR);
            }
            else
            {
                ctx.DrawEllipse(brush, null, p, dotR, dotR);
            }
        }
    }

    private void RenderBars(DrawingContext ctx, Rect chart)
    {
        IReadOnlyList<MonitoredItemConfig> items = GetItems?.Invoke()
            ?? (IReadOnlyList<MonitoredItemConfig>)Array.Empty<MonitoredItemConfig>();

        // Determine peak for vertical scale (log-scaled so 1 ms / 1 s ranges coexist).
        int peak = 1;
        for (int i = 0; i < HistoryColumns; i++)
        {
            int total = m_ka[i];
            foreach (int[] perItem in m_perItem.Values)
            {
                total += perItem[i];
            }
            if (total > peak)
            {
                peak = total;
            }
        }

        // Bar width: clamp to at least the natural width so zoom-out still
        // fills the entire chart pane (TimeScale < 1 would otherwise compress
        // the bars to a fraction of the chart and leave empty space on the
        // right).  At TimeScale >= 1 the bars stretch as designed.
        double colW = chart.Width / HistoryColumns * Math.Max(TimeScale, 1.0);
        double maxBar = chart.Height - 14;
        double yBaseline = chart.Bottom - 2;

        var axisPen = new Pen(s_axisBrush, 0.5);
        ctx.DrawLine(axisPen, new Point(chart.X, yBaseline), new Point(chart.Right, yBaseline));
        var scaleText = new FormattedText($"peak {peak}", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 10, s_dimBrush);
        ctx.DrawText(scaleText, new Point(chart.Right - scaleText.Width - 4, chart.Y));

        for (int i = 0; i < HistoryColumns; i++)
        {
            int idx = (m_head + 1 + i) % HistoryColumns;
            int kv = m_ka[idx];
            int tot = kv;
            foreach (int[] perItem in m_perItem.Values)
            {
                tot += perItem[idx];
            }
            if (tot == 0)
            {
                continue;
            }

            double scale = Math.Log10(1 + tot) / Math.Log10(1 + peak);
            double barH = scale * maxBar;
            // Newest at LEFT (i=HistoryColumns-1 → x=chart.X), oldest at RIGHT.
            double x = chart.X + (HistoryColumns - 1 - i) * colW;
            if (x >= chart.Right)
            {
                continue;
            }
            double w = Math.Max(1, colW - 1);

            // Stack order: KA at the baseline; items stacked above in the
            // same order as `items` so the bar colours match the legend rows
            // top-to-bottom (legend top row = first item, KA at the bottom).
            double y = yBaseline;
            if (kv > 0)
            {
                double h = barH * kv / tot;
                ctx.FillRectangle(s_kaBrush, new Rect(x, y - h, w, h));
                y -= h;
            }
            // Items stacked from bottom (just above KA) upwards, in items
            // order — last item ends up on top of the bar.
            for (int it = 0; it < items.Count; it++)
            {
                int id = items[it].Id;
                if (!m_perItem.TryGetValue(id, out int[]? perItem))
                {
                    continue;
                }
                int v = perItem[idx];
                if (v <= 0)
                {
                    continue;
                }
                double h = barH * v / tot;
                ctx.FillRectangle(ItemColors.ForItemId(id), new Rect(x, y - h, w, h));
                y -= h;
            }
        }

        // Bottom-right legend (Bars-mode only).
        DrawBarsLegend(ctx, chart, items);
    }

    /// <summary>
    /// Bottom-right legend used by the Bars renderer.  Each item gets a
    /// row with a colour swatch + (truncated) display name; KA sits at
    /// the bottom in violet.  Drawn over the bars so it's always visible.
    /// </summary>
    private static void DrawBarsLegend(DrawingContext ctx, Rect chart,
        IReadOnlyList<MonitoredItemConfig> items)
    {
        if (items.Count == 0)
        {
            // Just the KA row.
        }
        const double rowH = 14;
        const double swatchW = 10;
        const double padX = 6;
        const double padY = 4;
        const int maxNameChars = 22;

        // Measure widest label.
        double nameW = 24;
        var typeface = s_mono;
        foreach (MonitoredItemConfig it in items)
        {
            string label = TruncateLabel(it.DisplayName, maxNameChars);
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, s_dimBrush);
            if (ft.Width > nameW)
            {
                nameW = ft.Width;
            }
        }
        double boxW = swatchW + 6 + nameW + padX * 2;
        double boxH = rowH * (items.Count + 1) + padY * 2;
        double bx = chart.Right - boxW - 6;
        double by = chart.Bottom - boxH - 6;
        // Background — semi-transparent so bars peek through faintly.
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0xC0, 0x0B, 0x12, 0x20)),
            new Rect(bx, by, boxW, boxH));
        ctx.DrawRectangle(null, new Pen(s_axisBrush, 0.6), new Rect(bx, by, boxW, boxH));

        double rowY = by + padY;
        for (int i = 0; i < items.Count; i++)
        {
            DrawLegendRow(ctx, bx + padX, rowY, swatchW, ItemColors.ForItemId(items[i].Id),
                TruncateLabel(items[i].DisplayName, maxNameChars));
            rowY += rowH;
        }
        DrawLegendRow(ctx, bx + padX, rowY, swatchW, s_kaBrush, "KA");
    }

    private static void DrawLegendRow(DrawingContext ctx, double x, double y,
        double swatchW, IBrush brush, string label)
    {
        ctx.FillRectangle(brush, new Rect(x, y + 2, swatchW, 8));
        var ft = new FormattedText(label, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 10, s_dimBrush);
        ctx.DrawText(ft, new Point(x + swatchW + 6, y));
    }

    /// <summary>
    /// Right-aligned per-lane label used in Dots mode.  Centres the text
    /// vertically on <paramref name="centerY"/> and right-aligns it on
    /// <paramref name="rightEdge"/>.
    /// </summary>
    private static void DrawLaneLabelRight(DrawingContext ctx, string label,
        double rightEdge, double centerY, IBrush brush)
    {
        var ft = new FormattedText(label, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 10, brush);
        ctx.DrawText(ft, new Point(rightEdge - ft.Width, centerY - ft.Height / 2.0));
    }

    private static string TruncateLabel(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Length <= max ? s : string.Concat(s.AsSpan(0, Math.Max(1, max - 1)), "…");
    }

    /// <summary>
    /// Lines view: KA at the bottom + one lane per item. Each item's
    /// <c>(timestamp, double)</c> samples are plotted using the per-item
    /// <see cref="LineStyle"/>.  Y-axis auto-fits the expand-only
    /// min/max envelope.  When the envelope contains both negative and
    /// positive values, a faint horizontal line at <c>y=0</c> is drawn
    /// for visual reference.
    /// </summary>
    private void RenderLines(DrawingContext ctx, Rect chart)
    {
        IReadOnlyList<MonitoredItemConfig> items = GetItems?.Invoke()
            ?? (IReadOnlyList<MonitoredItemConfig>)Array.Empty<MonitoredItemConfig>();
        int laneCount = items.Count + 1;
        double laneH = chart.Height / Math.Max(1, laneCount);

        var axisPen = new Pen(s_axisBrush, 0.5);
        for (int i = 1; i < laneCount; i++)
        {
            double sy = chart.Y + laneH * i;
            ctx.DrawLine(axisPen, new Point(chart.X, sy), new Point(chart.Right, sy));
        }

        DateTime now = DateTime.UtcNow;
        double pxPerSec = chart.Width / DotsWindowSeconds * TimeScale;

        // Reset lane label hit-rects each render so click handling
        // matches the current layout.
        m_laneLabelHits.Clear();

        for (int i = 0; i < items.Count; i++)
        {
            MonitoredItemConfig item = items[i];
            int id = item.Id;
            double laneTop = chart.Y + i * laneH;
            double laneBottom = laneTop + laneH;
            double cy = laneTop + laneH * 0.5;
            IBrush itemBrush = ItemColors.ForItemId(id);
            LineStyle style = m_perItemLineStyle.TryGetValue(id, out LineStyle s) ? s : LineStyle.Interpolated;
            string glyph = StyleGlyph(style);

            string label = TruncateLabel(item.DisplayName, 24) + "  " + glyph;
            DrawAndRecordLaneLabelRight(ctx, label, chart.Right - 6, cy, itemBrush, id);

            if (!m_perItemSamples.TryGetValue(id, out Queue<LineSample>? samples) || samples.Count == 0
                || !m_perItemRange.TryGetValue(id, out (double Min, double Max) range))
            {
                var ph = new FormattedText("(no numeric data)", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, s_mono, 10, s_dimBrush);
                ctx.DrawText(ph, new Point(chart.X + 6, cy - ph.Height / 2.0));
                continue;
            }

            double pad = laneH * 0.10;     // small vertical padding so lines don't touch the lane edges
            double yMin = range.Min;
            double yMax = range.Max;
            double yRange = Math.Max(1e-9, yMax - yMin);

            double MapY(double v)
            {
                double t = (v - yMin) / yRange;
                return laneBottom - pad - t * (laneH - 2 * pad);
            }

            // Zero-line indicator (lane shows both signs).
            if (yMin < 0 && yMax > 0)
            {
                var zeroPen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x94, 0xA3, 0xB8)), 1.0);
                double y0 = MapY(0.0);
                ctx.DrawLine(zeroPen, new Point(chart.X, y0), new Point(chart.Right, y0));
            }

            // Build a list of (x,y) for samples whose age fits the chart.
            // Iterate the queue oldest→newest; we'll order them rightmost
            // (oldest, large age) → leftmost (newest, small age).  After
            // collection, sort by x ascending so geometry building is
            // straightforward.
            var pts = new List<Point>(samples.Count);
            foreach (LineSample sm in samples)
            {
                double age = (now - sm.TimestampUtc).TotalSeconds;
                if (age < 0)
                {
                    age = 0;
                }

                double x = chart.X + age * pxPerSec;
                if (x > chart.Right)
                {
                    continue;
                }

                pts.Add(new Point(x, MapY(sm.Value)));
            }
            if (pts.Count == 0)
            {
                continue;
            }

            pts.Sort((a, b) => a.X.CompareTo(b.X));

            var linePen = new Pen(itemBrush, 1.5);
            switch (style)
            {
                case LineStyle.Zigzag:
                    DrawZigzag(ctx, linePen, pts);
                    break;
                case LineStyle.Wave:
                    DrawWave(ctx, linePen, pts);
                    break;
                default:
                    DrawInterpolated(ctx, linePen, pts);
                    break;
            }

            // Min / max axis labels at the lane's right edge, just inside the
            // chart so they don't overlap the right-aligned lane label above.
            DrawAxisMiniLabel(ctx, FormatNumber(yMax), chart.Right - 80, laneTop + 1);
            DrawAxisMiniLabel(ctx, FormatNumber(yMin), chart.Right - 80, laneBottom - 12);
        }

        // KA lane at the bottom: short tick per keep-alive event.
        double kaLaneTop = chart.Y + (laneCount - 1) * laneH;
        double kaCy = kaLaneTop + laneH * 0.5;
        DrawLaneLabelRight(ctx, "KA", chart.Right - 6, kaCy, s_kaBrush);
        var kaPen = new Pen(s_kaBrush, 1.0);
        foreach (DotEvent d in m_dots)
        {
            if (d.Kind != NotificationKind.KeepAlive)
            {
                continue;
            }

            double age = (now - d.TimestampUtc).TotalSeconds;
            if (age < 0)
            {
                age = 0;
            }

            double x = chart.X + age * pxPerSec;
            if (x > chart.Right)
            {
                continue;
            }

            ctx.DrawLine(kaPen, new Point(x, kaLaneTop + 4), new Point(x, kaLaneTop + laneH - 4));
        }
    }

    private static void DrawInterpolated(DrawingContext ctx, IPen pen, List<Point> pts)
    {
        if (pts.Count < 2)
        {
            return;
        }

        var geom = new StreamGeometry();
        using (StreamGeometryContext c = geom.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++)
            {
                c.LineTo(pts[i]);
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geom);
    }

    private static void DrawZigzag(DrawingContext ctx, IPen pen, List<Point> pts)
    {
        if (pts.Count < 2)
        {
            return;
        }

        var geom = new StreamGeometry();
        using (StreamGeometryContext c = geom.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++)
            {
                // Step: hold previous y, then jump to new y.
                c.LineTo(new Point(pts[i].X, pts[i - 1].Y));
                c.LineTo(pts[i]);
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geom);
    }

    private static void DrawWave(DrawingContext ctx, IPen pen, List<Point> pts)
    {
        if (pts.Count < 2)
        {
            return;
        }

        var geom = new StreamGeometry();
        using (StreamGeometryContext c = geom.Open())
        {
            c.BeginFigure(pts[0], false);
            // Catmull-Rom → cubic Bezier conversion, tension = 0.5.
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Point p0 = i == 0 ? pts[i] : pts[i - 1];
                Point p1 = pts[i];
                Point p2 = pts[i + 1];
                Point p3 = i + 2 < pts.Count ? pts[i + 2] : p2;
                Point cp1 = new(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
                Point cp2 = new(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
                c.CubicBezierTo(cp1, cp2, p2);
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geom);
    }

    private static string StyleGlyph(LineStyle s) => s switch
    {
        LineStyle.Wave => "≈",
        LineStyle.Zigzag => "⌐",
        _ => "~"
    };

    private static string FormatNumber(double v)
    {
        if (Math.Abs(v) < 1e-3 && v != 0)
        {
            return v.ToString("0.###e+0", CultureInfo.InvariantCulture);
        }

        if (Math.Abs(v) >= 10000)
        {
            return v.ToString("0.###e+0", CultureInfo.InvariantCulture);
        }

        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void DrawAxisMiniLabel(DrawingContext ctx, string text, double x, double y)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 9, s_dimBrush);
        ctx.DrawText(ft, new Point(x + (74 - ft.Width), y));
    }

    /// <summary>
    /// Right-aligns the lane label like <see cref="DrawLaneLabelRight"/>
    /// and additionally records the click rectangle used by
    /// <see cref="OnPointerPressed"/> to cycle the per-item LineStyle.
    /// </summary>
    private void DrawAndRecordLaneLabelRight(DrawingContext ctx, string label,
        double rightEdge, double centerY, IBrush brush, int itemId)
    {
        var ft = new FormattedText(label, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 10, brush);
        var origin = new Point(rightEdge - ft.Width, centerY - ft.Height / 2.0);
        ctx.DrawText(ft, origin);
        // Slightly inflate the hit rect so the user gets some forgiveness.
        m_laneLabelHits.Add(
            (new Rect(origin.X - 2, origin.Y - 2, ft.Width + 4, ft.Height + 4), itemId));
    }

    protected override void OnPointerPressed(global::Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (m_mode != AnimationMode.Lines)
        {
            return;
        }
        Point p = e.GetPosition(this);
        for (int i = 0; i < m_laneLabelHits.Count; i++)
        {
            if (m_laneLabelHits[i].Rect.Contains(p))
            {
                int itemId = m_laneLabelHits[i].ItemId;
                LineStyle current = m_perItemLineStyle.TryGetValue(itemId, out LineStyle s)
                    ? s : LineStyle.Interpolated;
                LineStyle next = current switch
                {
                    LineStyle.Interpolated => LineStyle.Wave,
                    LineStyle.Wave => LineStyle.Zigzag,
                    _ => LineStyle.Interpolated
                };
                m_perItemLineStyle[itemId] = next;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Draws the CPU% (cyan, left-axis 0..100) and Memory MiB (pink, right-axis
    /// 0..max) line overlays.  Both share the dot/bar X-axis (newest at the
    /// LEFT, oldest at the RIGHT — same direction as the notification animation).
    /// </summary>
    private void RenderResourceOverlay(DrawingContext ctx, Rect chart)
    {
        if (m_res.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        double pxPerSec = chart.Width / DotsWindowSeconds * TimeScale;

        // Find peak memory over the visible window so the right-axis scale
        // is auto-fit.  CPU is fixed 0..100.
        double maxMem = 1;
        foreach (ResSample s in m_res)
        {
            if (s.MemMiB > maxMem)
            {
                maxMem = s.MemMiB;
            }
        }
        // Round up to a "pretty" upper bound for the right-axis label.
        double memTop = Math.Pow(10, Math.Ceiling(Math.Log10(Math.Max(1, maxMem))));
        memTop = Math.Max(memTop, 16);

        // Y-axis labels (left = CPU 0..100%, right = Mem 0..memTop MiB).
        DrawAxisLabel(ctx, "CPU% 100", chart.X + 2, chart.Y + 2, s_cpuBrush);
        DrawAxisLabel(ctx, "0", chart.X + 2, chart.Bottom - 12, s_cpuBrush);
        DrawAxisLabel(ctx, $"Mem {memTop:0} MiB",
                                       chart.Right - 100, chart.Y + 2, s_memBrush);
        DrawAxisLabel(ctx, "0", chart.Right - 12, chart.Bottom - 12, s_memBrush);

        var cpuPen = new Pen(s_cpuBrush, 1.5);
        var memPen = new Pen(s_memBrush, 1.5);

        // Build polylines from oldest (right edge) → newest (left edge).
        // We also remember the first CPU/Mem points and the last ones so we
        // can extend the lines horizontally to the chart edges — otherwise
        // the polyline starts and ends inside the pane (right edge before
        // the buffer has filled, left edge until the next sample arrives),
        // producing the gaps the user reported.
        Point? firstCpu = null, firstMem = null;
        Point? lastCpu = null, lastMem = null;
        Point? prevCpu = null, prevMem = null;
        foreach (ResSample s in m_res)
        {
            double age = (now - s.Ts).TotalSeconds;
            if (age < 0)
            {
                age = 0;
            }

            double x = chart.X + age * pxPerSec;
            if (x > chart.Right)
            {
                continue;
            }

            double yCpu = chart.Bottom - Math.Clamp(s.Cpu, 0, 100) / 100.0 * chart.Height;
            double yMem = chart.Bottom - Math.Clamp(s.MemMiB, 0, memTop) / memTop * chart.Height;
            var pCpu = new Point(x, yCpu);
            var pMem = new Point(x, yMem);

            if (prevCpu is { } pc && !double.IsNaN(s.Cpu))
            {
                ctx.DrawLine(cpuPen, pc, pCpu);
            }

            if (prevMem is { } pm)
            {
                ctx.DrawLine(memPen, pm, pMem);
            }

            prevCpu = double.IsNaN(s.Cpu) ? null : pCpu;
            prevMem = pMem;
            // Iteration is oldest → newest, so the first iteration captures
            // the right-edge anchors and the last captures the left-edge
            // anchors.  These get extended outward below.
            firstCpu ??= double.IsNaN(s.Cpu) ? null : pCpu;
            firstMem ??= pMem;
            if (!double.IsNaN(s.Cpu))
            {
                lastCpu = pCpu;
            }

            lastMem = pMem;
        }
        // Extend the polyline horizontally to both pane edges so it never
        // appears to drop off in mid-air.
        if (firstCpu is { } fc && fc.X < chart.Right)
        {
            ctx.DrawLine(cpuPen, fc, new Point(chart.Right, fc.Y));
        }
        if (firstMem is { } fm && fm.X < chart.Right)
        {
            ctx.DrawLine(memPen, fm, new Point(chart.Right, fm.Y));
        }
        if (lastCpu is { } lc && lc.X > chart.X)
        {
            ctx.DrawLine(cpuPen, lc, new Point(chart.X, lc.Y));
        }
        if (lastMem is { } lm && lm.X > chart.X)
        {
            ctx.DrawLine(memPen, lm, new Point(chart.X, lm.Y));
        }
    }

    private static void DrawAxisLabel(DrawingContext ctx, string text, double x, double y, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, s_mono, 9, brush);
        ctx.DrawText(ft, new Point(x, y));
    }
}

/// <summary>One CPU / memory sample for the overlay buffer.</summary>
internal readonly record struct ResSample(double Cpu, double MemMiB, DateTime Ts);

/// <summary>One numeric sample for the Lines-mode per-item buffer.</summary>
internal readonly record struct LineSample(DateTime TimestampUtc, double Value);

/// <summary>Single notification snapshot for the dot-plot buffer.</summary>
internal readonly record struct DotEvent(
    NotificationKind Kind,
    int ItemId,
    uint SequenceNumber,
    DateTime TimestampUtc);
