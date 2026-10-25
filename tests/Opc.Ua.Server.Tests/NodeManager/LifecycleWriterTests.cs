/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
    /// Verifies writer ownership independently of request, binding and retirement implementations.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class LifecycleWriterTests
    {
        /// <summary>
        /// A canceled waiter cannot release another mutation or overtake its successor.
        /// </summary>
        [Test]
        public async Task MutationOwnershipOrdersWaitersAndCancellationCannotReleasePeerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation first = await writer.BeginMutationAsync(timeout.Token).ConfigureAwait(false);
            using var canceled = new CancellationTokenSource();
            Task<LifecycleWriter.Mutation> canceledWaiter = writer.BeginMutationAsync(canceled.Token).AsTask();
            Task<LifecycleWriter.Mutation> secondWaiter = writer.BeginMutationAsync(timeout.Token).AsTask();
            canceled.Cancel();
            try
            {
                await canceledWaiter.ConfigureAwait(false);
                Assert.Fail("Canceled mutation admission unexpectedly succeeded.");
            }
            catch (OperationCanceledException)
            {
                Assert.That(secondWaiter.IsCompleted, Is.False);
            }
            first.Dispose();
            using LifecycleWriter.Mutation second = await secondWaiter.WaitAsync(timeout.Token).ConfigureAwait(false);
            Task<LifecycleWriter.Mutation> thirdWaiter = writer.BeginMutationAsync(timeout.Token).AsTask();
            Assert.That(thirdWaiter.IsCompleted, Is.False, "The second owner must still exclude its successor.");
            second.Dispose();
            using LifecycleWriter.Mutation third = await thirdWaiter.WaitAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Decision rejection follows spawned execution contexts only for the lifetime of the decision.
        /// </summary>
        [Test]
        public async Task DecisionRejectsSpawnedReentrancyAndExpiresInCapturedContextAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            var rejected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? spawned = null;
            await mutation.DecideAsync(async token =>
            {
                Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
                spawned = Task.Run(async () =>
                {
                    Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
                    rejected.TrySetResult(true);
                    await expired.Task.WaitAsync(token).ConfigureAwait(false);
                    using LifecycleWriter.OperationLifetime allowed = writer.EnterOperation();
                }, token);
                await rejected.Task.WaitAsync(token).ConfigureAwait(false);
            }, timeout.Token).ConfigureAwait(false);
            expired.TrySetResult(true);
            Assert.That(spawned, Is.Not.Null);
            await spawned!.WaitAsync(timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Releasing mutation serialization does not release the operation's shutdown exclusion.
        /// </summary>
        [Test]
        public async Task ShutdownWaitsForOperationAfterMutationOwnershipEndsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            mutation.Dispose();
            Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(writer.IsShuttingDown, Is.True);
                Assert.That(drained.IsCompleted, Is.False);
                Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
            }
            operation.Dispose();
            await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
            writer.EndShutdown(prepared: false, completing: true);
        }

        /// <summary>
        /// Independent sessions admit ordinary work and advance only their own successful publication evidence.
        /// </summary>
        [Test]
        public async Task PublicationSessionsCoexistAndOnlyObserveTheirOwnAdvancementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            LifecycleWriter.PublicationRevision revision = CreateRevision(writer, out NodeManagerRoutingTable routes);
            await using LifecycleWriter.PublicationSession first = writer.BeginPublication(revision);
            await using LifecycleWriter.PublicationSession second = writer.BeginPublication(revision);
            using LifecycleWriter.OperationLifetime ordinary = writer.EnterOperation();
            using (LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                first.ValidateRevision();
                second.ValidateRevision();
                routes.AddInitial(Mock.Of<IAsyncNodeManager>());
                first.AdvanceRevision();
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(first.IsCurrent, Is.True);
                Assert.That(second.IsCurrent, Is.False);
                Assert.Throws<InvalidOperationException>(() => second.ValidateRevision());
            }
            int preparations = 0;
            await Assert.ThatAsync(async () => await second.PrepareAsync(() =>
            {
                preparations++;
                return new ValueTask<PublicationCleanup>(new PublicationCleanup(() => { }));
            }).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(preparations, Is.Zero);
            _ = await first.PrepareAsync(() =>
            {
                preparations++;
                return new ValueTask<PublicationCleanup>(new PublicationCleanup(() => { }));
            }).ConfigureAwait(false);
            Assert.That(preparations, Is.EqualTo(1));
        }

        /// <summary>
        /// Disposal drains pending preparation, including cancellation, before releasing shutdown ownership.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicationDisposalWaitsForPreparationAndReleasesShutdownOwnershipAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            using var writer = new LifecycleWriter();
            LifecycleWriter.PublicationRevision revision = CreateRevision(writer, out _);
            await using LifecycleWriter.PublicationSession session = writer.BeginPublication(revision);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int disposals = 0;
            Task<PublicationCleanup> preparing = session.PrepareAsync<PublicationCleanup>(async () =>
            {
                await release.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);
                return new PublicationCleanup(() => disposals++);
            }).AsTask();
            Task disposal = session.DisposeAsync().AsTask();
            Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(drained.IsCompleted, Is.False);
                    Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
                }
                if (cancel)
                {
                    cancellation.Cancel();
                    await Assert.ThatAsync(() => preparing.WaitAsync(timeout.Token),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                }
                else
                {
                    release.TrySetResult(true);
                    _ = await preparing.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await disposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
                Assert.That(disposals, Is.EqualTo(cancel ? 0 : 1));
            }
            finally
            {
                cancellation.Cancel();
                writer.EndShutdown(prepared: false, completing: true);
            }
        }

        /// <summary>
        /// A failing transaction cleanup does not skip its siblings or release another invocation's lifetime.
        /// </summary>
        [Test]
        public async Task PublicationCleanupFailureStillDrainsAllTransactionsAndPreservesPeerOwnershipAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            LifecycleWriter.PublicationRevision revision = CreateRevision(writer, out _);
            await using LifecycleWriter.PublicationSession first = writer.BeginPublication(revision);
            await using LifecycleWriter.PublicationSession second = writer.BeginPublication(revision);
            int firstDisposals = 0;
            int peerDisposals = 0;
            _ = await first.PrepareAsync(() => new ValueTask<PublicationCleanup>(new PublicationCleanup(() =>
            {
                firstDisposals++;
                throw new InvalidOperationException("Transaction cleanup failed.");
            }))).ConfigureAwait(false);
            _ = await first.PrepareAsync(() => new ValueTask<PublicationCleanup>(
                new PublicationCleanup(() => firstDisposals++))).ConfigureAwait(false);
            _ = await second.PrepareAsync(() => new ValueTask<PublicationCleanup>(
                new PublicationCleanup(() => peerDisposals++))).ConfigureAwait(false);
            Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                await Assert.ThatAsync(() => first.DisposeAsync().AsTask(),
                    Throws.TypeOf<AggregateException>()).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(firstDisposals, Is.EqualTo(2));
                    Assert.That(peerDisposals, Is.Zero);
                    Assert.That(drained.IsCompleted, Is.False);
                }
                await second.DisposeAsync().ConfigureAwait(false);
                await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(peerDisposals, Is.EqualTo(1));
            }
            finally
            {
                writer.EndShutdown(prepared: false, completing: true);
            }
        }

        /// <summary>
        /// Canceling shutdown before admission must not close the writer or leak shutdown ownership.
        /// </summary>
        [Test]
        public async Task CanceledShutdownLeavesPublicationAdmissionOpenAsync()
        {
            using var writer = new LifecycleWriter();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(
                () => writer.BeginShutdownAsync(cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            Assert.That(writer.IsShuttingDown, Is.False);
        }

        /// <summary>
        /// Creates independent serving metadata whose routing revision tests can advance explicitly.
        /// </summary>
        private static LifecycleWriter.PublicationRevision CreateRevision(
            LifecycleWriter writer, out NodeManagerRoutingTable routes)
        {
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            var host = new Mock<IDynamicNodeManagerHost>();
            var routing = new NodeManagerRoutingTable();
            host.As<IDynamicNodeManagerBatchHost>().SetupGet(value => value.RoutingRevision)
                .Returns(() => routing.Revision);
            routes = routing;
            return writer.CaptureRevision(server.Object, host.Object);
        }

        /// <summary>
        /// Runs deterministic transaction cleanup without any server or timer work.
        /// </summary>
        private sealed class PublicationCleanup(Action cleanup) : IAsyncDisposable
        {
            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                cleanup();
                return default;
            }
        }
    }
}
