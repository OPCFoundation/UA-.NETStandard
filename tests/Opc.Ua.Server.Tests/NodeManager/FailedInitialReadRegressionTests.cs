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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class FailedInitialReadRegressionTests
    {
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(true, 3)]
        public void FatalInitialReadRemovesItemRegistrationAndCache(bool samplingGroups, int failure)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CreateHooks(server.Object, samplingGroups))
            {
                manager.ReadStatus = failure switch
                {
                    0 => StatusCodes.BadAttributeIdInvalid,
                    1 => StatusCodes.BadDataEncodingInvalid,
                    2 => StatusCodes.BadDataEncodingUnsupported,
                    _ => StatusCodes.BadNoCommunication
                };
                (ServiceResult status, IMonitoredItem item) = manager.Create();
                if (failure < 3)
                {
                    Assert.That(status.StatusCode, Is.EqualTo(manager.ReadStatus));
                    Assert.That(item, Is.Null);
                    Assert.That(manager.ItemCount, Is.Zero);
                    Assert.That(manager.CreatedCount, Is.Zero);
                    Assert.That(manager.CachedNode, Is.Null);

                    manager.ReadStatus = StatusCodes.Good;
                    (ServiceResult recoveredStatus, IMonitoredItem recoveredItem) = manager.Create();
                    Assert.That(recoveredStatus.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(manager.ItemCount, Is.EqualTo(1));
                    Assert.That(manager.GetItem(recoveredItem.Id), Is.SameAs(recoveredItem));
                    Assert.That(manager.CreatedCount, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(manager.ItemCount, Is.EqualTo(1));
                    Assert.That(manager.GetItem(item.Id), Is.SameAs(item));
                    Assert.That(manager.CreatedCount, Is.EqualTo(1));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedInitialReadPreservesPreviouslyRegisteredItem(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CreateHooks(server.Object, samplingGroups))
            {
                (ServiceResult initialStatus, IMonitoredItem original) = manager.Create();
                Assert.That(initialStatus.StatusCode, Is.EqualTo(StatusCodes.Good));
                NodeState cached = manager.CachedNode;
                manager.ReadStatus = StatusCodes.BadDataEncodingInvalid;

                (ServiceResult status, IMonitoredItem rejected) = manager.Create();
                Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadDataEncodingInvalid));
                Assert.That(rejected, Is.Null);
                Assert.That(manager.ItemCount, Is.EqualTo(1));
                Assert.That(manager.GetItem(original.Id), Is.SameAs(original));
                Assert.That(manager.CachedNode, Is.SameAs(cached));
                Assert.That(manager.CreatedCount, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThrowingInitialReadAlsoRemovesTheUnacceptedItem(bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var manager = new CreateHooks(server.Object, samplingGroups) { ThrowOnRead = true })
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => manager.Create());
                Assert.That(error.Message, Is.EqualTo("initial read failed"));
                Assert.That(manager.ItemCount, Is.Zero);
                Assert.That(manager.CachedNode, Is.Null);
                manager.ThrowOnRead = false;
                (ServiceResult status, IMonitoredItem item) = manager.Create();
                Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.GetItem(item.Id), Is.SameAs(item));
            }
        }

        private sealed class CreateHooks : CustomNodeManager2
        {
            public CreateHooks(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups,
                    NullLogger.Instance,
                    "urn:tests:initial-read-cleanup")
            {
                m_node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(1, NamespaceIndex),
                    DataType = DataTypeIds.Int32,
                    Value = 1,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    OnSimpleReadValue = OnRead
                };
                AddPredefinedNode(SystemContext, m_node);
            }

            public StatusCode ReadStatus { get; set; }
            public bool ThrowOnRead { get; set; }
            public int CreatedCount { get; private set; }
            public int ItemCount => MonitoredItems.Count;
            public NodeState CachedNode => LookupNodeInComponentCache(
                SystemContext, new NodeHandle { NodeId = m_node.NodeId });

            public (ServiceResult Status, IMonitoredItem Item) Create()
            {
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = m_node.NodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Disabled,
                    RequestedParameters = new MonitoringParameters
                    {
                        SamplingInterval = 1000,
                        QueueSize = 1,
                        DiscardOldest = true
                    }
                };
                var errors = new List<ServiceResult> { null };
                var filterErrors = new List<MonitoringFilterResult> { null };
                var items = new List<IMonitoredItem> { null };
                CreateMonitoredItems(
                    context, 1, 1000, TimestampsToReturn.Both, [request],
                    errors, filterErrors, items, false, m_ids);
                return (errors[0], items[0]);
            }

            public IMonitoredItem GetItem(uint id)
            {
                return MonitoredItems[id];
            }

            protected override void OnMonitoredItemCreated(
                ServerSystemContext context,
                NodeHandle handle,
                ISampledDataChangeMonitoredItem monitoredItem)
            {
                CreatedCount++;
            }

            protected override ServiceResult ReadInitialValue(
                ISystemContext context,
                NodeHandle handle,
                IDataChangeMonitoredItem2 monitoredItem)
            {
                if (ThrowOnRead)
                {
                    throw new InvalidOperationException("initial read failed");
                }
                return base.ReadInitialValue(context, handle, monitoredItem);
            }

            private ServiceResult OnRead(ISystemContext context, NodeState node, ref Variant value)
            {
                value = new Variant(1);
                return ReadStatus;
            }

            private readonly BaseDataVariableState m_node;
            private readonly MonitoredItemIdFactory m_ids = new();
        }
    }
}
