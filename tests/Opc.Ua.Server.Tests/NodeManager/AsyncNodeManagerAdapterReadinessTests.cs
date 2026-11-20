// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
    public class AsyncNodeManagerAdapterReadinessTests
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

        [Test]
        public async Task AsyncReadinessIsPreferredOverSyncReadinessAsync()
        {
            var manager = new Mock<INodeManager>();
            var asyncReady = manager.As<INodeManagerReadinessNotification>();
            var syncReady = manager.As<ISyncNodeManagerReadinessNotification>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            await adapter.OnServerReadyAsync(CancellationToken.None).ConfigureAwait(false);

            asyncReady.Verify(m => m.OnServerReadyAsync(It.IsAny<CancellationToken>()), Times.Once);
            syncReady.Verify(m => m.OnServerReady(), Times.Never);
            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.True);
        }

        [Test]
        public async Task SyncOnlyReadinessIsInvokedOnceAsync()
        {
            var manager = new Mock<INodeManager>();
            var syncReady = manager.As<ISyncNodeManagerReadinessNotification>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            await adapter.OnServerReadyAsync(CancellationToken.None).ConfigureAwait(false);

            syncReady.Verify(m => m.OnServerReady(), Times.Once);
            Assert.That(NodeManagerReadiness.IsParticipant(adapter), Is.True);
        }

        [Test]
        public void SyncReadinessIsNotInvokedWhenCancelledBeforeCall()
        {
            var manager = new Mock<INodeManager>();
            var syncReady = manager.As<ISyncNodeManagerReadinessNotification>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await adapter.OnServerReadyAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            syncReady.Verify(m => m.OnServerReady(), Times.Never);
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
