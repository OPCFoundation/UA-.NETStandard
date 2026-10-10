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
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies bounded aggregate retention arithmetic in synchronous and asynchronous node-manager filter revision.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class AggregateFilterRevisionRegressionTests
    {
        /// <summary>
        /// Verifies that extreme queue sizes and intervals revise retention consistently without overflowing start
        /// times.
        /// </summary>
        [TestCase(0u, 1000.0, 0.0)]
        [TestCase(1u, 1000.0, 0.0)]
        [TestCase(2u, 1000.0, 1000.0)]
        [TestCase(2u, 0.0, 1000.0)]
        [TestCase(2u, -1.0, 1000.0)]
        [TestCase(uint.MaxValue, 1000.0, 4294967294000.0)]
        [TestCase(uint.MaxValue, double.MaxValue, -1.0)]
        [TestCase(2u, double.NaN, 1000.0)]
        [TestCase(2u, double.PositiveInfinity, 1000.0)]
        [TestCase(2u, double.NegativeInfinity, 1000.0)]
        public async Task AggregateRetentionRevisionBoundsClientArithmeticAsync(
            uint queueSize,
            double interval,
            double retainedMilliseconds)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, time);
            using (queues)
            using (var synchronous = new SyncHooks(server.Object))
            using (var asynchronous = new AsyncHooks(server.Object))
            {
                using var aggregates = new AggregateManager(server.Object);
                server.SetupGet(value => value.AggregateManager).Returns(aggregates);
                var syncFilter = new ServerAggregateFilter
                {
                    ProcessingInterval = interval,
                    StartTime = DateTimeUtc.MinValue,
                    AggregateConfiguration = new AggregateConfiguration()
                };
                var asyncFilter = new ServerAggregateFilter
                {
                    ProcessingInterval = interval,
                    StartTime = DateTimeUtc.MinValue,
                    AggregateConfiguration = new AggregateConfiguration()
                };
                DateTimeUtc expectedStart = retainedMilliseconds < 0
                    ? DateTimeUtc.MinValue
                    : ((DateTimeUtc)time.GetUtcNow().UtcDateTime).SubtractMilliseconds(retainedMilliseconds);

                StatusCode asyncStatus = await asynchronous.ReviseAsync(queueSize, asyncFilter).ConfigureAwait(false);
                Assert.That(asyncStatus, Is.EqualTo(StatusCodes.Good));
                Assert.That(asyncFilter.StartTime, Is.EqualTo(expectedStart));

                StatusCode syncStatus = synchronous.Revise(queueSize, syncFilter);
                Assert.That(syncStatus, Is.EqualTo(StatusCodes.Good));
                Assert.That(syncFilter.StartTime, Is.EqualTo(expectedStart));
                Assert.That(syncFilter.ProcessingInterval, Is.EqualTo(asyncFilter.ProcessingInterval));
            }
        }

        /// <summary>
        /// Verifies that a NaN, infinite, non-positive or sub-tick processing interval is revised to a finite,
        /// positive interval (Part 4 §5.13.2.1, §7.22.4) and that an aggregate calculator built from the revised
        /// filter terminates (M7-9: a NaN interval made every slice zero-width and the calculator loop forever).
        /// </summary>
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(0.0)]
        [TestCase(-5.0)]
        [TestCase(1e-6)]
        public async Task InvalidProcessingIntervalIsRevisedToUsableIntervalAsync(double interval)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, time);
            using (queues)
            using (var synchronous = new SyncHooks(server.Object))
            using (var asynchronous = new AsyncHooks(server.Object))
            {
                using var aggregates = new AggregateManager(server.Object) { MinimumProcessingInterval = 0 };
                server.SetupGet(value => value.AggregateManager).Returns(aggregates);
                var syncFilter = new ServerAggregateFilter
                {
                    ProcessingInterval = interval,
                    AggregateConfiguration = new AggregateConfiguration()
                };
                var asyncFilter = new ServerAggregateFilter
                {
                    ProcessingInterval = interval,
                    AggregateConfiguration = new AggregateConfiguration()
                };

                Assert.That(synchronous.Revise(1, syncFilter), Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    await asynchronous.ReviseAsync(1, asyncFilter).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));

                foreach (ServerAggregateFilter revised in new[] { syncFilter, asyncFilter })
                {
                    Assert.That(revised.ProcessingInterval.IsFinite(), Is.True);
                    Assert.That(
                        revised.ProcessingInterval * TimeSpan.TicksPerMillisecond,
                        Is.GreaterThanOrEqualTo(1));
                    Assert.That(
                        CountProcessedValues(revised.StartTime, revised.ProcessingInterval),
                        Is.LessThan(kProcessedValueCap));
                }
            }
        }

        /// <summary>
        /// Verifies that the aggregate calculator rejects a processing interval that cannot advance its slices
        /// instead of producing processed values forever (M7-9 defensive guard).
        /// </summary>
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-1.0)]
        [TestCase(1e-6)]
        public void AggregateCalculatorRejectsUnusableProcessingInterval(double interval)
        {
            Assert.That(
                () => CreateAverageCalculator(new DateTimeUtc(DateTime.UtcNow), interval),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Verifies that the revised processing interval is at least twice the revised sampling interval
        /// (Part 4 §7.22.4 AggregateFilterResult, M5-4) in both node-manager implementations.
        /// </summary>
        [TestCase(1500.0, 2000.0, 4000.0)]
        [TestCase(5000.0, 2000.0, 5000.0)]
        [TestCase(100.0, 0.0, 1000.0)]
        public async Task ProcessingIntervalIsAtLeastTwiceSamplingIntervalAsync(
            double requested,
            double samplingInterval,
            double expected)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, time);
            using (queues)
            using (var synchronous = new SyncHooks(server.Object))
            using (var asynchronous = new AsyncHooks(server.Object))
            {
                using var aggregates = new AggregateManager(server.Object);
                server.SetupGet(value => value.AggregateManager).Returns(aggregates);
                var syncFilter = new ServerAggregateFilter
                {
                    ProcessingInterval = requested,
                    AggregateConfiguration = new AggregateConfiguration()
                };
                var asyncFilter = new ServerAggregateFilter
                {
                    ProcessingInterval = requested,
                    AggregateConfiguration = new AggregateConfiguration()
                };

                Assert.That(synchronous.Revise(1, syncFilter, samplingInterval), Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    await asynchronous.ReviseAsync(1, asyncFilter, samplingInterval).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(syncFilter.ProcessingInterval, Is.EqualTo(expected));
                Assert.That(asyncFilter.ProcessingInterval, Is.EqualTo(expected));
            }
        }

        /// <summary>
        /// Verifies that a past start time is advanced by whole processing intervals so the revised start time
        /// stays on the client's boundary (startTime + revisedProcessingInterval * n, Part 4 §7.22.4, M5-3).
        /// </summary>
        [TestCase(1u, "2024-01-01T10:17:00.000Z")]
        [TestCase(3u, "2024-01-01T10:15:00.000Z")]
        [TestCase(1000u, "2024-01-01T08:00:00.000Z")]
        public async Task RevisedStartTimeStaysOnRequestedBoundaryAsync(uint queueSize, string expected)
        {
            var time = new FakeTimeProvider(new DateTimeOffset(2024, 1, 1, 10, 17, 23, 456, TimeSpan.Zero));
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queues, time);
            using (queues)
            using (var synchronous = new SyncHooks(server.Object))
            using (var asynchronous = new AsyncHooks(server.Object))
            {
                using var aggregates = new AggregateManager(server.Object);
                server.SetupGet(value => value.AggregateManager).Returns(aggregates);
                var requestedStart = new DateTimeUtc(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc));
                var syncFilter = new ServerAggregateFilter
                {
                    StartTime = requestedStart,
                    ProcessingInterval = 60000,
                    AggregateConfiguration = new AggregateConfiguration()
                };
                var asyncFilter = new ServerAggregateFilter
                {
                    StartTime = requestedStart,
                    ProcessingInterval = 60000,
                    AggregateConfiguration = new AggregateConfiguration()
                };

                Assert.That(synchronous.Revise(queueSize, syncFilter), Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    await asynchronous.ReviseAsync(queueSize, asyncFilter).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));

                var expectedStart = new DateTimeUtc(DateTime.Parse(
                    expected,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
                Assert.That(syncFilter.StartTime, Is.EqualTo(expectedStart));
                Assert.That(asyncFilter.StartTime, Is.EqualTo(expectedStart));
            }
        }

        private const int kProcessedValueCap = 1000;

        /// <summary>
        /// Feeds three raw samples one second apart and counts the processed values, capped to detect a
        /// calculator that never stops producing values.
        /// </summary>
        private static int CountProcessedValues(DateTimeUtc startTime, double processingInterval)
        {
            AverageAggregateCalculator calculator = CreateAverageCalculator(startTime, processingInterval);
            for (int ii = 0; ii < 3; ii++)
            {
                calculator.QueueRawValue(new DataValue(
                    new Variant((double)ii),
                    StatusCodes.Good,
                    startTime.AddMilliseconds(ii * 1000)));
            }

            int count = 0;
            while (count < kProcessedValueCap && calculator.TryGetProcessedValue(false, out _))
            {
                count++;
            }

            return count;
        }

        private static AverageAggregateCalculator CreateAverageCalculator(
            DateTimeUtc startTime,
            double processingInterval)
        {
            return new AverageAggregateCalculator(
                ObjectIds.AggregateFunction_Average,
                startTime,
                DateTimeUtc.MaxValue,
                processingInterval,
                false,
                new AggregateConfiguration
                {
                    TreatUncertainAsBad = true,
                    PercentDataBad = 100,
                    PercentDataGood = 100,
                    UseSlopedExtrapolation = false
                },
                NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Exposes synchronous aggregate-filter revision for comparison with the asynchronous implementation.
        /// </summary>
        private sealed class SyncHooks : CustomNodeManager2
        {
            /// <summary>
            /// Creates a synchronous node manager using the deterministic server's aggregate services and clock.
            /// </summary>
            public SyncHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:sync-aggregate-revision")
            {
            }

            /// <summary>
            /// Revises aggregate retention for the supplied queue size and mutable filter.
            /// </summary>
            public StatusCode Revise(uint queueSize, ServerAggregateFilter filter, double samplingInterval = 0)
            {
                return ReviseAggregateFilter(SystemContext, CreateHandle(), samplingInterval, queueSize, filter);
            }
        }

        /// <summary>
        /// Exposes asynchronous aggregate-filter revision for comparison with the synchronous implementation.
        /// </summary>
        private sealed class AsyncHooks : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates an asynchronous node manager using the deterministic server's aggregate services and clock.
            /// </summary>
            public AsyncHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:async-aggregate-revision")
            {
            }

            /// <summary>
            /// Revises aggregate retention asynchronously for the supplied queue size and mutable filter.
            /// </summary>
            public ValueTask<StatusCode> ReviseAsync(
                uint queueSize,
                ServerAggregateFilter filter,
                double samplingInterval = 0)
            {
                return ReviseAggregateFilterAsync(SystemContext, CreateHandle(), samplingInterval, queueSize, filter);
            }
        }

        /// <summary>
        /// Creates the variable handle required by aggregate-filter revision.
        /// </summary>
        private static NodeHandle CreateHandle()
        {
            var node = new BaseDataVariableState(null) { NodeId = new NodeId(1, 1) };
            return new NodeHandle(node.NodeId, node);
        }
    }
}
