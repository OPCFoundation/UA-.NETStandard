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
                        false,
                        [new HistoryReadValueId { NodeId = notifierId }],
                        RequestLifetime.None).ConfigureAwait(false);
                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    return response.Results[0].StatusCode;
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
                }
            }

            private int m_eventReads;
        }
    }
}
