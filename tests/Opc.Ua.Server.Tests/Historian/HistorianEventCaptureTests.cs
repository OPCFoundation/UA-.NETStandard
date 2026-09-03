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
    public sealed class HistorianEventCaptureTests
    {
        [Test]
        public async Task HistorizeEventsCapturesSynchronousAndAsynchronousReportsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:event-capture");
            var registry = new HistorianProviderRegistry(namespaceUris);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(value => value.TypeTree)
                .Returns(new TypeTable(namespaceUris));
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(registry);
            var context = new ServerSystemContext(server.Object);
            var notifier = new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 1),
                BrowseName = new QualifiedName("Notifier", 1)
            };
            int forwarded = 0;
            notifier.OnReportEventAsync = (_, _, _, _) =>
            {
                forwarded++;
                return default;
            };
            using var provider = new InMemoryHistorianProvider();
            HistorianNodeCapabilities capabilities = HistorianNodeCapabilities.ReadWrite with
            {
                EventTypes = [ObjectTypeIds.BaseEventType]
            };
            var builder = new HistorianBuilder(server.Object);
            builder.UseProvider(provider);
            await builder.HistorizeEventsAsync(
                notifier,
                context,
                installConfiguration: false,
                capabilities: capabilities,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            var reportedEvent = new BaseEventState(null);
            reportedEvent.Initialize(
                context,
                notifier,
                EventSeverity.Medium,
                new LocalizedText("captured"));

            await notifier.ReportEventAsync(
                context,
                reportedEvent,
                CancellationToken.None).ConfigureAwait(false);
            var synchronousEvent = new BaseEventState(null);
            synchronousEvent.Initialize(
                context,
                notifier,
                EventSeverity.Medium,
                new LocalizedText("captured synchronously"));
            notifier.ReportEvent(context, synchronousEvent);
            await builder.DisposeAsync().ConfigureAwait(false);

            Assert.That(forwarded, Is.EqualTo(2));
            using var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryRead,
                RequestLifetime.None);
            var historianContext = new HistorianOperationContext(
                context,
                operationContext,
                notifier,
                HistoryUpdateType.Insert);
            HistorianPage<HistorianEventRecord> page =
                await provider.ReadEventsAsync(
                    historianContext,
                    new HistorianEventReadRequest
                    {
                        NodeId = notifier.NodeId,
                        StartTime = DateTimeUtc.MinValue,
                        EndTime = DateTimeUtc.MaxValue,
                        IsForward = true,
                        Filter = new EventFilter()
                    },
                    default,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(page.Values[0].EventId.IsEmpty, Is.False);
            Assert.That(page.Values[0].EventType, Is.EqualTo(ObjectTypeIds.BaseEventType));
        }

        [Test]
        public async Task HistorianRejectionDoesNotSuppressLiveEventDeliveryAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:event-capture");
            var registry = new HistorianProviderRegistry(namespaceUris);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(value => value.TypeTree)
                .Returns(new TypeTable(namespaceUris));
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(registry);
            var context = new ServerSystemContext(server.Object);
            var notifier = new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 1),
                BrowseName = new QualifiedName("Notifier", 1)
            };
            int forwarded = 0;
            notifier.OnReportEventAsync = (_, _, _, _) =>
            {
                forwarded++;
                return default;
            };
            var builder = new HistorianBuilder(server.Object);
            builder.UseProvider(new RejectingEventProvider());
            await builder.HistorizeEventsAsync(
                notifier,
                context,
                installConfiguration: false,
                capabilities: HistorianNodeCapabilities.ReadWrite with
                {
                    EventTypes = [ObjectTypeIds.BaseEventType]
                },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            var reportedEvent = new BaseEventState(null);
            reportedEvent.Initialize(
                context,
                notifier,
                EventSeverity.Medium,
                new LocalizedText("live delivery must win"));

            await notifier.ReportEventAsync(
                context,
                reportedEvent,
                CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            await notifier.ReportEventAsync(
                context,
                reportedEvent,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(forwarded, Is.EqualTo(2));
            Assert.That(
                async () => await builder.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task UnsupportedEventTypeDoesNotSuppressLiveEventDeliveryAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:event-capture");
            var registry = new HistorianProviderRegistry(namespaceUris);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(value => value.TypeTree)
                .Returns(new TypeTable(namespaceUris));
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(registry);
            var context = new ServerSystemContext(server.Object);
            var notifier = new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 1),
                BrowseName = new QualifiedName("Notifier", 1)
            };
            int forwarded = 0;
            notifier.OnReportEventAsync = (_, _, _, _) =>
            {
                forwarded++;
                return default;
            };
            var builder = new HistorianBuilder(server.Object);
            builder.UseProvider(new RejectingEventProvider());
            await builder.HistorizeEventsAsync(
                notifier,
                context,
                installConfiguration: false,
                capabilities: HistorianNodeCapabilities.EventReadWrite with
                {
                    EventTypes = [ObjectTypeIds.AuditEventType]
                },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            var reportedEvent = new BaseEventState(null);
            reportedEvent.Initialize(
                context,
                notifier,
                EventSeverity.Medium,
                new LocalizedText("unsupported historical type"));

            Assert.That(
                async () => await notifier.ReportEventAsync(
                    context,
                    reportedEvent,
                    CancellationToken.None).ConfigureAwait(false),
                Throws.Nothing);
            await builder.DisposeAsync().ConfigureAwait(false);

            Assert.That(forwarded, Is.EqualTo(1));
        }

        private sealed class RejectingEventProvider :
            HistorianProviderBase,
            IHistorianEventProvider
        {
            public ValueTask<HistorianPage<HistorianEventRecord>> ReadEventsAsync(
                HistorianOperationContext context,
                HistorianEventReadRequest request,
                HistorianResumeToken resumeToken,
                CancellationToken ct)
            {
                return new ValueTask<
                    HistorianPage<HistorianEventRecord>>(
                    HistorianPage<HistorianEventRecord>.Empty);
            }

            public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>
                InsertEventsAsync(
                    HistorianOperationContext context,
                    NodeId nodeId,
                    ArrayOf<HistorianEventRecord> events,
                    CancellationToken ct)
            {
                return Reject(events.Count);
            }

            public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>
                ReplaceEventsAsync(
                    HistorianOperationContext context,
                    NodeId nodeId,
                    ArrayOf<HistorianEventRecord> events,
                    CancellationToken ct)
            {
                return Reject(events.Count);
            }

            public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>
                UpdateEventsAsync(
                    HistorianOperationContext context,
                    NodeId nodeId,
                    ArrayOf<HistorianEventRecord> events,
                    CancellationToken ct)
            {
                return Reject(events.Count);
            }

            public ValueTask<HistorianUpdateOutcome<HistorianEventRecord>>
                DeleteEventsAsync(
                    HistorianOperationContext context,
                    NodeId nodeId,
                    ArrayOf<ByteString> eventIds,
                    CancellationToken ct)
            {
                return Reject(eventIds.Count);
            }

            private static ValueTask<
                HistorianUpdateOutcome<HistorianEventRecord>> Reject(
                    int count)
            {
                var statuses = new StatusCode[count];
                for (int i = 0; i < statuses.Length; i++)
                {
                    statuses[i] = StatusCodes.BadUnexpectedError;
                }
                return new ValueTask<
                    HistorianUpdateOutcome<HistorianEventRecord>>(
                    new HistorianUpdateOutcome<HistorianEventRecord>(
                        statuses));
            }
        }
    }
}
