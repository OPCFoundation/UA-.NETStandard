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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using SkiaSharp;
using UaLens.Plugins.Historian;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.Observe;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class HistorianViewWorkflowTests
{
    private static readonly DateTime s_time = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    [Test]
    public Task PlotContainsExactNumericTimestampValueSeriesAndTracksCollectionRemoval()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new HistorianPlugin(host);
            var view = new HistorianView { DataContext = plugin };
            DesktopInteraction.Owner.Content = view;
            try
            {
                Plot plot = DesktopInteraction.Control<AvaPlot>(view, "HistoryPlot").Plot;
                Assert.That(plot.GetPlottables(), Is.Empty);
                HistoryRow first = Row(0, new Variant(-2), StatusCodes.Good);
                HistoryRow last = Row(10, new Variant(7.5), StatusCodes.BadOutOfRange);
                plugin.Rows.Add(first);
                plugin.Rows.Add(Row(2, new Variant("not numeric"), StatusCodes.Good));
                plugin.Rows.Add(Row(4, Variant.Null, StatusCodes.Good));
                plugin.Rows.Add(Row(6, new Variant(double.PositiveInfinity), StatusCodes.Good));
                plugin.Rows.Add(last);
                await FlushAsync().ConfigureAwait(true);

                AssertSeries(plot, [-2, 7.5], [s_time.ToOADate(), s_time.AddSeconds(10).ToOADate()]);
                Scatter series = plot.GetPlottables().OfType<Scatter>().Single();
                Assert.That(series.LineWidth, Is.EqualTo(1.5));
                Assert.That(series.MarkerSize, Is.EqualTo(4));
                plugin.Rows.Remove(first);
                await FlushAsync().ConfigureAwait(true);
                AssertSeries(plot, [7.5], [s_time.AddSeconds(10).ToOADate()]);
                plugin.Rows.Clear();
                await FlushAsync().ConfigureAwait(true);
                Assert.That(plot.GetPlottables(), Is.Empty);
                Assert.That(plugin.HasAnyNumericRow, Is.False);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task RowsReboundOrDetachedCannotPublishIntoAnotherDocumentsSeries()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var first = new HistorianPlugin(host);
            await using var second = new HistorianPlugin(host);
            first.Rows.Add(Row(0, new Variant(12), StatusCodes.Good));
            second.Rows.Add(Row(10, new Variant(81), StatusCodes.Good));
            var view = new HistorianView { DataContext = first };
            DesktopInteraction.Owner.Content = view;
            try
            {
                Plot plot = DesktopInteraction.Control<AvaPlot>(view, "HistoryPlot").Plot;
                AssertSeries(plot, [12], [s_time.ToOADate()]);
                view.DataContext = second;
                first.Rows.Add(Row(1, new Variant(999), StatusCodes.Good));
                second.Rows.Add(Row(11, new Variant(82), StatusCodes.Good));
                await FlushAsync().ConfigureAwait(true);
                AssertSeries(plot, [81, 82],
                    [s_time.AddSeconds(10).ToOADate(), s_time.AddSeconds(11).ToOADate()]);
                DesktopInteraction.Owner.Content = null;
                second.Rows.Clear();
                second.Rows.Add(Row(12, new Variant(83), StatusCodes.Good));
                await FlushAsync().ConfigureAwait(true);
                AssertSeries(plot, [81, 82],
                    [s_time.AddSeconds(10).ToOADate(), s_time.AddSeconds(11).ToOADate()]);
                DesktopInteraction.Owner.Content = view;
                AssertSeries(plot, [83], [s_time.AddSeconds(12).ToOADate()]);
                second.Rows.Add(Row(13, new Variant(84), StatusCodes.Good));
                await FlushAsync().ConfigureAwait(true);
                AssertSeries(plot, [83, 84],
                    [s_time.AddSeconds(12).ToOADate(), s_time.AddSeconds(13).ToOADate()]);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [TestCase(2, 0)]
    [TestCase(8, 10)]
    public Task RightClickCapturesNearestRowAndInsertCoordinatesRatherThanCurrentSelection(
        int clickSeconds, int nearestSeconds)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new HistorianPlugin(host);
            plugin.TargetNodeId = new NodeId("Temperature", 2);
            HistoryRow first = Row(0, new Variant(2), StatusCodes.Good);
            HistoryRow last = Row(10, new Variant(9), StatusCodes.Good);
            plugin.Rows.Add(first);
            plugin.Rows.Add(Row(clickSeconds, new Variant("unplotted"), StatusCodes.Good));
            plugin.Rows.Add(last);
            plugin.SelectedRow = nearestSeconds == 0 ? last : first;
            var view = new HistorianView { DataContext = plugin };
            DesktopInteraction.Owner.Content = view;
            ContextMenu? menu = null;
            using IDisposable opened = MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((value, _) => menu = value);
            try
            {
                AvaPlot control = DesktopInteraction.Control<AvaPlot>(view, "HistoryPlot");
                DesktopInteraction.Owner.UpdateLayout();
                using SKSurface surface = SKSurface.Create(new SKImageInfo(800, 400));
                control.Plot.Render(surface.Canvas, 800, 400);
                Pixel pixel = control.Plot.GetPixel(new Coordinates(s_time.AddSeconds(clickSeconds).ToOADate(), 5));
                Point position = control.TranslatePoint(new Point(pixel.X, pixel.Y), DesktopInteraction.Owner)
                    ?? throw new AssertionException("The history plot is not attached to the owned desktop.");
                using var pointer = new Pointer(27, PointerType.Mouse, isPrimary: true);
                var pressed = new PointerPressedEventArgs(control, pointer, DesktopInteraction.Owner,
                    position, 0,
                    new PointerPointProperties(
                        RawInputModifiers.RightMouseButton,
                        PointerUpdateKind.RightButtonPressed),
                    KeyModifiers.None, 1);
                control.RaiseEvent(pressed);
                var released = new PointerReleasedEventArgs(control, pointer, DesktopInteraction.Owner,
                    position, 0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonReleased),
                    KeyModifiers.None, MouseButton.Right);
                Assert.That(released.GetPosition(control), Is.EqualTo(new Point(pixel.X, pixel.Y)));
                control.RaiseEvent(released);

                Assert.That(released.Handled, Is.True);
                Assert.That(menu, Is.Not.Null);
                MenuItem[] items = menu!.Items.OfType<MenuItem>().ToArray();
                Assert.That(items, Has.Length.EqualTo(3));
                Assert.That(items[0].Command, Is.SameAs(plugin.InsertAtCommand));
                var insert = (InsertAtArgs)items[0].CommandParameter!;
                Assert.That((insert.Timestamp - s_time.AddSeconds(clickSeconds)).TotalMilliseconds,
                    Is.Zero.Within(2));
                Assert.That(insert.Value, Is.EqualTo(5).Within(0.01));
                Assert.That(items[1].Header!.ToString(),
                    Does.Contain(nearestSeconds == 0 ? first.DisplayTimestamp : last.DisplayTimestamp));
                Assert.That(items[1].Command, Is.SameAs(plugin.EditNearestCommand));
                Assert.That(items[2].Command, Is.SameAs(plugin.DeleteNearestCommand));
                Assert.That(items[1].IsEnabled, Is.True);
                Assert.That(items[2].CommandParameter, Is.EqualTo(new NearestArgs(insert.Timestamp)));
                Assert.That(plugin.SelectedRow, Is.SameAs(nearestSeconds == 0 ? last : first));
            }
            finally
            {
                menu?.Close();
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    private static HistoryRow Row(int seconds, Variant value, StatusCode status)
    {
        return new HistoryRow(s_time.AddSeconds(seconds), s_time.AddSeconds(seconds + 1), value, status);
    }

    private static void AssertSeries(Plot plot, double[] expectedY, double[] expectedX)
    {
        Assert.That(plot.GetPlottables().ToArray(), Has.Length.EqualTo(1));
        Scatter scatter = plot.GetPlottables().OfType<Scatter>().Single();
        Coordinates[] points = scatter.Data.GetScatterPoints().ToArray();
        Assert.That(points.Select(point => point.X), Is.EqualTo(expectedX));
        Assert.That(points.Select(point => point.Y), Is.EqualTo(expectedY));
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
