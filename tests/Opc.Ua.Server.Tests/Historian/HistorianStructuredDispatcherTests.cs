/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class HistorianStructuredDispatcherTests
    {
        [Test]
        public async Task StructuredCrudRoutesThroughProviderAndAuditsAsync()
        {
            using var fixture = new Fixture();
            DataValue inserted = MakePair(1.0);
            HistoryUpdateResult insert = await fixture.DispatchAsync(
                PerformUpdateType.Insert,
                inserted).ConfigureAwait(false);
            Assert.That(
                insert.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));

            DataValue replaced = MakePair(2.0);
            HistoryUpdateResult replace = await fixture.DispatchAsync(
                PerformUpdateType.Replace,
                replaced).ConfigureAwait(false);
            Assert.That(
                replace.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryReplaced));

            DataValue updated = MakePair(3.0);
            HistoryUpdateResult update = await fixture.DispatchAsync(
                PerformUpdateType.Update,
                updated).ConfigureAwait(false);
            Assert.That(
                update.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryReplaced));

            HistoryUpdateResult remove = await fixture.DispatchAsync(
                PerformUpdateType.Remove,
                updated).ConfigureAwait(false);
            Assert.That(
                remove.OperationResults[0],
                Is.EqualTo(StatusCodes.Good));

            HistorianPage<HistoricalDataValue> remaining =
                await fixture.Provider.ReadRawAsync(
                    fixture.HistorianContext,
                    new HistorianRawReadRequest
                    {
                        NodeId = fixture.Variable.NodeId,
                        StartTime =
                            kCaptureTime.ToDateTime().AddMinutes(-1),
                        EndTime =
                            kCaptureTime.ToDateTime().AddMinutes(1),
                        IsForward = true
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(remaining.Values, Is.Empty);
            Assert.That(fixture.AuditEvents, Has.Count.EqualTo(4));
            for (int i = 0; i < fixture.AuditEvents.Count; i++)
            {
                Assert.That(
                    fixture.AuditEvents[i],
                    Is.TypeOf<AuditHistoryValueUpdateEventState>());
            }
            var replaceAudit =
                (AuditHistoryValueUpdateEventState)fixture.AuditEvents[1];
            ArrayOf<DataValue> oldValues =
                replaceAudit.OldValues?.Value ??
                ArrayOf<DataValue>.Null;
            Assert.That(oldValues.Count, Is.EqualTo(1));
            Assert.That(ReadPairValue(oldValues[0]), Is.EqualTo(1.0));
        }

        private static DataValue MakePair(double value)
        {
            var pair = new KeyValuePair
            {
                Key = new QualifiedName("Pressure", 1),
                Value = Variant.From(value)
            };
            return new DataValue(
                new Variant(new ExtensionObject(pair)),
                StatusCodes.Good,
                kCaptureTime);
        }

        private static double ReadPairValue(DataValue value)
        {
            Assert.That(
                value.WrappedValue.TryGetValue(
                    out ExtensionObject extension),
                Is.True);
            Assert.That(
                extension.TryGetValue(
                    out KeyValuePair pair),
                Is.True);
            Assert.That(
                pair.Value.TryGetValue(out double reading),
                Is.True);
            return reading;
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture()
            {
                ITelemetryContext telemetry =
                    NUnitTelemetryContext.Create();
                var namespaceUris = new NamespaceTable();
                namespaceUris.Append("urn:test:structured-dispatch");
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.NamespaceUris)
                    .Returns(namespaceUris);
                server.SetupGet(value => value.ServerUris)
                    .Returns(new StringTable());
                server.SetupGet(value => value.TypeTree)
                    .Returns(new TypeTable(namespaceUris));
                server.SetupGet(value => value.Factory)
                    .Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry)
                    .Returns(telemetry);
                Mock<IAuditEventServer> audit = server.As<IAuditEventServer>();
                audit.SetupGet(value => value.Auditing).Returns(true);
                audit.Setup(
                    value => value.ReportAuditEvent(
                        It.IsAny<ISystemContext>(),
                        It.IsAny<AuditEventState>()))
                    .Callback<ISystemContext, AuditEventState>(
                        (_, value) => AuditEvents.Add(value));
                m_operationContext = new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryUpdate,
                    RequestLifetime.None);
                SystemContext = new ServerSystemContext(
                    server.Object,
                    m_operationContext);
                audit.SetupGet(value => value.DefaultAuditContext)
                    .Returns(SystemContext);
                Variable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Structured", 1),
                    BrowseName = new QualifiedName("Structured", 1),
                    Historizing = true,
                    AccessLevel =
                        AccessLevels.HistoryRead |
                        AccessLevels.HistoryWrite
                };
                Provider = new InMemoryHistorianProvider();
                Provider.RegisterStructured(
                    Variable.NodeId,
                    KeyValuePairStructuredDataKeySelector.Instance);
                HistorianContext = new HistorianOperationContext(
                    SystemContext,
                    m_operationContext,
                    Variable,
                    HistoryUpdateType.Insert);
            }

            public List<AuditEventState> AuditEvents { get; } = [];

            public HistorianOperationContext HistorianContext { get; }

            public InMemoryHistorianProvider Provider { get; }

            public ServerSystemContext SystemContext { get; }

            public BaseDataVariableState Variable { get; }

            public async ValueTask<HistoryUpdateResult> DispatchAsync(
                PerformUpdateType updateType,
                DataValue value)
            {
                var details = new UpdateStructureDataDetails
                {
                    NodeId = Variable.NodeId,
                    PerformInsertReplace = updateType,
                    UpdateValues = [value]
                };
                var result = new HistoryUpdateResult();
                ServiceResult serviceResult =
                    await HistorianDispatcher
                        .DispatchStructuredDataUpdateAsync(
                            SystemContext,
                            Provider,
                            Variable,
                            details,
                            result,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                Assert.That(
                    ServiceResult.IsGood(serviceResult),
                    Is.True);
                return result;
            }

            public void Dispose()
            {
                Provider.Dispose();
                m_operationContext.Dispose();
            }

            private readonly OperationContext m_operationContext;
        }

        private static readonly DateTimeUtc kCaptureTime =
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
