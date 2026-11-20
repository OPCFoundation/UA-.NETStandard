/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies duplicate monitored-item restoration does not replace existing registrations or raise creation
    /// callbacks.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class FailedMonitoredItemRestoreRegressionTests
    {
        /// <summary>
        /// Verifies duplicate restore rejection across node-manager and sampling-group implementations.
        /// </summary>
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

        /// <summary>
        /// Creates a disabled persisted data-change item with a good last value and the requested identifier.
        /// </summary>
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

        /// <summary>
        /// Creates a variable handle matching the persisted item's node identity.
        /// </summary>
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

        /// <summary>
        /// Unifies observable restoration behavior across synchronous and asynchronous node managers.
        /// </summary>
        private interface IRestoreHooks : IDisposable
        {
            /// <summary>
            /// Gets the number of creation callbacks raised for successfully restored items.
            /// </summary>
            int CreatedCount { get; }

            /// <summary>
            /// Gets the number of items retained in the manager's registry.
            /// </summary>
            int Count { get; }

            /// <summary>
            /// Attempts to restore a persisted item and returns the accepted instance when successful.
            /// </summary>
            bool Restore(IStoredMonitoredItem stored, out IMonitoredItem item);

            /// <summary>
            /// Gets the item registered under the specified identifier.
            /// </summary>
            IMonitoredItem GetItem(uint id);
        }

        /// <summary>
        /// Exposes synchronous node-manager restoration and accepted-item callback counts.
        /// </summary>
        private sealed class SyncHooks : CustomNodeManager2, IRestoreHooks
        {
            /// <summary>
            /// Creates a synchronous manager using the requested sampling-group implementation.
            /// </summary>
            public SyncHooks(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups,
                    NullLogger.Instance,
                    "urn:tests:sync-restore")
            {
            }

            /// <inheritdoc/>
            public int CreatedCount { get; private set; }

            /// <inheritdoc/>
            public int Count => MonitoredItems.Count;

            /// <inheritdoc/>
            public bool Restore(IStoredMonitoredItem stored, out IMonitoredItem item)
            {
                return RestoreMonitoredItem(
                    SystemContext, CreateHandle(stored), stored, new UserIdentity(), out item!);
            }

            /// <inheritdoc/>
            public IMonitoredItem GetItem(uint id)
            {
                return MonitoredItems[id];
            }

            /// <summary>
            /// Counts creation notifications after the synchronous manager accepts a restored item.
            /// </summary>
            protected override void OnMonitoredItemCreated(
                ServerSystemContext context,
                NodeHandle handle,
                ISampledDataChangeMonitoredItem monitoredItem)
            {
                CreatedCount++;
            }
        }

        /// <summary>
        /// Exposes asynchronous node-manager restoration and accepted-item callback counts.
        /// </summary>
        private sealed class AsyncHooks : AsyncCustomNodeManager, IRestoreHooks
        {
            /// <summary>
            /// Creates an asynchronous manager using the requested sampling-group implementation.
            /// </summary>
            public AsyncHooks(IServerInternal server, bool samplingGroups)
                : base(
                    server,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    samplingGroups,
                    NullLogger.Instance,
                    "urn:tests:async-restore")
            {
            }

            /// <inheritdoc/>
            public int CreatedCount { get; private set; }

            /// <inheritdoc/>
            public int Count => MonitoredItems.Count;

            /// <inheritdoc/>
            public bool Restore(IStoredMonitoredItem stored, out IMonitoredItem item)
            {
                return RestoreMonitoredItem(
                    SystemContext, CreateHandle(stored), stored, new UserIdentity(), out item!);
            }

            /// <inheritdoc/>
            public IMonitoredItem GetItem(uint id)
            {
                return MonitoredItems[id];
            }

            /// <summary>
            /// Counts creation notifications after the asynchronous manager accepts a restored item.
            /// </summary>
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
