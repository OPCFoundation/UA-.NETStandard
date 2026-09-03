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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianTransactionalDispatcherTests
    {
        [TestCase(PerformUpdateType.Insert)]
        [TestCase(PerformUpdateType.Replace)]
        [TestCase(PerformUpdateType.Update)]
        public async Task TransactionalProviderUsesMatchingAtomicOperationAsync(
            PerformUpdateType performUpdate)
        {
            var provider = new Mock<IHistorianProvider>();
            Mock<IHistorianDataProvider> data = provider.As<IHistorianDataProvider>();
            Mock<IHistorianTransactionalProvider> transactional =
                provider.As<IHistorianTransactionalProvider>();
            string selectedOperation = string.Empty;
            int bestEffortCalls = 0;

            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.ReadWrite));

            data
                .Setup(p => p.InsertAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => bestEffortCalls++)
                .Returns(new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));
            data
                .Setup(p => p.ReplaceAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => bestEffortCalls++)
                .Returns(new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));
            data
                .Setup(p => p.UpdateAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => bestEffortCalls++)
                .Returns(new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));
            transactional
                .Setup(p => p.InsertAtomicAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => selectedOperation = nameof(PerformUpdateType.Insert))
                .Returns(new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));
            transactional
                .Setup(p => p.ReplaceAtomicAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => selectedOperation = nameof(PerformUpdateType.Replace))
                .Returns(new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));
            transactional
                .Setup(p => p.UpdateAtomicAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => selectedOperation = nameof(PerformUpdateType.Update))
                .Returns(new ValueTask<HistorianUpdateOutcome<DataValue>>(
                    new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])));

            var nodeId = new NodeId("transactional-selection", 1);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("TransactionalSelection", 1)
            };
            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = performUpdate,
                UpdateValues = [MakeValue(s_baseTime, 1.0)]
            };
            var result = new HistoryUpdateResult();
            (ServerSystemContext systemContext, OperationContext operationContext) =
                CreateSystemContext();
            using (operationContext)
            {
                ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                    systemContext,
                    provider.Object,
                    variable,
                    details,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(error), Is.True);
                Assert.That(bestEffortCalls, Is.Zero);
                Assert.That(selectedOperation, Is.EqualTo(performUpdate.ToString()));
                Assert.That(result.OperationResults, Is.EqualTo([StatusCodes.Good]));
            }
        }

        [TestCase(PerformUpdateType.Insert)]
        [TestCase(PerformUpdateType.Replace)]
        [TestCase(PerformUpdateType.Update)]
        public async Task NonTransactionalProviderUsesMatchingBestEffortOperationAsync(
            PerformUpdateType performUpdate)
        {
            var provider = new Mock<IHistorianProvider>();
            Mock<IHistorianDataProvider> data = provider.As<IHistorianDataProvider>();
            string selectedOperation = string.Empty;
            ValueTask<HistorianUpdateOutcome<DataValue>> success =
                new(new HistorianUpdateOutcome<DataValue>([StatusCodes.Good]));

            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(HistorianNodeCapabilities.ReadWrite));

            switch (performUpdate)
            {
                case PerformUpdateType.Insert:
                    data
                        .Setup(p => p.InsertAsync(
                            It.IsAny<HistorianOperationContext>(),
                            It.IsAny<NodeId>(),
                            It.IsAny<ArrayOf<DataValue>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback(() => selectedOperation = nameof(PerformUpdateType.Insert))
                        .Returns(success);
                    break;
                case PerformUpdateType.Replace:
                    data
                        .Setup(p => p.ReplaceAsync(
                            It.IsAny<HistorianOperationContext>(),
                            It.IsAny<NodeId>(),
                            It.IsAny<ArrayOf<DataValue>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback(() => selectedOperation = nameof(PerformUpdateType.Replace))
                        .Returns(success);
                    break;
                case PerformUpdateType.Update:
                    data
                        .Setup(p => p.UpdateAsync(
                            It.IsAny<HistorianOperationContext>(),
                            It.IsAny<NodeId>(),
                            It.IsAny<ArrayOf<DataValue>>(),
                            It.IsAny<CancellationToken>()))
                        .Callback(() => selectedOperation = nameof(PerformUpdateType.Update))
                        .Returns(success);
                    break;
            }

            var nodeId = new NodeId("best-effort-selection", 1);
            var variable = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("BestEffortSelection", 1)
            };
            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = performUpdate,
                UpdateValues = [MakeValue(s_baseTime, 1.0)]
            };
            var result = new HistoryUpdateResult();
            (ServerSystemContext systemContext, OperationContext operationContext) =
                CreateSystemContext();
            using (operationContext)
            {
                ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                    systemContext,
                    provider.Object,
                    variable,
                    details,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(error), Is.True);
                Assert.That(selectedOperation, Is.EqualTo(performUpdate.ToString()));
                Assert.That(result.OperationResults, Is.EqualTo([StatusCodes.Good]));
            }
        }

        [Test]
        public async Task InsertCollisionRollsBackEntireDispatchedBatchAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("transactional-dispatch", 1);
            provider.Register(nodeId);

            (ServerSystemContext systemContext, OperationContext operationContext) =
                CreateSystemContext();
            using (operationContext)
            {
                var variable = new BaseDataVariableState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("TransactionalDispatch", 1)
                };
                var providerContext = new HistorianOperationContext(
                    systemContext,
                    operationContext,
                    variable,
                    HistoryUpdateType.Insert);

                await provider.InsertAsync(
                    providerContext,
                    nodeId,
                    [MakeValue(s_baseTime.AddSeconds(2), 99.0)],
                    CancellationToken.None).ConfigureAwait(false);

                var details = new UpdateDataDetails
                {
                    NodeId = nodeId,
                    PerformInsertReplace = PerformUpdateType.Insert,
                    UpdateValues =
                    [
                        MakeValue(s_baseTime.AddSeconds(1), 1.0),
                        MakeValue(s_baseTime.AddSeconds(2), 2.0),
                        MakeValue(s_baseTime.AddSeconds(3), 3.0)
                    ]
                };
                var result = new HistoryUpdateResult();

                ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                    systemContext,
                    provider,
                    variable,
                    details,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTransactionFailed));
                Assert.That(result.OperationResults, Has.Count.EqualTo(3));
                Assert.That(
                    result.OperationResults[0],
                    Is.EqualTo(StatusCodes.BadTransactionFailed));
                Assert.That(result.OperationResults[1], Is.EqualTo(StatusCodes.BadEntryExists));
                Assert.That(
                    result.OperationResults[2],
                    Is.EqualTo(StatusCodes.BadTransactionFailed));

                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    providerContext,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = s_baseTime,
                        EndTime = s_baseTime.AddSeconds(10),
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(page.Values, Has.Count.EqualTo(1));
                Assert.That(
                    page.Values[0].Value.SourceTimestamp,
                    Is.EqualTo(s_baseTime.AddSeconds(2)));
            }
        }

        private static DataValue MakeValue(DateTime sourceTimestamp, double value)
        {
            return new DataValue(
                new Variant(value),
                StatusCodes.Good,
                sourceTimestamp,
                sourceTimestamp);
        }

        private static (ServerSystemContext SystemContext, OperationContext OperationContext)
            CreateSystemContext()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:historian-transactional-dispatch");
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryUpdate,
                RequestLifetime.None);
            return (new ServerSystemContext(mockServer.Object, operationContext), operationContext);
        }

        private static readonly DateTime s_baseTime =
            new(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
