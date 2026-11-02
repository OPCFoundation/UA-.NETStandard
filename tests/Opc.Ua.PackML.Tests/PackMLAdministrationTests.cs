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
        public void SparseModeNumbersUseRankTwoModeThenStateIndexing()
        {
            var (admin, node, clock) = Create();
            admin.RecordState(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(3));
            admin.Refresh();
            PackMLAdministrationSnapshot snapshot = admin.Capture();
            Assert.That(snapshot.ModeCurrentTime.Count, Is.EqualTo(8));
            Assert.That(snapshot.ModeCurrentTime[7], Is.EqualTo(3));
            Assert.That(snapshot.StateCumulativeTime.Dimensions, Is.EqualTo(new[] { 8, 20 }));
            Assert.That(snapshot.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.EqualTo(3));
            Assert.That(snapshot.StateCumulativeTime.Span[(6 * 20) + (int)PackMLStateNumbers.Execute], Is.Zero);
            Assert.That(node.StateCumulativeTime.Required().ValueRank, Is.EqualTo(ValueRanks.TwoDimensions));
            Assert.That(node.ModeCumulativeTime.Required().ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void FractionalSecondsSurviveRefreshAndDuplicateStateNotifications()
        {
            var (admin, node, clock) = Create();
            admin.RecordState(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            admin.Refresh();
            Assert.That(admin.Capture().ModeCurrentTime[7], Is.Zero);
            admin.RecordState(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromMilliseconds(600));
            admin.Refresh();
            Assert.That(admin.Capture().ModeCurrentTime[7], Is.EqualTo(1));
            admin.RecordState(7, PackMLStateNumbers.Held);
            clock.Advance(TimeSpan.FromSeconds(2));
            PackMLAdministrationSnapshot snapshot = admin.Capture();
            Assert.That(snapshot.ModeCurrentTime[7], Is.EqualTo(3));
            Assert.That(snapshot.StateCurrentTime.Span[(7 * 20) + (int)PackMLStateNumbers.Held], Is.EqualTo(2));
            Assert.That(snapshot.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.EqualTo(1));
            Assert.That(snapshot.AccTimeSinceReset, Is.EqualTo(3));
            Assert.That(node.ModeCurrentTime.Required().Value[7], Is.EqualTo(3),
                "capturing a persistence checkpoint also refreshes the published timers");
            Assert.That(node.AccTimeSinceReset.Required().Value, Is.EqualTo(3));
        }

        [Test]
        public void ReenteringStateResetsCurrentButPreservesCumulativeTime()
        {
            var (admin, _, clock) = Create();
            admin.RecordState(1, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(2));
            admin.RecordState(1, PackMLStateNumbers.Held);
            clock.Advance(TimeSpan.FromSeconds(3));
            admin.RecordState(1, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(1));
            PackMLAdministrationSnapshot snapshot = admin.Capture();
            Assert.That(snapshot.StateCurrentTime.Span[20 + (int)PackMLStateNumbers.Execute], Is.EqualTo(1));
            Assert.That(snapshot.StateCumulativeTime.Span[20 + (int)PackMLStateNumbers.Execute], Is.EqualTo(3));
            Assert.That(snapshot.ModeCurrentTime[1], Is.EqualTo(6));
            admin.RecordState(1, 0);
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.That(admin.Capture().ModeCumulativeTime[1], Is.EqualTo(6), "inactive nested machine");
            Assert.That(admin.Capture().AccTimeSinceReset, Is.EqualTo(11));
        }

        [Test]
        public void TimersRollAtInt32MaximumAndResetStartsNewEpoch()
        {
            var (admin, _, clock) = Create();
            admin.RecordState(7, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(int.MaxValue - 1));
            PackMLAdministrationSnapshot before = admin.Capture();
            Assert.That(before.ModeCurrentTime[7], Is.EqualTo(int.MaxValue - 1));
            Assert.That(before.AccTimeSinceReset, Is.EqualTo(int.MaxValue - 1));
            clock.Advance(TimeSpan.FromSeconds(1));
            PackMLAdministrationSnapshot at = admin.Capture();
            Assert.That(at.ModeCurrentTime[7], Is.Zero);
            Assert.That(at.ModeCumulativeTime[7], Is.Zero);
            Assert.That(at.StateCurrentTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.Zero);
            Assert.That(at.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.Zero);
            Assert.That(at.AccTimeSinceReset, Is.Zero);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(admin.Capture().AccTimeSinceReset, Is.EqualTo(1));
            admin.ResetCounters();
            PackMLAdministrationSnapshot reset = admin.Capture();
            Assert.That(reset.ModeCumulativeTime.ToArray(), Is.All.Zero);
            Assert.That(reset.StateCumulativeTime.Span.ToArray(), Is.All.Zero);
            Assert.That(reset.AccTimeSinceReset, Is.Zero);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.That(admin.Capture().ModeCurrentTime[7], Is.EqualTo(2),
                "counter reset does not reset machine state");
        }

        [Test]
        public void RestartRestoresCumulativeTimersWithoutCountingDowntimeOrReplayingState()
        {
            var (first, _, firstClock) = Create();
            first.RecordState(7, PackMLStateNumbers.Execute);
            firstClock.Advance(TimeSpan.FromSeconds(12));
            PackMLAdministrationSnapshot saved = first.Capture();
            var (second, _, secondClock) = Create();
            secondClock.Advance(TimeSpan.FromDays(3));
            second.Restore(saved);
            secondClock.Advance(TimeSpan.FromSeconds(5));
            Assert.That(second.Capture().ModeCumulativeTime[7], Is.EqualTo(12));
            Assert.That(second.Capture().StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute],
                Is.EqualTo(12));
            second.RecordState(7, PackMLStateNumbers.Stopped);
            secondClock.Advance(TimeSpan.FromSeconds(2));
            PackMLAdministrationSnapshot actual = second.Capture();
            Assert.That(actual.ModeCurrentTime[7], Is.EqualTo(2));
            Assert.That(actual.ModeCumulativeTime[7], Is.EqualTo(14));
            Assert.That(actual.StateCurrentTime.Span[(7 * 20) + (int)PackMLStateNumbers.Stopped], Is.EqualTo(2));
            Assert.That(actual.StateCumulativeTime.Span[(7 * 20) + (int)PackMLStateNumbers.Execute], Is.EqualTo(12));
        }

        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void InvalidPersistedValueDoesNotPartiallyRestore(int invalid)
        {
            var (admin, _, clock) = Create();
            admin.RecordState(1, PackMLStateNumbers.Execute);
            clock.Advance(TimeSpan.FromSeconds(4));
            PackMLAdministrationSnapshot valid = admin.Capture();
            int[] changed = valid.ModeCumulativeTime.Span.ToArray();
            changed[7] = invalid;
            Assert.That(() => admin.Restore(valid with { ModeCumulativeTime = changed.ToArrayOf() }),
                Throws.ArgumentException);
            Assert.That(admin.Capture().ModeCumulativeTime[1], Is.EqualTo(4));
            Assert.That(admin.Capture().ModeCumulativeTime[7], Is.Zero);
        }

        [Test]
        public void RestoreRejectsTransposedAndWrongSizedStateMatrices()
        {
            var (admin, _, _) = Create();
            PackMLAdministrationSnapshot valid = admin.Capture();
            Assert.That(() => admin.Restore(valid with { StateCurrentTime = new int[20, 8] }),
                Throws.ArgumentException);
            Assert.That(() => admin.Restore(valid with { StateCumulativeTime = new int[8, 19] }),
                Throws.ArgumentException);
            Assert.That(admin.Capture().StateCurrentTime.Dimensions, Is.EqualTo(new[] { 8, 20 }));
        }

        [Test]
        public void ResetClearsCountButPreservesAccCountAndProviderOwnedStructures()
        {
            var (admin, node, _) = Create();
            var count = new PackMLCountDataType { Count = 5, AccCount = 100 };
            admin.Apply(new PackMLAdministrationData
            {
                ProcessedCounts = [count],
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTime.UtcNow
            }, new PackMLAdministrationExtents { ProcessedCounts = 1 });
            admin.ResetCounters();
            ArrayOf<PackMLCountDataType> published =
                node.ProdProcessedCount.Required().Value.GetStructureArray<PackMLCountDataType>();
            Assert.That(published[0].Count, Is.Zero);
            Assert.That(published[0].AccCount, Is.EqualTo(100));
            Assert.That(count.Count, Is.EqualTo(5), "the equipment sample is borrowed");
            Assert.That(count.AccCount, Is.EqualTo(100));
        }

        [Test]
        public void AdministrationExtentsAreCapacityNotPopulationAndRejectBeforePublication()
        {
            var (admin, node, _) = Create();
            var extents = new PackMLAdministrationExtents { Alarms = 4, Warnings = 3, StopReasons = 1 };
            var sample = new PackMLAdministrationData
            {
                MachineDesignSpeed = 80,
                StatusCode = StatusCodes.UncertainDataSubNormal,
                SourceTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            admin.Apply(sample, extents);
            Assert.That(node.AlarmExtent.Required().Value, Is.EqualTo(4));
            Assert.That(node.WarningExtent.Required().Value, Is.EqualTo(3));
            Assert.That(node.StopReasonExtent.Required().Value, Is.EqualTo(1));
            Assert.That(node.MachDesignSpeed.Required().Value, Is.EqualTo(80f));
            Assert.That(node.MachDesignSpeed.Required().StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(node.MachDesignSpeed.Required().Timestamp, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(() => admin.Apply(sample with
            {
                MachineDesignSpeed = 99,
                Alarms = [new PackMLAlarmDataType()]
            }, extents with { Alarms = 0 }), Throws.ArgumentException);
            Assert.That(node.MachDesignSpeed.Required().Value, Is.EqualTo(80f),
                "whole sample validated before writing");
            Assert.That(node.AlarmExtent.Required().Value, Is.EqualTo(4));
        }

        [Test]
        public void NullAdministrationStructuresAreRejectedBeforeAnyFieldChanges()
        {
            var (admin, node, _) = Create();
            var sample = new PackMLAdministrationData
            {
                MachineDesignSpeed = 80,
                SourceTimestamp = DateTime.UtcNow,
                StatusCode = StatusCodes.Good
            };
            var extents = new PackMLAdministrationExtents { ProcessedCounts = 1 };
            admin.Apply(sample, extents);
            Assert.That(() => admin.Apply(sample with
            {
                MachineDesignSpeed = 99,
                ProcessedCounts = [null!]
            }, extents), Throws.ArgumentException);
            Assert.That(node.MachDesignSpeed.Required().Value, Is.EqualTo(80f));
            Assert.That(node.ProdProcessedCount.Required().Value.GetStructureArray<PackMLCountDataType>().Count,
                Is.Zero);
        }
    }
}
