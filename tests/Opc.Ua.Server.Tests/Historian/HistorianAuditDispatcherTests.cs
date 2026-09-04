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
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
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
    public sealed class HistorianAuditDispatcherTests
    {
        [Test]
        public async Task EventDeleteAuditsTypedOldValueAndFailureAsync()
        {
            using var fixture = new AuditFixture();
            var eventId = ByteString.From([0x43, 0x87]);
            DateTimeUtc eventTime = DateTime.UtcNow.AddHours(-1);
            var record = new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                eventTime,
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    [BrowseNames.EventId] = new Variant(eventId),
                    [BrowseNames.Message] =
                        new Variant(new LocalizedText("deleted event"))
                }.ToArrayOf());
            await fixture.Provider.InsertEventsAsync(
                fixture.OperationContext,
                fixture.Notifier.NodeId,
                [record],
                CancellationToken.None).ConfigureAwait(false);
            var details = new DeleteEventDetails
            {
                NodeId = fixture.Notifier.NodeId,
                EventIds = [eventId]
            };
            var result = new HistoryUpdateResult();

            ServiceResult serviceResult =
                await HistorianDispatcher.DispatchDeleteEventsAsync(
                    fixture.SystemContext,
                    fixture.Provider,
                    fixture.Notifier,
                    details,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(serviceResult), Is.True);
            Assert.That(fixture.Events, Has.Count.EqualTo(1));
            Assert.That(
                fixture.Events[0],
                Is.TypeOf<AuditHistoryEventDeleteEventState>());
            var audit =
                (AuditHistoryEventDeleteEventState)fixture.Events[0];
            Assert.That(audit.Status?.Value, Is.True);
            Assert.That(audit.OldValues?.Value, Is.Not.Null);
            Assert.That(
                ContainsEventId(
                    audit.OldValues!.Value!.EventFields,
                    eventId),
                Is.True);

            fixture.Events.Clear();
            result = new HistoryUpdateResult();
            serviceResult =
                await HistorianDispatcher.DispatchDeleteEventsAsync(
                    fixture.SystemContext,
                    fixture.Provider,
                    fixture.Notifier,
                    details,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(serviceResult), Is.True);
            Assert.That(fixture.Events, Has.Count.EqualTo(1));
            audit = (AuditHistoryEventDeleteEventState)fixture.Events[0];
            Assert.That(audit.Status?.Value, Is.False);
            Assert.That(audit.OldValues?.Value, Is.Not.Null);
            Assert.That(audit.OldValues!.Value!.EventFields, Is.Empty);
        }

        [Test]
        public async Task AnnotationReplaceAuditsTypedOldAndNewValuesAsync()
        {
            using var fixture = new AuditFixture();
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("Variable", 1),
                BrowseName = new QualifiedName("Variable", 1),
                Historizing = true,
                AccessLevel =
                    AccessLevels.HistoryRead |
                    AccessLevels.HistoryWrite
            };
            fixture.Provider.Register(
                variable.NodeId,
                new HistorianNodeCapabilities
                {
                    InsertAnnotation = true
                });
            DateTimeUtc annotationTime = DateTime.UtcNow.AddHours(-1);
            var original = new Annotation
            {
                AnnotationTime = annotationTime,
                Message = "original",
                UserName = "operator"
            };
            var operationContext = new HistorianOperationContext(
                fixture.SystemContext,
                fixture.RequestContext,
                variable,
                HistoryUpdateType.Insert);
            await fixture.Provider.InsertAnnotationsAsync(
                operationContext,
                variable.NodeId,
                [original],
                CancellationToken.None).ConfigureAwait(false);
            var replacement = new Annotation
            {
                AnnotationTime = annotationTime,
                Message = "replacement",
                UserName = "operator"
            };
            var details = new UpdateStructureDataDetails
            {
                NodeId = new NodeId("Variable/Annotations", 1),
                PerformInsertReplace = PerformUpdateType.Replace,
                UpdateValues =
                [
                    new DataValue(
                        new Variant(new ExtensionObject(replacement)),
                        StatusCodes.Good,
                        annotationTime)
                ]
            };
            var result = new HistoryUpdateResult();

            ServiceResult serviceResult =
                await HistorianDispatcher.DispatchAnnotationUpdateAsync(
                    fixture.SystemContext,
                    fixture.Provider,
                    variable,
                    details,
                    result,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(serviceResult), Is.True);
            Assert.That(fixture.Events, Has.Count.EqualTo(1));
            Assert.That(
                fixture.Events[0],
                Is.TypeOf<AuditHistoryAnnotationUpdateEventState>());
            var audit =
                (AuditHistoryAnnotationUpdateEventState)fixture.Events[0];
            Assert.That(audit.Status?.Value, Is.True);
            ArrayOf<Annotation> newValues =
                audit.NewValues?.Value ?? ArrayOf<Annotation>.Null;
            Assert.That(newValues.Count, Is.EqualTo(1));
            Assert.That(
                newValues[0].Message,
                Is.EqualTo("replacement"));
            ArrayOf<Annotation> oldValues =
                audit.OldValues?.Value ?? ArrayOf<Annotation>.Null;
            Assert.That(oldValues.Count, Is.EqualTo(1));
            Assert.That(
                oldValues[0].Message,
                Is.EqualTo("original"));
        }

        private static bool ContainsEventId(
            ArrayOf<Variant> fields,
            ByteString eventId)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].TryGetValue(out ByteString value) &&
                    value == eventId)
                {
                    return true;
                }
            }
            return false;
        }

        private sealed class AuditFixture : IDisposable
        {
            public AuditFixture()
            {
                ITelemetryContext telemetry =
                    NUnitTelemetryContext.Create();
                var namespaceUris = new NamespaceTable();
                namespaceUris.Append("urn:test:historian-audit");
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
                        (_, value) => Events.Add(value));
                RequestContext = new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryUpdate,
                    RequestLifetime.None);
                SystemContext = new ServerSystemContext(
                    server.Object,
                    RequestContext);
                audit.SetupGet(value => value.DefaultAuditContext)
                    .Returns(SystemContext);
                Notifier = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Notifier", 1),
                    BrowseName = new QualifiedName("Notifier", 1),
                    EventNotifier =
                        EventNotifiers.HistoryRead |
                        EventNotifiers.HistoryWrite
                };
                Provider = new InMemoryHistorianProvider();
                Provider.Register(
                    Notifier.NodeId,
                    new HistorianNodeCapabilities
                    {
                        ReadRawData = false,
                        ReadModifiedData = false,
                        ReadAtTime = false,
                        ReadProcessedData = false,
                        ReadEventHistory = true,
                        DeleteEvent = true,
                        EventTypes = [ObjectTypeIds.BaseEventType]
                    });
                OperationContext = new HistorianOperationContext(
                    SystemContext,
                    RequestContext,
                    Notifier,
                    HistoryUpdateType.Insert);
            }

            public List<AuditEventState> Events { get; } = [];

            public BaseObjectState Notifier { get; }

            public HistorianOperationContext OperationContext { get; }

            public InMemoryHistorianProvider Provider { get; }

            public OperationContext RequestContext { get; }

            public ServerSystemContext SystemContext { get; }

            public void Dispose()
            {
                Provider.Dispose();
                RequestContext.Dispose();
            }
        }
    }
}
