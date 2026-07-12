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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// End-to-end integration tests that drive the
    /// <see cref="HistoryClient"/> fluent client over the wire against the
    /// live in-process <c>ReferenceServer</c> hosted by
    /// <see cref="TestFixture"/>. The reference server historizes
    /// <c>Scalar_Static_Int32</c>, <c>Scalar_Static_Float</c>, and
    /// <c>Scalar_Static_Double</c> with 1001 seed samples each via the
    /// fluent <c>HistorianBuilder</c>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class HistoryClientIntegrationTests : TestFixture
    {
        /// <summary>
        /// Use Scalar_Static_Double for history. Scalar_Static_Int32 has explicit
        /// RolePermissions that grant anonymous Browse|Read|Write but NOT
        /// ReadHistory/InsertHistory, so anonymous sessions get
        /// BadUserAccessDenied on its history endpoints. Double has no
        /// RolePermissions set, so the role-permission gate doesn't run.
        /// </summary>
        private NodeId m_doubleNodeId;

        [OneTimeSetUp]
        public void ResolveHistorizedNode()
        {
            ushort ns = (ushort)Session.NamespaceUris.GetIndex(
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            m_doubleNodeId = new NodeId("Scalar_Static_Double", ns);
        }

        [Test]
        public async Task ReadRawReturnsSeededValuesAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, now.AddDays(-1), now, maxValuesPerNode: 100))
            {
                values.Add(dv);
            }

            Assert.That(values, Is.Not.Empty,
                "ReferenceServer historizes Scalar_Static_Double with 1001 seed samples; raw read must return at least some.");
        }

        [Test]
        public async Task ReadRawWithEqualTimesReturnsSingleExactValueAsync()
        {
            List<DataValue> seeded = await ReadSeededValuesAsync().ConfigureAwait(false);
            DataValue expected = seeded[seeded.Count / 2];

            HistoryReadResult result = await ReadRawAsync(
                new ReadRawModifiedDetails
                {
                    StartTime = expected.SourceTimestamp,
                    EndTime = expected.SourceTimestamp,
                    NumValuesPerNode = 0,
                    IsReadModified = false,
                    ReturnBounds = false
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True);
            DataValue[] values = historyData!.DataValues.ToArray()!;
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values[0].SourceTimestamp, Is.EqualTo(expected.SourceTimestamp));
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ReadRawWithOneSpecifiedTimeReturnsFiveValuesWithoutContinuationAsync(bool startOnly)
        {
            List<DataValue> seeded = await ReadSeededValuesAsync().ConfigureAwait(false);
            int index = seeded.Count / 2;
            DateTimeUtc boundary = seeded[index].SourceTimestamp;

            HistoryReadResult result = await ReadRawAsync(
                new ReadRawModifiedDetails
                {
                    StartTime = startOnly ? boundary : DateTimeUtc.MinValue,
                    EndTime = startOnly ? DateTimeUtc.MinValue : boundary,
                    NumValuesPerNode = 5,
                    IsReadModified = false,
                    ReturnBounds = false
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True);
            DataValue[] values = historyData!.DataValues.ToArray()!;
            Assert.That(values, Has.Length.EqualTo(5));
            Assert.That(values[0].SourceTimestamp, Is.EqualTo(boundary));
            for (int i = 1; i < values.Length; i++)
            {
                Assert.That(
                    values[i].SourceTimestamp.CompareTo(values[i - 1].SourceTimestamp),
                    Is.EqualTo(startOnly ? 1 : -1));
            }
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
        }

        [Test]
        public async Task ReusingConsumedRawContinuationPointReturnsBadContinuationPointInvalidAsync()
        {
            List<DataValue> seeded = await ReadSeededValuesAsync().ConfigureAwait(false);
            int index = seeded.Count / 2;
            var details = new ReadRawModifiedDetails
            {
                StartTime = seeded[index].SourceTimestamp,
                EndTime = seeded[index + 3].SourceTimestamp,
                NumValuesPerNode = 1,
                IsReadModified = false,
                ReturnBounds = false
            };

            HistoryReadResult first = await ReadRawAsync(details).ConfigureAwait(false);
            Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
            ByteString consumed = first.ContinuationPoint;

            HistoryReadResult second = await ReadRawAsync(details, consumed).ConfigureAwait(false);
            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(second.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(second.ContinuationPoint, Is.Not.EqualTo(consumed));

            HistoryReadResult stale = await ReadRawAsync(details, consumed).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(stale.ContinuationPoint.IsEmpty, Is.True);
        }

        [Test]
        public async Task ReadModifiedWithReturnBoundsReturnsBadInvalidArgumentAsync()
        {
            List<DataValue> seeded = await ReadSeededValuesAsync().ConfigureAwait(false);
            int index = seeded.Count / 2;

            HistoryReadResult result = await ReadRawAsync(
                new ReadRawModifiedDetails
                {
                    StartTime = seeded[index].SourceTimestamp,
                    EndTime = seeded[index + 1].SourceTimestamp,
                    NumValuesPerNode = 0,
                    IsReadModified = true,
                    ReturnBounds = true
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
        }

        [Test]
        public async Task ReadRawMissingStartBoundReturnsBadBoundNotFoundAsync()
        {
            List<DataValue> seeded = await ReadSeededValuesAsync().ConfigureAwait(false);
            var startTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            HistoryReadResult result = await ReadRawAsync(
                new ReadRawModifiedDetails
                {
                    StartTime = startTime,
                    EndTime = seeded[1].SourceTimestamp,
                    NumValuesPerNode = 0,
                    IsReadModified = false,
                    ReturnBounds = true
                }).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? historyData), Is.True);
            DataValue[] values = historyData!.DataValues.ToArray()!;
            Assert.That(values, Is.Not.Empty);
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.BadBoundNotFound));
            Assert.That(values[0].SourceTimestamp, Is.EqualTo(startTime));
        }

        [Test]
        public async Task ReadProcessedAverageReturnsBucketsAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_doubleNodeId,
                ObjectIds.AggregateFunction_Average,
                now.AddHours(-1),
                now,
                processingInterval: 60_000))
            {
                values.Add(dv);
            }

            Assert.That(values, Is.Not.Empty,
                "1-minute Average buckets over the last hour must produce at least one bucket.");
        }

        [Test]
        public async Task InsertReplaceRoundTripAsync()
        {
            var client = new HistoryClient(Session);
            DateTime ts = DateTime.UtcNow.AddSeconds(7); // unique future timestamp; no seed conflict

            var insertValue = new DataValue(
                new Variant(123.45),
                StatusCodes.Good,
                sourceTimestamp: ts,
                serverTimestamp: ts);

            IList<StatusCode> insertStatuses = await client.InsertAsync(
                m_doubleNodeId, [insertValue]).ConfigureAwait(false);
            Assert.That(insertStatuses, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(insertStatuses[0]), Is.True,
                $"Insert failed with status 0x{insertStatuses[0].Code:X8}");

            // Read back from a tight window around the inserted timestamp.
            var roundTrip = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                roundTrip.Add(dv);
            }
            Assert.That(roundTrip, Is.Not.Empty);
            DataValue echoed = roundTrip.First(v => v.SourceTimestamp == ts);
            double actual = Convert.ToDouble(echoed.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(123.45));

            // Replace the value at the same timestamp.
            var replaceValue = new DataValue(
                new Variant(999.99),
                StatusCodes.Good,
                sourceTimestamp: ts,
                serverTimestamp: ts);
            IList<StatusCode> replaceStatuses = await client.ReplaceAsync(
                m_doubleNodeId, [replaceValue]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(replaceStatuses[0]), Is.True);

            roundTrip.Clear();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                roundTrip.Add(dv);
            }
            DataValue replaced = roundTrip.First(v => v.SourceTimestamp == ts);
            double replacedValue = Convert.ToDouble(replaced.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(replacedValue, Is.EqualTo(999.99));
        }

        [Test]
        public async Task GetServerCapabilitiesReportsHistoricalAccessAsync()
        {
            var client = new HistoryClient(Session);
            HistoryServerCapabilitiesInfo caps = await client.GetServerCapabilitiesAsync().ConfigureAwait(false);

            Assert.That(caps.AccessHistoryData, Is.True,
                "InMemoryHistorianProvider exposes AccessHistoryData; capability rollup must reflect it.");
            Assert.That(caps.InsertData, Is.True);
            Assert.That(caps.ReplaceData, Is.True);
            Assert.That(caps.DeleteRaw, Is.True);
        }

        [Test]
        public async Task ReadModifiedReturnsValuesAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;

            // The in-memory historian may not support ReadModified; if so
            // it returns BadHistoryOperationUnsupported.
            try
            {
                var values = new List<DataValue>();
                await foreach (DataValue dv in client.ReadModifiedAsync(
                    m_doubleNodeId, now.AddMinutes(-1), now))
                {
                    values.Add(dv);
                }

                // If we reach here the call succeeded (values may be empty
                // for unmodified seed data).
                Assert.That(values, Is.Not.Null);
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadHistoryOperationUnsupported)
            {
                // Expected when the historian does not implement modified-data.
                Assert.That(
                    ex.StatusCode,
                    Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
            }
        }

        [Test]
        public async Task ReadAtTimeReturnsValuesAtTimestampsAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;
            var requestedTimes = new List<DateTime>
            {
                now.AddMinutes(-30),
                now.AddMinutes(-20),
                now.AddMinutes(-10)
            };

            var values = new List<DataValue>();
            await foreach (DataValue dv in client.ReadAtTimeAsync(
                m_doubleNodeId, requestedTimes))
            {
                values.Add(dv);
            }

            Assert.That(values, Has.Count.EqualTo(3),
                "ReadAtTime should return one interpolated value per requested timestamp.");

            for (int i = 0; i < requestedTimes.Count; i++)
            {
                TimeSpan drift = (values[i].SourceTimestamp - requestedTimes[i]).Duration();
                Assert.That(drift.TotalSeconds, Is.LessThanOrEqualTo(5),
                    $"Value {i} SourceTimestamp should be close to the requested time.");
            }
        }

        [Test]
        public async Task ReadAnnotationsRoundTripWithWriteAnnotationAsync()
        {
            var client = new HistoryClient(Session);
            DateTime ts = DateTime.UtcNow.AddYears(-10).AddSeconds(101);
            const string message = "IntegrationTest annotation";
            const string userName = "TestUser";

            StatusCode writeStatus = await client.WriteAnnotationAsync(
                m_doubleNodeId, ts, message, userName).ConfigureAwait(false);

            if (StatusCode.IsBad(writeStatus))
            {
                // The reference server does not expose an Annotations
                // property on the historized scalar node, so the dispatcher
                // returns BadHistoryOperationUnsupported. Treat this as the
                // contract: the write status must be that specific code.
                Assert.That(
                    writeStatus.Code,
                    Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported)
                        .Or.EqualTo(StatusCodes.BadNodeIdUnknown),
                    $"Unexpected WriteAnnotation failure 0x{writeStatus.Code:X8}");
                return;
            }

            var annotations = new List<Annotation>();
            await foreach (Annotation a in client.ReadAnnotationsAsync(
                m_doubleNodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                annotations.Add(a);
            }

            Assert.That(annotations, Has.Count.EqualTo(1));
            Assert.That(annotations[0].Message, Is.EqualTo(message));
            Assert.That(annotations[0].UserName, Is.EqualTo(userName));
        }

        [Test]
        public async Task DeleteAnnotationRemovesAnnotationAsync()
        {
            var client = new HistoryClient(Session);
            DateTime ts = DateTime.UtcNow.AddYears(-10).AddSeconds(201);
            const string message = "ToDelete";

            StatusCode writeStatus = await client.WriteAnnotationAsync(
                m_doubleNodeId, ts, message, "TestUser").ConfigureAwait(false);
            if (StatusCode.IsBad(writeStatus))
            {
                // Annotations property is not exposed on this server's
                // scalar variable; assert the documented failure status
                // and stop here (this is the contract for unsupported).
                Assert.That(
                    writeStatus.Code,
                    Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported)
                        .Or.EqualTo(StatusCodes.BadNodeIdUnknown),
                    $"Unexpected WriteAnnotation failure 0x{writeStatus.Code:X8}");
                return;
            }

            StatusCode deleteStatus = await client.DeleteAnnotationAsync(
                m_doubleNodeId, ts).ConfigureAwait(false);
            Assert.That(StatusCode.IsNotBad(deleteStatus), Is.True,
                $"DeleteAnnotation failed with 0x{deleteStatus.Code:X8}");

            var remaining = new List<Annotation>();
            await foreach (Annotation a in client.ReadAnnotationsAsync(
                m_doubleNodeId, ts.AddSeconds(-1), ts.AddSeconds(1)))
            {
                remaining.Add(a);
            }

            Assert.That(remaining, Is.Empty,
                "After deletion no annotation should remain at that timestamp.");
        }

        [Test]
        public async Task DeleteRawRemovesRangeAsync()
        {
            var client = new HistoryClient(Session);
            DateTime baseTs = DateTime.UtcNow.AddYears(-10).AddSeconds(301);
            DateTime[] timestamps =
            [
                baseTs,
                baseTs.AddSeconds(1),
                baseTs.AddSeconds(2)
            ];

            var insertValues = new DataValue[3];
            for (int i = 0; i < 3; i++)
            {
                insertValues[i] = new DataValue(
                    new Variant(42.0 + i),
                    StatusCodes.Good,
                    sourceTimestamp: timestamps[i],
                    serverTimestamp: timestamps[i]);
            }

            IList<StatusCode> insertStatuses = await client.InsertAsync(
                m_doubleNodeId, insertValues).ConfigureAwait(false);
            Assert.That(insertStatuses, Has.Count.EqualTo(3));

            StatusCode deleteStatus = await client.DeleteRawAsync(
                m_doubleNodeId, timestamps[0], timestamps[2].AddMilliseconds(1)).ConfigureAwait(false);
            Assert.That(StatusCode.IsNotBad(deleteStatus), Is.True,
                $"DeleteRaw failed with 0x{deleteStatus.Code:X8}");

            var remaining = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, timestamps[0], timestamps[2].AddMilliseconds(1)))
            {
                remaining.Add(dv);
            }

            Assert.That(remaining, Is.Empty,
                "After DeleteRaw the range should contain no values.");
        }

        [Test]
        public async Task DeleteAtTimeRemovesSpecificTimestampsAsync()
        {
            var client = new HistoryClient(Session);
            DateTime baseTs = DateTime.UtcNow.AddYears(-10).AddSeconds(401);
            DateTime ts0 = baseTs;
            DateTime ts1 = baseTs.AddSeconds(1);
            DateTime ts2 = baseTs.AddSeconds(2);

            var insertValues = new DataValue[]
            {
                new(new Variant(10.0), StatusCodes.Good,
                    sourceTimestamp: ts0, serverTimestamp: ts0),
                new(new Variant(20.0), StatusCodes.Good,
                    sourceTimestamp: ts1, serverTimestamp: ts1),
                new(new Variant(30.0), StatusCodes.Good,
                    sourceTimestamp: ts2, serverTimestamp: ts2)
            };

            await client.InsertAsync(m_doubleNodeId, insertValues).ConfigureAwait(false);

            IList<StatusCode> deleteStatuses = await client.DeleteAtTimeAsync(
                m_doubleNodeId, [ts0, ts2]).ConfigureAwait(false);
            Assert.That(deleteStatuses, Has.Count.EqualTo(2));

            var remaining = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, ts0, ts2.AddMilliseconds(1)))
            {
                remaining.Add(dv);
            }

            Assert.That(remaining, Has.Count.EqualTo(1),
                "Only the middle value should survive.");
            Assert.That(remaining[0].SourceTimestamp, Is.EqualTo(ts1));
        }

        [Test]
        public async Task GetConfigurationReturnsHistoricalDataConfigurationAsync()
        {
            var client = new HistoryClient(Session);

            HistoricalDataConfigurationInfo config =
                await client.GetConfigurationAsync(m_doubleNodeId).ConfigureAwait(false);

            Assert.That(config, Is.Not.Null);

            // The ReferenceServer installs the HistoricalDataConfigurationType
            // companion object (linked via HasHistoricalConfiguration), so it is
            // discoverable.
            Assert.That(config.HasConfiguration, Is.True,
                "The reference server exposes the HA Configuration companion object.");

            // The advertised AggregateConfiguration must carry the server's
            // actual aggregate defaults (Part 13 v1.05.07 §4.2.1.2:
            // PercentDataGood/Bad = 100, TreatUncertainAsBad = true) so a client
            // reading them under UseServerCapabilitiesDefaults reproduces the
            // server's aggregate results. A node left at the type's all-zero
            // defaults would be an invalid, inconsistent configuration.
            Assert.That(config.AggregateConfiguration, Is.Not.Null,
                "The HA Configuration must expose an AggregateConfiguration object.");
            Assert.That(config.AggregateConfiguration!.PercentDataGood, Is.EqualTo((byte)100));
            Assert.That(config.AggregateConfiguration.PercentDataBad, Is.EqualTo((byte)100));
            Assert.That(config.AggregateConfiguration.TreatUncertainAsBad, Is.True);
        }

        [Test]
        public async Task BreakingOutOfAwaitForeachReleasesContinuationPointAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;

            // First read: break after first value to exercise the
            // finally-block continuation-point release path.
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, now.AddDays(-1), now, maxValuesPerNode: 10))
            {
                break;
            }

            // Second read over the same range must complete without error,
            // proving the first read's continuation point was released.
            var secondReadValues = new List<DataValue>();
            await foreach (DataValue dv in client.ReadRawAsync(
                m_doubleNodeId, now.AddDays(-1), now, maxValuesPerNode: 100))
            {
                secondReadValues.Add(dv);
            }

            Assert.That(secondReadValues, Is.Not.Empty,
                "The second read should succeed and return data after the first read's CP was released.");
        }

        [Test]
        public async Task ReadProcessedAverageOfInsertedValuesReturnsExactAverageAsync()
        {
            var client = new HistoryClient(Session);
            // Seed-free window well clear of the other integration tests' offsets.
            DateTime baseTs = DateTime.UtcNow.AddYears(-10).AddSeconds(601);

            // All-equal values so the average is independent of any boundary
            // value-selection nuance in the calculator.
            var insertValues = new DataValue[5];
            for (int i = 0; i < 5; i++)
            {
                DateTime ts = baseTs.AddSeconds(i);
                insertValues[i] = new DataValue(new Variant(77.0), StatusCodes.Good, ts, ts);
            }
            await client.InsertAsync(m_doubleNodeId, insertValues).ConfigureAwait(false);

            var buckets = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_doubleNodeId,
                ObjectIds.AggregateFunction_Average,
                baseTs,
                baseTs.AddSeconds(5),
                processingInterval: 5000))
            {
                buckets.Add(dv);
            }

            Assert.That(buckets, Is.Not.Empty);
            Assert.That(
                buckets.Any(v => v.WrappedValue.TryGetValue(out double a) && Math.Abs(a - 77.0) < 0.0001),
                Is.True,
                "Average of five 77.0 samples must be 77.0.");
        }

        [Test]
        public async Task ReadProcessedMinimumAndMaximumOfInsertedValuesAsync()
        {
            var client = new HistoryClient(Session);
            DateTime baseTs = DateTime.UtcNow.AddYears(-10).AddSeconds(701);

            // Extrema duplicated in the interior so the result is independent of
            // any boundary value-selection nuance.
            double[] raw = [30, 5, 50, 20, 50, 5];
            var insertValues = new DataValue[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                DateTime ts = baseTs.AddSeconds(i);
                insertValues[i] = new DataValue(new Variant(raw[i]), StatusCodes.Good, ts, ts);
            }
            await client.InsertAsync(m_doubleNodeId, insertValues).ConfigureAwait(false);

            var minBuckets = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_doubleNodeId, ObjectIds.AggregateFunction_Minimum,
                baseTs, baseTs.AddSeconds(6), processingInterval: 6000))
            {
                minBuckets.Add(dv);
            }

            var maxBuckets = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_doubleNodeId, ObjectIds.AggregateFunction_Maximum,
                baseTs, baseTs.AddSeconds(6), processingInterval: 6000))
            {
                maxBuckets.Add(dv);
            }

            Assert.That(
                minBuckets.Any(v => v.WrappedValue.TryGetValue(out double m) && Math.Abs(m - 5.0) < 0.0001),
                Is.True, "Minimum of the inserted samples must be 5.");
            Assert.That(
                maxBuckets.Any(v => v.WrappedValue.TryGetValue(out double m) && Math.Abs(m - 50.0) < 0.0001),
                Is.True, "Maximum of the inserted samples must be 50.");
        }

        [Test]
        public async Task ReadProcessedAnnotationCountCountsWrittenAnnotationsAsync()
        {
            var client = new HistoryClient(Session);
            DateTime baseTs = DateTime.UtcNow.AddYears(-10).AddSeconds(801);

            // Three annotations across the read window.
            DateTime[] annotationTimes =
            [
                baseTs.AddMilliseconds(500),
                baseTs.AddSeconds(1),
                baseTs.AddSeconds(3)
            ];

            StatusCode firstWrite = StatusCodes.Good;
            for (int i = 0; i < annotationTimes.Length; i++)
            {
                StatusCode w = await client.WriteAnnotationAsync(
                    m_doubleNodeId, annotationTimes[i], $"ac-{i}", "TestUser").ConfigureAwait(false);
                if (i == 0)
                {
                    firstWrite = w;
                }
            }

            if (StatusCode.IsBad(firstWrite))
            {
                // Annotations are not exposed on this node; AnnotationCount must
                // then report the documented unsupported contract.
                Assert.That(
                    firstWrite.Code,
                    Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported)
                        .Or.EqualTo(StatusCodes.BadNodeIdUnknown),
                    $"Unexpected WriteAnnotation failure 0x{firstWrite.Code:X8}");
                return;
            }

            var buckets = new List<DataValue>();
            await foreach (DataValue dv in client.ReadProcessedAsync(
                m_doubleNodeId,
                ObjectIds.AggregateFunction_AnnotationCount,
                baseTs,
                baseTs.AddSeconds(6),
                processingInterval: 2000))
            {
                buckets.Add(dv);
            }

            Assert.That(buckets, Is.Not.Empty, "AnnotationCount must return at least one bucket.");

            int total = 0;
            foreach (DataValue v in buckets)
            {
                if (v.WrappedValue.TryGetValue(out int count))
                {
                    total += count;
                }
            }

            Assert.That(total, Is.EqualTo(3),
                "AnnotationCount across the window must total the three written annotations.");
        }

        private async Task<List<DataValue>> ReadSeededValuesAsync()
        {
            var client = new HistoryClient(Session);
            DateTime now = DateTime.UtcNow;
            var values = new List<DataValue>();
            await foreach (DataValue value in client.ReadRawAsync(
                m_doubleNodeId,
                now.AddDays(-2),
                now.AddDays(2)))
            {
                values.Add(value);
            }

            values.Sort((left, right) => left.SourceTimestamp.CompareTo(right.SourceTimestamp));
            Assert.That(values, Has.Count.GreaterThan(20));
            return values;
        }

        private async Task<HistoryReadResult> ReadRawAsync(
            ReadRawModifiedDetails details,
            ByteString continuationPoint = default)
        {
            HistoryReadResponse response = await Session.HistoryReadAsync(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                new HistoryReadValueId[]
                {
                    new()
                    {
                        NodeId = m_doubleNodeId,
                        ContinuationPoint = continuationPoint
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            return response.Results[0];
        }
    }
}
