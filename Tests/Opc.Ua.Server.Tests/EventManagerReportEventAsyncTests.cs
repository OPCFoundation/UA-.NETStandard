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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Focused tests for the permission gate added to
    /// <see cref="EventManager.ReportEventAsync"/> (closes the
    /// permission bypass in the legacy
    /// <c>EventManager.ReportEvent(IFilterTarget, IList&lt;IEventMonitoredItem&gt;)</c>
    /// overload).
    /// </summary>
    [TestFixture]
    [Category("EventManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EventManagerReportEventAsyncTests
    {
        /// <summary>
        /// When the node manager grants <c>ReceiveEvents</c>, the event is
        /// queued on every supplied item.
        /// </summary>
        [Test]
        public async Task ReportEventAsync_WithPermission_QueuesToAllItems()
        {
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

            var item1 = new Mock<IEventMonitoredItem>();
            item1.Setup(m => m.Id).Returns(1u);
            var item2 = new Mock<IEventMonitoredItem>();
            item2.Setup(m => m.Id).Returns(2u);

            IList<IEventMonitoredItem> receivers = [item1.Object, item2.Object];
            var ev = new BaseEventState(null);

            await EventManager.ReportEventAsync(ev, nodeManagerMock.Object, receivers).ConfigureAwait(false);

            item1.Verify(m => m.QueueEvent(ev), Times.Once);
            item2.Verify(m => m.QueueEvent(ev), Times.Once);
        }

        /// <summary>
        /// When the node manager denies <c>ReceiveEvents</c>, the event is
        /// dropped silently — no item receives a queue call.
        /// </summary>
        [Test]
        public async Task ReportEventAsync_WithoutPermission_DropsEvent()
        {
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));

            var item = new Mock<IEventMonitoredItem>();
            item.Setup(m => m.Id).Returns(1u);

            IList<IEventMonitoredItem> receivers = [item.Object];
            var ev = new BaseEventState(null);

            await EventManager.ReportEventAsync(ev, nodeManagerMock.Object, receivers).ConfigureAwait(false);

            item.Verify(m => m.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
        }

        /// <summary>
        /// The gate is evaluated per item, so a mixed-permission set still
        /// delivers to the permitted item only.
        /// </summary>
        [Test]
        public async Task ReportEventAsync_MixedPermissions_DeliversOnlyToPermittedItem()
        {
            var permittedItem = new Mock<IEventMonitoredItem>();
            permittedItem.Setup(m => m.Id).Returns(1u);
            var deniedItem = new Mock<IEventMonitoredItem>();
            deniedItem.Setup(m => m.Id).Returns(2u);

            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    permittedItem.Object,
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            nodeManagerMock
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    deniedItem.Object,
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadUserAccessDenied)));

            IList<IEventMonitoredItem> receivers = [permittedItem.Object, deniedItem.Object];
            var ev = new BaseEventState(null);

            await EventManager.ReportEventAsync(ev, nodeManagerMock.Object, receivers).ConfigureAwait(false);

            permittedItem.Verify(m => m.QueueEvent(ev), Times.Once);
            deniedItem.Verify(m => m.QueueEvent(It.IsAny<IFilterTarget>()), Times.Never);
        }
    }
}
