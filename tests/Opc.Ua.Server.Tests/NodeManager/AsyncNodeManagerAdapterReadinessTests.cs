// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Covers the deprecated synchronous node manager API on purpose.

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
