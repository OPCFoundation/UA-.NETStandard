/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies atomic in-memory historian insertion and rollback of failed batches.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryTransactionalProviderTests
    {
        /// <summary>
        /// Verifies that atomic insertion commits when every value is new.
        /// </summary>
        [Test]
        public async Task InsertAtomicCommitsWhenAllValuesNewAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions(),
                new FakeTimeProvider(BaseTime));
            var nodeId = new NodeId("tx.var", 1);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>
            {
                MakeValue(BaseTime.AddSeconds(1), 1.0),
                MakeValue(BaseTime.AddSeconds(2), 2.0),
                MakeValue(BaseTime.AddSeconds(3), 3.0)
            };

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                context, nodeId, values, CancellationToken.None).ConfigureAwait(false);

            Assert.That(outcome.OperationResults, Has.Count.EqualTo(3));
            Assert.That(outcome.TransactionRolledBack, Is.False);
            foreach (StatusCode sc in outcome.OperationResults)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        /// <summary>
        /// Verifies that atomic insertion rolls back the batch on its first failure.
        /// </summary>
        [Test]
        public async Task InsertAtomicRollsBackOnFirstFailureAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions(),
                new FakeTimeProvider(BaseTime));
            var nodeId = new NodeId("tx.var", 1);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();

            // Pre-existing entry at t=2 — will collide with the atomic insert below.
            await provider.InsertAsync(context, nodeId,
                [MakeValue(BaseTime.AddSeconds(2), 99.0)], CancellationToken.None).ConfigureAwait(false);

            var batch = new List<DataValue>
            {
                MakeValue(BaseTime.AddSeconds(1), 1.0),
                MakeValue(BaseTime.AddSeconds(2), 2.0), // collision
                MakeValue(BaseTime.AddSeconds(3), 3.0)
            };

            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAtomicAsync(
                context, nodeId, batch, CancellationToken.None).ConfigureAwait(false);

            Assert.That(outcome.OperationResults, Has.Count.EqualTo(3));
            Assert.That(outcome.TransactionRolledBack, Is.True);
            Assert.That(outcome.OperationResults[1].Code, Is.EqualTo(StatusCodes.BadEntryExists.Code));
            // Rollback markers on the other two slots.
            Assert.That(outcome.OperationResults[0].Code, Is.EqualTo(StatusCodes.BadTransactionFailed.Code));
            Assert.That(outcome.OperationResults[2].Code, Is.EqualTo(StatusCodes.BadTransactionFailed.Code));

            // Verify nothing else was inserted.
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddSeconds(10),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

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
