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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using NUnit.Framework;
using UaLens.Connection;
using UaLens.Subscriptions;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class AnimationCanvasWorkflowTests
{
    [Test]
    public Task DrainSeparatesDataEventsKeepAliveAndNumericSamples()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Channel<NotificationEvent> channel = Channel.CreateUnbounded<NotificationEvent>();
            var recorder = new NotificationRecorder();
            var canvas = new AnimationCanvas();
            canvas.Bind(channel.Reader, new SubscriptionCounters(), recorder);
            NotificationEvent[] events =
            [
                ScottPlotPumpTests.Event(4, 11, -8) with { ValueCount = 3 },
                ScottPlotPumpTests.Event(4, 12, 21) with { ValueCount = 0 },
                ScottPlotPumpTests.Event(9, 13, null) with { Kind = NotificationKind.Event, ValueCount = 2 },
                ScottPlotPumpTests.Event(9, 14, 999) with { Kind = NotificationKind.KeepAlive, ValueCount = 7 },
                ScottPlotPumpTests.Event(81, 15, null) with { ValueCount = -4 }
            ];
            foreach (NotificationEvent notification in events)
            {
                Assert.That(channel.Writer.TryWrite(notification), Is.True);
            }
            canvas.Drain();

            Assert.That(canvas.DotSnapshot.Select(dot => dot.SequenceNumber),
                Is.EqualTo(new uint[] { 11, 11, 11, 12, 13, 13, 14, 15 }));
            Assert.That(canvas.DotSnapshot.Single(dot => dot.SequenceNumber == 14).Kind,
                Is.EqualTo(NotificationKind.KeepAlive));
            Assert.That(canvas.LineSampleCountFor(4), Is.EqualTo(2));
            Assert.That(canvas.LineRangeFor(4), Is.EqualTo((-8.0, 21.0)));
            Assert.That(canvas.LineSampleCountFor(9), Is.Zero);
            Assert.That(canvas.LineRangeFor(81), Is.Null);
            Assert.That(recorder.Snapshot().ToArray(), Is.EqualTo(events));
            Assert.That(recorder.TotalWritten, Is.EqualTo(5));
            Assert.That(channel.Reader.TryRead(out _), Is.False);
            return Task.CompletedTask;
        });
    }

    [TestCase(2047, 2047, 0)]
    [TestCase(2048, 2048, 0)]
    [TestCase(2049, 2048, 1)]
    public Task DrainHonorsPerCallBudgetAndLeavesRemainderQueued(int count, int drained, int remaining)
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Channel<NotificationEvent> channel = Channel.CreateUnbounded<NotificationEvent>();
            var recorder = new NotificationRecorder();
            var canvas = new AnimationCanvas();
            canvas.Bind(channel.Reader, null, recorder);
            for (int i = 1; i <= count; i++)
            {
                channel.Writer.TryWrite(ScottPlotPumpTests.Event(4, (uint)i, i));
            }
            canvas.Drain();
            Assert.That(canvas.DotSnapshot.Select(dot => dot.SequenceNumber),
                Is.EqualTo(Enumerable.Range(1, drained).Select(i => (uint)i)));
            Assert.That(channel.Reader.Count, Is.EqualTo(remaining));
            Assert.That(recorder.TotalWritten, Is.EqualTo(drained));
            if (remaining != 0)
            {
                Assert.That(channel.Reader.TryPeek(out NotificationEvent next), Is.True);
                Assert.That(next.SequenceNumber, Is.EqualTo(2049));
            }
            canvas.Drain();
            Assert.That(canvas.DotSnapshot.Last().SequenceNumber, Is.EqualTo((uint)count));
            Assert.That(channel.Reader.Count, Is.Zero);
            Assert.That(recorder.TotalWritten, Is.EqualTo(count));
            return Task.CompletedTask;
        });
    }

    [TestCase(4095, 4095, 4095, 1)]
    [TestCase(4096, 4096, 4096, 1)]
    [TestCase(4097, 4097, 4096, 1)]
    [TestCase(7999, 7999, 4096, 1)]
    [TestCase(8000, 8000, 4096, 1)]
    [TestCase(8001, 8000, 4096, 2)]
    public Task DotAndLineCapsDropOldestNotNewestSamples(int count, int dots, int samples, int first)
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Channel<NotificationEvent> channel = Channel.CreateUnbounded<NotificationEvent>();
            var canvas = new AnimationCanvas();
            canvas.Bind(channel.Reader, null);
            for (int i = 1; i <= count; i++)
            {
                channel.Writer.TryWrite(ScottPlotPumpTests.Event(4, (uint)i, i == 1 ? -100 : i));
            }
            while (channel.Reader.Count != 0)
            {
                canvas.Drain();
            }
            Assert.That(canvas.DotSnapshot, Has.Count.EqualTo(dots));
            Assert.That(canvas.DotSnapshot.First().SequenceNumber, Is.EqualTo((uint)first));
            Assert.That(canvas.DotSnapshot.Last().SequenceNumber, Is.EqualTo((uint)count));
            Assert.That(canvas.LineSampleCountFor(4), Is.EqualTo(samples));
            Assert.That(canvas.LineRangeFor(4), Is.EqualTo((-100.0, (double)count)));
            Assert.That(canvas.LineSampleCountFor(9), Is.Zero);
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task RebindResetsStateAndDoesNotConsumeOldReader()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Channel<NotificationEvent> oldChannel = Channel.CreateUnbounded<NotificationEvent>();
            Channel<NotificationEvent> newChannel = Channel.CreateUnbounded<NotificationEvent>();
            var oldRecorder = new NotificationRecorder();
            var newRecorder = new NotificationRecorder();
            var canvas = new AnimationCanvas();
            canvas.Bind(oldChannel.Reader, null, oldRecorder);
            oldChannel.Writer.TryWrite(ScottPlotPumpTests.Event(4, 11, 31));
            canvas.Drain();
            oldChannel.Writer.TryWrite(ScottPlotPumpTests.Event(4, 12, 80));
            newChannel.Writer.TryWrite(ScottPlotPumpTests.Event(9, 21, -5));
            canvas.Bind(newChannel.Reader, null, newRecorder);
            Assert.That(canvas.DotSnapshot, Is.Empty);
            Assert.That(canvas.LineRangeFor(4), Is.Null);
            canvas.Drain();
            Assert.That(canvas.DotSnapshot.Single().SequenceNumber, Is.EqualTo(21));
            Assert.That(canvas.LineRangeFor(9), Is.EqualTo((-5.0, -5.0)));
            Assert.That(oldChannel.Reader.TryRead(out NotificationEvent untouched), Is.True);
            Assert.That(untouched.SequenceNumber, Is.EqualTo(12));
            Assert.That(oldRecorder.TotalWritten, Is.EqualTo(1));
            Assert.That(newRecorder.Snapshot()[0].ItemId, Is.EqualTo(9));
            canvas.Bind(null, null);
            canvas.Drain();
            Assert.That(canvas.DotSnapshot, Is.Empty);
            Assert.That(canvas.LineSampleCountFor(9), Is.Zero);
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task RenderDistinguishesDataEventKeepAliveBarsAndLines()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            Channel<NotificationEvent> channel = Channel.CreateUnbounded<NotificationEvent>();
            var canvas = new AnimationCanvas
            {
                GetItems = () =>
                [
                    ScottPlotPumpTests.Item(4, "Temperature sensor in the north production area"),
                    ScottPlotPumpTests.Item(9, "Pressure")
                ],
                GetHeaderText = () => "Controlled subscription",
                GetGapMetrics = () => (3, 2, 1),
                TimeScale = 0.125
            };
            canvas.Bind(channel.Reader, null);
            DateTime now = DateTime.UtcNow;
            DateTime future = now.AddDays(1);
            channel.Writer.TryWrite(ScottPlotPumpTests.Event(4, 11, -10) with { ReceivedAtUtc = now.AddSeconds(-2) });
            channel.Writer.TryWrite(ScottPlotPumpTests.Event(4, 12, 30) with { ReceivedAtUtc = now.AddSeconds(-1) });
            channel.Writer.TryWrite(ScottPlotPumpTests.Event(9, 13, null) with
            {
                Kind = NotificationKind.Event,
                ReceivedAtUtc = future
            });
            channel.Writer.TryWrite(ScottPlotPumpTests.Event(0, 14, null) with
            {
                Kind = NotificationKind.KeepAlive,
                ReceivedAtUtc = future
            });
            channel.Writer.TryWrite(ScottPlotPumpTests.Event(81, 15, 999) with { ReceivedAtUtc = future });
            canvas.Drain();
            GeometryDrawing[] dots = Geometry(Render(canvas, 600, 400)).Where(IsDot).ToArray();
            Assert.That(dots, Has.Length.EqualTo(4));
            GeometryDrawing hollow = dots.Single(g => g.Brush is null);
            Assert.That(hollow.Pen!.Thickness, Is.EqualTo(1.5));
            Assert.That(hollow.Geometry!.Bounds.Size, Is.EqualTo(new Size(5, 5)));
            Assert.That(dots.Count(g => g.Pen is null && g.Brush is not null), Is.EqualTo(3));
            Assert.That(dots.Max(g => g.Geometry!.Bounds.Center.Y),
                Is.GreaterThan(hollow.Geometry.Bounds.Center.Y));

            canvas.Mode = AnimationMode.Bars;
            GeometryDrawing[] bars = Geometry(Render(canvas, 600, 400)).Where(g =>
                g.Geometry?.Bounds.Width == 2 && g.Brush is not null).ToArray();
            Assert.That(bars, Has.Length.EqualTo(3));
            Assert.That(bars[1].Geometry!.Bounds.Height,
                Is.EqualTo(2 * bars[0].Geometry!.Bounds.Height).Within(0.000001));
            Assert.That(bars[2].Geometry!.Bounds.Height,
                Is.EqualTo(bars[0].Geometry!.Bounds.Height).Within(0.000001));
            Assert.That(bars.Select(g => g.Geometry!.Bounds.X), Is.All.EqualTo(0d));

            canvas.Mode = AnimationMode.Lines;
            GeometryDrawing line = Geometry(Render(canvas, 600, 400)).Single(g =>
                g.Brush is null && g.Pen?.Thickness == 1.5);
            Assert.That(line.Brush, Is.Null);
            Assert.That(line.Geometry!.Bounds.Height, Is.EqualTo(344.0 / 3 * 0.8).Within(0.0001));
            Assert.That(Geometry(Render(canvas, 12, 50)).Any(IsDot), Is.False);
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task RenderOverlayUsesControlledResourceInputsAndBindClearsSamples()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var sampled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var canvas = new AnimationCanvas
            {
                ShowResourceOverlay = true,
                GetResourceSample = () =>
                {
                    calls++;
                    sampled.TrySetResult();
                    return (25, 64);
                }
            };
            DesktopInteraction.Owner.Content = canvas;
            try
            {
                await sampled.Task.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
                GeometryDrawing[] lines = Geometry(Render(canvas, 600, 400)).Where(g =>
                    g.Geometry?.Bounds.Height == 0 && g.Pen?.Thickness == 1.5).ToArray();
                Assert.That(lines, Has.Length.GreaterThanOrEqualTo(2));
                Assert.That(lines.Any(g => Math.Abs(g.Geometry!.Bounds.Top - 310) < 0.000001), Is.True);
                Assert.That(lines.Any(g => Math.Abs(g.Geometry!.Bounds.Top - 175.84) < 0.000001), Is.True);
                int detachedCalls = calls;
                canvas.ShowResourceOverlay = false;
                Assert.That(Geometry(Render(canvas, 600, 400)).Count(g => g.Pen?.Thickness == 1.5), Is.Zero);
                Assert.That(calls, Is.EqualTo(detachedCalls));
                canvas.Bind(null, null);
                canvas.ShowResourceOverlay = true;
                Assert.That(Geometry(Render(canvas, 600, 400)).Count(g => g.Pen?.Thickness == 1.5), Is.Zero);
            }
            finally
            {
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    public Task LanePointerCyclesInterpolatedWaveAndStepGeometryAndRetainsStyleAcrossBind()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var canvas = new AnimationCanvas
            {
                Mode = AnimationMode.Lines,
                TimeScale = 0.125,
                GetItems = () => [ScottPlotPumpTests.Item(4, "Temperature")]
            };
            DesktopInteraction.Owner.Content = canvas;
            DateTime now = DateTime.UtcNow;
            NotificationEvent[] events =
            [
                ScottPlotPumpTests.Event(4, 1, -10) with { ReceivedAtUtc = now.AddSeconds(-7) },
                ScottPlotPumpTests.Event(4, 2, 30) with { ReceivedAtUtc = now.AddSeconds(-4) },
                ScottPlotPumpTests.Event(4, 3, -10) with { ReceivedAtUtc = now.AddSeconds(-1) }
            ];
            Channel<NotificationEvent> channel = Channel.CreateUnbounded<NotificationEvent>();
            foreach (NotificationEvent notification in events)
            {
                channel.Writer.TryWrite(notification);
            }
            canvas.Bind(channel.Reader, null);
            canvas.Drain();
            GeometryDrawing interpolated = DataLine(canvas);
            Assert.That(interpolated.Geometry!.Bounds.Width, Is.EqualTo(45).Within(0.001));
            Assert.That(interpolated.Geometry.Bounds.Height, Is.EqualTo(137.6).Within(0.001));
            Assert.That(interpolated.Geometry.StrokeContains(interpolated.Pen!,
                new Point(interpolated.Geometry.Bounds.Left + 11.25, 138)), Is.True);
            using var pointer = new Pointer(17, PointerType.Mouse, isPrimary: true);
            ClickLane(canvas, pointer);
            GeometryDrawing wave = DataLine(canvas);
            double waveX = wave.Geometry!.Bounds.Left + 9.84375;
            Assert.That(wave.Geometry.StrokeContains(wave.Pen!, new Point(waveX, 129.4)), Is.True);
            Assert.That(wave.Geometry.StrokeContains(wave.Pen!, new Point(waveX, 146.6)), Is.False);
            ClickLane(canvas, pointer);
            GeometryDrawing step = DataLine(canvas);
            Assert.That(step.Geometry!.StrokeContains(step.Pen!,
                new Point(step.Geometry.Bounds.Left + 11.25, 206.8)), Is.True);
            Assert.That(step.Geometry.StrokeContains(step.Pen!,
                new Point(step.Geometry.Bounds.Left + 11.25, 138)), Is.False);

            Channel<NotificationEvent> replacement = Channel.CreateUnbounded<NotificationEvent>();
            foreach (NotificationEvent notification in events)
            {
                replacement.Writer.TryWrite(notification);
            }
            canvas.Bind(replacement.Reader, null);
            canvas.Drain();
            GeometryDrawing retained = DataLine(canvas);
            Assert.That(retained.Geometry!.StrokeContains(retained.Pen!,
                new Point(retained.Geometry.Bounds.Left + 11.25, 206.8)), Is.True);
            ClickLane(canvas, pointer);
            GeometryDrawing reset = DataLine(canvas);
            Assert.That(reset.Geometry!.StrokeContains(reset.Pen!,
                new Point(reset.Geometry.Bounds.Left + 11.25, 138)), Is.True);
            Assert.That(canvas.LineSampleCountFor(4), Is.EqualTo(3));
            Assert.That(canvas.LineRangeFor(4), Is.EqualTo((-10.0, 30.0)));
            return Task.CompletedTask;
        });
    }

    [Test]
    public Task CounterHeaderMovesTheChartWithoutChangingNotificationLanes()
    {
        return AvaloniaDesktopTestHost.RunAsync(() =>
        {
            var counters = new SubscriptionCounters();
            counters.IncDataMessage(3);
            counters.IncEventMessage(2);
            counters.IncKeepAlive();
            DateTime future = DateTime.UtcNow.AddDays(1);
            NotificationEvent[] events =
            [
                ScottPlotPumpTests.Event(4, 11, 7) with { ReceivedAtUtc = future },
                ScottPlotPumpTests.Event(0, 12, null) with
                {
                    Kind = NotificationKind.KeepAlive, ReceivedAtUtc = future
                }
            ];
            var plain = new AnimationCanvas { GetItems = () => [ScottPlotPumpTests.Item(4, "Temperature")] };
            var counted = new AnimationCanvas { GetItems = plain.GetItems };
            Channel<NotificationEvent> first = Channel.CreateUnbounded<NotificationEvent>();
            Channel<NotificationEvent> second = Channel.CreateUnbounded<NotificationEvent>();
            foreach (NotificationEvent notification in events)
            {
                first.Writer.TryWrite(notification);
                second.Writer.TryWrite(notification);
            }
            plain.Bind(first.Reader, null);
            counted.Bind(second.Reader, counters);
            plain.Drain();
            counted.Drain();
            double[] plainY = Geometry(Render(plain, 300, 400)).Where(IsDot)
                .Select(g => g.Geometry!.Bounds.Center.Y).Order().ToArray();
            double[] countedY = Geometry(Render(counted, 300, 400)).Where(IsDot)
                .Select(g => g.Geometry!.Bounds.Center.Y).Order().ToArray();
            Assert.That(plainY, Has.Length.EqualTo(2));
            Assert.That(countedY, Has.Length.EqualTo(2));
            Assert.That(countedY[0] - plainY[0], Is.EqualTo(9).Within(0.001));
            Assert.That(countedY[1] - plainY[1], Is.EqualTo(3).Within(0.001));
            Assert.That(counted.DotSnapshot.Select(dot => dot.SequenceNumber), Is.EqualTo(new uint[] { 11, 12 }));
            return Task.CompletedTask;
        });
    }

    private static GeometryDrawing DataLine(AnimationCanvas canvas)
    {
        return Geometry(Render(canvas, 600, 400)).Single(drawing =>
            drawing.Brush is null && drawing.Pen?.Thickness == 1.5);
    }

    private static void ClickLane(AnimationCanvas canvas, Pointer pointer)
    {
        DesktopInteraction.Owner.UpdateLayout();
        Point position = canvas.TranslatePoint(new Point(592, 138), DesktopInteraction.Owner)
            ?? throw new AssertionException("The canvas is not attached to the owned desktop.");
        var pressed = new PointerPressedEventArgs(canvas, pointer, DesktopInteraction.Owner, position, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None, 1);
        canvas.RaiseEvent(pressed);
        Assert.That(pressed.Handled, Is.True);
    }

    private static DrawingGroup Render(AnimationCanvas canvas, double width, double height)
    {
        canvas.Width = width;
        canvas.Height = height;
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        Assert.That(canvas.Bounds.Size, Is.EqualTo(new Size(width, height)));
        var group = new DrawingGroup();
        using (DrawingContext context = group.Open())
        {
            canvas.Render(context);
        }
        return group;
    }

    private static bool IsDot(GeometryDrawing drawing)
    {
        Geometry? geometry = drawing.Geometry;
        return geometry is not null && geometry.Bounds.Size == new Size(5, 5)
            && geometry.FillContains(geometry.Bounds.Center)
            && !geometry.FillContains(geometry.Bounds.TopLeft);
    }

    private static IEnumerable<GeometryDrawing> Geometry(Drawing drawing)
    {
        if (drawing is GeometryDrawing geometry)
        {
            yield return geometry;
        }
        if (drawing is DrawingGroup group)
        {
            foreach (Drawing child in group.Children)
            {
                foreach (GeometryDrawing nested in Geometry(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
