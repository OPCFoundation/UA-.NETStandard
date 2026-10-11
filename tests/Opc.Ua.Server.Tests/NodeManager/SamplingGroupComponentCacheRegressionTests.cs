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

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that deleting a sampling-group monitored item does not release a component-cache
    /// reference that the item never took.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroup")]
    public sealed class SamplingGroupComponentCacheRegressionTests
    {
        /// <summary>
        /// Verifies the synchronous node manager keeps a foreign cache reference after a delete.
        /// </summary>
        [Test]
        public void DeletingSampledItemKeepsForeignComponentCacheReference()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new SyncHooks(server.Object))
            {
                manager.HoldCacheReference();
                IMonitoredItem item = manager.Create();
                Assert.That(item, Is.Not.Null);

                manager.Delete(item);

                Assert.That(manager.CachedNode, Is.Not.Null, "the delete released a reference it never took");
            }
        }

        /// <summary>
        /// Verifies the asynchronous node manager keeps a foreign cache reference after a delete.
        /// </summary>
        [Test]
        public async Task DeletingSampledItemKeepsForeignComponentCacheReferenceAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new AsyncHooks(server.Object))
            {
                await manager.InitializeAsync().ConfigureAwait(false);
                manager.HoldCacheReference();
                IMonitoredItem item = await manager.CreateAsync().ConfigureAwait(false);
                Assert.That(item, Is.Not.Null);

                await manager.DeleteAsync(item).ConfigureAwait(false);

                Assert.That(manager.CachedNode, Is.Not.Null, "the delete released a reference it never took");
            }
        }

        private static OperationContext NewContext(RequestType requestType)
        {
            return new OperationContext(new RequestHeader(), null, requestType, RequestLifetime.None);
        }

        private static MonitoredItemCreateRequest NewRequest(NodeId nodeId)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Disabled,
                RequestedParameters = new MonitoringParameters
                {
                    SamplingInterval = 1000,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            };
        }

        private static BaseDataVariableState NewVariable(ushort namespaceIndex)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId(1, namespaceIndex),
                DataType = DataTypeIds.Int32,
                Value = 1,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead
            };
        }

        private sealed class SyncHooks : CustomNodeManager2
        {
            public SyncHooks(IServerInternal server)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    true,
                    NullLogger.Instance,
                    "urn:tests:sampling-cache-sync")
            {
                m_node = NewVariable(NamespaceIndex);
                AddPredefinedNode(SystemContext, m_node);
            }

            public NodeState CachedNode => LookupNodeInComponentCache(SystemContext, Handle())!;

            public void HoldCacheReference()
            {
                AddNodeToComponentCache(SystemContext, Handle(), m_node);
            }

            public IMonitoredItem Create()
            {
                using OperationContext context = NewContext(RequestType.CreateMonitoredItems);
                var errors = new List<ServiceResult> { null! };
                var filterErrors = new List<MonitoringFilterResult> { null! };
                var items = new List<IMonitoredItem> { null! };
                CreateMonitoredItems(
                    context, 1, 1000, TimestampsToReturn.Both, [NewRequest(m_node.NodeId)],
                    errors, filterErrors, items, false, new MonitoredItemIdFactory());
                return (ServiceResult.IsGood(errors[0]) ? items[0] : null)!;
            }

            public void Delete(IMonitoredItem item)
            {
                using OperationContext context = NewContext(RequestType.DeleteMonitoredItems);
                var errors = new List<ServiceResult> { null! };
                DeleteMonitoredItems(context, [item], new List<bool> { false }, errors);
                Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            }

            private NodeHandle Handle()
            {
                return new NodeHandle { NodeId = m_node.NodeId, Node = m_node, Validated = true };
            }

            private readonly BaseDataVariableState m_node;
        }

        private sealed class AsyncHooks : AsyncCustomNodeManager
        {
            public AsyncHooks(IServerInternal server)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    true,
                    NullLogger.Instance,
                    "urn:tests:sampling-cache-async")
            {
                m_node = NewVariable(NamespaceIndex);
            }

            public NodeState CachedNode => LookupNodeInComponentCache(SystemContext, Handle())!;

            public ValueTask InitializeAsync()
            {
                return AddPredefinedNodeAsync(SystemContext, m_node);
            }

            public void HoldCacheReference()
            {
                AddNodeToComponentCache(SystemContext, Handle(), m_node);
            }

            public async Task<IMonitoredItem> CreateAsync()
            {
                using OperationContext context = NewContext(RequestType.CreateMonitoredItems);
                var errors = new List<ServiceResult> { null! };
                var filterErrors = new List<MonitoringFilterResult> { null! };
                var items = new List<IMonitoredItem> { null! };
                await CreateMonitoredItemsAsync(
                    context, 1, 1000, TimestampsToReturn.Both, [NewRequest(m_node.NodeId)],
                    errors, filterErrors, items, false, new MonitoredItemIdFactory()).ConfigureAwait(false);
                return (ServiceResult.IsGood(errors[0]) ? items[0] : null)!;
            }

            public async Task DeleteAsync(IMonitoredItem item)
            {
                using OperationContext context = NewContext(RequestType.DeleteMonitoredItems);
                var errors = new List<ServiceResult> { null! };
                await DeleteMonitoredItemsAsync(context, [item], new List<bool> { false }, errors)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            }

            private NodeHandle Handle()
            {
                return new NodeHandle { NodeId = m_node.NodeId, Node = m_node, Validated = true };
            }

            private readonly BaseDataVariableState m_node;
        }
    }
}
