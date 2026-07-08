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

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ClassicMonitoredItemCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        private MonitoredItem CreateItem()
        {
            return new MonitoredItem(m_telemetry);
        }

        [Test]
        public void DefaultConstructorSetsExpectedDefaults()
        {
            MonitoredItem item = CreateItem();

            Assert.Multiple(() =>
            {
                Assert.That(item.DisplayName, Is.EqualTo("MonitoredItem"));
                Assert.That(item.StartNodeId.IsNull, Is.True);
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.Value));
                Assert.That(item.Encoding.IsNull, Is.True);
                Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
                Assert.That(item.SamplingInterval, Is.EqualTo(-1));
                Assert.That(item.DiscardOldest, Is.True);
                Assert.That(item.QueueSize, Is.Zero);
                Assert.That(item.IndexRange, Is.Null);
                Assert.That(item.RelativePath, Is.Null);
                Assert.That(item.Filter, Is.Null);
                Assert.That(item.AttributesModified, Is.True);
                Assert.That(item.ServerId, Is.Zero);
                Assert.That(item.Created, Is.False);
                Assert.That(item.ResolvedNodeId.IsNull, Is.True);
                Assert.That(item.Status, Is.Not.Null);
            });
        }

        [Test]
        public void ClientHandleConstructorAssignsProvidedHandle()
        {
            var item = new MonitoredItem(555u, m_telemetry);

            Assert.That(item.ClientHandle, Is.EqualTo(555u));
        }

        [Test]
        public void ObsoleteParameterlessConstructorProducesDefaults()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var item = new MonitoredItem();
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.Multiple(() =>
            {
                Assert.That(item.DisplayName, Is.EqualTo("MonitoredItem"));
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.Value));
            });
        }

        [Test]
        public void ConstructorWithOptionsAppliesProvidedValues()
        {
            var options = new MonitoredItemOptions
            {
                DisplayName = "Temperature",
                StartNodeId = new NodeId("Sensor", 3),
                AttributeId = Attributes.Value,
                IndexRange = "0:3",
                Encoding = new QualifiedName("Binary", 4),
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = 750,
                QueueSize = 12,
                DiscardOldest = false
            };

            var item = new MonitoredItem(88u, m_telemetry, options);

            Assert.Multiple(() =>
            {
                Assert.That(item.ClientHandle, Is.EqualTo(88u));
                Assert.That(item.DisplayName, Is.EqualTo("Temperature"));
                Assert.That(item.StartNodeId, Is.EqualTo(new NodeId("Sensor", 3)));
                Assert.That(item.IndexRange, Is.EqualTo("0:3"));
                Assert.That(item.Encoding, Is.EqualTo(new QualifiedName("Binary", 4)));
                Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                Assert.That(item.SamplingInterval, Is.EqualTo(750));
                Assert.That(item.QueueSize, Is.EqualTo(12u));
                Assert.That(item.DiscardOldest, Is.False);
            });
        }

        [Test]
        public void PropertySettersRoundTripExpectedValues()
        {
            MonitoredItem item = CreateItem();

            var startNodeId = new NodeId("Level", 2);
            var encoding = new QualifiedName("DefaultBinary", 1);
            object handle = new object();

            item.DisplayName = "Tank";
            item.StartNodeId = startNodeId;
            item.AttributeId = Attributes.DisplayName;
            item.IndexRange = "1:5";
            item.Encoding = encoding;
            item.MonitoringMode = MonitoringMode.Disabled;
            item.SamplingInterval = 333;
            item.QueueSize = 9;
            item.DiscardOldest = false;
            item.Handle = handle;
            item.ServerId = 4711;

            Assert.Multiple(() =>
            {
                Assert.That(item.DisplayName, Is.EqualTo("Tank"));
                Assert.That(item.StartNodeId, Is.EqualTo(startNodeId));
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.DisplayName));
                Assert.That(item.IndexRange, Is.EqualTo("1:5"));
                Assert.That(item.Encoding, Is.EqualTo(encoding));
                Assert.That(item.MonitoringMode, Is.EqualTo(MonitoringMode.Disabled));
                Assert.That(item.SamplingInterval, Is.EqualTo(333));
                Assert.That(item.QueueSize, Is.EqualTo(9u));
                Assert.That(item.DiscardOldest, Is.False);
                Assert.That(item.Handle, Is.SameAs(handle));
                Assert.That(item.ServerId, Is.EqualTo(4711u));
                Assert.That(item.Status.Id, Is.EqualTo(4711u));
            });
        }

        [Test]
        public void ResolvedNodeIdFollowsRelativePathState()
        {
            MonitoredItem item = CreateItem();
            item.StartNodeId = new NodeId("Start", 2);

            item.RelativePath = "/Child";
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);

            item.ResolvedNodeId = new NodeId(42u);
            Assert.That(item.ResolvedNodeId, Is.EqualTo(new NodeId(42u)));

            item.RelativePath = "/Other";
            Assert.That(item.ResolvedNodeId.IsNull, Is.True);

            item.RelativePath = null;
            Assert.That(item.ResolvedNodeId, Is.EqualTo(new NodeId("Start", 2)));
        }

        [Test]
        public void NodeClassObjectConfiguresEventDefaults()
        {
            MonitoredItem item = CreateItem();

            item.NodeClass = NodeClass.Object;

            Assert.Multiple(() =>
            {
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.Object));
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.EventNotifier));
                Assert.That(item.QueueSize, Is.EqualTo((uint)int.MaxValue));
                Assert.That(item.Filter, Is.InstanceOf<EventFilter>());
                Assert.That(((EventFilter)item.Filter).SelectClauses, Has.Count.EqualTo(9));
            });
        }

        [Test]
        public void NodeClassViewConfiguresEventDefaults()
        {
            MonitoredItem item = CreateItem();

            item.NodeClass = NodeClass.View;

            Assert.Multiple(() =>
            {
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.View));
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.EventNotifier));
                Assert.That(item.Filter, Is.InstanceOf<EventFilter>());
            });
        }

        [Test]
        public void NodeClassSameValueIsNoOp()
        {
            MonitoredItem item = CreateItem();

            item.NodeClass = NodeClass.Variable;

            Assert.Multiple(() =>
            {
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(item.Filter, Is.Null);
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.Value));
            });
        }

        [Test]
        public void NodeClassBackToVariableClearsEventConfiguration()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;

            item.NodeClass = NodeClass.Variable;

            Assert.Multiple(() =>
            {
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(item.AttributeId, Is.EqualTo(Attributes.Value));
                Assert.That(item.Filter, Is.Null);
                Assert.That(item.QueueSize, Is.EqualTo(1u));
            });
        }

        [Test]
        public void SamplingIntervalChangeMarksAttributesModified()
        {
            MonitoredItem item = CreateItem();
            item.SetTransferResult(item.ClientHandle);
            Assert.That(item.AttributesModified, Is.False);

            item.SamplingInterval = 999;

            Assert.That(item.AttributesModified, Is.True);
        }

        [Test]
        public void QueueSizeChangeMarksAttributesModified()
        {
            MonitoredItem item = CreateItem();
            item.SetTransferResult(item.ClientHandle);
            Assert.That(item.AttributesModified, Is.False);

            item.QueueSize = 50;

            Assert.That(item.AttributesModified, Is.True);
        }

        [Test]
        public void DiscardOldestChangeMarksAttributesModified()
        {
            MonitoredItem item = CreateItem();
            item.SetTransferResult(item.ClientHandle);
            Assert.That(item.AttributesModified, Is.False);

            item.DiscardOldest = false;

            Assert.That(item.AttributesModified, Is.True);
        }

        [Test]
        public void FilterOnVariableAcceptsDataChangeFilter()
        {
            MonitoredItem item = CreateItem();
            item.SetTransferResult(item.ClientHandle);

            var filter = new DataChangeFilter { DeadbandValue = 2.5 };
            item.Filter = filter;

            Assert.Multiple(() =>
            {
                Assert.That(item.Filter, Is.SameAs(filter));
                Assert.That(item.NodeClass, Is.EqualTo(NodeClass.Variable));
                Assert.That(item.AttributesModified, Is.True);
            });
        }

        [Test]
        public void FilterOnMethodNodeClassThrowsBadFilterNotAllowed()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Method;

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => item.Filter = new DataChangeFilter());

            Assert.That(ex.StatusCode.Code, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
        }

        [Test]
        public void SetErrorStoresProvidedServiceResult()
        {
            MonitoredItem item = CreateItem();

            item.SetError(new ServiceResult(StatusCodes.BadUnexpectedError));

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Error, Is.Not.Null);
                Assert.That(item.Status.Error.Code, Is.EqualTo(StatusCodes.BadUnexpectedError));
            });
        }

        [Test]
        public void SetCreateResultGoodUpdatesStatus()
        {
            MonitoredItem item = CreateItem();
            MonitoredItemCreateRequest request = BuildCreateRequest();
            var result = new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                MonitoredItemId = 4242,
                RevisedSamplingInterval = 500,
                RevisedQueueSize = 10
            };

            item.SetCreateResult(request, result, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Id, Is.EqualTo(4242u));
                Assert.That(item.Created, Is.True);
                Assert.That(item.Status.SamplingInterval, Is.EqualTo(500));
                Assert.That(item.Status.QueueSize, Is.EqualTo(10u));
                Assert.That(item.Status.Error, Is.Null);
                Assert.That(item.AttributesModified, Is.False);
            });
        }

        [Test]
        public void SetCreateResultBadStoresError()
        {
            MonitoredItem item = CreateItem();
            MonitoredItemCreateRequest request = BuildCreateRequest();
            var result = new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.BadNodeIdUnknown
            };

            item.SetCreateResult(request, result, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Id, Is.Zero);
                Assert.That(item.Created, Is.False);
                Assert.That(item.Status.Error, Is.Not.Null);
                Assert.That(item.Status.Error.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(item.AttributesModified, Is.False);
            });
        }

        [Test]
        public void SetModifyResultGoodUpdatesStatus()
        {
            MonitoredItem item = CreateItem();
            var request = new MonitoredItemModifyRequest
            {
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = item.ClientHandle,
                    SamplingInterval = 100,
                    QueueSize = 3,
                    DiscardOldest = true
                }
            };
            var result = new MonitoredItemModifyResult
            {
                StatusCode = StatusCodes.Good,
                RevisedSamplingInterval = 200,
                RevisedQueueSize = 6
            };

            item.SetModifyResult(request, result, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.SamplingInterval, Is.EqualTo(200));
                Assert.That(item.Status.QueueSize, Is.EqualTo(6u));
                Assert.That(item.Status.Error, Is.Null);
                Assert.That(item.AttributesModified, Is.False);
            });
        }

        [Test]
        public void SetModifyResultBadStoresError()
        {
            MonitoredItem item = CreateItem();
            var request = new MonitoredItemModifyRequest
            {
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = item.ClientHandle
                }
            };
            var result = new MonitoredItemModifyResult
            {
                StatusCode = StatusCodes.BadMonitoredItemIdInvalid
            };

            item.SetModifyResult(request, result, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Error, Is.Not.Null);
                Assert.That(item.Status.Error.Code, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
                Assert.That(item.AttributesModified, Is.False);
            });
        }

        [Test]
        public void SetDeleteResultGoodClearsServerId()
        {
            MonitoredItem item = CreateItem();
            item.ServerId = 987;

            item.SetDeleteResult(StatusCodes.Good, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Id, Is.Zero);
                Assert.That(item.Status.Error, Is.Null);
            });
        }

        [Test]
        public void SetDeleteResultBadStoresError()
        {
            MonitoredItem item = CreateItem();
            item.ServerId = 987;

            item.SetDeleteResult(StatusCodes.BadMonitoredItemIdInvalid, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Id, Is.Zero);
                Assert.That(item.Status.Error, Is.Not.Null);
                Assert.That(item.Status.Error.Code, Is.EqualTo(StatusCodes.BadMonitoredItemIdInvalid));
            });
        }

        [Test]
        public void SetTransferResultCopiesStateAndResetsModified()
        {
            MonitoredItem item = CreateItem();
            item.StartNodeId = new NodeId("Transfer", 2);
            item.MonitoringMode = MonitoringMode.Sampling;

            item.SetTransferResult(777u);

            Assert.Multiple(() =>
            {
                Assert.That(item.ClientHandle, Is.EqualTo(777u));
                Assert.That(item.Status.ClientHandle, Is.EqualTo(777u));
                Assert.That(item.Status.NodeId, Is.EqualTo(new NodeId("Transfer", 2)));
                Assert.That(item.Status.MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
                Assert.That(item.AttributesModified, Is.False);
            });
        }

        [Test]
        public void StatusSetMonitoringModeUpdatesValue()
        {
            MonitoredItem item = CreateItem();

            item.Status.SetMonitoringMode(MonitoringMode.Reporting);

            Assert.That(item.Status.MonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
        }

        [Test]
        public void SetResolvePathResultGoodResetsResolvedNodeId()
        {
            MonitoredItem item = CreateItem();
            item.StartNodeId = new NodeId("Start", 2);
            item.RelativePath = "/Child";
            item.ResolvedNodeId = new NodeId(5u);

            var result = new BrowsePathResult { StatusCode = StatusCodes.Good };
            item.SetResolvePathResult(result, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.ResolvedNodeId.IsNull, Is.True);
                Assert.That(item.Status.Error, Is.Null);
            });
        }

        [Test]
        public void SetResolvePathResultBadStoresError()
        {
            MonitoredItem item = CreateItem();

            var result = new BrowsePathResult { StatusCode = StatusCodes.BadNodeIdUnknown };
            item.SetResolvePathResult(result, 0, [], new ResponseHeader());

            Assert.Multiple(() =>
            {
                Assert.That(item.Status.Error, Is.Not.Null);
                Assert.That(item.Status.Error.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public void SnapshotCapturesCurrentState()
        {
            var item = new MonitoredItem(321u, m_telemetry)
            {
                DisplayName = "Snap",
                ServerId = 654,
                CacheQueueSize = 8
            };

            item.Snapshot(out MonitoredItemState state);

            Assert.Multiple(() =>
            {
                Assert.That(state.ClientId, Is.EqualTo(321u));
                Assert.That(state.ServerId, Is.EqualTo(654u));
                Assert.That(state.CacheQueueSize, Is.EqualTo(8u));
                Assert.That(state.DisplayName, Is.EqualTo("Snap"));
            });
        }

        [Test]
        public void RestoreAppliesStateValues()
        {
            MonitoredItem item = CreateItem();
            var state = new MonitoredItemState
            {
                DisplayName = "Restored",
                StartNodeId = new NodeId("Node", 2),
                ClientId = 246,
                ServerId = 135,
                CacheQueueSize = 10
            };

            item.Restore(state);

            Assert.Multiple(() =>
            {
                Assert.That(item.ClientHandle, Is.EqualTo(246u));
                Assert.That(item.ServerId, Is.EqualTo(135u));
                Assert.That(item.CacheQueueSize, Is.EqualTo(10u));
                Assert.That(item.StartNodeId, Is.EqualTo(new NodeId("Node", 2)));
                Assert.That(item.DisplayName, Is.EqualTo("Restored"));
            });
        }

        [Test]
        public void RestoreClampsCacheQueueSizeToOne()
        {
            MonitoredItem item = CreateItem();
            var state = new MonitoredItemState { ClientId = 111, CacheQueueSize = 0 };

            item.Restore(state);

            Assert.That(item.CacheQueueSize, Is.EqualTo(1u));
        }

        [Test]
        public void CloneAppendsHandleSuffixAndIsIndependent()
        {
            MonitoredItem item = CreateItem();
            item.DisplayName = "Sensor";
            item.Handle = "local";

            var clone = (MonitoredItem)item.Clone();

            Assert.Multiple(() =>
            {
                Assert.That(clone.DisplayName, Is.EqualTo("Sensor 0"));
                Assert.That(clone.ClientHandle, Is.Not.EqualTo(item.ClientHandle));
                Assert.That(clone.Handle, Is.EqualTo("local"));
            });

            clone.DisplayName = "Changed";
            Assert.That(item.DisplayName, Is.EqualTo("Sensor"));
        }

        [Test]
        public void CloneMonitoredItemWithCopyClientHandleKeepsHandle()
        {
            MonitoredItem item = CreateItem();

            MonitoredItem clone = item.CloneMonitoredItem(false, true);

            Assert.That(clone.ClientHandle, Is.EqualTo(item.ClientHandle));
        }

        [Test]
        public void TemplateConstructorTruncatesDisplayNameAtLastSpace()
        {
            MonitoredItem item = CreateItem();
            item.DisplayName = "Tank Level 5";

            var clone = new MonitoredItem(item);

            Assert.That(clone.DisplayName, Is.EqualTo("Tank Level 0"));
        }

        [Test]
        public void GetFieldNameReturnsFormattedBrowsePath()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;

            Assert.Multiple(() =>
            {
                Assert.That(item.GetFieldName(0), Is.EqualTo("/EventId"));
                Assert.That(item.GetFieldName(1), Is.EqualTo("/EventType"));
                Assert.That(item.GetFieldName(-1), Is.Null);
                Assert.That(item.GetFieldName(99), Is.Null);
            });
        }

        [Test]
        public void GetFieldNameReturnsNullWhenNotEventFilter()
        {
            MonitoredItem item = CreateItem();

            Assert.That(item.GetFieldName(0), Is.Null);
        }

        [Test]
        public void GetFieldValueMatchesBrowseName()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;
            EventFieldList eventFields = BuildEventFields(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));

            object value = item.GetFieldValue(
                eventFields,
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventId));

            Assert.That(value, Is.EqualTo("id"));
        }

        [Test]
        public void GetFieldValueReturnsNullWhenNoMatch()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;
            EventFieldList eventFields = BuildEventFields(DateTime.UtcNow);

            object value = item.GetFieldValue(
                eventFields,
                ObjectTypeIds.BaseEventType,
                QualifiedName.From("DoesNotExist"));

            Assert.That(value, Is.Null);
        }

        [Test]
        public void GetFieldValueReturnsNullForNullEventFields()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;

            object value = item.GetFieldValue(
                null,
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventId));

            Assert.That(value, Is.Null);
        }

        [Test]
        public void GetFieldValueReturnsNullWhenNotEventFilter()
        {
            MonitoredItem item = CreateItem();
            EventFieldList eventFields = BuildEventFields(DateTime.UtcNow);

            object value = item.GetFieldValue(
                eventFields,
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventId));

            Assert.That(value, Is.Null);
        }

        [Test]
        public void GetEventTimeReturnsMinValueForUtcTimeField()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;
            EventFieldList eventFields = BuildEventFields(
                new DateTime(2024, 6, 7, 8, 9, 10, DateTimeKind.Utc));

            // Characterization of a KNOWN DEFECT: GetEventTime reads the Time field via
            // Variant.AsBoxedObject() (boxed as this fork's DateTimeUtc), so its 'as DateTime?'
            // cast never matches and it always returns DateTime.MinValue even for a valid UTC
            // time. Locked in here so a future GetEventTime fix (extract via TryGetValue) updates it.
            DateTime eventTime = item.GetEventTime(eventFields);

            Assert.That(eventTime, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void GetEventTimeReturnsMinValueWhenFieldNotDateTime()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;
            var eventFields = new EventFieldList
            {
                EventFields = [
                    new Variant("id"),
                    new Variant("type"),
                    new Variant("srcnode"),
                    new Variant("srcname"),
                    new Variant(12345),
                    new Variant("receive"),
                    new Variant("local"),
                    new Variant("message"),
                    new Variant((ushort)3)
                ]
            };

            DateTime eventTime = item.GetEventTime(eventFields);

            Assert.That(eventTime, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void GetServiceResultReturnsStatusFromDataChange()
        {
            var notification = new MonitoredItemNotification
            {
                Value = new DataValue(new Variant(1), StatusCodes.BadInternalError, DateTime.UtcNow),
                Message = new NotificationMessage()
            };

            ServiceResult result = MonitoredItem.GetServiceResult(notification);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Code, Is.EqualTo(StatusCodes.BadInternalError));
            });
        }

        [Test]
        public void GetServiceResultReturnsNullWhenMessageMissing()
        {
            var notification = new MonitoredItemNotification
            {
                Value = new DataValue(new Variant(1), StatusCodes.Good, DateTime.UtcNow)
            };

            ServiceResult result = MonitoredItem.GetServiceResult(notification);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetServiceResultReturnsNullForNonDataChange()
        {
            ServiceResult result = MonitoredItem.GetServiceResult(new EventFieldList());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetServiceResultWithIndexReturnsNullForNonEvent()
        {
            ServiceResult result = MonitoredItem.GetServiceResult(new MonitoredItemNotification(), 0);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetServiceResultWithIndexReturnsNullWhenIndexOutOfRange()
        {
            var eventFields = new EventFieldList { Message = new NotificationMessage() };

            ServiceResult result = MonitoredItem.GetServiceResult(eventFields, 0);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SaveValueInCacheRaisesNotificationEvent()
        {
            MonitoredItem item = CreateItem();
            MonitoredItemNotificationEventArgs captured = null;
            item.Notification += (_, e) => captured = e;

            var notification = new MonitoredItemNotification
            {
                ClientHandle = item.ClientHandle,
                Value = new DataValue(new Variant(7), StatusCodes.Good, DateTime.UtcNow)
            };
            item.SaveValueInCache(notification);

            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured.NotificationValue, Is.SameAs(notification));
            });
        }

        [Test]
        public void DetachNotificationEventHandlersStopsCallbacks()
        {
            MonitoredItem item = CreateItem();
            int count = 0;
            item.Notification += (_, _) => count++;
            item.DetachNotificationEventHandlers();

            item.SaveValueInCache(new MonitoredItemNotification
            {
                ClientHandle = item.ClientHandle,
                Value = new DataValue(new Variant(1), StatusCodes.Good, DateTime.UtcNow)
            });

            Assert.That(count, Is.Zero);
        }

        [Test]
        public void LastValueAndLastMessageReflectDataChange()
        {
            MonitoredItem item = CreateItem();
            var message = new NotificationMessage();
            var notification = new MonitoredItemNotification
            {
                ClientHandle = item.ClientHandle,
                Value = new DataValue(new Variant(11), StatusCodes.Good, DateTime.UtcNow),
                Message = message
            };

            item.SaveValueInCache(notification);

            Assert.Multiple(() =>
            {
                Assert.That(item.LastValue, Is.SameAs(notification));
                Assert.That(item.LastMessage, Is.SameAs(message));
            });
        }

        [Test]
        public void DequeueEventsReturnsQueuedEvents()
        {
            MonitoredItem item = CreateItem();
            item.NodeClass = NodeClass.Object;

            for (int ii = 0; ii < 3; ii++)
            {
                item.SaveValueInCache(new EventFieldList
                {
                    EventFields = [new Variant(ii)],
                    Message = new NotificationMessage()
                });
            }

            IList<EventFieldList> events = item.DequeueEvents();

            Assert.Multiple(() =>
            {
                Assert.That(events, Has.Count.EqualTo(3));
                Assert.That((int)events[0].EventFields[0], Is.Zero);
                Assert.That((int)events[2].EventFields[0], Is.EqualTo(2));
            });
        }

        [Test]
        public void CacheQueueSizeReflectsConfiguredSize()
        {
            var item = new MonitoredItem(m_telemetry) { CacheQueueSize = 7 };

            Assert.That(item.CacheQueueSize, Is.EqualTo(7u));
        }

        [Test]
        public void DataCacheClampsQueueSizeToOne()
        {
            var cache = new MonitoredItemDataCache(m_telemetry, 1);
            var value = new DataValue(new Variant(5), StatusCodes.Good, DateTime.UtcNow);

            cache.OnNotification(new MonitoredItemNotification { Value = value });

            Assert.Multiple(() =>
            {
                Assert.That(cache.QueueSize, Is.EqualTo(1u));
                Assert.That(cache.LastValue, Is.EqualTo(value));
                Assert.That((int)cache.Publish().Single().WrappedValue, Is.EqualTo(5));
            });
        }

        [Test]
        public void DataCacheKeepsMostRecentValuesWithinQueue()
        {
            var cache = new MonitoredItemDataCache(m_telemetry, 3);

            foreach (int expected in new[] { 1, 2, 3, 4 })
            {
                cache.OnNotification(new MonitoredItemNotification
                {
                    Value = new DataValue(new Variant(expected), StatusCodes.Good, DateTime.UtcNow)
                });
            }

            int[] expectedValues = [2, 3, 4];
            Assert.That(cache.Publish().Select(v => (int)v.WrappedValue), Is.EqualTo(expectedValues));
        }

        [Test]
        public void DataCacheSetQueueSizeShrinksQueue()
        {
            var cache = new MonitoredItemDataCache(m_telemetry, 5);
            foreach (int value in new[] { 1, 2, 3, 4, 5 })
            {
                cache.OnNotification(new MonitoredItemNotification
                {
                    Value = new DataValue(new Variant(value), StatusCodes.Good, DateTime.UtcNow)
                });
            }

            cache.SetQueueSize(2);

            Assert.Multiple(() =>
            {
                Assert.That(cache.QueueSize, Is.EqualTo(2u));
                Assert.That(cache.Publish(), Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void EventCacheQueuesUpToConfiguredSize()
        {
            var cache = new MonitoredItemEventCache(2);
            var last = new EventFieldList { EventFields = [new Variant(3)] };

            cache.OnNotification(new EventFieldList { EventFields = [new Variant(1)] });
            cache.OnNotification(new EventFieldList { EventFields = [new Variant(2)] });
            cache.OnNotification(last);

            Assert.Multiple(() =>
            {
                Assert.That(cache.LastEvent, Is.SameAs(last));
                Assert.That(cache.Publish(), Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void EventCacheSetQueueSizeClampsToOne()
        {
            var cache = new MonitoredItemEventCache(3);
            cache.OnNotification(new EventFieldList { EventFields = [new Variant(1)] });
            cache.OnNotification(new EventFieldList { EventFields = [new Variant(2)] });

            cache.SetQueueSize(0);

            Assert.Multiple(() =>
            {
                Assert.That(cache.QueueSize, Is.EqualTo(1u));
                Assert.That(cache.Publish(), Has.Count.EqualTo(1));
            });
        }

        private MonitoredItemCreateRequest BuildCreateRequest()
        {
            return new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 7,
                    SamplingInterval = 250,
                    QueueSize = 5,
                    DiscardOldest = true
                },
                ItemToMonitor = new ReadValueId
                {
                    NodeId = new NodeId("Node", 2),
                    AttributeId = Attributes.Value
                }
            };
        }

        private static EventFieldList BuildEventFields(DateTime eventTime)
        {
            return new EventFieldList
            {
                EventFields = [
                    new Variant("id"),
                    new Variant("type"),
                    new Variant("srcnode"),
                    new Variant("srcname"),
                    new Variant(eventTime),
                    new Variant(DateTime.UtcNow),
                    new Variant(DateTime.UtcNow),
                    new Variant("message"),
                    new Variant((ushort)5)
                ]
            };
        }
    }
}
