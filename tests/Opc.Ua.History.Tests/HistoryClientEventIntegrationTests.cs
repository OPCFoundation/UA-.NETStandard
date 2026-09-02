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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.History.Tests
{
    [TestFixture]
    [Category("Historian")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class HistoryClientEventIntegrationTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(HistoryClientEventIntegrationTests),
                Guid.NewGuid().ToString("N"));
            m_serverFixture = new ServerFixture<ReferenceServer>(
                telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                OperationLimits = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.AddAsync(
                new EventHistoryNodeManagerFactory(),
                callerContext: null).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(telemetry: m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            var serverUrl = new Uri(
                $"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}");
            m_session = await m_clientFixture
                .ConnectAsync(serverUrl, SecurityPolicies.None)
                .ConfigureAwait(false);

            int namespaceIndex = m_session.NamespaceUris.GetIndex(TestNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            m_notifierId = new NodeId(NotifierIdentifier, (ushort)namespaceIndex);
            m_annotationVariableId = new NodeId(
                AnnotationVariableIdentifier,
                (ushort)namespaceIndex);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync().ConfigureAwait(false);
                m_session.Dispose();
            }
            if (m_clientFixture != null)
            {
                await m_clientFixture.DisposeAsync().ConfigureAwait(false);
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public async Task EventHistoryRoundTripsThroughHistoryClientAsync()
        {
            var client = new HistoryClient(m_session);
            EventFilter filter = CreateEventFilter();
            DateTime eventTime = DateTime.UtcNow.AddYears(-10).AddSeconds(1401);
            var eventId = (ByteString)new byte[] { 0x43, 0x87 };

            ArrayOf<StatusCode> insertStatuses = await client.InsertEventsAsync(
                m_notifierId,
                filter,
                [CreateEvent(eventId, eventTime, "inserted")]).ConfigureAwait(false);
            Assert.That(insertStatuses, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(insertStatuses[0]), Is.True);

            List<HistoryEventFieldList> events = await ReadEventsAsync(
                client,
                filter,
                eventTime).ConfigureAwait(false);
            AssertEvent(events, eventId, "inserted");

            ArrayOf<StatusCode> replaceStatuses = await client.ReplaceEventsAsync(
                m_notifierId,
                filter,
                [CreateEvent(eventId, eventTime, "replaced")]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(replaceStatuses[0]), Is.True);

            events = await ReadEventsAsync(client, filter, eventTime).ConfigureAwait(false);
            AssertEvent(events, eventId, "replaced");

            ArrayOf<StatusCode> updateStatuses = await client.UpdateEventsAsync(
                m_notifierId,
                filter,
                [CreateEvent(eventId, eventTime, "updated")]).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(updateStatuses[0]), Is.True);

            events = await ReadEventsAsync(client, filter, eventTime).ConfigureAwait(false);
            AssertEvent(events, eventId, "updated");

            ArrayOf<StatusCode> deleteStatuses = await client.DeleteEventsAsync(
                m_notifierId,
                [eventId]).ConfigureAwait(false);
            Assert.That(deleteStatuses, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(deleteStatuses[0]), Is.True);

            events = await ReadEventsAsync(client, filter, eventTime).ConfigureAwait(false);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public async Task BatchedAnnotationsRoundTripAndRemoveAsync()
        {
            var client = new HistoryClient(m_session);
            DateTime firstTime = DateTime.UtcNow.AddYears(-10).AddSeconds(1501);
            DateTime secondTime = firstTime.AddSeconds(1);
            ArrayOf<Annotation> annotations =
            [
                new Annotation
                {
                    AnnotationTime = firstTime,
                    Message = "first batch annotation",
                    UserName = "BatchUser"
                },
                new Annotation
                {
                    AnnotationTime = secondTime,
                    Message = "second batch annotation",
                    UserName = "BatchUser"
                }
            ];

            ArrayOf<StatusCode> writeStatuses = await client.WriteAnnotationsAsync(
                m_annotationVariableId,
                annotations).ConfigureAwait(false);
            AssertGoodStatuses(writeStatuses, "write");

            var readBack = new List<Annotation>();
            await foreach (Annotation annotation in client.ReadAnnotationsAsync(
                m_annotationVariableId,
                firstTime.AddMilliseconds(-1),
                secondTime.AddMilliseconds(1)).ConfigureAwait(false))
            {
                readBack.Add(annotation);
            }

            Assert.That(readBack, Has.Count.EqualTo(2));
            Assert.That(
                readBack.Exists(annotation => annotation.Message == "first batch annotation"),
                Is.True);
            Assert.That(
                readBack.Exists(annotation => annotation.Message == "second batch annotation"),
                Is.True);

            ArrayOf<StatusCode> deleteStatuses = await client.WriteAnnotationsAsync(
                m_annotationVariableId,
                annotations,
                PerformUpdateType.Remove).ConfigureAwait(false);
            AssertGoodStatuses(deleteStatuses, "delete");

            readBack.Clear();
            await foreach (Annotation annotation in client.ReadAnnotationsAsync(
                m_annotationVariableId,
                firstTime.AddMilliseconds(-1),
                secondTime.AddMilliseconds(1)).ConfigureAwait(false))
            {
                readBack.Add(annotation);
            }
            Assert.That(readBack, Is.Empty);
        }

        private static EventFilter CreateEventFilter()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventId,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventType,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Time,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);
            return filter;
        }

        private static HistoryEventFieldList CreateEvent(
            ByteString eventId,
            DateTime eventTime,
            string message)
        {
            return new HistoryEventFieldList
            {
                EventFields =
                [
                    new Variant(eventId),
                    new Variant(ObjectTypeIds.BaseEventType),
                    new Variant((DateTimeUtc)eventTime),
                    new Variant(new LocalizedText(message))
                ]
            };
        }

        private async Task<List<HistoryEventFieldList>> ReadEventsAsync(
            HistoryClient client,
            EventFilter filter,
            DateTime eventTime)
        {
            var events = new List<HistoryEventFieldList>();
            await foreach (HistoryEventFieldList fields in client.ReadEventsAsync(
                m_notifierId,
                eventTime.AddMilliseconds(-1),
                eventTime.AddMilliseconds(1),
                filter).ConfigureAwait(false))
            {
                events.Add(fields);
            }
            return events;
        }

        private static void AssertEvent(
            List<HistoryEventFieldList> events,
            ByteString expectedEventId,
            string expectedMessage)
        {
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(
                events[0].EventFields[0].TryGetValue(out ByteString eventId),
                Is.True);
            Assert.That(eventId, Is.EqualTo(expectedEventId));
            Assert.That(
                events[0].EventFields[3].TryGetValue(out LocalizedText message),
                Is.True);
            Assert.That(message.Text, Is.EqualTo(expectedMessage));
        }

        private static void AssertGoodStatuses(
            ArrayOf<StatusCode> statuses,
            string operation)
        {
            Assert.That(statuses, Has.Count.EqualTo(2));
            for (int i = 0; i < statuses.Count; i++)
            {
                Assert.That(
                    StatusCode.IsGood(statuses[i]),
                    Is.True,
                    $"Annotation {operation} {i} failed with 0x{statuses[i].Code:X8}.");
            }
        }

        private sealed class EventHistoryNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [TestNamespaceUri];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
#pragma warning disable CA2000 // Ownership transfers to the server node-manager lifecycle.
                var nodeManager = new EventHistoryNodeManager(server, configuration);
#pragma warning restore CA2000
                return new ValueTask<IAsyncNodeManager>(nodeManager);
            }
        }

        private sealed class EventHistoryNodeManager : AsyncCustomNodeManager
        {
            public EventHistoryNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
                : base(server, configuration, TestNamespaceUri)
            {
                m_provider = new InMemoryHistorianProvider();
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var notifier = new BaseObjectState(null);
                notifier.CreateAsPredefinedNode(SystemContext);
                notifier.NodeId = new NodeId(NotifierIdentifier, NamespaceIndex);
                notifier.BrowseName = new QualifiedName("EventHistoryNotifier", NamespaceIndex);
                notifier.DisplayName = new LocalizedText("EventHistoryNotifier");
                notifier.TypeDefinitionId = ObjectTypeIds.BaseObjectType;
                notifier.EventNotifier =
                    EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;

                m_provider.Register(notifier.NodeId);
                await AddPredefinedNodeAsync(
                    SystemContext,
                    notifier,
                    cancellationToken).ConfigureAwait(false);

                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(SystemContext);
                variable.NodeId = new NodeId(AnnotationVariableIdentifier, NamespaceIndex);
                variable.BrowseName = new QualifiedName(
                    "AnnotationHistoryVariable",
                    NamespaceIndex);
                variable.DisplayName = new LocalizedText("AnnotationHistoryVariable");
                variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
                variable.DataType = DataTypeIds.Double;
                variable.ValueRank = ValueRanks.Scalar;
                variable.Value = new Variant(0.0);
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;

                await using (var builder = new HistorianBuilder(Server))
                {
                    builder
                        .UseProvider(m_provider)
                        .Historize(
                            variable,
                            systemContext: SystemContext,
                            capabilities: HistorianNodeCapabilities.ReadWrite,
                            autoCapture: false);
                }
                await AddPredefinedNodeAsync(
                    SystemContext,
                    variable,
                    cancellationToken).ConfigureAwait(false);
            }

            protected override IHistorianProvider? GetHistorianProvider(NodeState node)
            {
                return m_provider;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_provider.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly InMemoryHistorianProvider m_provider;
        }

        private const string TestNamespaceUri =
            "urn:opcfoundation:history-tests:event-history-client";
        private const string NotifierIdentifier = "EventHistoryNotifier";
        private const string AnnotationVariableIdentifier = "AnnotationHistoryVariable";

        private string m_pkiRoot = null!;
        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private Opc.Ua.Client.ISession m_session = null!;
        private ITelemetryContext m_telemetry = null!;
        private NodeId m_notifierId;
        private NodeId m_annotationVariableId;
    }
}
