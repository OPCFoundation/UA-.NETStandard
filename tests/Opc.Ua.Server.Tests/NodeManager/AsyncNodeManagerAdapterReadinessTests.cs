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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Unit tests for readiness notification forwarding in <see cref="AsyncNodeManagerAdapter"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AsyncNodeManagerAdapterReadinessTests
    {
        /// <summary>
        /// Disposing a round-trip facade reaches its underlying asynchronous manager exactly once.
        /// </summary>
        /// <param name="asynchronous">Whether the owner requests asynchronous disposal.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RoundtripDisposalReleasesUnderlyingManagerAsync(bool asynchronous)
        {
            var manager = new Mock<IAsyncNodeManager>();
            var disposable = manager.As<IDisposable>();
            var asyncDisposable = manager.As<IAsyncDisposable>();
            var adapter = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(manager.Object));
            if (asynchronous)
            {
                await adapter.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                adapter.Dispose();
            }
            disposable.Verify(value => value.Dispose(), asynchronous ? Times.Never() : Times.Once());
            asyncDisposable.Verify(value => value.DisposeAsync(), asynchronous ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// A classic manager's TAP readiness callback receives the caller's cancellation token.
        /// </summary>
        [Test]
        public async Task ClassicReadinessReceivesCallerCancellationTokenAsync()
        {
            var manager = new Mock<INodeManager>();
            var asyncReady = manager.As<INodeManagerReadinessNotification>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);
            using var cancellation = new CancellationTokenSource();

            await adapter.OnServerReadyAsync(cancellation.Token).ConfigureAwait(false);

            asyncReady.Verify(m => m.OnServerReadyAsync(cancellation.Token), Times.Once);
            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.True);
        }

        /// <summary>
        /// A classic manager's synchronously completed TAP readiness callback is called once.
        /// </summary>
        [Test]
        public async Task SyncOnlyReadinessIsInvokedOnceAsync()
        {
            var manager = new Mock<INodeManager>();
            Mock<INodeManagerReadinessNotification> ready = manager.As<INodeManagerReadinessNotification>();
            ready.Setup(value => value.OnServerReadyAsync(CancellationToken.None)).Returns(default(ValueTask));
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            Assert.That(manager.Object, Is.Not.InstanceOf<IAsyncNodeManager>());
            await adapter.OnServerReadyAsync(CancellationToken.None).ConfigureAwait(false);

            ready.Verify(value => value.OnServerReadyAsync(CancellationToken.None), Times.Once);
            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.True);
        }

        /// <summary>
        /// A classic manager's TAP readiness callback receives and observes cancellation.
        /// </summary>
        [Test]
        public async Task ClassicReadinessReceivesCanceledTokenAsync()
        {
            var manager = new Mock<INodeManager>();
            Mock<INodeManagerReadinessNotification> ready = manager.As<INodeManagerReadinessNotification>();
            ready.Setup(value => value.OnServerReadyAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return default;
                });
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                async () => await adapter.OnServerReadyAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            ready.Verify(value => value.OnServerReadyAsync(cts.Token), Times.Once);
        }

        [Test]
        public void ManagerWithoutReadinessCapabilityCompletesImmediately()
        {
            var manager = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            ValueTask pending = adapter.OnServerReadyAsync(CancellationToken.None);

            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.False);
        }

        [Test]
        public void AsyncFacadeOverSyncManagerWithoutReadinessCapabilityIsNotParticipant()
        {
            var manager = new Mock<IAsyncNodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(manager.Object));

            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.False);
        }

        [Test]
        public void AsyncFacadeOverSyncManagerWithReadinessCapabilityIsParticipant()
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.As<INodeManagerReadinessNotification>();
            using var adapter = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(manager.Object));

            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.True);
        }
    }
}
