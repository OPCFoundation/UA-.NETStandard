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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("EventManager")]
    [Parallelizable]
    public class EventManagerTests
    {
        private Mock<IServerInternal> m_mockServer;
        private EventManager m_eventManager;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_mockServer.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_eventManager = new EventManager(m_mockServer.Object, 1000, 5000);
        }

        [TearDown]
        public void TearDown()
        {
            m_eventManager?.Dispose();
        }

        [Test]
        public void ConstructorThrowsWhenServerNull()
        {
            Assert.That(() => new EventManager(null!, 100, 200), Throws.ArgumentNullException);
        }

        [Test]
        public void GetMonitoredItemsReturnsEmptyListInitially()
        {
            var items = m_eventManager.GetMonitoredItems();

            Assert.That(items, Is.Empty);
        }

        [Test]
        public void DeleteMonitoredItemDoesNotThrowWhenItemNotFound()
        {
            Assert.DoesNotThrow(() => m_eventManager.DeleteMonitoredItem(9999));
        }

        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            m_eventManager.Dispose();
            Assert.DoesNotThrow(() => m_eventManager.Dispose());
        }

        [Test]
        public void ReportEventThrowsWhenEventNull()
        {
#pragma warning disable CS0618 // testing obsolete method
            Assert.That(
                () => EventManager.ReportEvent(null!, new List<IEventMonitoredItem>()),
                Throws.ArgumentNullException);
#pragma warning restore CS0618
        }

        [Test]
        public void ReportEventQueuesEventToAllItems()
        {
            var item1 = new Mock<IEventMonitoredItem>();
            var item2 = new Mock<IEventMonitoredItem>();
            var ev = new Mock<IFilterTarget>();
            IList<IEventMonitoredItem> items = [item1.Object, item2.Object];

#pragma warning disable CS0618 // testing obsolete method
            EventManager.ReportEvent(ev.Object, items);
#pragma warning restore CS0618

            item1.Verify(m => m.QueueEvent(ev.Object), Times.Once);
            item2.Verify(m => m.QueueEvent(ev.Object), Times.Once);
        }

        [Test]
        public async Task ReportEventAsyncThrowsWhenEventNullAsync()
        {
            var nm = new Mock<IAsyncNodeManager>();
            var items = new List<IEventMonitoredItem>();

            await Assert.ThatAsync(
                async () => await EventManager.ReportEventAsync(null!, nm.Object, items).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReportEventAsyncThrowsWhenNodeManagerNullAsync()
        {
            var ev = new Mock<IFilterTarget>();
            var items = new List<IEventMonitoredItem>();

            await Assert.ThatAsync(
                async () => await EventManager.ReportEventAsync(ev.Object, null!, items).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReportEventAsyncThrowsWhenMonitoredItemsNullAsync()
        {
            var ev = new Mock<IFilterTarget>();
            var nm = new Mock<IAsyncNodeManager>();

            await Assert.ThatAsync(
                async () => await EventManager.ReportEventAsync(ev.Object, nm.Object, null!).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReportEventAsyncSkipsNullItemsAsync()
        {
            var nm = new Mock<IAsyncNodeManager>();
            nm.Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var item1 = new Mock<IEventMonitoredItem>();
            var ev = new Mock<IFilterTarget>();
            IList<IEventMonitoredItem> items = [null!, item1.Object];

            await EventManager.ReportEventAsync(ev.Object, nm.Object, items).ConfigureAwait(false);

            item1.Verify(m => m.QueueEvent(ev.Object), Times.Once);
        }

        [Test]
        public async Task ReportEventAsyncSkipsItemsWithBadPermissionAsync()
        {
            var nm = new Mock<IAsyncNodeManager>();
            nm.Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));

            var item1 = new Mock<IEventMonitoredItem>();
            var ev = new Mock<IFilterTarget>();
            IList<IEventMonitoredItem> items = [item1.Object];

            await EventManager.ReportEventAsync(ev.Object, nm.Object, items).ConfigureAwait(false);

            item1.Verify(m => m.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
        }
    }
}
