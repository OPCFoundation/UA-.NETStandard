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

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// A HistoryRead at requested times answers every requested time whatever the archive
    /// holds, so the number of requested times and the raw values scanned to answer them
    /// are bounded.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [NonParallelizable]
    public class HistorianAtTimeBudgetTests
    {
        private static readonly DateTime s_baseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// More requested times than one operation may ask for are rejected before the
        /// provider is read.
        /// </summary>
        [Test]
        public async Task TooManyRequestedTimesAreRejectedAsync()
        {
            var provider = new ScanningProvider(samples: 10);
            var times = new DateTimeUtc[HistorianDispatcher.kMaxAtTimeRequestedValues + 1];
            for (int ii = 0; ii < times.Length; ii++)
            {
                times[ii] = new DateTimeUtc(s_baseTime.AddMilliseconds(ii));
            }

            HistoryReadResult result = await ReadAtTimeAsync(provider.Object, times);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(result.HistoryData.IsNull, Is.True);
            Assert.That(provider.Reads, Is.Zero);
        }

        /// <summary>
        /// A provider that returns its whole archive whatever range is asked for (or an
        /// archive of Bad values that hides every bound) made each requested time scan the
        /// archive: 10 000 requested times over 1 000 values allocated gigabytes for seconds.
        /// Once the scan budget of the operation is spent the remaining requested times are
        /// answered with Bad_BoundNotSupported without reading the provider again.
        /// </summary>
        [Test]
        public async Task ScanningEveryRequestedTimeIsBoundedAsync()
        {
            var provider = new ScanningProvider(samples: 1000);
            DateTimeUtc[] times = RequestedTimes(10_000, TimeSpan.FromMilliseconds(1));

            long before = GetTotalAllocatedBytes();
            var watch = Stopwatch.StartNew();
            HistoryReadResult result = await ReadAtTimeAsync(provider.Object, times);
            watch.Stop();
            long allocated = GetTotalAllocatedBytes() - before;
            TestContext.Out.WriteLine(
                "status={0} reads={1} scanned={2} time={3} ms allocated={4} MB",
                result.StatusCode, provider.Reads, provider.Scanned, watch.ElapsedMilliseconds, allocated >> 20);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? data), Is.True);
            Assert.That(data!.DataValues.Count, Is.EqualTo(times.Length));
            // the first requested times found their value, the last ones hit the budget
            Assert.That(StatusCode.IsGood(data.DataValues[0].StatusCode), Is.True);
            Assert.That(data.DataValues[^1].StatusCode, Is.EqualTo(StatusCodes.BadBoundNotSupported));
            long budget = HistorianDispatcher.kMaxProcessedBufferedOutputs +
                ((long)HistorianDispatcher.kAtTimeScanValuesPerRequestedTime * times.Length);
            // at most one page past the budget
            Assert.That(provider.Scanned, Is.LessThanOrEqualTo(budget + 1000));
            Assert.That(allocated, Is.LessThan(512L << 20));
        }

        /// <summary>
        /// The budget does not reject an ordinary at-time read: 10 000 requested times over an
        /// archive of 2 000 Good values are all answered, interpolated between the stored values.
        /// </summary>
        [Test]
        public async Task ManyRequestedTimesOverAnOrdinaryArchiveAreAnsweredAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero },
                new FakeTimeProvider(s_baseTime));
            var nodeId = new NodeId("at-time", 1);
            provider.Register(nodeId);
            var values = new List<DataValue>();
            for (int ii = 0; ii < 2000; ii++)
            {
                DateTime timestamp = s_baseTime.AddSeconds(ii);
                values.Add(new DataValue(new Variant((double)ii), StatusCodes.Good, timestamp, timestamp));
            }
            ServerSystemContext systemContext = CreateSystemContext();
            await provider.InsertAsync(
                new HistorianOperationContext(
                    systemContext, systemContext.OperationContext!, null, HistoryUpdateType.Insert),
                nodeId,
                values,
                CancellationToken.None);
            // between the stored values: 0.1 s, 0.3 s, ... up to 1999.9 s
            DateTimeUtc[] times = RequestedTimes(10_000, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100));

            var watch = Stopwatch.StartNew();
            HistoryReadResult result = await ReadAtTimeAsync(provider, times, nodeId, systemContext);
            TestContext.Out.WriteLine("status={0} time={1} ms", result.StatusCode, watch.ElapsedMilliseconds);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? data), Is.True);
            Assert.That(data!.DataValues.Count, Is.EqualTo(times.Length));
            for (int ii = 0; ii < times.Length; ii += 997)
            {
                DataValue value = data.DataValues[ii];
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, value.StatusCode.ToString());
                Assert.That(value.WrappedValue.GetDouble(), Is.EqualTo((ii * 0.2) + 0.1).Within(1e-9));
            }
        }

        /// <summary>
        /// The requested times are answered for every node of the request, so a HistoryRead
        /// whose requested times multiplied by its nodes exceed the limit is refused as a whole.
        /// </summary>
        [Test]
        public async Task RequestedTimesTimesNodesAreLimitedPerRequestAsync()
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true
            };
            try
            {
                StandardServer server = await fixture.StartAsync();
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync(nameof(RequestedTimesTimesNodesAreLimitedPerRequestAsync));
                DateTimeUtc[] times = RequestedTimes(1000, TimeSpan.FromSeconds(1));

                Task<HistoryReadResponse> ReadAsync(int nodes)
                {
                    var nodesToRead = new HistoryReadValueId[nodes];
                    for (int ii = 0; ii < nodes; ii++)
                    {
                        nodesToRead[ii] = new HistoryReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime };
                    }
                    requestHeader.Timestamp = DateTimeUtc.Now;
                    return server.HistoryReadAsync(
                        secureChannelContext,
                        requestHeader,
                        new ExtensionObject(new ReadAtTimeDetails { ReqTimes = times.ToArrayOf() }),
                        TimestampsToReturn.Source,
                        false,
                        nodesToRead.ToArrayOf(),
                        RequestLifetime.None).AsTask();
                }

                int allowed = HistorianDispatcher.kMaxAtTimeRequestedValues / times.Length;
                HistoryReadResponse response = await ReadAsync(allowed);
                Assert.That(response.Results.Count, Is.EqualTo(allowed));

                StatusCode status = StatusCodes.Good;
                try
                {
                    await ReadAsync(allowed + 1);
                }
                catch (ServiceResultException e)
                {
                    status = e.StatusCode;
                }
                Assert.That(status, Is.EqualTo(StatusCodes.BadTooManyOperations));
            }
            finally
            {
                await fixture.StopAsync();
            }
        }

        /// <summary>
        /// The bytes allocated by the process so far; zero on .NET Framework, which has no
        /// process-wide counter (the scan count still bounds the work there).
        /// </summary>
        private static long GetTotalAllocatedBytes()
        {
#if NETFRAMEWORK
            return 0;
#else
            return GC.GetTotalAllocatedBytes(precise: true);
#endif
        }

        private static DateTimeUtc[] RequestedTimes(int count, TimeSpan step, TimeSpan offset = default)
        {
            var times = new DateTimeUtc[count];
            for (int ii = 0; ii < count; ii++)
            {
                times[ii] = new DateTimeUtc(s_baseTime + offset + TimeSpan.FromTicks(step.Ticks * ii));
            }
            return times;
        }

        private static async Task<HistoryReadResult> ReadAtTimeAsync(
            IHistorianProvider provider,
            DateTimeUtc[] times,
            NodeId? nodeId = null,
            ServerSystemContext? systemContext = null)
        {
            nodeId ??= ScanningProvider.NodeId;
            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId.Value,
                BrowseName = new QualifiedName("AtTime"),
                AccessLevel = AccessLevels.HistoryRead,
                Historizing = true
            };
            var result = new HistoryReadResult();
            await HistorianDispatcher.DispatchAtTimeReadAsync(
                systemContext ?? CreateSystemContext(),
                provider,
                node,
                new HistoryReadValueId { NodeId = nodeId.Value },
                new ReadAtTimeDetails { ReqTimes = times.ToArrayOf() },
                TimestampsToReturn.Source,
                result,
                CancellationToken.None);
            return result;
        }

        private static ServerSystemContext CreateSystemContext()
        {
            var namespaceUris = new NamespaceTable();
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            var session = new Mock<ISession>();
            session.Setup(s => s.ContinuationPoints).Returns(
                new SessionContinuationPoints(() => NodeId.Null, maxBrowse: 10, maxHistory: 10, store: null));
            var context = new OperationContext(
                new RequestHeader(), null!, RequestType.HistoryRead, RequestLifetime.None, session.Object);
            return new ServerSystemContext(server.Object, context);
        }

        /// <summary>
        /// A raw provider that ignores the requested range and returns its whole archive in
        /// pages of 1 000 values.
        /// </summary>
        private sealed class ScanningProvider
        {
            public static readonly NodeId NodeId = new("scanning", 1);

            public ScanningProvider(int samples)
            {
                m_samples = samples;
                var provider = new Mock<IHistorianProvider>();
                provider
                    .Setup(p => p.IsHistorizingAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(true));
                provider
                    .Setup(p => p.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.ReadOnly));
                provider.As<IHistorianDataProvider>()
                    .Setup(p => p.ReadRawAsync(
                        It.IsAny<HistorianOperationContext>(),
                        It.IsAny<HistorianRawReadRequest>(),
                        It.IsAny<HistorianResumeToken>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((
                        HistorianOperationContext _,
                        HistorianRawReadRequest _,
                        HistorianResumeToken token,
                        CancellationToken _) => new ValueTask<HistorianPage<HistoricalDataValue>>(ReadPage(token)));
                Object = provider.Object;
            }

            public IHistorianProvider Object { get; }

            public int Reads { get; private set; }

            public long Scanned { get; private set; }

            private HistorianPage<HistoricalDataValue> ReadPage(HistorianResumeToken token)
            {
                Reads++;
                int first = token.IsEmpty ? 0 : BitConverter.ToInt32(token.State.ToArray(), 0);
                int count = Math.Min(1000, m_samples - first);
                var values = new List<HistoricalDataValue>(count);
                for (int ii = first; ii < first + count; ii++)
                {
                    DateTime timestamp = s_baseTime.AddMilliseconds(ii);
                    values.Add(new HistoricalDataValue(
                        new DataValue(new Variant((double)ii), StatusCodes.Good, timestamp, timestamp)));
                }
                Scanned += count;
                int next = first + count;
                return next < m_samples
                    ? new HistorianPage<HistoricalDataValue>(
                        values.ToArrayOf(),
                        new HistorianResumeToken(ByteString.From(BitConverter.GetBytes(next))))
                    : new HistorianPage<HistoricalDataValue>(values.ToArrayOf());
            }

            private readonly int m_samples;
        }
    }
}
