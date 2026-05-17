/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScottPlot.Avalonia;
using UaLens.Subscriptions;

namespace UaLens.Views;

/// <summary>
/// Avalonia UserControl that hosts a ScottPlot <see cref="AvaPlot"/> and
/// switches between the new view modes (Signal / Histogram / Heatmap)
/// via pluggable <see cref="IScottPlotPump"/> implementations.  Drives
/// channel-drained <see cref="NotificationEvent"/>s into the active pump
/// and refreshes the plot at 15 fps via a <see cref="DispatcherTimer"/>.
/// </summary>
internal sealed partial class ScottPlotView : UserControl, IDisposable
{
    private AvaPlot? m_plot;
    private DispatcherTimer? m_timer;
    private CancellationTokenSource? m_readerCts;
    private Task? m_readerTask;

    private IScottPlotPump? m_pump;
    private AnimationMode m_mode = AnimationMode.Signal;
    private ChannelReader<NotificationEvent>? m_reader;
    private IReadOnlyList<MonitoredItemConfig> m_items = Array.Empty<MonitoredItemConfig>();
    private UaLens.Connection.NotificationRecorder? m_recorder;

    /// <summary>
    /// Reader task pushes events here; the UI-thread timer drains them
    /// onto the pump.  This avoids racing the plot's data buffers with
    /// the rendering path (DataStreamer/Histogram/Heatmap mutate plot
    /// state directly and aren't thread-safe).
    /// </summary>
    private readonly ConcurrentQueue<NotificationEvent> m_pending = new();

    public ScottPlotView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        m_plot = this.RequiredControl<AvaPlot>("Plot");
    }

    /// <summary>Returns true when <paramref name="mode"/> is one of the ScottPlot view modes.</summary>
    public static bool IsScottPlotMode(AnimationMode mode) =>
        mode is AnimationMode.Signal or AnimationMode.Histogram or AnimationMode.Heatmap;

    /// <summary>
    /// Bind the view to a subscription channel + item list and a chosen mode.
    /// Safe to call repeatedly; replaces the active pump and channel reader.
    /// Pass <c>null</c> reader/items to clear without immediately rebinding.
    /// If <paramref name="restoreLimits"/> is non-null, those axis limits
    /// are applied after the pump is bound (used to restore the user's
    /// previous pan/zoom on this mode).
    /// </summary>
    public void Bind(ChannelReader<NotificationEvent>? events,
                     IReadOnlyList<MonitoredItemConfig>? items,
                     AnimationMode mode,
                     ScottPlot.AxisLimits? restoreLimits = null,
                     UaLens.Connection.NotificationRecorder? recorder = null)
    {
        if (m_plot is null)
        {
            return;
        }

        StopReader();
        m_pump?.Dispose();
        m_pump = null;
        m_pending.Clear();

        m_mode = mode;
        m_reader = events;
        m_items = items ?? Array.Empty<MonitoredItemConfig>();
        m_recorder = recorder;

        if (!IsScottPlotMode(mode))
        {
            m_plot.Plot.Clear();
            m_plot.Refresh();
            return;
        }

        m_pump = mode switch
        {
            AnimationMode.Signal => new SignalPump(),
            AnimationMode.Histogram => new HistogramPump(),
            AnimationMode.Heatmap => new HeatmapPump(),
            _ => null
        };
        m_pump?.Bind(m_plot.Plot, m_plot.Refresh);
        m_pump?.OnItemsChanged(m_items);

        // Restore previously-saved axis state (if any) so switching back
        // to this mode preserves the user's pan/zoom.
        if (restoreLimits is { } lim && m_pump is not null)
        {
            m_plot.Plot.Axes.SetLimits(lim);
            m_plot.Refresh();
        }

        if (m_reader is not null)
        {
            StartReader(m_reader);
        }
    }

    /// <summary>Snapshot the current axis limits — used to save state on mode switch.</summary>
    public ScottPlot.AxisLimits? CurrentLimits => m_plot?.Plot.Axes.GetLimits();

    /// <summary>Update the live monitored-items list (call from MainWindow on Items.CollectionChanged).</summary>
    public void OnItemsChanged(IReadOnlyList<MonitoredItemConfig> items)
    {
        m_items = items;
        m_pump?.OnItemsChanged(items);
    }

    /// <summary>Forward an X-axis zoom to the active pump (no-op when no pump bound).</summary>
    public void ApplyXZoom(double factor) => m_pump?.ApplyXZoom(factor);

    /// <summary>Reset the active pump's X axis to auto-fit defaults.</summary>
    public void ResetXZoom() => m_pump?.ResetXZoom();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        m_timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(66),
                                        DispatcherPriority.Background,
                                        OnTick);
        m_timer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        m_timer?.Stop();
        StopReader();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Drain the pending queue onto the active pump on the UI thread,
        // then refresh the plot.  This serialises plot-state mutations
        // through a single thread so DataStreamer/Histogram/Heatmap can
        // ignore concurrency.
        IScottPlotPump? pump = m_pump;
        if (pump is null)
        {
            return;
        }

        int drained = 0;
        while (m_pending.TryDequeue(out NotificationEvent ev))
        {
            pump.OnEvent(ev);
            if (++drained >= 1024)
            {
                break;
            }
        }
        pump.Refresh();
    }

    private void StartReader(ChannelReader<NotificationEvent> reader)
    {
        m_readerCts = new CancellationTokenSource();
        CancellationToken ct = m_readerCts.Token;
        m_readerTask = Task.Run(async () =>
        {
            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out NotificationEvent ev))
                    {
                        m_recorder?.Record(ev);
                        m_pending.Enqueue(ev);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScottPlotView reader failed: {ex}");
            }
        }, ct);
    }

    private void StopReader()
    {
        try
        {
            m_readerCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        m_readerCts?.Dispose();
        m_readerCts = null;
        m_readerTask = null;
    }

    public void Dispose()
    {
        StopReader();
        m_pump?.Dispose();
        m_pump = null;
    }
}

