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
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Avalonia view for the Subscription Bench tab. Owns a single
/// <see cref="AvaPlot"/> that plots four throughput series (1s/10s/30s/60s
/// averages) on the primary Y axis and CPU / memory on a secondary
/// right-hand axis. Renders at 1 Hz against the snapshot collected by
/// the bound <see cref="SubscriptionBenchPlugin"/>.
/// </summary>
internal sealed partial class SubscriptionBenchView : UserControl
{
    private AvaPlot? m_chart;
    private DataLogger? m_dl1s;
    private DataLogger? m_dl10s;
    private DataLogger? m_dl30s;
    private DataLogger? m_dl60s;
    private DataLogger? m_dlCpu;
    private DataLogger? m_dlMem;
    private DispatcherTimer? m_timer;
    private SubscriptionBenchPlugin? m_vm;

    // Mirror the PerformanceView palette so both plug-ins look uniform
    // against the DarkNavy theme.
    private static readonly Color s_text = Color.FromHex("#E2E8F0");
    private static readonly Color s_dim = Color.FromHex("#94A3B8");
    private static readonly Color s_grid = Color.FromHex("#1E293B");
    private static readonly Color s_figBg = Color.FromHex("#0F172A");
    private static readonly Color s_dataBg = Color.FromHex("#0B1220");

    // Throughput colours: bright primary, then progressively dimmer for
    // longer windows. CPU = warm yellow, memory = green so the secondary
    // axis pair stays visually distinct from the blue throughput stack.
    private static readonly Color s_c1s = Color.FromHex("#0EA5E9");
    private static readonly Color s_c10s = Color.FromHex("#22C55E");
    private static readonly Color s_c30s = Color.FromHex("#F59E0B");
    private static readonly Color s_c60s = Color.FromHex("#A78BFA");
    private static readonly Color s_cCpu = Color.FromHex("#F87171");
    private static readonly Color s_cMem = Color.FromHex("#FBBF24");

    public SubscriptionBenchView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        m_chart = this.FindControl<AvaPlot>("Chart");
        ConfigureChart();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        m_vm = DataContext as SubscriptionBenchPlugin;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        m_timer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000),
            DispatcherPriority.Background,
            OnTick);
        m_timer.Start();
        m_vm = DataContext as SubscriptionBenchPlugin;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        m_timer?.Stop();
    }

    private void ConfigureChart()
    {
        if (m_chart is null)
        {
            return;
        }

        Plot plot = m_chart.Plot;
        ApplyDarkTheme(plot, "Subscription throughput", "seconds", "values/sec");

        ScottPlot.AxisPanels.RightAxis rightAxis = plot.Axes.AddRightAxis();
        rightAxis.Label.Text = "CPU % / Mem MB";
        rightAxis.Label.ForeColor = s_dim;
        rightAxis.Label.FontName = "Cascadia Mono";
        rightAxis.TickLabelStyle.ForeColor = s_dim;
        rightAxis.TickLabelStyle.FontName = "Cascadia Mono";

        m_dl1s = AddLogger(plot, s_c1s, 1.5f, "1s rate");
        m_dl10s = AddLogger(plot, s_c10s, 1.5f, "10s rate");
        m_dl30s = AddLogger(plot, s_c30s, 1.5f, "30s rate");
        m_dl60s = AddLogger(plot, s_c60s, 1.5f, "60s rate");
        m_dlCpu = AddLogger(plot, s_cCpu, 1.2f, "CPU %");
        m_dlMem = AddLogger(plot, s_cMem, 1.2f, "Mem MB");

        // CPU + memory go on the secondary right axis so resource usage
        // and value throughput share an X timeline but stay readable
        // even when their magnitudes differ by orders of magnitude.
        if (m_dlCpu is not null)
        {
            m_dlCpu.Axes.YAxis = rightAxis;
        }
        if (m_dlMem is not null)
        {
            m_dlMem.Axes.YAxis = rightAxis;
        }

        // Sliding 60s window applied to every logger; ViewSlide on one
        // sets the X-axis policy for the plot but we call it on each
        // logger for safety.
        m_dl1s?.ViewSlide(60);
        m_dl10s?.ViewSlide(60);
        m_dl30s?.ViewSlide(60);
        m_dl60s?.ViewSlide(60);
        m_dlCpu?.ViewSlide(60);
        m_dlMem?.ViewSlide(60);

        // Legend mirrors ScottPlotPump.ApplyDarkTheme so colours/font
        // match the rest of the app's dark-navy charts.
        plot.Legend.BackgroundColor = s_figBg;
        plot.Legend.FontColor = s_text;
        plot.Legend.FontName = "Cascadia Mono";
        plot.Legend.OutlineColor = s_dim;
        plot.ShowLegend();

        plot.Axes.Margins(0, 0.1);
        m_chart.Refresh();
    }

    private static DataLogger AddLogger(Plot plot, Color color, float lineWidth, string label)
    {
        DataLogger logger = plot.Add.DataLogger();
        logger.Color = color;
        logger.LineWidth = lineWidth;
        logger.LegendText = label;
        return logger;
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
        if (m_vm is null || m_chart is null)
        {
            return;
        }

        if (m_vm.WasReset)
        {
            m_dl1s?.Clear();
            m_dl10s?.Clear();
            m_dl30s?.Clear();
            m_dl60s?.Clear();
            m_dlCpu?.Clear();
            m_dlMem?.Clear();
            m_vm.WasReset = false;
        }

        while (m_vm.TryDequeueChartSample(out SubscriptionBenchPlugin.ChartSample sample))
        {
            m_dl1s?.Add(sample.Seconds, sample.ValuesPerSec1s);
            m_dl10s?.Add(sample.Seconds, sample.ValuesPerSec10s);
            m_dl30s?.Add(sample.Seconds, sample.ValuesPerSec30s);
            m_dl60s?.Add(sample.Seconds, sample.ValuesPerSec60s);
            if (!double.IsNaN(sample.Cpu))
            {
                m_dlCpu?.Add(sample.Seconds, sample.Cpu);
            }
            if (sample.MemMiB > 0)
            {
                m_dlMem?.Add(sample.Seconds, sample.MemMiB);
            }
        }

        m_chart.Refresh();
    }
}
