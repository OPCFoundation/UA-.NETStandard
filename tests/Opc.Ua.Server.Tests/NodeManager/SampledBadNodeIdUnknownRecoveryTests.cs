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

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that a sampled Bad_NodeIdUnknown produced by a real NodeManager read
    /// does not permanently block a MonitoredItem queue, mirroring the read loop that
    /// <see cref="SamplingGroup"/> runs for sampled data change items.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("MonitoredItem")]
    [Parallelizable]
    public sealed class SampledBadNodeIdUnknownRecoveryTests
    {
        [Test]
        public async Task TransientBadNodeIdUnknownSampleRecoversToTheNextGoodValueAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var logger = new Mock<ILogger>();
                using var manager = new UnresolvableNodeManager(
                    server.Object,
                    CreateConfiguration(),
                    logger.Object);
                var asyncManager = new AsyncNodeManagerAdapter(manager);
                NodeId nodeId = manager.UnresolvableNodeId;
                using MonitoredItem item = CreateMonitoredItem(server.Object, asyncManager, nodeId);

                manager.NodeIsResolvable = false;
                (DataValue badValue, ServiceResult badError) =
                    await SampleAsync(asyncManager, nodeId).ConfigureAwait(false);
                item.QueueValue(badValue, badError);

                manager.NodeIsResolvable = true;
                (DataValue goodValue, ServiceResult goodError) =
                    await SampleAsync(asyncManager, nodeId).ConfigureAwait(false);
                item.QueueValue(goodValue, goodError);

                var notifications = new Queue<MonitoredItemNotification>();
                var diagnostics = new Queue<DiagnosticInfo>();
                bool more = item.Publish(
                    new OperationContext(item),
                    notifications,
                    diagnostics,
                    10,
                    logger.Object);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        badError.StatusCode.Code,
                        Is.EqualTo(StatusCodes.BadNodeIdUnknown),
                        "the node manager read must produce the sampled bad status");
                    Assert.That(
                        ServiceResult.IsGood(goodError),
                        Is.True,
                        "the recovered read must succeed");
                    Assert.That(
                        goodValue.WrappedValue,
                        Is.EqualTo(new Variant(42)),
                        "the recovered read must return the node value");
                    Assert.That(((IMonitoredItemLifecycle)item).IsDeleted, Is.False);
                    Assert.That(notifications, Has.Count.EqualTo(1));
                    Assert.That(
                        notifications.Peek().Value.StatusCode.Code,
                        Is.EqualTo(StatusCodes.Good),
                        "published status");
                    Assert.That(
                        notifications.Peek().Value.WrappedValue,
                        Is.EqualTo(new Variant(42)));
                    Assert.That(more, Is.False);
                });
            }
        }

        /// <summary>
        /// Runs the same read and queue sequence that SamplingGroup performs per sample.
        /// </summary>
        private static async Task<(DataValue Value, ServiceResult Error)> SampleAsync(
            AsyncNodeManagerAdapter manager,
            NodeId nodeId)
        {
            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = nodeId, AttributeId = Attributes.Value }
            };
            var values = new List<DataValue> { default };
            var errors = new List<ServiceResult> { null };

            await manager.ReadAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.Read,
                    RequestLifetime.None),
                0,
                nodesToRead,
                values,
                errors).ConfigureAwait(false);

            return (values[0], errors[0]);
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    AvailableSamplingRates = []
                }
            };
        }

        private static MonitoredItem CreateMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            NodeId nodeId)
        {
            return new MonitoredItem(
                server,
                nodeManager,
                new object(),
                subscriptionId: 1,
                id: 2,
                itemToMonitor: new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: 3,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 0,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 0);
        }

        /// <summary>
        /// A NodeManager whose node resolves through the unvalidated handle path, so a
        /// read reports Bad_NodeIdUnknown while the node cannot be resolved.
        /// </summary>
        private sealed class UnresolvableNodeManager : CustomNodeManager2
        {
            public UnresolvableNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger)
                : base(server, configuration, false, logger, DeterministicServerMock.TestNamespaceUri)
            {
                UnresolvableNodeId = new NodeId("Sampled", NamespaceIndex);
                m_node = new BaseDataVariableState(null);
                m_node.CreateAsPredefinedNode(SystemContext);
                m_node.NodeId = UnresolvableNodeId;
                m_node.BrowseName = new QualifiedName("Sampled", NamespaceIndex);
                m_node.DisplayName = new LocalizedText("Sampled");
                m_node.DataType = DataTypeIds.Int32;
                m_node.ValueRank = ValueRanks.Scalar;
                m_node.Value = 42;
            }

            public NodeId UnresolvableNodeId { get; }

            public bool NodeIsResolvable { get; set; }

            protected override NodeHandle GetManagerHandle(
                ServerSystemContext context,
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache)
            {
                if (!IsNodeIdInNamespace(nodeId) || nodeId != UnresolvableNodeId)
                {
                    return null;
                }

                // Report an unvalidated handle so the read has to resolve the node,
                // exactly like a NodeManager that serves component paths.
                return new NodeHandle { NodeId = nodeId, Node = null, Validated = false };
            }

            protected override NodeState ValidateNode(
                ServerSystemContext context,
                NodeHandle handle,
                IDictionary<NodeId, NodeState> cache)
            {
                if (!NodeIsResolvable)
                {
                    return null;
                }

                handle.Node = m_node;
                handle.Validated = true;
                return m_node;
            }

            private readonly BaseDataVariableState m_node;
        }
    }
}