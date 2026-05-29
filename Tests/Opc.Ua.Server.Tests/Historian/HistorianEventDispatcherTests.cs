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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianEventDispatcherTests
    {
        [Test]
        public async Task EventInsertReadRoundTripAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var notifier = new NodeId("notifier", 1);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);
            var eventId = new ByteString(Encoding.UTF8.GetBytes("evt-1"));

            var record = new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                when,
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    [BrowseNames.EventId] = new Variant(eventId),
                    [BrowseNames.EventType] = new Variant(ObjectTypeIds.BaseEventType),
                    [BrowseNames.Time] = new Variant((DateTimeUtc)when),
                    [BrowseNames.Message] = new Variant(new LocalizedText("hello"))
                });

            IList<StatusCode> insertStatuses = await provider.InsertEventsAsync(
                context, notifier, [record], CancellationToken.None);
            Assert.That(StatusCode.IsGood(insertStatuses[0]), Is.True);

            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventId, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Message, Attributes.Value);

            HistorianPage<HistorianEventRecord> page = await provider.ReadEventsAsync(
                context,
                new HistorianEventReadRequest
                {
                    NodeId = notifier,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                    Filter = filter
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values, Has.Count.EqualTo(1));

            // Project through dispatcher's selector helper.
            HistoryEventFieldList projected = HistorianDispatcher.ProjectEventFields(page.Values[0], filter);
            Assert.That(projected.EventFields, Has.Count.EqualTo(2));
            Assert.That(projected.EventFields[0].TryGetValue(out ByteString idOut), Is.True);
            Assert.That(idOut, Is.EqualTo(eventId));
            Assert.That(projected.EventFields[1].TryGetValue(out LocalizedText messageOut), Is.True);
            Assert.That(messageOut.Text, Is.EqualTo("hello"));
        }

        [Test]
        public async Task EventDeleteRemovesByIdAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var notifier = new NodeId("notifier", 1);

            HistorianOperationContext context = CreateContext();

            var ids = new ByteString[2];
            for (int i = 0; i < 2; i++)
            {
                ids[i] = new ByteString(Encoding.UTF8.GetBytes($"evt-{i}"));
                await provider.InsertEventsAsync(context, notifier,
                    [new HistorianEventRecord(ids[i], ObjectTypeIds.BaseEventType,
                        BaseTime.AddSeconds(i),
                        new Dictionary<string, Variant>(StringComparer.Ordinal))],
                    CancellationToken.None);
            }

            IList<StatusCode> deleted = await provider.DeleteEventsAsync(
                context, notifier, [ids[0]], CancellationToken.None);
            Assert.That(StatusCode.IsGood(deleted[0]), Is.True);

            HistorianPage<HistorianEventRecord> remaining = await provider.ReadEventsAsync(
                context,
                new HistorianEventReadRequest
                {
                    NodeId = notifier,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                    Filter = new EventFilter()
                },
                default,
                CancellationToken.None);

            Assert.That(remaining.Values, Has.Count.EqualTo(1));
            Assert.That(remaining.Values[0].EventId, Is.EqualTo(ids[1]));
        }

        private static readonly DateTime BaseTime
            = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static HistorianOperationContext CreateContext()
        {
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var opContext = new OperationContext(
                new RequestHeader(), null!, RequestType.HistoryUpdate, RequestLifetime.None);
            var systemContext = new ServerSystemContext(mockServer.Object, opContext);
            return new HistorianOperationContext(
                systemContext, opContext, null, HistoryUpdateType.Insert);
        }
    }
}
