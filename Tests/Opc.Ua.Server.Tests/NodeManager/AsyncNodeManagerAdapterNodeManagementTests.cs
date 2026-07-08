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

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Unit tests for the NodeManagement facet on <see cref="AsyncNodeManagerAdapter"/>.
    /// The adapter forwards to the wrapped <see cref="INodeManager"/> when it also
    /// implements <see cref="INodeManagementAsyncNodeManager"/>; otherwise the
    /// methods return <see cref="StatusCodes.BadServiceUnsupported"/> and
    /// <see cref="INodeManagementAsyncNodeManager.AllowNodeManagement"/> is
    /// <c>false</c>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("NodeManagement")]
    [Parallelizable(ParallelScope.All)]
    public class AsyncNodeManagerAdapterNodeManagementTests
    {
        [Test]
        public void AllowNodeManagement_LegacySyncManager_ReturnsFalse()
        {
            var sync = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(sync.Object);

            Assert.That(adapter.AllowNodeManagement, Is.False);
        }

        [Test]
        public void AllowNodeManagement_NmSupportingFacet_ForwardsValue()
        {
            var combined = new Mock<INodeManagerWithNodeManagement>();
            combined.Setup(m => m.AllowNodeManagement).Returns(true);
            using var adapter = new AsyncNodeManagerAdapter(combined.Object);

            Assert.That(adapter.AllowNodeManagement, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_LegacySyncManager_ReturnsBadServiceUnsupportedAsync()
        {
            var sync = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(sync.Object);

            (ServiceResult result, NodeId added) = await adapter.AddNodeAsync(
                NewOpContext(RequestType.AddNodes),
                new AddNodesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
            Assert.That(added.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_NmSupportingFacet_ForwardsCallAsync()
        {
            var combined = new Mock<INodeManagerWithNodeManagement>();
            var newId = new NodeId(42, 2);
            combined.Setup(m => m.AddNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddNodesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceResult.Good, newId));
            using var adapter = new AsyncNodeManagerAdapter(combined.Object);

            (ServiceResult result, NodeId added) = await adapter.AddNodeAsync(
                NewOpContext(RequestType.AddNodes),
                new AddNodesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(added, Is.EqualTo(newId));
        }

        [Test]
        public async Task DeleteNodeAsync_LegacySyncManager_ReturnsBadServiceUnsupportedAsync()
        {
            var sync = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(sync.Object);

            ServiceResult result = await adapter.DeleteNodeAsync(
                NewOpContext(RequestType.DeleteNodes),
                new DeleteNodesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public async Task DeleteNodeAsync_NmSupportingFacet_ForwardsCallAsync()
        {
            var combined = new Mock<INodeManagerWithNodeManagement>();
            combined.Setup(m => m.DeleteNodeAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteNodesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            using var adapter = new AsyncNodeManagerAdapter(combined.Object);

            ServiceResult result = await adapter.DeleteNodeAsync(
                NewOpContext(RequestType.DeleteNodes),
                new DeleteNodesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task AddReferenceAsync_LegacySyncManager_ReturnsBadServiceUnsupportedAsync()
        {
            var sync = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(sync.Object);

            ServiceResult result = await adapter.AddReferenceAsync(
                NewOpContext(RequestType.AddReferences),
                new AddReferencesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public async Task AddReferenceAsync_NmSupportingFacet_ForwardsCallAsync()
        {
            var combined = new Mock<INodeManagerWithNodeManagement>();
            combined.Setup(m => m.AddReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<AddReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            using var adapter = new AsyncNodeManagerAdapter(combined.Object);

            ServiceResult result = await adapter.AddReferenceAsync(
                NewOpContext(RequestType.AddReferences),
                new AddReferencesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task DeleteReferenceAsync_LegacySyncManager_ReturnsBadServiceUnsupportedAsync()
        {
            var sync = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(sync.Object);

            ServiceResult result = await adapter.DeleteReferenceAsync(
                NewOpContext(RequestType.DeleteReferences),
                new DeleteReferencesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadServiceUnsupported));
        }

        [Test]
        public async Task DeleteReferenceAsync_NmSupportingFacet_ForwardsCallAsync()
        {
            var combined = new Mock<INodeManagerWithNodeManagement>();
            combined.Setup(m => m.DeleteReferenceAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<DeleteReferencesItem>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            using var adapter = new AsyncNodeManagerAdapter(combined.Object);

            ServiceResult result = await adapter.DeleteReferenceAsync(
                NewOpContext(RequestType.DeleteReferences),
                new DeleteReferencesItem(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        /// <summary>
        /// Composite test interface combining <see cref="INodeManager"/> with the
        /// new NodeManagement facet so a single Moq object satisfies both ends.
        /// </summary>
        public interface INodeManagerWithNodeManagement : INodeManager, INodeManagementAsyncNodeManager;

        private static OperationContext NewOpContext(RequestType type)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(), null!, type, RequestLifetime.None, session.Object);
        }
    }
}
