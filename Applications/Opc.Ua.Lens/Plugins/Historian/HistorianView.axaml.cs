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
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Avalonia view for the Historian tab.  Owns one <see cref="AvaPlot"/>
/// that renders the numeric subset of <see cref="HistorianPlugin.Rows"/>
/// as a line chart and refreshes whenever the bound collection changes.
/// All MVVM data flows through compiled bindings declared in the XAML.
/// </summary>
internal sealed partial class HistorianView : UserControl
{
    private AvaPlot? m_plot;
    private Scatter? m_scatter;
    private HistorianPlugin? m_vm;

    /// <summary>
    /// Snapshot of a right-click on the chart: where the cursor was
    /// (pixel + data coords) and the nearest plotted numeric row at the
    /// time the menu opened. Captured into a context menu's Tag so the
    /// click-handlers see a consistent state even if the user later
    /// moves the cursor or new rows arrive.
    /// </summary>
    private sealed record ChartClickContext(
        Pixel PixelPosition,
        DateTime DataTimestamp,
        double DataValue,
        HistoryRow? NearestNumeric);

    private static readonly Color s_text = Color.FromHex("#E2E8F0");
    private static readonly Color s_dim = Color.FromHex("#94A3B8");
    private static readonly Color s_grid = Color.FromHex("#1E293B");
    private static readonly Color s_figBg = Color.FromHex("#0F172A");
    private static readonly Color s_dataBg = Color.FromHex("#0B1220");
    private static readonly Color s_line = Color.FromHex("#0EA5E9");

    public HistorianView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        m_plot = this.FindControl<AvaPlot>("HistoryPlot");
        ConfigurePlot();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachVm();
        AttachVm(DataContext as HistorianPlugin);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachVm(DataContext as HistorianPlugin);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DetachVm();
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachVm(HistorianPlugin? vm)
    {
        if (vm is null || ReferenceEquals(vm, m_vm))
        {
            return;
        }

        m_vm = vm;
        vm.Rows.CollectionChanged += OnRowsChanged;
        RefreshPlot();
    }

    private void DetachVm()
    {
        if (m_vm is null)
        {
            return;
        }

        m_vm.Rows.CollectionChanged -= OnRowsChanged;
        m_vm = null;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshPlot, DispatcherPriority.Background);
    }

    private void ConfigurePlot()
    {
        if (m_plot is null)
        {
            return;
        }

        Plot plot = m_plot.Plot;
        plot.FigureBackground.Color = s_figBg;
        plot.DataBackground.Color = s_dataBg;
        plot.Axes.Color(s_dim);
        foreach (ScottPlot.IAxis a in plot.Axes.GetAxes())
        {
            a.MajorTickStyle.Color = s_dim;
            a.MinorTickStyle.Color = s_dim;
            a.FrameLineStyle.Color = s_grid;
            a.Label.ForeColor = s_text;
            a.TickLabelStyle.ForeColor = s_text;
        }
        plot.Grid.MajorLineColor = s_grid;
        plot.Axes.DateTimeTicksBottom();
        // Suppress ScottPlot's default right-click menu so our Avalonia
        // ContextMenu (Insert/Edit/Remove) is the only one shown.
        // The menu lives on the control wrapper (AvaPlot.Menu), not on
        // the underlying Plot object.
        try
        {
            m_plot.Menu?.Clear();
        }
        catch
        {
            // If the API surface changes, fall back gracefully — the
            // default menu remains but our handler still fires.
        }
        m_plot.PointerReleased += OnChartPointerReleased;
        m_plot.Refresh();
    }

    private void RefreshPlot()
    {
        if (m_plot is null || m_vm is null)
        {
            return;
        }

        Plot plot = m_plot.Plot;
        plot.Clear();
        m_scatter = null;

        var xs = new List<double>();
        var ys = new List<double>();
        foreach (HistoryRow r in m_vm.Rows)
        {
            if (!r.IsNumeric)
            {
                continue;
            }

            xs.Add(r.SourceTimestamp.ToOADate());
            ys.Add(r.Numeric);
        }
        if (xs.Count == 0)
        {
            m_plot.Refresh();
            return;
        }
        double[] x = xs.ToArray();
        double[] y = ys.ToArray();
        m_scatter = plot.Add.Scatter(x, y);
        m_scatter.Color = s_line;
        m_scatter.LineWidth = 1.5f;
        m_scatter.MarkerSize = 4;
        plot.Axes.AutoScale();
        m_plot.Refresh();
    }

    /// <summary>
    /// Right-click on the chart converts the pixel position to data
    /// coordinates (timestamp + value), captures an immutable
    /// <see cref="ChartClickContext"/> snapshot, and opens a context
    /// menu with Insert / Edit nearest / Remove nearest. PointerReleased
    /// fires after ScottPlot's own pan/right-click handling so we don't
    /// race with built-in input processing.
    /// </summary>
    private void OnChartPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (m_plot is null || m_vm is null)
        {
            return;
        }
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }
        Point pos = e.GetPosition(m_plot);
        var pixel = new Pixel((float)pos.X, (float)pos.Y);
        Coordinates coords;
        try
        {
            coords = m_plot.Plot.GetCoordinates(pixel);
        }
        catch
        {
            return;
        }

        DateTime ts;
        try
        {
            ts = DateTime.SpecifyKind(DateTime.FromOADate(coords.X), DateTimeKind.Utc);
        }
        catch (ArgumentException)
        {
            return;
        }
        HistoryRow? nearest = m_vm.FindNearestNumeric(ts);
        var ctx = new ChartClickContext(pixel, ts, coords.Y, nearest);

        ContextMenu menu = BuildChartContextMenu(m_vm, ctx);
        menu.PlacementTarget = m_plot;
        menu.Open(m_plot);
        e.Handled = true;
    }

    private static ContextMenu BuildChartContextMenu(HistorianPlugin vm, ChartClickContext ctx)
    {
        var menu = new ContextMenu();

        string tsLabel = ctx.DataTimestamp.ToString("u",
            System.Globalization.CultureInfo.InvariantCulture);

        var insert = new MenuItem
        {
            Header = $"✚ Insert here ({tsLabel})…"
        };
        insert.Click += async (_, _) =>
            await vm.InsertAtAsync(ctx.DataTimestamp, ctx.DataValue).ConfigureAwait(true);
        menu.Items.Add(insert);

        menu.Items.Add(new Separator());

        string nearestLabel = ctx.NearestNumeric is { } nr
            ? $" ({nr.DisplayTimestamp})"
            : string.Empty;
        var edit = new MenuItem
        {
            Header = $"✎ Edit nearest{nearestLabel}…",
            IsEnabled = ctx.NearestNumeric is not null
        };
        edit.Click += async (_, _) =>
            await vm.EditNearestAsync(ctx.DataTimestamp).ConfigureAwait(true);
        menu.Items.Add(edit);

        var remove = new MenuItem
        {
            Header = $"🗑 Remove nearest{nearestLabel}",
            IsEnabled = ctx.NearestNumeric is not null
        };
        remove.Click += async (_, _) =>
            await vm.DeleteNearestAsync(ctx.DataTimestamp).ConfigureAwait(true);
        menu.Items.Add(remove);

        return menu;
    }

    /// <summary>
    /// Right-click on a history row sets <see cref="HistorianPlugin.SelectedRow"/>
    /// to the clicked row and attaches a context menu with the per-row
    /// actions (Edit row… / Insert after… / Delete row / Edit annotation…)
    /// so the user can dispatch operations against any row without first
    /// selecting it via the keyboard or single-click.
    /// </summary>
    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not HistorianPlugin vm)
        {
            return;
        }
        if (sender is not Control row || row.DataContext is not HistoryRow item)
        {
            return;
        }
        PointerPointProperties props = e.GetCurrentPoint(row).Properties;
        if (props.IsRightButtonPressed || props.IsLeftButtonPressed)
        {
            vm.SelectedRow = item;
        }
        if (props.IsRightButtonPressed)
        {
            row.ContextMenu = BuildRowContextMenu(vm);
        }
    }

    private static ContextMenu BuildRowContextMenu(HistorianPlugin vm)
    {
        var menu = new ContextMenu();

        var editRow = new MenuItem { Header = "✎ _Edit row…" };
        editRow.Click += async (_, _) => await vm.EditSelectedAsync().ConfigureAwait(true);
        menu.Items.Add(editRow);

        var insertAfter = new MenuItem { Header = "✚ _Insert after…" };
        insertAfter.Click += async (_, _) =>
            await vm.InsertAfterSelectedAsync().ConfigureAwait(true);
        menu.Items.Add(insertAfter);

        var deleteRow = new MenuItem { Header = "🗑 _Delete row" };
        deleteRow.Click += async (_, _) => await vm.DeleteSelectedAsync().ConfigureAwait(true);
        menu.Items.Add(deleteRow);

        menu.Items.Add(new Separator());

        var editAnn = new MenuItem { Header = "Edit _annotation…" };
        editAnn.Click += async (_, _) => await vm.EditAnnotationAsync().ConfigureAwait(true);
        menu.Items.Add(editAnn);

        return menu;
    }
}
