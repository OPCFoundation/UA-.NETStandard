/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PackML.Server;

namespace Opc.Ua.PackML.Tests
{
    [TestFixture]
    [Category("PackML")]
    public sealed class PackMLAdministrationTests
    {
        private static (PackMLAdministration Admin, PackMLAdminObjectState Node, ManualTimeProvider Clock) Create()
        {
            SystemContext context = PackMLTestSupport.CreateContext();
            PackMLBaseObjectState unit =
                context.CreateInstanceOfPackMLBaseObjectType(browseName: PackMLTestSupport.Name(context));
            var clock = new ManualTimeProvider();
            PackMLAdminObjectState node = unit.Admin.Required();
            return (new PackMLAdministration(context, node, clock, 7), node, clock);
        }

        [Test]
        public async Task SparseModeNumbersUseRankTwoModeThenStateIndexing()
        {
            var (admin, node, clock) = Create();
            await admin.RecordStateAsync(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(3));
            await admin.RefreshAsync();
            PackMLAdministrationSnapshot snapshot = await admin.CaptureAsync();
            Assert.That(snapshot.ModeCurrentTime.Count, Is.EqualTo(8));
            Assert.That(snapshot.ModeCurrentTime[7], Is.EqualTo(3));
            Assert.That(snapshot.StateCumulativeTime.Dimensions, Is.EqualTo(s_stateDimensions));
            Assert.That(snapshot.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.EqualTo(3));
            Assert.That(snapshot.StateCumulativeTime.Span[(6 * 20) + (int)PackMLStateNumbers.Execute], Is.Zero);
            Assert.That(node.StateCumulativeTime.Required().ValueRank, Is.EqualTo(ValueRanks.TwoDimensions));
            Assert.That(node.ModeCumulativeTime.Required().ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public async Task FractionalSecondsSurviveRefreshAndDuplicateStateNotifications()
        {
            var (admin, node, clock) = Create();
            await admin.RecordStateAsync(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await admin.RefreshAsync();
            Assert.That((await admin.CaptureAsync()).ModeCurrentTime[7], Is.Zero);
            await admin.RecordStateAsync(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await admin.RefreshAsync();
            Assert.That((await admin.CaptureAsync()).ModeCurrentTime[7], Is.EqualTo(1));
            await admin.RecordStateAsync(7, PackMLStateNumbers.Held);
            clock.Advance(TimeSpan.FromSeconds(2));
            PackMLAdministrationSnapshot snapshot = await admin.CaptureAsync();
            Assert.That(snapshot.ModeCurrentTime[7], Is.EqualTo(3));
            Assert.That(snapshot.StateCurrentTime.Span[(7 * 20) + (int)PackMLStateNumbers.Held], Is.EqualTo(2));
            Assert.That(snapshot.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.EqualTo(1));
            Assert.That(snapshot.AccTimeSinceReset, Is.EqualTo(3));
            Assert.That(node.ModeCurrentTime.Required().Value[7], Is.EqualTo(3),
                "capturing a persistence checkpoint also refreshes the published timers");
            Assert.That(node.AccTimeSinceReset.Required().Value, Is.EqualTo(3));
        }

        [Test]
        public async Task ReenteringStateResetsCurrentButPreservesCumulativeTime()
        {
            var (admin, _, clock) = Create();
            await admin.RecordStateAsync(1, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(2));
            await admin.RecordStateAsync(1, PackMLStateNumbers.Held);
            clock.Advance(TimeSpan.FromSeconds(3));
            await admin.RecordStateAsync(1, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(1));
            PackMLAdministrationSnapshot snapshot = await admin.CaptureAsync();
            Assert.That(snapshot.StateCurrentTime.Span[20 + (int)PackMLStateNumbers.Execute], Is.EqualTo(1));
            Assert.That(snapshot.StateCumulativeTime.Span[20 + (int)PackMLStateNumbers.Execute], Is.EqualTo(3));
            Assert.That(snapshot.ModeCurrentTime[1], Is.EqualTo(6));
            await admin.RecordStateAsync(1, 0);
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.That((await admin.CaptureAsync()).ModeCumulativeTime[1], Is.EqualTo(6), "inactive nested machine");
            Assert.That((await admin.CaptureAsync()).AccTimeSinceReset, Is.EqualTo(11));
        }

        [Test]
        public async Task TimersRollAtInt32MaximumAndResetStartsNewEpoch()
        {
            var (admin, _, clock) = Create();
            await admin.RecordStateAsync(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(int.MaxValue - 1));
            PackMLAdministrationSnapshot before = await admin.CaptureAsync();
            Assert.That(before.ModeCurrentTime[7], Is.EqualTo(int.MaxValue - 1));
            Assert.That(before.AccTimeSinceReset, Is.EqualTo(int.MaxValue - 1));
            clock.Advance(TimeSpan.FromSeconds(1));
            PackMLAdministrationSnapshot at = await admin.CaptureAsync();
            Assert.That(at.ModeCurrentTime[7], Is.Zero);
            Assert.That(at.ModeCumulativeTime[7], Is.Zero);
            Assert.That(at.StateCurrentTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.Zero);
            Assert.That(at.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.Zero);
            Assert.That(at.AccTimeSinceReset, Is.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That((await admin.CaptureAsync()).AccTimeSinceReset, Is.EqualTo(1));
            await admin.ResetCountersAsync();
            PackMLAdministrationSnapshot reset = await admin.CaptureAsync();
            Assert.That(reset.ModeCumulativeTime.ToArray(), Is.All.Zero);
            Assert.That(reset.StateCumulativeTime.Span.ToArray(), Is.All.Zero);
            Assert.That(reset.AccTimeSinceReset, Is.Zero);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.That((await admin.CaptureAsync()).ModeCurrentTime[7], Is.EqualTo(2),
                "counter reset does not reset machine state");
        }

        [Test]
        public async Task RestartRestoresCumulativeTimersWithoutCountingDowntimeOrReplayingState()
        {
            var (first, _, firstClock) = Create();
            await first.RecordStateAsync(7, PackMLStateNumbers.Execute);
            firstClock.Advance(TimeSpan.FromSeconds(12));
            PackMLAdministrationSnapshot saved = await first.CaptureAsync();
            var (second, _, secondClock) = Create();
            secondClock.Advance(TimeSpan.FromDays(3));
            await second.RestoreAsync(saved);
            secondClock.Advance(TimeSpan.FromSeconds(5));
            PackMLAdministrationSnapshot restored = await second.CaptureAsync();
            Assert.That(restored.ModeCumulativeTime[7], Is.EqualTo(12));
            Assert.That(restored.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute],
                Is.EqualTo(12));
            await second.RecordStateAsync(7, PackMLStateNumbers.Stopped);
            secondClock.Advance(TimeSpan.FromSeconds(2));
            PackMLAdministrationSnapshot actual = await second.CaptureAsync();
            Assert.That(actual.ModeCurrentTime[7], Is.EqualTo(2));
            Assert.That(actual.ModeCumulativeTime[7], Is.EqualTo(14));
            Assert.That(actual.StateCurrentTime.Span[(7 * 20) + (int)PackMLStateNumbers.Stopped], Is.EqualTo(2));
            Assert.That(actual.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.EqualTo(12));
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public async Task InvalidPersistedValueDoesNotPartiallyRestore(int invalid)
        {
            var (admin, _, clock) = Create();
            await admin.RecordStateAsync(1, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(4));
            PackMLAdministrationSnapshot valid = await admin.CaptureAsync();
            int[] changed = valid.ModeCumulativeTime.Span.ToArray();
            changed[7] = invalid;
            await Assert.ThatAsync(
                async () => await admin.RestoreAsync(valid with { ModeCumulativeTime = changed.ToArrayOf() }),
                Throws.ArgumentException);
            Assert.That((await admin.CaptureAsync()).ModeCumulativeTime[1], Is.EqualTo(4));
            Assert.That((await admin.CaptureAsync()).ModeCumulativeTime[7], Is.Zero);
        }

        [Test]
        public async Task RestoreRejectsTransposedAndWrongSizedStateMatrices()
        {
            var (admin, _, _) = Create();
            PackMLAdministrationSnapshot valid = await admin.CaptureAsync();
            await Assert.ThatAsync(
                async () => await admin.RestoreAsync(valid with { StateCurrentTime = new int[20, 8] }),
                Throws.ArgumentException);
            await Assert.ThatAsync(
                async () => await admin.RestoreAsync(valid with { StateCumulativeTime = new int[8, 19] }),
                Throws.ArgumentException);
            Assert.That((await admin.CaptureAsync()).StateCurrentTime.Dimensions, Is.EqualTo(s_stateDimensions));
        }

        [Test]
        public async Task ResetClearsCountButPreservesAccCountAndProviderOwnedStructures()
        {
            var (admin, node, _) = Create();
            var count = new PackMLCountDataType { Count = 5, AccCount = 100 };
            await admin.ApplyAsync(new PackMLAdministrationData
            {
                ProcessedCounts = [count],
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTime.UtcNow
            }, new PackMLAdministrationExtents { ProcessedCounts = 1 });
            await admin.ResetCountersAsync();
            ArrayOf<PackMLCountDataType> published =
                node.ProdProcessedCount.Required().Value;
            Assert.That(published[0].Count, Is.Zero);
            Assert.That(published[0].AccCount, Is.EqualTo(100));
            Assert.That(count.Count, Is.EqualTo(5), "the equipment sample is borrowed");
            Assert.That(count.AccCount, Is.EqualTo(100));
        }

        [Test]
        public async Task AdministrationExtentsAreCapacityNotPopulationAndRejectBeforePublication()
        {
            var (admin, node, _) = Create();
            var extents = new PackMLAdministrationExtents { Alarms = 4, Warnings = 3, StopReasons = 1 };
            var sample = new PackMLAdministrationData
            {
                MachineDesignSpeed = 80,
                StatusCode = StatusCodes.UncertainDataSubNormal,
                SourceTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            await admin.ApplyAsync(sample, extents);
            Assert.That(node.AlarmExtent.Required().Value, Is.EqualTo(4));
            Assert.That(node.WarningExtent.Required().Value, Is.EqualTo(3));
            Assert.That(node.StopReasonExtent.Required().Value, Is.EqualTo(1));
            Assert.That(node.MachDesignSpeed.Required().Value, Is.EqualTo(80f));
            Assert.That(node.MachDesignSpeed.Required().StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(node.MachDesignSpeed.Required().Timestamp, Is.EqualTo(sample.SourceTimestamp));
            await Assert.ThatAsync(async () => await admin.ApplyAsync(sample with
            {
                MachineDesignSpeed = 99,
                Alarms = [new PackMLAlarmDataType()]
            }, extents with { Alarms = 0 }), Throws.ArgumentException);
            Assert.That(node.MachDesignSpeed.Required().Value, Is.EqualTo(80f),
                "whole sample validated before writing");
            Assert.That(node.AlarmExtent.Required().Value, Is.EqualTo(4));
        }

        [Test]
        public async Task NullAdministrationStructuresAreRejectedBeforeAnyFieldChanges()
        {
            var (admin, node, _) = Create();
            var sample = new PackMLAdministrationData
            {
                MachineDesignSpeed = 80,
                SourceTimestamp = DateTime.UtcNow,
                StatusCode = StatusCodes.Good
            };
            var extents = new PackMLAdministrationExtents { ProcessedCounts = 1 };
            await admin.ApplyAsync(sample, extents);
            await Assert.ThatAsync(async () => await admin.ApplyAsync(sample with
            {
                MachineDesignSpeed = 99,
                ProcessedCounts = [null!]
            }, extents), Throws.ArgumentException);
            Assert.That(node.MachDesignSpeed.Required().Value, Is.EqualTo(80f));
            Assert.That(node.ProdProcessedCount.Required().Value.Count,
                Is.Zero);
        }

        private static readonly int[] s_stateDimensions = [8, 20];
    }
}
