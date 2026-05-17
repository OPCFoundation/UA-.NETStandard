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
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;

namespace UaLens.Plugins.Performance;

/// <summary>
/// Avalonia view for the Performance tab.  Owns two <see cref="AvaPlot"/>
/// instances (live throughput line chart + bucket-histogram of latency),
/// renders them on a 5 Hz <see cref="DispatcherTimer"/> against the
/// snapshot collected by the bound <see cref="PerformancePlugin"/>.
/// All MVVM data flows through compiled bindings declared in the XAML.
/// </summary>
internal sealed partial class PerformanceView : UserControl
{
    private AvaPlot? m_throughputPlot;
    private AvaPlot? m_histogramPlot;
    private DataLogger? m_throughputLogger;
    private BarPlot? m_histogramBars;
    private List<Bar>? m_histogramBarList;
    private VerticalLine? m_p50Line;
    private VerticalLine? m_p95Line;
    private VerticalLine? m_p99Line;
    private DispatcherTimer? m_timer;
    private PerformancePlugin? m_vm;

    private static readonly Color s_text = Color.FromHex("#E2E8F0");
    private static readonly Color s_dim = Color.FromHex("#94A3B8");
    private static readonly Color s_grid = Color.FromHex("#1E293B");
    private static readonly Color s_figBg = Color.FromHex("#0F172A");
    private static readonly Color s_dataBg = Color.FromHex("#0B1220");
    private static readonly Color s_line = Color.FromHex("#0EA5E9");
    private static readonly Color s_bar = Color.FromHex("#22C55E");
    private static readonly Color s_p50 = Color.FromHex("#22C55E");
    private static readonly Color s_p95 = Color.FromHex("#F59E0B");
    private static readonly Color s_p99 = Color.FromHex("#EF4444");

    public PerformanceView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        m_throughputPlot = this.FindControl<AvaPlot>("ThroughputPlot");
        m_histogramPlot = this.FindControl<AvaPlot>("HistogramPlot");
        ConfigureThroughputPlot();
        ConfigureHistogramPlot();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        m_vm = DataContext as PerformancePlugin;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        m_timer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(200),
            DispatcherPriority.Background,
            OnTick);
        m_timer.Start();
        m_vm = DataContext as PerformancePlugin;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        m_timer?.Stop();
    }

    private void ConfigureThroughputPlot()
    {
        if (m_throughputPlot is null)
        {
            return;
        }

        Plot plot = m_throughputPlot.Plot;
        ApplyDarkTheme(plot, "Throughput (ops/sec)", "seconds since run start", "ops/sec");
        m_throughputLogger = plot.Add.DataLogger();
        m_throughputLogger.Color = s_line;
        m_throughputLogger.LineWidth = 1.5f;
        m_throughputLogger.ViewSlide(60);
        plot.Axes.Margins(0, 0.1);
        m_throughputPlot.Refresh();
    }

    private void ConfigureHistogramPlot()
    {
        if (m_histogramPlot is null)
        {
            return;
        }

        Plot plot = m_histogramPlot.Plot;
        ApplyDarkTheme(plot, "Latency distribution (log buckets, ms)", "latency (ms)", "count");
        // Build 80 bars at log-spaced positions.
        var bars = new List<Bar>(LatencyHistogram.BucketCount);
        for (int i = 0; i < LatencyHistogram.BucketCount; i++)
        {
            double lo = LatencyHistogram.BucketLowerMs(i);
            double hi = LatencyHistogram.BucketUpperMs(i);
            double mid = (lo + hi) / 2.0;
            bars.Add(new Bar
            {
                Position = Math.Log10(mid),
                Value = 0,
                Size = (Math.Log10(hi) - Math.Log10(lo)) * 0.9,
                FillColor = s_bar,
                LineColor = s_grid,
                LineWidth = 0
            });
        }
        m_histogramBars = plot.Add.Bars(bars);
        m_histogramBarList = bars;

        // Tick generator with log labels.
        var ticks = new List<Tick>();
        for (int decade = -3; decade <= 4; decade++)
        {
            double v = Math.Pow(10, decade);
            string label = decade switch
            {
                <= -3 => "1µs",
                -2 => "10µs",
                -1 => "100µs",
                0 => "1ms",
                1 => "10ms",
                2 => "100ms",
                3 => "1s",
                _ => "10s+"
            };
            ticks.Add(new Tick(decade, label));
        }
        plot.Axes.Bottom.TickGenerator =
            new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());

        m_p50Line = plot.Add.VerticalLine(0);
        m_p50Line.Color = s_p50;
        m_p50Line.LinePattern = LinePattern.Dashed;
        m_p50Line.LineWidth = 1.5f;
        m_p50Line.LabelText = "p50";
        m_p50Line.LabelOppositeAxis = true;

        m_p95Line = plot.Add.VerticalLine(0);
        m_p95Line.Color = s_p95;
        m_p95Line.LinePattern = LinePattern.Dashed;
        m_p95Line.LineWidth = 1.5f;
        m_p95Line.LabelText = "p95";
        m_p95Line.LabelOppositeAxis = true;

        m_p99Line = plot.Add.VerticalLine(0);
        m_p99Line.Color = s_p99;
        m_p99Line.LinePattern = LinePattern.Dashed;
        m_p99Line.LineWidth = 1.5f;
        m_p99Line.LabelText = "p99";
        m_p99Line.LabelOppositeAxis = true;

        plot.Axes.SetLimitsX(
            Math.Log10(LatencyHistogram.MinMs),
            Math.Log10(LatencyHistogram.MaxMs));
        plot.Axes.Margins(0, 0.1);
        m_histogramPlot.Refresh();
    }

    private static void ApplyDarkTheme(Plot plot, string title, string xLabel, string yLabel)
    {
        plot.FigureBackground.Color = s_figBg;
        plot.DataBackground.Color = s_dataBg;
        plot.Axes.Color(s_text);
        plot.Grid.MajorLineColor = s_grid;
        plot.Grid.MinorLineColor = s_grid;
        plot.Axes.Title.Label.Text = title;
        plot.Axes.Title.Label.ForeColor = s_text;
        plot.Axes.Title.Label.FontName = "Cascadia Mono";
        plot.Axes.Title.Label.FontSize = 12;
        plot.XLabel(xLabel);
        plot.YLabel(yLabel);
        plot.Axes.Bottom.Label.ForeColor = s_dim;
        plot.Axes.Bottom.Label.FontName = "Cascadia Mono";
        plot.Axes.Bottom.TickLabelStyle.ForeColor = s_dim;
        plot.Axes.Bottom.TickLabelStyle.FontName = "Cascadia Mono";
        plot.Axes.Left.Label.ForeColor = s_dim;
        plot.Axes.Left.Label.FontName = "Cascadia Mono";
        plot.Axes.Left.TickLabelStyle.ForeColor = s_dim;
        plot.Axes.Left.TickLabelStyle.FontName = "Cascadia Mono";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (m_vm is null)
        {
            return;
        }

        // Drain throughput samples.
        if (m_throughputLogger is not null)
        {
            while (m_vm.TryDequeueThroughput(out double seconds, out double opsPerSec))
            {
                m_throughputLogger.Add(seconds, opsPerSec);
            }
        }
        if (m_vm.WasReset)
        {
            m_throughputLogger?.Clear();
            m_vm.WasReset = false;
        }

        // Refresh histogram bars.
        if (m_histogramBars is not null && m_histogramBarList is not null)
        {
            long[] snapshot = m_vm.GetHistogramSnapshot();
            for (int i = 0; i < LatencyHistogram.BucketCount && i < m_histogramBarList.Count; i++)
            {
                m_histogramBarList[i].Value = snapshot[i];
            }
        }

        // Move percentile markers.
        if (m_p50Line is not null)
        {
            m_p50Line.X = Math.Log10(Math.Max(LatencyHistogram.MinMs, m_vm.P50Ms));
        }

        if (m_p95Line is not null)
        {
            m_p95Line.X = Math.Log10(Math.Max(LatencyHistogram.MinMs, m_vm.P95Ms));
        }

        if (m_p99Line is not null)
        {
            m_p99Line.X = Math.Log10(Math.Max(LatencyHistogram.MinMs, m_vm.P99Ms));
        }

        // Autoscale the Y axis on the histogram so the tallest bar fills.
        if (m_histogramPlot is not null && m_histogramBarList is not null && m_vm.HistogramTotal > 0)
        {
            long max = 1;
            foreach (Bar b in m_histogramBarList)
            {
                if (b.Value > max)
                {
                    max = (long)b.Value;
                }
            }
            m_histogramPlot.Plot.Axes.SetLimitsY(0, max * 1.1);
        }

        m_throughputPlot?.Refresh();
        m_histogramPlot?.Refresh();
    }
}
