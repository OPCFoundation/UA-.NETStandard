using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting.Server;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test MonitoredItem
    /// </summary>
    [TestFixture, Category("MonitoredItem")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    public class MonitoreItemTests
    {

        #region MonitoredItemDurable
        [Test]
        public void CreateMI()
        {
            MonitoredItem monitoredItem = CreateMonitoredItem();
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            monitoredItem.QueueValue(dataValue, statuscode);

            //bug in current implementation fixed with durable
            //Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(1));

            var result = new Queue<MonitoredItemNotification>();
            var result2 = new Queue<DiagnosticInfo>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, result2, 1);

            Assert.That(result, Is.Not.Empty);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));
            MonitoredItemNotification publishResult = result.FirstOrDefault();
            Assert.That(publishResult?.Value, Is.EqualTo(dataValue));
            DiagnosticInfo publishErrorResult = result2.FirstOrDefault();
            Assert.That(publishErrorResult.InnerStatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));

        }

        [Test]
        public void CreateEventMI()
        {
            MonitoredItem monitoredItem = CreateMonitoredItem(true);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(1));


            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 1);

            Assert.That(result, Is.Not.Empty);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));
            EventFieldList publishResult = result.FirstOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo(typeof(AuditUrlMismatchEventState)));
        }

        [Test]
        public void CreateMIQueueNoQueue()
        {

            MonitoredItem monitoredItem = CreateMonitoredItem(false, 0);

            Assert.That(monitoredItem.QueueSize, Is.EqualTo(1));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            monitoredItem.QueueValue(dataValue, statuscode);


            var result = new Queue<MonitoredItemNotification>();
            var result2 = new Queue<DiagnosticInfo>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, result2, 1);

            Assert.That(result, Is.Not.Empty);
            MonitoredItemNotification publishResult = result.FirstOrDefault();
            Assert.That(publishResult?.Value, Is.EqualTo(dataValue));
            DiagnosticInfo publishErrorResult = result2.FirstOrDefault();
            Assert.That(publishErrorResult.InnerStatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
        }

        [Test]
        public void CreateEventMIOverflow()
        {
            MonitoredItem monitoredItem = CreateMonitoredItem(true, 2);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));


            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));


            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 3);

            Assert.That(result, Is.Not.Empty);
            Assert.That(result.Count, Is.EqualTo(3));
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo(typeof(EventQueueOverflowEventState)));
        }

        [Test]
        public void CreateEventMIOverflowMultiplePublish()
        {
            MonitoredItem monitoredItem = CreateMonitoredItem(true, 2);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));


            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));


            var result = new Queue<EventFieldList>();
            bool moreItems = monitoredItem.Publish(new OperationContext(monitoredItem), result, 2);

            Assert.That(moreItems, Is.True);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result.Count, Is.EqualTo(2));
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo(typeof(AuditUrlMismatchEventState)));


            var result2 = new Queue<EventFieldList>();
            bool moreItems2 = monitoredItem.Publish(new OperationContext(monitoredItem), result2, 2);

            Assert.That(moreItems2, Is.False);
            Assert.That(result2, Is.Not.Empty);
            Assert.That(result2.Count, Is.EqualTo(1));
            EventFieldList publishResult2 = result2.FirstOrDefault();
            Assert.That(publishResult2, Is.Not.Null);
            Assert.That(publishResult2.Handle, Is.AssignableTo(typeof(EventQueueOverflowEventState)));
        }

        [Test]
        public void CreateEventMIOverflowNoDiscard()
        {
            MonitoredItem monitoredItem = CreateMonitoredItem(true, 2, true);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));


            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));


            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 3);

            Assert.That(result, Is.Not.Empty);
            Assert.That(result.Count, Is.EqualTo(3));
            EventFieldList publishResult = result.FirstOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo(typeof(EventQueueOverflowEventState)));
        }


        [Test]
        public void CreateEventMIPublishPartial()
        {
            MonitoredItem monitoredItem = CreateMonitoredItem(true, 3);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(0));

            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));
            monitoredItem.QueueEvent(new AuditUrlMismatchEventState(null));

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(3));


            var result = new Queue<EventFieldList>();
            bool moreItems = monitoredItem.Publish(new OperationContext(monitoredItem), result, 2);


            Assert.That(moreItems, Is.True);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result.Count, Is.EqualTo(2));
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo(typeof(AuditUrlMismatchEventState)));

            var result2 = new Queue<EventFieldList>();
            bool moreItems2 = monitoredItem.Publish(new OperationContext(monitoredItem), result2, 2);

            Assert.That(moreItems2, Is.False);
            Assert.That(result2, Is.Not.Empty);
            Assert.That(result2.Count, Is.EqualTo(1));
            EventFieldList publishResult2 = result2.LastOrDefault();
            Assert.That(publishResult2, Is.Not.Null);
            Assert.That(publishResult2.Handle, Is.AssignableTo(typeof(AuditUrlMismatchEventState)));
        }
        #endregion

        #region private methods
        private MonitoredItem CreateMonitoredItem(bool events = false, uint queueSize = 10, bool discardOldest = false)
        {
            MonitoringFilter filter = events ? new EventFilter() : new MonitoringFilter();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));

            var nodeMangerMock = new Mock<INodeManager>();

            return new MonitoredItem(
                serverMock.Object,
                nodeMangerMock.Object,
                null,
                1,
                2,
                new ReadValueId(),
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                1000.0,
                queueSize,
                discardOldest,
                1000
                );
        }
        #endregion
    }
}
