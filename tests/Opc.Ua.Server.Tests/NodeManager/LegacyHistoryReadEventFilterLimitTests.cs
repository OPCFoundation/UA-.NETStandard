// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Covers the deprecated synchronous node manager API on purpose.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// The select clause limit of a HistoryRead event filter applies to legacy
    /// <see cref="CustomNodeManager2"/> node managers too, so a filter over the limit never
    /// reaches their historian (and the continuation points that keep the filter).
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("Historian")]
    public sealed class LegacyHistoryReadEventFilterLimitTests
    {
        [Test]
        public async Task LegacyNodeManagerRejectsEventFilterOverSelectClauseLimitAsync()
        {
            LegacyEventHistoryNodeManager? nodeManager = null;
            var fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(new Factory(created => nodeManager = created));
                return server;
            })
            {
                AutoAccept = true
            };

            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync(nameof(LegacyEventHistoryNodeManager))
                        .ConfigureAwait(false);
                Assert.That(nodeManager, Is.Not.Null);
                ushort namespaceIndex = (ushort)server.CurrentInstance.NamespaceUris
                    .GetIndex(LegacyEventHistoryNodeManager.NamespaceUri);
                var notifierId = new NodeId(LegacyEventHistoryNodeManager.NotifierName, namespaceIndex);

                async Task<StatusCode> ReadEventsAsync(int selectClauses)
                {
                    return (await ReadAsync(selectClauses, default, false).ConfigureAwait(false)).StatusCode;
                }

                async Task<HistoryReadResult> ReadAsync(
                    int selectClauses,
                    ByteString continuationPoint,
                    bool release)
                {
                    var filter = new EventFilter();
                    for (int ii = 0; ii < selectClauses; ii++)
                    {
                        filter.AddSelectClause(
                            ObjectTypeIds.BaseEventType,
                            BrowseNames.Message,
                            Attributes.Value);
                    }
                    requestHeader.Timestamp = DateTimeUtc.Now;
                    HistoryReadResponse response = await server.HistoryReadAsync(
                        secureChannelContext,
                        requestHeader,
                        new ExtensionObject(new ReadEventDetails
                        {
                            StartTime = DateTime.UtcNow.AddMinutes(-1),
                            EndTime = DateTime.UtcNow.AddSeconds(1),
                            NumValuesPerNode = 0,
                            Filter = filter
                        }),
                        TimestampsToReturn.Neither,
                        release,
                        [new HistoryReadValueId { NodeId = notifierId, ContinuationPoint = continuationPoint }],
                        RequestLifetime.None).ConfigureAwait(false);
                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    return response.Results[0];
                }

                int limit = fixture.Config.ServerConfiguration!.MaxSelectClauseParameters;
                Assert.That(limit, Is.GreaterThan(0));

                Assert.That(
                    await ReadEventsAsync(limit + 1).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.BadEventFilterInvalid));
                Assert.That(nodeManager!.EventReads, Is.Zero);

                Assert.That(
                    await ReadEventsAsync(limit).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(nodeManager.EventReads, Is.EqualTo(1));

                // resuming an issued point with an oversized filter is rejected as well, and
                // the point is released instead of reaching the legacy manager.
                nodeManager.IssueContinuationPoints = true;
                HistoryReadResult first = await ReadAsync(limit, default, false).ConfigureAwait(false);
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(first.ContinuationPoint.IsEmpty, Is.False);
                Assert.That(nodeManager.EventReads, Is.EqualTo(2));
                nodeManager.IssueContinuationPoints = false;

                HistoryReadResult resumed = await ReadAsync(limit + 1, first.ContinuationPoint, false)
                    .ConfigureAwait(false);
                Assert.That(resumed.StatusCode, Is.EqualTo(StatusCodes.BadEventFilterInvalid));
                Assert.That(nodeManager.EventReads, Is.EqualTo(2));
                Assert.That(
                    (await ReadAsync(limit, first.ContinuationPoint, false).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadContinuationPointInvalid));

                // a release-only request still releases a point whatever its filter.
                nodeManager.IssueContinuationPoints = true;
                HistoryReadResult second = await ReadAsync(limit, default, false).ConfigureAwait(false);
                Assert.That(second.ContinuationPoint.IsEmpty, Is.False);
                nodeManager.IssueContinuationPoints = false;
                Assert.That(
                    (await ReadAsync(limit + 1, second.ContinuationPoint, true).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    (await ReadAsync(limit, second.ContinuationPoint, false).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.BadContinuationPointInvalid));

                requestHeader.Timestamp = DateTimeUtc.Now;
                await server.CloseSessionAsync(secureChannelContext, requestHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class Factory(Action<LegacyEventHistoryNodeManager> created) : INodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => [LegacyEventHistoryNodeManager.NamespaceUri];

            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
#pragma warning disable CA2000 // Ownership of the node manager is transferred to the server.
                var nodeManager = new LegacyEventHistoryNodeManager(server, configuration);
#pragma warning restore CA2000
                created(nodeManager);
                return nodeManager;
            }
        }

        /// <summary>
        /// A legacy node manager with one event notifier that records its event history reads.
        /// </summary>
        private sealed class LegacyEventHistoryNodeManager : CustomNodeManager2
        {
            public const string NamespaceUri = "urn:tests:legacy-event-history";
            public const string NotifierName = "Notifier";

            public LegacyEventHistoryNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, NamespaceUri)
            {
            }

            public int EventReads => Volatile.Read(ref m_eventReads);

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                base.CreateAddressSpace(externalReferences);
                ushort namespaceIndex = NamespaceIndexes[0];
                var notifier = new BaseObjectState(null)
                {
                    NodeId = new NodeId(NotifierName, namespaceIndex),
                    BrowseName = new QualifiedName(NotifierName, namespaceIndex),
                    DisplayName = new LocalizedText(NotifierName),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType,
                    EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead
                };
                AddPredefinedNode(SystemContext, notifier);
            }

            protected override void HistoryReadEvents(
                ServerSystemContext context,
                ReadEventDetails details,
                TimestampsToReturn timestampsToReturn,
                ArrayOf<HistoryReadValueId> nodesToRead,
                IList<HistoryReadResult> results,
                IList<ServiceResult> errors,
                List<NodeHandle> nodesToProcess,
                IDictionary<NodeId, NodeState> cache)
            {
                Interlocked.Increment(ref m_eventReads);
                foreach (NodeHandle handle in nodesToProcess)
                {
                    errors[handle.Index] = ServiceResult.Good;
                    if (IssueContinuationPoints &&
                        context.OperationContext?.Session?.ContinuationPoints is { } continuationPoints)
                    {
#pragma warning disable CA2000 // The session takes ownership of the saved point.
                        var point = new ContinuationPoint();
#pragma warning restore CA2000
                        continuationPoints.SaveHistory(point);
                        results[handle.Index].ContinuationPoint = point.Id.ToByteArray().ToByteString();
                    }
                }
            }

            /// <summary>
            /// Whether event reads return a continuation point.
            /// </summary>
            public bool IssueContinuationPoints { get; set; }

            private int m_eventReads;
        }

        private sealed class ContinuationPoint : IHistoryContinuationPoint
        {
            public Guid Id { get; } = Guid.NewGuid();

            public void Dispose()
            {
            }
        }
    }
}
