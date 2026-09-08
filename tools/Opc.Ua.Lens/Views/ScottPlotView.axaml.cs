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
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScottPlot.Avalonia;
using UaLens.Subscriptions;

namespace UaLens.Views
{
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

        private IScottPlotPump? m_pump;
        private ChannelReader<NotificationEvent>? m_reader;
        private IReadOnlyList<MonitoredItemConfig> m_items = [];
        private Connection.NotificationRecorder? m_recorder;

        public ScottPlotView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            m_plot = this.RequiredControl<AvaPlot>("Plot");
        }

        /// <summary>
        /// Returns true when <paramref name="mode"/> is one of the ScottPlot view modes.
        /// </summary>
        public static bool IsScottPlotMode(AnimationMode mode)
        {
            return mode is AnimationMode.Signal or AnimationMode.Histogram or AnimationMode.Heatmap;
        }

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
                         Connection.NotificationRecorder? recorder = null)
        {
            if (m_plot is null)
            {
                return;
            }

            m_pump?.Dispose();
            m_pump = null;

            m_reader = events;
            m_items = items ?? [];
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
            ApplyPalette();

            // Each pump calls ShowLegend()/Axes setup on Bind — re-apply
            // the user's chart-element visibility right after so the user's
            // toggle isn't blown away on mode switch.
            ApplyChartElementsVisibility();

            // Restore previously-saved axis state (if any) so switching back
            // to this mode preserves the user's pan/zoom.
            if (restoreLimits is { } lim && m_pump is not null)
            {
                m_plot.Plot.Axes.SetLimits(lim);
                m_plot.Refresh();
            }
        }

        /// <summary>
        /// Snapshot the current axis limits — used to save state on mode switch.
        /// </summary>
        public ScottPlot.AxisLimits? CurrentLimits => m_plot?.Plot.Axes.GetLimits();

        /// <summary>
        /// Updates the live monitored-items list when the document's collection changes.
        /// </summary>
        public void OnItemsChanged(IReadOnlyList<MonitoredItemConfig> items)
        {
            m_items = items;
            m_pump?.OnItemsChanged(items);
        }

        /// <summary>
        /// Forward an X-axis zoom to the active pump (no-op when no pump bound).
        /// </summary>
        public void ApplyXZoom(double factor)
        {
            m_pump?.ApplyXZoom(factor);
        }

        /// <summary>
        /// Reset the active pump's X axis to auto-fit defaults.
        /// </summary>
        public void ResetXZoom()
        {
            m_pump?.ResetXZoom();
        }

        /// <summary>
        /// Toggle chart-element visibility (legend / X axis / Y axis).
        /// Safe to call before a pump is bound — the values are remembered
        /// and re-applied on the next <see cref="Bind"/>.
        /// </summary>
        public void SetChartElementsVisible(bool legend, bool xAxis, bool yAxis)
        {
            m_showLegend = legend;
            m_showXAxis = xAxis;
            m_showYAxis = yAxis;
            ApplyChartElementsVisibility();
        }

        private bool m_showLegend = true;
        private bool m_showXAxis = true;
        private bool m_showYAxis = true;

        private void ApplyChartElementsVisibility()
        {
            if (m_plot is null)
            {
                return;
            }
            if (m_showLegend)
            {
                m_plot.Plot.ShowLegend();
            }
            else
            {
                m_plot.Plot.HideLegend();
            }
            m_plot.Plot.Axes.Bottom.IsVisible = m_showXAxis;
            m_plot.Plot.Axes.Left.IsVisible = m_showYAxis;
            m_plot.Refresh();
        }

        protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            m_timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(66),
                                            DispatcherPriority.Background,
                                            OnTick);
            m_timer.Start();
            Themes.ThemeManager.ThemeChanged += ApplyPalette;
            ApplyPalette();
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            m_timer?.Stop();
            Themes.ThemeManager.ThemeChanged -= ApplyPalette;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            IScottPlotPump? pump = m_pump;
            if (pump is null)
            {
                return;
            }

            int drained = 0;
            while (m_reader is not null && m_reader.TryRead(out NotificationEvent ev))
            {
                m_recorder?.Record(in ev);
                pump.OnEvent(ev);
                if (++drained >= 1024)
                {
                    break;
                }
            }
            pump.Refresh();
        }

        public void Dispose()
        {
            m_timer?.Stop();
            m_reader = null;
            Themes.ThemeManager.ThemeChanged -= ApplyPalette;
            m_pump?.Dispose();
            m_pump = null;
        }

        private void ApplyPalette()
        {
            if (m_plot is null)
            {
                return;
            }
            Themes.ChartTheme.Apply(m_plot.Plot);
            m_plot.Refresh();
        }
    }
}
