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
        /// The host window stays visible to callback descendants until the host releases its reservation.
        /// </summary>
        [Test]
        public async Task HostDecisionWindowOutlivesCallbackOnlyInItsOwnFlowAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime operation = writer.EnterOperation();
            using LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Schedule the independent probe before entering the reservation's compatibility scope.
            LifecycleWriter.Mutation.DecisionWindow? reservation = null;
            Task<bool> independent = Task.Run(async () =>
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                return reservation!.IsInCurrentFlow;
            }, timeout.Token);

            Task<bool> descended;
            using (reservation = mutation.EnterDecisionWindow())
            {
                await mutation.DecideAsync(_ => default, reservation, timeout.Token).ConfigureAwait(false);
                entered.TrySetResult(true);
                Assert.That(await independent.WaitAsync(timeout.Token).ConfigureAwait(false), Is.False);
                Assert.That(reservation.IsInCurrentFlow, Is.True);
                descended = Task.Run(async () =>
                {
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    return reservation.IsInCurrentFlow;
                }, timeout.Token);
            }

            release.TrySetResult(true);
            Assert.That(await descended.WaitAsync(timeout.Token).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// A host cannot run a decision against an expired reservation or another mutation's reservation.
        /// </summary>
        [Test]
        public async Task DecisionRequiresAnActiveReservationFromItsMutationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using var otherWriter = new LifecycleWriter();
            using LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            using LifecycleWriter.Mutation otherMutation = await otherWriter.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false);
            using LifecycleWriter.Mutation.DecisionWindow reservation = mutation.EnterDecisionWindow();

            static ValueTask UnexpectedDecision(CancellationToken _) =>
                throw new AssertionException("An invalid reservation must not invoke the durable decision.");

            await Assert.ThatAsync(
                async () => await otherMutation.DecideAsync(UnexpectedDecision, reservation, timeout.Token)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("not active for this mutation"))
                .ConfigureAwait(false);

            reservation.Dispose();
            await Assert.ThatAsync(
                async () => await mutation.DecideAsync(UnexpectedDecision, reservation, timeout.Token)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("not active for this mutation"))
                .ConfigureAwait(false);
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
        /// Independently transferred operations retain shutdown exclusion across contexts and repeated disposal.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task TransferredOperationsDrainIndependentlyAcrossExecutionContextsAsync(bool releaseFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using LifecycleWriter.OperationLifetime first = writer.EnterOperation();
            using LifecycleWriter.OperationLifetime second = writer.EnterOperation();
            Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(writer.ActiveOperationCount, Is.EqualTo(2));
            Assert.That(drained.IsCompleted, Is.False);
            LifecycleWriter.OperationLifetime released = releaseFirst ? first : second;
            LifecycleWriter.OperationLifetime retained = releaseFirst ? second : first;
            Task release;
            using (ExecutionContext.SuppressFlow())
            {
                release = Task.Run(() =>
                {
                    released.Dispose();
                    released.Dispose();
                }, timeout.Token);
            }
            await release.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(writer.ActiveOperationCount, Is.EqualTo(1));
            Assert.That(drained.IsCompleted, Is.False);
            Assert.Throws<InvalidOperationException>(() => writer.EnterOperation());
            retained.Dispose();
            await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(writer.ActiveOperationCount, Is.Zero);
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
            using var serving = new PublicationServingFixture(writer);
            LifecycleWriter.PublicationRevision revision = serving.Revision;
            await using LifecycleWriter.PublicationSession first = writer.BeginPublication(revision);
            await using LifecycleWriter.PublicationSession second = writer.BeginPublication(revision);
            using LifecycleWriter.OperationLifetime ordinary = writer.EnterOperation();
            using (LifecycleWriter.Mutation mutation = await writer.BeginMutationAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                first.ValidateRevision();
                second.ValidateRevision();
                serving.Routes.AddInitial(Mock.Of<IAsyncNodeManager>());
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
            using var serving = new PublicationServingFixture(writer);
            LifecycleWriter.PublicationRevision revision = serving.Revision;
            await using LifecycleWriter.PublicationSession session = writer.BeginPublication(revision);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int disposals = 0;
            Task<PublicationCleanup> preparing = session.PrepareAsync<PublicationCleanup>(async () =>
            {
                await release.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);
                return new PublicationCleanup(() => disposals++);
            }).AsTask();
            Assert.That(preparing.IsCompleted, Is.False, "The factory must be admitted and waiting for release.");
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
            using var serving = new PublicationServingFixture(writer);
            LifecycleWriter.PublicationRevision revision = serving.Revision;
            LifecycleWriter.PublicationSession first = writer.BeginPublication(revision);
            await using LifecycleWriter.PublicationSession second = writer.BeginPublication(revision);
            int firstDisposals = 0;
            int peerDisposals = 0;
            var failure = new InvalidOperationException("Transaction cleanup failed.");
            AggregateException? observed = null;
            _ = await first.PrepareAsync(() => new ValueTask<PublicationCleanup>(new PublicationCleanup(() =>
            {
                firstDisposals++;
                throw failure;
            }))).ConfigureAwait(false);
            _ = await first.PrepareAsync(() => new ValueTask<PublicationCleanup>(
                new PublicationCleanup(() => firstDisposals++))).ConfigureAwait(false);
            _ = await second.PrepareAsync(() => new ValueTask<PublicationCleanup>(
                new PublicationCleanup(() => peerDisposals++))).ConfigureAwait(false);
            Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                observed = await Assert.ThrowsAsync<AggregateException>(
                    async () => await first.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(observed?.InnerException, Is.SameAs(failure));
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
                try
                {
                    AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(
                        async () => await first.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(repeated?.InnerException, Is.SameAs(failure));
                    if (observed is not null)
                    {
                        Assert.That(repeated, Is.SameAs(observed));
                    }
                    Assert.That(firstDisposals, Is.EqualTo(2));
                }
                finally
                {
                    writer.EndShutdown(prepared: false, completing: true);
                }
            }
        }

        /// <summary>
        /// Completed units are removed by reference without losing an earlier disposal failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicationReleasesExactCompletedUnitAndPreservesFailureAsync(bool retainFailure)
        {
            using var writer = new LifecycleWriter();
            using var serving = new PublicationServingFixture(writer);
            LifecycleWriter.PublicationRevision revision = serving.Revision;
            LifecycleWriter.PublicationSession session = writer.BeginPublication(revision);
            int firstDisposals = 0;
            int secondDisposals = 0;
            var first = new EqualPublicationCleanup(() => firstDisposals++);
            var second = new EqualPublicationCleanup(() => secondDisposals++);
            var failure = new InvalidOperationException("The second cleanup initially failed.");
            AggregateException? observed = null;
            bool failureRetained = false;
            try
            {
                Assert.That(first, Is.EqualTo(second));
                _ = await session.PrepareAsync(() => new ValueTask<EqualPublicationCleanup>(first))
                    .ConfigureAwait(false);
                _ = await session.PrepareAsync(() => new ValueTask<EqualPublicationCleanup>(second))
                    .ConfigureAwait(false);

                await second.DisposeAsync().ConfigureAwait(false);
                session.ReleaseCompletedBatch(second, retainFailure ? failure : null);
                failureRetained = retainFailure;
                if (retainFailure)
                {
                    observed = await Assert.ThrowsAsync<AggregateException>(
                        async () => await session.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(observed?.InnerException, Is.SameAs(failure));
                }
                else
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(firstDisposals, Is.EqualTo(1));
                    Assert.That(secondDisposals, Is.EqualTo(1));
                    Assert.That(writer.ActiveOperationCount, Is.Zero);
                    Assert.That(session.IsCurrent, Is.False);
                }
                session.ReleaseCompletedBatch(first, failure);
                Assert.Throws<ObjectDisposedException>(() => session.ValidateRevision());
                Assert.Throws<ObjectDisposedException>(() => session.AdvanceRevision());
            }
            finally
            {
                if (failureRetained)
                {
                    AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(
                        async () => await session.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(repeated?.InnerException, Is.SameAs(failure));
                    if (observed is not null)
                    {
                        Assert.That(repeated, Is.SameAs(observed));
                    }
                }
                else
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstDisposals, Is.EqualTo(1));
                Assert.That(secondDisposals, Is.EqualTo(1));
                Assert.That(writer.ActiveOperationCount, Is.Zero);
            }
        }

        /// <summary>
        /// Invocation disposal joins an active unit without allowing its completion to mutate the drain.
        /// </summary>
        [Test]
        public async Task PublicationDisposalJoinsConcurrentUnitCleanupAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using var serving = new PublicationServingFixture(writer);
            LifecycleWriter.PublicationRevision revision = serving.Revision;
            await using LifecycleWriter.PublicationSession session = writer.BeginPublication(revision);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            Task? cleanup = null;
            first.Setup(value => value.DisposeAsync()).Returns(() => new ValueTask(cleanup ??= CompleteAsync()));
            int secondDisposals = 0;
            _ = await session.PrepareAsync(() => new ValueTask<IAsyncDisposable>(first.Object))
                .ConfigureAwait(false);
            _ = await session.PrepareAsync(() => new ValueTask<PublicationCleanup>(
                new PublicationCleanup(() => secondDisposals++))).ConfigureAwait(false);

            Task unitDisposal = first.Object.DisposeAsync().AsTask();
            Task invocationDisposal = session.DisposeAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(invocationDisposal.IsCompleted, Is.False);
                release.TrySetResult(true);
                await unitDisposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                await invocationDisposal.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(secondDisposals, Is.EqualTo(1));
                    Assert.That(writer.ActiveOperationCount, Is.Zero);
                }
                first.Verify(value => value.DisposeAsync(), Times.Exactly(2));
            }
            finally
            {
                release.TrySetResult(true);
            }

            async Task CompleteAsync()
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                session.ReleaseCompletedBatch(first.Object);
            }
        }

        /// <summary>
        /// Concurrent cleanup observers receive the same failure before ownership is released.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublicationDisposalSharesFailureAndReleasesShutdownOwnershipAsync(bool observeCompletion)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var writer = new LifecycleWriter();
            using var serving = new PublicationServingFixture(writer);
            LifecycleWriter.PublicationRevision revision = serving.Revision;
            LifecycleWriter.PublicationSession session = writer.BeginPublication(revision);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("Transaction cleanup failed.");
            var cleanup = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            cleanup.Setup(value => value.DisposeAsync()).Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                throw failure;
            });
            _ = await session.PrepareAsync(() => new ValueTask<IAsyncDisposable>(cleanup.Object))
                .ConfigureAwait(false);
            int siblingDisposals = 0;
            _ = await session.PrepareAsync(() => new ValueTask<PublicationCleanup>(
                new PublicationCleanup(() => siblingDisposals++))).ConfigureAwait(false);
            Task disposal = session.DisposeAsync().AsTask();
            Task? observer = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                observer = observeCompletion ? session.Finished : session.DisposeAsync().AsTask();
                Task drained = await writer.BeginShutdownAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(disposal.IsCompleted, Is.False);
                    Assert.That(observer.IsCompleted, Is.False);
                    Assert.That(drained.IsCompleted, Is.False);
                    Assert.That(writer.ActiveOperationCount, Is.EqualTo(1));
                    Assert.That(siblingDisposals, Is.Zero);
                }
                release.TrySetResult(true);
                await Assert.ThatAsync(() => disposal.WaitAsync(timeout.Token),
                    Throws.TypeOf<AggregateException>().With.InnerException.SameAs(failure)).ConfigureAwait(false);
                await Assert.ThatAsync(() => observer.WaitAsync(timeout.Token),
                    Throws.TypeOf<AggregateException>().With.InnerException.SameAs(failure)).ConfigureAwait(false);
                await drained.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(observer.Exception!.InnerException, Is.SameAs(disposal.Exception!.InnerException));
                    Assert.That(siblingDisposals, Is.EqualTo(1));
                    Assert.That(writer.ActiveOperationCount, Is.Zero);
                }
                AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(
                    async () => await session.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(repeated, Is.SameAs(disposal.Exception!.InnerException));
                cleanup.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await Assert.ThatAsync(() => disposal.WaitAsync(timeout.Token),
                        Throws.TypeOf<AggregateException>().With.InnerException.SameAs(failure)).ConfigureAwait(false);
                    if (observer is not null)
                    {
                        await Assert.ThatAsync(() => observer.WaitAsync(timeout.Token),
                            Throws.TypeOf<AggregateException>().With.InnerException.SameAs(failure))
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    writer.EndShutdown(prepared: false, completing: true);
                }
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
        /// Owns the serving objects independently of the writer's non-owning captured revision.
        /// </summary>
        private sealed class PublicationServingFixture : IDisposable
        {
            /// <summary>
            /// Creates independent serving metadata whose routing revision tests can advance explicitly.
            /// </summary>
            public PublicationServingFixture(LifecycleWriter writer)
            {
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                var host = new Mock<IDynamicNodeManagerHost>();
                Routes = new NodeManagerRoutingTable();
                host.As<IDynamicNodeManagerBatchHost>().SetupGet(value => value.RoutingRevision)
                    .Returns(() => Routes.Revision);
                m_server = server.Object;
                m_host = host.Object;
                Revision = writer.CaptureRevision(m_server, m_host);
            }

            /// <summary>
            /// Gets the non-owning captured evidence shared by this fixture's sessions.
            /// </summary>
            public LifecycleWriter.PublicationRevision Revision { get; }

            /// <summary>
            /// Gets the serving route publisher used by explicit advancement controls.
            /// </summary>
            public NodeManagerRoutingTable Routes { get; }

            /// <summary>
            /// Keeps serving ownership through all session teardown, independently of JIT local lifetimes.
            /// </summary>
            public void Dispose()
            {
                m_server.Dispose();
                GC.KeepAlive(m_host);
                GC.SuppressFinalize(this);
            }

            /// <summary>The fixture-owned server and its current type/factory images.</summary>
            private readonly IServerInternal m_server;

            /// <summary>The fixture-owned routing host.</summary>
            private readonly IDynamicNodeManagerHost m_host;
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

        /// <summary>
        /// Supplies equal but independently owned transactions.
        /// </summary>
        private sealed class EqualPublicationCleanup(Action cleanup) : IAsyncDisposable
        {
            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                cleanup();
                return default;
            }

            /// <inheritdoc/>
            public override bool Equals(object? other)
            {
                return other is EqualPublicationCleanup;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
