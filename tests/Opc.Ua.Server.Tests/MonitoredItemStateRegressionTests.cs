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

// CA2000: test code; the monitored items and queue factories are disposed by the tests.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the state a <see cref="MonitoredItem"/> keeps between queueing,
    /// monitoring mode changes, modification and publishing.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemStateRegressionTests
    {
        /// <summary>
        /// Clearing the sampling error with null must leave a Good result, not a null one.
        /// </summary>
        [Test]
        public void SetSamplingErrorNullResetsToGood()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1);

            item.SetSamplingError(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            item.SetSamplingError(null);

            ServiceResult createResult = item.GetCreateResult(out MonitoredItemCreateResult created);
            ServiceResult modifyResult = item.GetModifyResult(out MonitoredItemModifyResult modified);

            Assert.That(createResult, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(createResult), Is.True);
            Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(modifyResult, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(modifyResult), Is.True);
            Assert.That(modified.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Growing the queue from one slot must not re-queue a value that was already published.
        /// </summary>
        [Test]
        public void GrowingQueueDoesNotRequeuePublishedValue()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

            ModifyQueueSize(item, 10);
            Assert.That(item.ItemsInQueue, Is.Zero);
            Assert.That(item.IsReadyToPublish, Is.False);

            item.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);
            List<MonitoredItemNotification> published = PublishData(harness, item);

            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].Value.WrappedValue, Is.EqualTo(Variant.From(2)));
        }

        /// <summary>
        /// Growing the queue from one slot keeps a value that has not been published yet.
        /// </summary>
        [Test]
        public void GrowingQueueKeepsUnpublishedValue()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            ModifyQueueSize(item, 10);
            item.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);
            List<MonitoredItemNotification> published = PublishData(harness, item);

            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published[0].Value.WrappedValue, Is.EqualTo(Variant.From(1)));
            Assert.That(published[1].Value.WrappedValue, Is.EqualTo(Variant.From(2)));
        }

        /// <summary>
        /// The first value queued after a triggering item leaves DISABLED must trigger its
        /// linked items. (A value queued before the first link exists is discarded by
        /// Subscription.SetTriggering, see SetTriggeringDiscardsTriggerQueuedBeforeFirstLink.)
        /// </summary>
        [Test]
        public void FirstValueAfterReenableIsReadyToTrigger()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            _ = PublishData(harness, item);

            item.SetMonitoringMode(MonitoringMode.Disabled);
            item.SetMonitoringMode(MonitoringMode.Reporting);
            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);

            Assert.That(item.IsReadyToTrigger, Is.True);
        }

        /// <summary>
        /// An item created DISABLED queues nothing until it is enabled. The first value it
        /// queues then must trigger the items linked to it in the meantime.
        /// </summary>
        [Test]
        public void FirstValueOfItemCreatedDisabledIsReadyToTrigger()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(
                queueSize: 1,
                MonitoringMode.Disabled,
                samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(item.IsReadyToTrigger, Is.False, "a disabled item queues nothing");

            item.SetMonitoringMode(MonitoringMode.Reporting);
            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);

            Assert.That(item.IsReadyToTrigger, Is.True);
        }

        /// <summary>
        /// A resend requested before the item leaves REPORTING must not publish the last value.
        /// </summary>
        [TestCase(MonitoringMode.Disabled)]
        [TestCase(MonitoringMode.Sampling)]
        public void ResendDataIsDroppedWhenItemStopsReporting(MonitoringMode monitoringMode)
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

            item.SetupResendDataTrigger();
            Assert.That(item.IsResendData, Is.True);

            item.SetMonitoringMode(monitoringMode);

            Assert.That(item.IsResendData, Is.False);
            Assert.That(PublishData(harness, item), Is.Empty);
        }

        /// <summary>
        /// A node deleted while the item is disabled must be reported as Bad_NodeIdUnknown
        /// once the item samples again, never as an empty Good value.
        /// </summary>
        [TestCase(1u, MonitoringMode.Sampling)]
        [TestCase(10u, MonitoringMode.Sampling)]
        [TestCase(1u, MonitoringMode.Reporting)]
        [TestCase(10u, MonitoringMode.Reporting)]
        public void NodeDeletedWhileDisabledReportsNodeIdUnknownAfterReenable(
            uint queueSize,
            MonitoringMode monitoringMode)
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

            item.SetMonitoringMode(MonitoringMode.Disabled);
            ((IDetachableMonitoredItem)item).MarkNodeDeleted();
            Assert.That(item.ItemsInQueue, Is.Zero);
            Assert.That(((IDetachableMonitoredItem)item).IsDeleted, Is.True);

            item.SetMonitoringMode(monitoringMode);
            if (monitoringMode == MonitoringMode.Sampling)
            {
                Assert.That(item.SetTriggered(), Is.True);
            }

            List<MonitoredItemNotification> published = PublishData(harness, item);

            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(
                published[0].Value.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Re-enabling an item whose node was deleted must not read the stale node and queue
        /// a Good value over the Bad_NodeIdUnknown the item reports (both managers).
        /// </summary>
        [TestCase(false, 1u)]
        [TestCase(false, 10u)]
        [TestCase(true, 1u)]
        [TestCase(true, 10u)]
        public async Task ReenablingDeletedNodeItemDoesNotQueueStaleValueAsync(
            bool useSamplingGroups,
            uint queueSize)
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize, samplingInterval: 0);
            var nodeManager = new Mock<IAsyncNodeManager>();
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("V", 1),
                DataType = DataTypeIds.Int32,
                Value = new Variant(42),
                StatusCode = StatusCodes.Good
            };
            var handle = new NodeHandle(node.NodeId, node);
            var context = new ServerSystemContext(harness.ServerMock.Object);

            IMonitoredItemManager manager;
            if (useSamplingGroups)
            {
                var samplingGroups = new SamplingGroupManager(
                    harness.ServerMock.Object, nodeManager.Object, 10, 10, [new SamplingRateGroup(1000, 0, 1)]);
                manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object, harness.ServerMock.Object, samplingGroups);
            }
            else
            {
                manager = new MonitoredNodeMonitoredItemManager(nodeManager.Object, harness.ServerMock.Object);
                handle.MonitoredNode = new MonitoredNode2(nodeManager.Object, harness.ServerMock.Object, node);
                manager.MonitoredNodes[node.NodeId] = handle.MonitoredNode;
            }

            using (manager)
            {
                manager.MonitoredItems[item.Id] = item;

                item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
                Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

                item.SetMonitoringMode(MonitoringMode.Disabled);
                ((IDetachableMonitoredItem)item).MarkNodeDeleted();

                (ServiceResult result, MonitoringMode? previousMode) = await manager.SetMonitoringModeAsync(
                    context, item, MonitoringMode.Reporting, handle).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(previousMode, Is.EqualTo(MonitoringMode.Disabled));

                List<MonitoredItemNotification> published = PublishData(harness, item);

                Assert.That(published, Has.Count.EqualTo(1));
                Assert.That(
                    published[0].Value.StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
        }

        /// <summary>
        /// A non-zero sampling interval requested for an event item must not delay delivery.
        /// </summary>
        [Test]
        public void EventItemIsNotThrottledBySamplingInterval()
        {
            using var harness = new Harness();
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A")],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter, samplingInterval: 5000);

            item.QueueEvent(new NamedFieldTarget());
            Assert.That(PublishEvents(item), Has.Count.EqualTo(1));

            item.QueueEvent(new NamedFieldTarget());

            Assert.That(item.IsReadyToPublish, Is.True);
            Assert.That(PublishEvents(item), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A restored durable item with queued notifications is ready to publish right away.
        /// </summary>
        [Test]
        public void RestoredItemWithQueuedEventsIsReadyToPublish()
        {
            using var harness = new Harness();
            IEventMonitoredItemQueue queue = harness.QueueFactory.CreateEventQueue(false, 4);
            queue.SetQueueSize(10, true);
            queue.Enqueue(new EventFieldList
            {
                ClientHandle = 5,
                EventFields = [Variant.From("A")]
            });
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A")],
                WhereClause = new ContentFilter()
            };
            var stored = new StoredMonitoredItem
            {
                Id = 4,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.Events,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier,
                ClientHandle = 5,
                QueueSize = 10,
                DiscardOldest = true,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None,
                OriginalFilter = filter,
                FilterToUse = filter,
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                RestoredEventQueue = queue
            };

            using var item = new MonitoredItem(
                harness.ServerMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                stored,
                harness.TimeProvider);

            Assert.That(item.ItemsInQueue, Is.EqualTo(1));
            Assert.That(item.IsReadyToPublish, Is.True);
            Assert.That(PublishEvents(item), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A restored durable data change item with queued values is ready to publish right away.
        /// </summary>
        [Test]
        public void RestoredItemWithQueuedValuesIsReadyToPublish()
        {
            using var harness = new Harness();
            IDataChangeMonitoredItemQueue queue = harness.QueueFactory.CreateDataChangeQueue(false, 2);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(7)), ServiceResult.Good);
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue };
            var stored = new StoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId("V", 1),
                AttributeId = Attributes.Value,
                ClientHandle = 3,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 0,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None,
                OriginalFilter = filter,
                FilterToUse = filter,
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(Variant.From(7)),
                RestoredDataChangeQueue = queue
            };

            using var item = new MonitoredItem(
                harness.ServerMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                stored,
                harness.TimeProvider);

            Assert.That(item.IsReadyToPublish, Is.True);
            List<MonitoredItemNotification> published = PublishData(harness, item);
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].Value.WrappedValue, Is.EqualTo(Variant.From(7)));
        }

        /// <summary>
        /// Values queued before a restart trigger the linked items of a restored reporting
        /// item, but not those of a sampling item, whose queue holds nothing new.
        /// </summary>
        [TestCase(MonitoringMode.Reporting, true)]
        [TestCase(MonitoringMode.Sampling, false)]
        public void RestoredItemWithQueuedValuesTriggersOnlyWhenReporting(
            MonitoringMode monitoringMode,
            bool readyToTrigger)
        {
            using var harness = new Harness();
            IDataChangeMonitoredItemQueue queue = harness.QueueFactory.CreateDataChangeQueue(false, 2);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(7)), ServiceResult.Good);
            var filter = new DataChangeFilter { Trigger = DataChangeTrigger.StatusValue };
            var stored = new StoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = monitoringMode,
                NodeId = new NodeId("V", 1),
                AttributeId = Attributes.Value,
                ClientHandle = 3,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 0,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.None,
                OriginalFilter = filter,
                FilterToUse = filter,
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(Variant.From(7)),
                RestoredDataChangeQueue = queue
            };

            using var item = new MonitoredItem(
                harness.ServerMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                stored,
                harness.TimeProvider);

            Assert.That(item.IsReadyToTrigger, Is.EqualTo(readyToTrigger));
        }

        /// <summary>
        /// Events queued before a modification of the select clauses are published in the
        /// new field layout and with the new client handle.
        /// </summary>
        [Test]
        public void ModifySelectClausesRebuildsQueuedEvents()
        {
            using var harness = new Harness();
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A"), CreateSelectClause("B")],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter);
            var target = new NamedFieldTarget();
            item.QueueEvent(target);

            var modified = new EventFilter
            {
                SelectClauses =
                [
                    CreateSelectClause("C"),
                    CreateSelectClause("A"),
                    CreateSelectClause("B")
                ],
                WhereClause = new ContentFilter()
            };
            ModifyEventItem(item, modified, clientHandle: 9);

            List<EventFieldList> published = PublishEvents(item);

            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].ClientHandle, Is.EqualTo(9u));
            Assert.That(published[0].Handle, Is.SameAs(target));
            Assert.That(
                published[0].EventFields.ToArray(),
                Is.EqualTo(new[] { Variant.From("C"), Variant.From("A"), Variant.From("B") }));
        }

        /// <summary>
        /// A modification that only changes the client handle keeps the queued fields and
        /// updates their client handle.
        /// </summary>
        [Test]
        public void ModifyClientHandleUpdatesQueuedEvents()
        {
            using var harness = new Harness();
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A")],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter);
            item.QueueEvent(new NamedFieldTarget());

            ModifyEventItem(item, filter, clientHandle: 11);

            List<EventFieldList> published = PublishEvents(item);
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].ClientHandle, Is.EqualTo(11u));
            Assert.That(published[0].EventFields.ToArray(), Is.EqualTo(new[] { Variant.From("A") }));
        }

        /// <summary>
        /// A queued event without a filter target (restored from a durable or redundant
        /// queue) keeps the fields still selected after the select clauses change; the new
        /// ones are null.
        /// </summary>
        [Test]
        public void ModifySelectClausesProjectsEventsWithoutTarget()
        {
            using var harness = new Harness();
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A"), CreateSelectClause("B")],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter);
            item.QueueEvent(new EventFieldList
            {
                ClientHandle = 5,
                EventFields = [Variant.From("a"), Variant.From("b")]
            });

            var modified = new EventFilter
            {
                SelectClauses =
                [
                    CreateSelectClause("C"),
                    CreateSelectClause("B"),
                    CreateSelectClause("A")
                ],
                WhereClause = new ContentFilter()
            };
            ModifyEventItem(item, modified, clientHandle: 7);

            List<EventFieldList> published = PublishEvents(item);
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].ClientHandle, Is.EqualTo(7u));
            Assert.That(
                published[0].EventFields.ToArray(),
                Is.EqualTo(new[] { Variant.Null, Variant.From("b"), Variant.From("a") }));
        }

        /// <summary>
        /// A queued event without a filter target none of whose fields is still selected
        /// is dropped and reported as an event queue overflow.
        /// </summary>
        [Test]
        public void ModifySelectClausesDropsEventsWithoutTarget()
        {
            using var harness = new Harness();
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A")],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter);
            item.QueueEvent(new EventFieldList
            {
                ClientHandle = 5,
                EventFields = [Variant.From("A")]
            });

            var modified = new EventFilter
            {
                SelectClauses = [CreateSelectClause("B"), CreateSelectClause("C")],
                WhereClause = new ContentFilter()
            };
            ModifyEventItem(item, modified, clientHandle: 5);

            Assert.That(item.ItemsInQueue, Is.Zero);
            List<EventFieldList> published = PublishEvents(item);
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].Handle, Is.AssignableTo<EventQueueOverflowEventState>());
            Assert.That(published[0].EventFields.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// A modification whose rebuild of the queued events fails must neither lose those
        /// events nor leave the item half modified.
        /// </summary>
        [Test]
        public void FailedRebuildKeepsQueuedEventsAndLeavesItemUnmodified()
        {
            using var harness = new Harness();
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A")],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter);
            var first = new NamedFieldTarget();
            var second = new NamedFieldTarget();
            item.QueueEvent(first);
            item.QueueEvent(second);

            var modified = new EventFilter
            {
                SelectClauses = [CreateSelectClause(NamedFieldTarget.Failing), CreateSelectClause("A")],
                WhereClause = new ContentFilter()
            };
            Assert.Throws<InvalidOperationException>(
                () => item.ModifyAttributes(
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    9,
                    modified,
                    modified,
                    null,
                    0,
                    1,
                    discardOldest: true));

            Assert.That(item.ClientHandle, Is.EqualTo(5u));
            Assert.That(item.Filter, Is.SameAs(filter));
            Assert.That(item.QueueSize, Is.EqualTo(10u));
            List<EventFieldList> published = PublishEvents(item);
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published[0].Handle, Is.SameAs(first));
            Assert.That(published[1].Handle, Is.SameAs(second));
            Assert.That(published[0].ClientHandle, Is.EqualTo(5u));
            Assert.That(published[0].EventFields.ToArray(), Is.EqualTo(new[] { Variant.From("A") }));
        }

        /// <summary>
        /// A rejected select clause returns a null field. It is rejected once for the
        /// filter, not validated again (and failing again) for every event.
        /// </summary>
        [Test]
        public void RejectedSelectClauseIsValidatedOnce()
        {
            using var harness = new Harness();
            SimpleAttributeOperand invalid = CreateSelectClause("B");
            invalid.IndexRange = "not a range";
            var filter = new EventFilter
            {
                SelectClauses = [CreateSelectClause("A"), invalid],
                WhereClause = new ContentFilter()
            };
            using MonitoredItem item = harness.CreateEventItem(filter);

            int testThread = Environment.CurrentManagedThreadId;
            int failedValidations = 0;
            void OnFirstChance(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
            {
                if (Environment.CurrentManagedThreadId == testThread &&
                    e.Exception is ServiceResultException)
                {
                    failedValidations++;
                }
            }

            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            try
            {
                for (int ii = 0; ii < 5; ii++)
                {
                    item.QueueEvent(new NamedFieldTarget());
                }
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            }

            Assert.That(failedValidations, Is.EqualTo(1));
            List<EventFieldList> published = PublishEvents(item);
            Assert.That(published, Has.Count.EqualTo(5));
            Assert.That(
                published[4].EventFields.ToArray(),
                Is.EqualTo(new[] { Variant.From("A"), Variant.Null }));
        }

        private static void ModifyEventItem(MonitoredItem item, EventFilter filter, uint clientHandle)
        {
            ServiceResult result = item.ModifyAttributes(
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                clientHandle,
                filter,
                filter,
                null,
                0,
                10,
                discardOldest: true);
            Assert.That(ServiceResult.IsBad(result), Is.False);
        }

        private static SimpleAttributeOperand CreateSelectClause(string name)
        {
            return new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(name)],
                AttributeId = Attributes.Value
            };
        }

        /// <summary>
        /// Event filter target that reports the browse name of each selected field as its value.
        /// </summary>
        private sealed class NamedFieldTarget : IFilterTarget
        {
            /// <summary>
            /// A field name the target fails to resolve with an exception.
            /// </summary>
            public const string Failing = "Failing";

            public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
            {
                return true;
            }

            public Variant GetAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                ArrayOf<QualifiedName> relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                if (relativePath[0].Name == Failing)
                {
                    throw new InvalidOperationException("The field cannot be resolved.");
                }
                return Variant.From(relativePath[0].Name);
            }
        }

        private static void ModifyQueueSize(MonitoredItem item, uint queueSize)
        {
            ServiceResult result = item.ModifyAttributes(
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                item.ClientHandle,
                item.Filter,
                item.Filter,
                null,
                item.SamplingInterval,
                queueSize,
                discardOldest: true);
            Assert.That(ServiceResult.IsBad(result), Is.False);
        }

        private static List<MonitoredItemNotification> PublishData(
            Harness harness,
            MonitoredItem item)
        {
            var notifications = new Queue<MonitoredItemNotification>();
            _ = item.Publish(
                new OperationContext(item),
                notifications,
                new Queue<DiagnosticInfo>(),
                100,
                harness.Logger);
            return [.. notifications];
        }

        private static List<EventFieldList> PublishEvents(MonitoredItem item)
        {
            var notifications = new Queue<EventFieldList>();
            _ = item.Publish(new OperationContext(item), notifications, 100);
            return [.. notifications];
        }

        private sealed class Harness : IDisposable
        {
            public Harness()
            {
                Telemetry = NUnitTelemetryContext.Create();
                Logger = Telemetry.CreateLogger<MonitoredItemStateRegressionTests>();
                QueueFactory = new MonitoredItemQueueFactory(Telemetry);
                StoreMock = new Mock<ISubscriptionStore>();
                ServerMock = new Mock<IServerInternal>();
                ServerMock.Setup(s => s.Telemetry).Returns(Telemetry);
                ServerMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                ServerMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                ServerMock.Setup(s => s.MonitoredItemQueueFactory).Returns(QueueFactory);
                ServerMock.Setup(s => s.SubscriptionStore).Returns(StoreMock.Object);
                TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            }

            public ITelemetryContext Telemetry { get; }

            public ILogger Logger { get; }

            public MonitoredItemQueueFactory QueueFactory { get; }

            public Mock<ISubscriptionStore> StoreMock { get; }

            public Mock<IServerInternal> ServerMock { get; }

            public FakeTimeProvider TimeProvider { get; }

            public MonitoredItem CreateDataItem(
                uint queueSize,
                MonitoringMode monitoringMode = MonitoringMode.Reporting,
                double samplingInterval = 1000)
            {
                var filter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue
                };
                return new MonitoredItem(
                    ServerMock.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    null,
                    1,
                    2,
                    new ReadValueId
                    {
                        NodeId = new NodeId("V", 1),
                        AttributeId = Attributes.Value
                    },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    monitoringMode,
                    3,
                    filter,
                    filter,
                    null,
                    samplingInterval,
                    queueSize,
                    discardOldest: true,
                    sourceSamplingInterval: 0,
                    createDurable: false,
                    TimeProvider);
            }

            public MonitoredItem CreateEventItem(
                EventFilter filter,
                double samplingInterval = 0,
                uint queueSize = 10)
            {
                return new MonitoredItem(
                    ServerMock.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    null,
                    1,
                    4,
                    new ReadValueId
                    {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    MonitoringMode.Reporting,
                    5,
                    filter,
                    filter,
                    null,
                    samplingInterval,
                    queueSize,
                    discardOldest: true,
                    sourceSamplingInterval: 0,
                    createDurable: false,
                    TimeProvider);
            }

            public void Dispose()
            {
                QueueFactory.Dispose();
            }
        }
    }
}
