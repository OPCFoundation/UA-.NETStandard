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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies simulated bounded storage, exact inclusive selection and non-consuming copy semantics.
    /// </summary>
    [TestFixture]
    public sealed class WmtpRecordStoreTests
    {
        /// <summary>
        /// Complete reports retain every value and isolate input, output, datasets and mutable structure members.
        /// </summary>
        [Test]
        public async Task ReportsCopyEveryRecordWithoutConsumingOrAliasingAsync()
        {
            await using var work = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using var service = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            WMTPOutputDataType original = Record(4);
            await work.AppendAsync(original).ConfigureAwait(false);
            await work.AppendAsync(Record(8)).ConfigureAwait(false);
            original.ActualValue = -700;
            original.EngineeringUnits.UnitId = 12;

            ArrayOf<WMTPOutputDataType> first = await work.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false);
            Assert.That(first.Count, Is.EqualTo(2));
            Assert.That(first[0].ActualValue, Is.EqualTo(4.5));
            Assert.That(first[1].Index, Is.EqualTo(8));
            Assert.That(first[0].EngineeringUnits.UnitId, Is.EqualTo(4408652));
            first[0].ActualValue = -800;
            first[0].EURange.High = -999;
            ArrayOf<WMTPOutputDataType> second = await work.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false);
            Assert.That(second[0].ActualValue, Is.EqualTo(4.5));
            Assert.That(second[0].EURange.High, Is.EqualTo(100));
            Assert.That(await work.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await service.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Selection and deletion include both endpoints, singleton ranges, and storage-order first/last semantics.
        /// </summary>
        [TestCase(WmtpRecordSelection.Index)]
        [TestCase(WmtpRecordSelection.Time)]
        public async Task InclusiveReportsCountsAndDeletionPreserveOutsideRecordsAsync(WmtpRecordSelection selection)
        {
            await using var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            for (uint index = 1; index <= 5; index++)
            {
                await store.AppendAsync(Record(index)).ConfigureAwait(false);
            }
            var query = new WmtpRecordQuery
            {
                Selection = selection, FromIndex = 2, ToIndex = 4,
                FromTime = Record(2).Timestamp, ToTime = Record(4).Timestamp
            };
            ArrayOf<WMTPOutputDataType> selected = await store.ReadAsync(query).ConfigureAwait(false);
            Assert.That(selected.ConvertAll(record => record.Index).ToArray(), Is.EqualTo(new uint[] { 2, 3, 4 }));
            Assert.That(await store.CountAsync(query).ConfigureAwait(false), Is.EqualTo(3));
            WmtpRecordQuery singleton = query with { ToIndex = 2, ToTime = Record(2).Timestamp };
            Assert.That(await store.CountAsync(singleton).ConfigureAwait(false), Is.EqualTo(1));
            await store.DeleteAsync(query).ConfigureAwait(false);
            ArrayOf<WMTPOutputDataType> remaining = await store.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false);
            Assert.That(remaining.ConvertAll(record => record.Index).ToArray(), Is.EqualTo(new uint[] { 1, 5 }));
            Assert.That((await store.ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.First })
                .ConfigureAwait(false))[0].Index, Is.EqualTo(1));
            Assert.That((await store.ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.Last })
                .ConfigureAwait(false))[0].Index, Is.EqualTo(5));
            await store.DeleteAsync(new WmtpRecordQuery()).ConfigureAwait(false);
            Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.Zero);
            Assert.That((await store.ReadAsync(new WmtpRecordQuery { Selection = WmtpRecordSelection.First })
                .ConfigureAwait(false)).Count, Is.Zero);
        }

        /// <summary>
        /// Empty, singleton and first/last reports do not invent a measurement.
        /// </summary>
        [Test]
        public async Task EmptyAndSingletonDatasetsRetainTheirExactShapeAsync()
        {
            await using var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            Assert.That((await store.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false)).IsEmpty, Is.True);
            await store.AppendAsync(Record(0)).ConfigureAwait(false);
            foreach (WmtpRecordSelection selection in new[]
            {
                WmtpRecordSelection.All, WmtpRecordSelection.First, WmtpRecordSelection.Last
            })
            {
                var query = new WmtpRecordQuery { Selection = selection };
                Assert.That(await store.CountAsync(query).ConfigureAwait(false), Is.EqualTo(1));
                Assert.That((await store.ReadAsync(query).ConfigureAwait(false))[0].ActualValue, Is.EqualTo(0.5));
            }
        }

        /// <summary>
        /// Complete-report byte limits fail explicitly without paging, deleting, or accepting capacity overflow.
        /// </summary>
        [Test]
        public async Task EncodingAndCapacityLimitsFailWithoutDataLossAsync()
        {
            await using var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create(), 2, 256);
            WMTPOutputDataType large = Record(1);
            large.Definition = new string('D', 400);
            await store.AppendAsync(large).ConfigureAwait(false);
            await store.AppendAsync(Record(2)).ConfigureAwait(false);
            ServiceResultException limit = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false))!;
            Assert.That(limit.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            ServiceResultException capacity = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await store.AppendAsync(Record(3)).ConfigureAwait(false))!;
            Assert.That(capacity.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(2));
            ArrayOf<WMTPOutputDataType> small = await store.ReadAsync(
                new WmtpRecordQuery { Selection = WmtpRecordSelection.Index, FromIndex = 2, ToIndex = 2 })
                .ConfigureAwait(false);
            Assert.That(small.Count, Is.EqualTo(1));
            Assert.That(small[0].Index, Is.EqualTo(2));
        }

        /// <summary>
        /// Receipt-time retention is explicit and never rewrites device acquisition timestamps.
        /// </summary>
        [Test]
        public async Task RetentionUsesInjectedReceiptClockAndExpiresAtBoundaryAsync()
        {
            var clock = new WmtpTestClock();
            await using var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create(), 1,
                timeProvider: clock, retention: TimeSpan.FromSeconds(5));
            WMTPOutputDataType record = Record(1);
            await store.AppendAsync(record).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(4.999));
            Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(1));
            Assert.That((await store.ReadAsync(new WmtpRecordQuery()).ConfigureAwait(false))[0].Timestamp,
                Is.EqualTo(record.Timestamp));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.Zero);
            await store.AppendAsync(Record(2)).ConfigureAwait(false);
            Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(1));
        }

        /// <summary>
        /// Cancellation and invalid selections cannot partially delete or append data.
        /// </summary>
        [Test]
        public async Task CancellationInvalidRangesAndDisposalPreserveAtomicityAsync()
        {
            var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await using (store.ConfigureAwait(false))
            {
                await store.AppendAsync(Record(3)).ConfigureAwait(false);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await store.AppendAsync(Record(4), cancellation.Token).ConfigureAwait(false));
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await store.DeleteAsync(new WmtpRecordQuery(), cancellation.Token).ConfigureAwait(false));
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await store.ReadAsync(new WmtpRecordQuery(), cancellation.Token).ConfigureAwait(false));
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await store.CountAsync(new WmtpRecordQuery(), cancellation.Token).ConfigureAwait(false));
                foreach (WmtpRecordQuery query in new WmtpRecordQuery[]
                {
                    new() { Selection = (WmtpRecordSelection)99 },
                    new() { Selection = WmtpRecordSelection.Index, FromIndex = 4, ToIndex = 2 },
                    new() { Selection = WmtpRecordSelection.Time },
                    new()
                    {
                        Selection = WmtpRecordSelection.Time,
                        FromTime = Record(4).Timestamp, ToTime = Record(2).Timestamp
                    }
                })
                {
                    Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await store.DeleteAsync(query).ConfigureAwait(false));
                }
                Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(1));
            }
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false));
            await store.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Ancillary defaults are allowed, while absent required unit/measurement/sample identity is refused.
        /// </summary>
        [Test]
        public async Task RequiredRecordIdentityRejectsInvalidInputsButAllowsAncillaryDefaultsAsync()
        {
            await using var store = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            var defaults = new WMTPOutputDataType
            {
                EngineeringUnits = WmtpTestEquipment.Definition().EngineeringUnits,
                ActualValue = 12.5, TypeOfMeasurement = 0, TypeOfSample = 0
            };
            await store.AppendAsync(defaults).ConfigureAwait(false);
            foreach (WMTPOutputDataType invalid in new WMTPOutputDataType[]
            {
                new() { ActualValue = 12 },
                Record(1).WithInvalidValue(double.NaN),
                Record(1).WithInvalidValue(double.PositiveInfinity),
                new() { EngineeringUnits = defaults.EngineeringUnits, ActualValue = 12, TypeOfMeasurement = 8 },
                new() { EngineeringUnits = defaults.EngineeringUnits, ActualValue = 12, TypeOfSample = 7 },
                new()
                {
                    EngineeringUnits = defaults.EngineeringUnits, ActualValue = 12, Definition = new string('x', 4097)
                }
            })
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await store.AppendAsync(invalid).ConfigureAwait(false));
            }
            Assert.That(await store.CountAsync(new WmtpRecordQuery()).ConfigureAwait(false), Is.EqualTo(1));
        }

        /// <summary>
        /// Constructor boundaries are enforced before storage is allocated.
        /// </summary>
        [TestCase(0, 256)]
        [TestCase(65537, 256)]
        [TestCase(1, 255)]
        [TestCase(1, 67108865)]
        public void StoreRejectsInvalidResourceLimits(int capacity, int bytes)
        {
            Assert.That(() => new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create(), capacity, bytes),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Creates independent output structures with all required semantic identity.
        /// </summary>
        internal static WMTPOutputDataType Record(uint index)
        {
            return new WMTPOutputDataType
            {
                EngineeringUnits = WmtpTestEquipment.Definition().EngineeringUnits,
                ActualValue = index + 0.5, TypeOfMeasurement = 0, TypeOfSample = 1,
                InstrumentRange = new Range { Low = -100, High = 200 },
                EURange = new Range { Low = 0, High = 100 }, ValuePrecision = 0.125,
                Definition = "actual test acquisition", SignalTag = "sensor-a",
                Timestamp = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                Index = index, MeasurementPeriod = 50, InternalUpdateInterval = 25
            };
        }
    }

    /// <summary>
    /// Constructs negative record partitions without obscuring which field is invalid.
    /// </summary>
    internal static class WmtpInvalidRecordExtensions
    {
        /// <summary>
        /// Replaces the measurement with a deliberately invalid test value.
        /// </summary>
        internal static WMTPOutputDataType WithInvalidValue(this WMTPOutputDataType record, double value)
        {
            record.ActualValue = value;
            return record;
        }
    }
}
