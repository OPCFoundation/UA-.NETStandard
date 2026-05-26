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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryTransactionalProviderTests
    {
        [Test]
        public async Task InsertAtomicCommitsWhenAllValuesNewAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("tx.var", 1);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>
            {
                MakeValue(BaseTime.AddSeconds(1), 1.0),
                MakeValue(BaseTime.AddSeconds(2), 2.0),
                MakeValue(BaseTime.AddSeconds(3), 3.0),
            };

            IList<StatusCode> statuses = await provider.InsertAtomicAsync(
                context, nodeId, values, CancellationToken.None);

            Assert.That(statuses, Has.Count.EqualTo(3));
            foreach (StatusCode sc in statuses)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task InsertAtomicRollsBackOnFirstFailureAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("tx.var", 1);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();

            // Pre-existing entry at t=2 — will collide with the atomic insert below.
            await provider.InsertAsync(context, nodeId,
                [MakeValue(BaseTime.AddSeconds(2), 99.0)], CancellationToken.None);

            var batch = new List<DataValue>
            {
                MakeValue(BaseTime.AddSeconds(1), 1.0),
                MakeValue(BaseTime.AddSeconds(2), 2.0), // collision
                MakeValue(BaseTime.AddSeconds(3), 3.0),
            };

            IList<StatusCode> statuses = await provider.InsertAtomicAsync(
                context, nodeId, batch, CancellationToken.None);

            Assert.That(statuses, Has.Count.EqualTo(3));
            Assert.That((uint)statuses[1].Code, Is.EqualTo(StatusCodes.BadEntryExists.Code));
            // Rollback markers on the other two slots.
            Assert.That((uint)statuses[0].Code, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported.Code));
            Assert.That((uint)statuses[2].Code, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported.Code));

            // Verify nothing else was inserted.
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddSeconds(10),
                    IsForward = true,
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values, Has.Count.EqualTo(1),
                "Only the pre-existing value should remain; rollback discarded the rest.");
        }

        private static readonly System.DateTime BaseTime
            = new(2025, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

        private static DataValue MakeValue(System.DateTime sourceTimestamp, double value)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, sourceTimestamp: sourceTimestamp, serverTimestamp: sourceTimestamp);
        }

        private static HistorianOperationContext CreateContext()
        {
            var mockTelemetry = new Moq.Mock<ITelemetryContext>();
            var mockServer = new Moq.Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var systemContext = new ServerSystemContext(mockServer.Object, opContext);
            return new HistorianOperationContext(
                systemContext, opContext, null, HistoryUpdateType.Insert);
        }
    }
}
