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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class AggregateFilterRevisionRegressionTests
    {
        [TestCase(0u, 1000.0, 0.0)]
        [TestCase(1u, 1000.0, 0.0)]
        [TestCase(2u, 1000.0, 1000.0)]
        [TestCase(2u, 0.0, 1000.0)]
        [TestCase(2u, -1.0, 1000.0)]
        [TestCase(uint.MaxValue, 1000.0, 4294967294000.0)]
        [TestCase(uint.MaxValue, double.MaxValue, -1.0)]
        [TestCase(2u, double.NaN, -1.0)]
        [TestCase(2u, double.PositiveInfinity, -1.0)]
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

        private sealed class SyncHooks : CustomNodeManager2
        {
            public SyncHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:sync-aggregate-revision")
            {
            }

            public StatusCode Revise(uint queueSize, ServerAggregateFilter filter)
            {
                return ReviseAggregateFilter(SystemContext, CreateHandle(), 0, queueSize, filter);
            }
        }

        private sealed class AsyncHooks : AsyncCustomNodeManager
        {
            public AsyncHooks(IServerInternal server)
                : base(server, NullLogger.Instance, "urn:tests:async-aggregate-revision")
            {
            }

            public ValueTask<StatusCode> ReviseAsync(uint queueSize, ServerAggregateFilter filter)
            {
                return ReviseAggregateFilterAsync(SystemContext, CreateHandle(), 0, queueSize, filter);
            }
        }

        private static NodeHandle CreateHandle()
        {
            var node = new BaseDataVariableState(null) { NodeId = new NodeId(1, 1) };
            return new NodeHandle(node.NodeId, node);
        }
    }
}
