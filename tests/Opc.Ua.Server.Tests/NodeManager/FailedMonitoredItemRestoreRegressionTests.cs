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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class FailedMonitoredItemRestoreRegressionTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DuplicateRestoreDoesNotReportSuccessOrRaiseCreated(
            bool asynchronous,
            bool samplingGroups)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (IRestoreHooks manager = asynchronous
                ? new AsyncHooks(server.Object, samplingGroups)
                : new SyncHooks(server.Object, samplingGroups))
            {
                StoredMonitoredItem stored = CreateStored(41);
                Assert.That(manager.Restore(stored, out IMonitoredItem original), Is.True);
                Assert.That(manager.CreatedCount, Is.EqualTo(1));
                Assert.That(manager.Count, Is.EqualTo(1));
                Assert.That(manager.GetItem(41), Is.SameAs(original));

                Assert.That(manager.Restore(stored, out IMonitoredItem rejected), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(manager.CreatedCount, Is.EqualTo(1));
                Assert.That(manager.Count, Is.EqualTo(1));
                Assert.That(manager.GetItem(41), Is.SameAs(original));

                Assert.That(manager.Restore(CreateStored(42), out IMonitoredItem next), Is.True);
                Assert.That(manager.CreatedCount, Is.EqualTo(2));
                Assert.That(manager.Count, Is.EqualTo(2));
                Assert.That(manager.GetItem(42), Is.SameAs(next));
            }
        }

        private static StoredMonitoredItem CreateStored(uint id)
        {
            return new StoredMonitoredItem
            {
                SubscriptionId = 1,
                Id = id,
                TypeMask = MonitoredItemTypeMask.DataChange,
                NodeId = new NodeId(1, 1),
                AttributeId = Attributes.Value,
                TimestampsToReturn = TimestampsToReturn.Both,
                ClientHandle = id,
                MonitoringMode = MonitoringMode.Disabled,
                SamplingInterval = 1000,
                QueueSize = 1,
                DiscardOldest = true,
                SourceSamplingInterval = 1000,
                LastValue = new DataValue(new Variant(1), StatusCodes.Good),
                LastError = ServiceResult.Good
            };
        }

        private static NodeHandle CreateHandle(IStoredMonitoredItem stored)
        {
            var node = new BaseDataVariableState(null)
            {
                NodeId = stored.NodeId,
                DataType = DataTypeIds.Int32,
                Value = 1
            };
            return new NodeHandle(node.NodeId, node);
        }

        private interface IRestoreHooks : IDisposable
        {
            int CreatedCount { get; }
            int Count { get; }
            bool Restore(IStoredMonitoredItem stored, out IMonitoredItem item);
            IMonitoredItem GetItem(uint id);
        }

        private sealed class SyncHooks : CustomNodeManager2, IRestoreHooks
        {
            public SyncHooks(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups,
                    NullLogger.Instance,
                    "urn:tests:sync-restore")
            {
            }

            public int CreatedCount { get; private set; }
            public int Count => MonitoredItems.Count;

            public bool Restore(IStoredMonitoredItem stored, out IMonitoredItem item)
            {
                return RestoreMonitoredItem(
                    SystemContext, CreateHandle(stored), stored, new UserIdentity(), out item);
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
        }

        private sealed class AsyncHooks : AsyncCustomNodeManager, IRestoreHooks
        {
            public AsyncHooks(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups,
                    NullLogger.Instance,
                    "urn:tests:async-restore")
            {
            }

            public int CreatedCount { get; private set; }
            public int Count => MonitoredItems.Count;

            public bool Restore(IStoredMonitoredItem stored, out IMonitoredItem item)
            {
                return RestoreMonitoredItem(
                    SystemContext, CreateHandle(stored), stored, new UserIdentity(), out item);
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
        }
    }
}
