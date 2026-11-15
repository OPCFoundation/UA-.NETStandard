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
 * copies of the Software, and to permit persons to whom the Software
 * is furnished to do so, subject to the following conditions:
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Retirement closes contributor admission and joins a synchronous aggregate read before destruction.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishedContributorRetirementDrainsExplicitAggregateReadAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var gate = new OneShotConformanceReadGate();
            ConformanceUnitsManager catalog = GetReadDrainConformanceManager();
            GatedConformanceNodeManager? created = null;
            LifecycleResult addition = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(
                CreateReadDrainFactory(
                catalog, gate, false, manager => created = manager)), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(addition.Registrations[0]);
            GatedConformanceNodeManager manager = created ??
                throw new InvalidOperationException("The factory did not create its manager.");
            var trigger = new FixedConformanceContributor(
                new QualifiedName("Read Drain Trigger"), "urn:opcfoundation.org:Tests:ReadDrain:trigger");
            Task? aggregate = null;
            Task<LifecycleResult>? removal = null;
            try
            {
                catalog.Register(manager);
                StartExplicitAggregateRead(catalog, gate, trigger, unregister);
                Task aggregateTask = gate.Aggregate;
                aggregate = aggregateTask;
                await gate.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                removal = CommitChangeAsync(
                    LifecycleInspection.Capture(
                    m_server.NodeManagerLifecycle, registration), LifecycleChange.Remove(
                    registration.Slot), timeout.Token);

                ConformanceUnitsManager.ConformanceCleanupDrainState reserved =
                    await WaitForReadDrainReservationAsync(catalog, manager, timeout.Token).ConfigureAwait(false);
                await reserved.AdmissionClosed.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertPendingConformanceReadDrain(catalog, manager, gate);

                gate.Release();
                await aggregateTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                // Observe the owned worker itself after the bounded wait, before releasing gate storage.
                await aggregateTask.ConfigureAwait(false);
                await reserved.Drain.WaitAsync(timeout.Token).ConfigureAwait(false);
                LifecycleResult result = await removal.WaitAsync(timeout.Token).ConfigureAwait(false);
                await result.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(manager.DeleteCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.CleanupDuringReadCount, Is.Zero);
                Assert.That(manager.PostDisposalReadCount, Is.Zero);
                int readsAfterRetirement = manager.ReadCount;
                await catalog.PublishAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.ReadCount, Is.EqualTo(readsAfterRetirement));
            }
            finally
            {
                gate.Release();
                try
                {
                    if (aggregate is not null)
                    {
                        await aggregate.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        catalog.Unregister(trigger);
                    }
                    finally
                    {
                        if (removal is null)
                        {
                            catalog.Unregister(manager);
                        }
                        else
                        {
                            LifecycleResult result = await removal.WaitAsync(TimeSpan.FromSeconds(30))
                                .ConfigureAwait(false);
                            await result.Retired.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// An aborted private candidate remains registered until unregister and admitted reads release ownership.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AbortedExplicitContributorDefersCleanupUntilUnregisterAndReadDrainAsync(bool unregister)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var gate = new OneShotConformanceReadGate();
            ConformanceUnitsManager catalog = GetReadDrainConformanceManager();
            GatedConformanceNodeManager? created = null;
            IPreparedChanges prepared = await m_server.NodeManagerLifecycle.PrepareAsync(
                [LifecycleChange.Add(
                CreateReadDrainFactory(
                catalog, gate, true, manager => created = manager))], timeout.Token).ConfigureAwait(false);
            GatedConformanceNodeManager manager = created ??
                throw new InvalidOperationException("The factory did not create its candidate.");
            var trigger = new FixedConformanceContributor(
                new QualifiedName("Read Drain Trigger"), "urn:opcfoundation.org:Tests:ReadDrain:trigger");
            Task? aggregate = null;
            Task? abort = null;
            NodeManagerCleanupDeferredException? deferred = null;
            bool abortAttempted = false;
            try
            {
                StartExplicitAggregateRead(catalog, gate, trigger, unregister);
                Task aggregateTask = gate.Aggregate;
                aggregate = aggregateTask;
                await gate.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                abortAttempted = true;
                Task abortTask = prepared.DisposeAsync().AsTask();
                abort = abortTask;
                deferred = await Assert.ThrowsAsync<NodeManagerCleanupDeferredException>(
                    async () => await abortTask.WaitAsync(timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(deferred, Is.TypeOf<NodeManagerCleanupDeferredException>());
                NodeManagerCleanupDeferredException cleanup = deferred ??
                    throw new InvalidOperationException("Disposal did not report deferred cleanup.");
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(cleanup.Completion.IsCompleted, Is.False);
                Assert.That(catalog.TryGetCleanupDrainState(manager, out var retained), Is.True);
                Assert.That(retained.IsRegistered, Is.True);
                Assert.That(retained.ActiveReads, Is.GreaterThan(0));
                Assert.That(retained.AdmissionClosed.IsCompleted, Is.False);
                Assert.That(retained.Drain.IsCompleted, Is.False);
                Assert.That(manager.DeleteCount, Is.Zero);
                Assert.That(manager.DisposeCount, Is.Zero);

                int readsBeforeDuplicate = manager.ReadCount;
                catalog.Register(manager);
                Assert.That(manager.ReadCount, Is.EqualTo(readsBeforeDuplicate));
                Assert.That(catalog.TryGetCleanupDrainState(manager, out retained), Is.True);
                Assert.That(retained.IsRegistered, Is.True);

                Assert.That(catalog.Unregister(manager), Is.True);
                Assert.That(catalog.Unregister(manager), Is.False);
                await retained.AdmissionClosed.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertPendingConformanceReadDrain(catalog, manager, gate);
                Assert.That(cleanup.Completion.IsCompleted, Is.False);

                int readsBeforeRejectedRegistration = manager.ReadCount;
                Assert.That(() => catalog.Register(manager), Throws.TypeOf<InvalidOperationException>());
                Assert.That(manager.ReadCount, Is.EqualTo(readsBeforeRejectedRegistration));
                AssertPendingConformanceReadDrain(catalog, manager, gate);

                gate.Release();
                await aggregateTask.WaitAsync(timeout.Token).ConfigureAwait(false);
                // Observe the owned worker itself after the bounded wait, before releasing gate storage.
                await aggregateTask.ConfigureAwait(false);
                await retained.Drain.WaitAsync(timeout.Token).ConfigureAwait(false);
                await cleanup.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(manager.DeleteCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                Assert.That(manager.CleanupDuringReadCount, Is.Zero);
                Assert.That(manager.PostDisposalReadCount, Is.Zero);
                int readsAfterCleanup = manager.ReadCount;
                Assert.That(() => catalog.Register(manager), Throws.TypeOf<InvalidOperationException>());
                await catalog.PublishAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(manager.ReadCount, Is.EqualTo(readsAfterCleanup));
            }
            finally
            {
                gate.Release();
                try
                {
                    if (aggregate is not null)
                    {
                        await aggregate.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        catalog.Unregister(manager);
                    }
                    finally
                    {
                        try
                        {
                            catalog.Unregister(trigger);
                        }
                        finally
                        {
                            if (!abortAttempted)
                            {
                                await prepared.DisposeAsync().AsTask()
                                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                            }
                            else if (abort is not null)
                            {
                                try
                                {
                                    await abort.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                                }
                                catch (NodeManagerCleanupDeferredException failure)
                                {
                                    deferred ??= failure;
                                }
                            }
                            if (deferred is not null)
                            {
                                await deferred.Completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A live writer releases a deferred private candidate after the conformance drain completes.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [Test]
        public async Task ActiveWriterReleasesDeferredCandidateAfterConformanceDrainAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var gate = new OneShotConformanceReadGate();
            ConformanceUnitsManager catalog = GetReadDrainConformanceManager();
            ILifecycleWriter writer = await m_server.NodeManagerLifecycle
                .BeginAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
            var trigger = new FixedConformanceContributor(
                new QualifiedName("Read Drain Trigger"), "urn:opcfoundation.org:Tests:ReadDrain:trigger");
            IPreparedChanges? prepared = null;
            WeakReference? candidate = null;
            Task? aggregate = null;
            Task? disposal = null;
            NodeManagerCleanupDeferredException? deferred = null;
            try
            {
                var pending = await PrepareDeferredWriterCandidateAsync(writer, catalog, gate, timeout.Token)
                    .ConfigureAwait(false);
                prepared = pending.Prepared;
                candidate = pending.Candidate;
                StartExplicitAggregateRead(catalog, gate, trigger, unregister: false);
                aggregate = gate.Aggregate;
                await gate.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);

                Task disposalAttempt = prepared.DisposeAsync().AsTask();
                disposal = disposalAttempt;
                deferred = await Assert.ThrowsAsync<NodeManagerCleanupDeferredException>(
                    async () => await disposalAttempt.WaitAsync(timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false);
                NodeManagerCleanupDeferredException cleanup = deferred ??
                    throw new AssertionException("Disposal did not report deferred cleanup.");
                Assert.That(candidate.IsAlive, Is.True);
                Assert.That(cleanup.Completion.IsCompleted, Is.False);
                AssertPrivateCleanupCounts(candidate, 0, 0);

                UnregisterPrivateCandidate(catalog, candidate, expected: true);
                Assert.That(cleanup.Completion.IsCompleted, Is.False);
                gate.Release();
                await aggregate.WaitAsync(timeout.Token).ConfigureAwait(false);
                // Observe the owned worker itself after the bounded wait, before releasing gate storage.
                await aggregate.ConfigureAwait(false);
                await cleanup.Completion.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertPrivateCleanupCounts(candidate, 1, 1);
                await Task.Yield();
                for (int attempt = 0; attempt < 3 && candidate.IsAlive; attempt++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                NodeManagerRegistrationSnapshot snapshot = prepared.Registrations[0];
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(candidate.IsAlive, Is.False);
                    Assert.That(snapshot.TryGetNodeManager(out _), Is.False);
                    Assert.That(snapshot.TryGetRegistration(out _), Is.False);
                    Assert.That(writer.IsCurrent, Is.True);
                    Assert.That(cleanup.Completion.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                }
                await Assert.ThatAsync(
                    () => writer.DisposeAsync().AsTask(),
                    Throws.TypeOf<AggregateException>().With.InnerException.SameAs(cleanup)).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                if (candidate is not null)
                {
                    UnregisterPrivateCandidate(catalog, candidate, expected: false);
                }
                catalog.Unregister(trigger);
                if (aggregate is not null)
                {
                    await aggregate.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                if (prepared is not null && deferred is null)
                {
                    try
                    {
                        await prepared.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (NodeManagerCleanupDeferredException failure)
                    {
                        deferred = failure;
                    }
                }
                if (deferred is not null)
                {
                    await deferred.Completion.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                try
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch (AggregateException failure) when (
                    deferred is not null && ReferenceEquals(failure.InnerException, deferred))
                {
                }
                GC.KeepAlive(prepared);
                GC.KeepAlive(disposal);
                GC.KeepAlive(writer);
            }
        }

        /// <summary>
        /// Prepares a privately registered contributor without exposing a strong reference to the caller.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<(IPreparedChanges Prepared, WeakReference Candidate)> PrepareDeferredWriterCandidateAsync(
            ILifecycleWriter writer,
            ConformanceUnitsManager catalog,
            OneShotConformanceReadGate gate,
            CancellationToken cancellationToken)
        {
            WeakReference? candidate = null;
            CountingNodeManagerFactory factory = CreateReadDrainFactory(
                catalog, gate, true, manager => candidate = new WeakReference(manager));
            IPreparedChanges prepared = await writer.PrepareAsync(
                [LifecycleChange.Add(factory)], cancellationToken).ConfigureAwait(false);
            return (prepared, candidate ?? throw new AssertionException("The factory did not create its candidate."));
        }

        /// <summary>
        /// Unregisters a live candidate without keeping its manager on the collection assertion's stack.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UnregisterPrivateCandidate(
            ConformanceUnitsManager catalog, WeakReference candidate, bool expected)
        {
            bool removed = candidate.Target is GatedConformanceNodeManager manager && catalog.Unregister(manager);
            if (expected)
            {
                Assert.That(removed, Is.True);
            }
        }

        /// <summary>
        /// Observes cleanup counts without retaining the manager across the collection assertion.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AssertPrivateCleanupCounts(WeakReference candidate, int deletes, int disposals)
        {
            if (candidate.Target is not GatedConformanceNodeManager manager)
            {
                throw new AssertionException("The private candidate was collected before cleanup was observed.");
            }
            Assert.That(manager.DeleteCount, Is.EqualTo(deletes));
            Assert.That(manager.DisposeCount, Is.EqualTo(disposals));
        }

        /// <summary>
        /// Gets the live catalog used for explicit registrations, including private test candidates.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private ConformanceUnitsManager GetReadDrainConformanceManager()
        {
            return (m_server.CurrentInstance as IConformanceUnitsManagerProvider)?.ConformanceUnitsManager ??
                throw new InvalidOperationException("The live server has no conformance catalog.");
        }

        /// <summary>
        /// Creates a direct manager without a shared-resource facade or identity alias.
        /// </summary>
        private CountingNodeManagerFactory CreateReadDrainFactory(
            ConformanceUnitsManager catalog,
            OneShotConformanceReadGate gate,
            bool registerPrivateCandidate,
            Action<GatedConformanceNodeManager> created)
        {
            string namespaceUri = kModelNamespaceUri + ":ReadDrain:" + Guid.NewGuid().ToString("N");
            return new CountingNodeManagerFactory(
                [namespaceUri],
                (server, configuration, _) =>
                {
                    var manager = new GatedConformanceNodeManager(
                        server, configuration, m_logger, namespaceUri, gate);
                    created(manager);
                    if (registerPrivateCandidate)
                    {
                        catalog.Register(manager);
                    }
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
        }

        /// <summary>
        /// Starts one Register or Unregister aggregate on a dedicated thread, not a lifecycle worker.
        /// </summary>
        private static void StartExplicitAggregateRead(
            ConformanceUnitsManager catalog,
            OneShotConformanceReadGate gate,
            FixedConformanceContributor trigger,
            bool unregister)
        {
            if (unregister)
            {
                catalog.Register(trigger);
            }
            gate.Arm();
            Task aggregate = Task.Factory.StartNew(
                () =>
                {
                    if (unregister)
                    {
                        Assert.That(catalog.Unregister(trigger), Is.True);
                    }
                    else
                    {
                        catalog.Register(trigger);
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            gate.TrackAggregate(aggregate);
        }

        /// <summary>
        /// Discovers the exact cleanup reservation without using elapsed time as proof of ordering.
        /// </summary>
        private static async Task<ConformanceUnitsManager.ConformanceCleanupDrainState>
            WaitForReadDrainReservationAsync(
                ConformanceUnitsManager catalog,
                IConformanceContributor contributor,
                CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (catalog.TryGetCleanupDrainState(contributor, out var state))
                {
                    return state;
                }
                await Task.Yield();
            }
        }

        /// <summary>
        /// Checks the owner-side admission boundary while the exact getter is demonstrably still active.
        /// </summary>
        private static void AssertPendingConformanceReadDrain(
            ConformanceUnitsManager catalog,
            GatedConformanceNodeManager manager,
            OneShotConformanceReadGate gate)
        {
            Assert.That(catalog.TryGetCleanupDrainState(manager, out var state), Is.True);
            Assert.That(state.AdmissionClosed.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(state.IsRegistered, Is.False);
            Assert.That(state.ActiveReads, Is.GreaterThan(0));
            Assert.That(state.Drain.IsCompleted, Is.False);
            Assert.That(gate.IsReadPending, Is.True);
            Assert.That(manager.DeleteCount, Is.Zero);
            Assert.That(manager.DisposeCount, Is.Zero);
        }

        /// <summary>
        /// Observes contributor reads and physical cleanup on one direct test-owned manager.
        /// </summary>
        private sealed class GatedConformanceNodeManager :
            NodeManagementLifecycleNodeManager,
            IConformanceContributor
        {
            /// <summary>
            /// Creates a manager whose next armed unit getter can be held by the test.
            /// </summary>
            public GatedConformanceNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                Microsoft.Extensions.Logging.ILogger logger,
                string namespaceUri,
                OneShotConformanceReadGate gate)
                : base(server, configuration, logger, namespaceUri)
            {
                m_gate = gate;
            }

            /// <summary>
            /// Gets the contribution getter invocation count.
            /// </summary>
            public int ReadCount => Volatile.Read(ref m_reads);

            /// <summary>
            /// Gets contribution getter invocations after completed disposal.
            /// </summary>
            public int PostDisposalReadCount => Volatile.Read(ref m_postDisposalReads);

            /// <summary>
            /// Gets address-space deletion attempts.
            /// </summary>
            public int DeleteCount => Volatile.Read(ref m_deletes);

            /// <summary>
            /// Gets disposal attempts.
            /// </summary>
            public int DisposeCount => Volatile.Read(ref m_disposals);

            /// <summary>
            /// Gets physical cleanup attempts while the gated getter remained active.
            /// </summary>
            public int CleanupDuringReadCount => Volatile.Read(ref m_cleanupDuringRead);

            /// <inheritdoc/>
            public ArrayOf<QualifiedName> ConformanceUnits
            {
                get
                {
                    RecordRead();
                    m_gate.ObserveRead();
                    return [new QualifiedName("Read Drain Candidate Unit")];
                }
            }

            /// <inheritdoc/>
            public ArrayOf<string> ServerProfiles
            {
                get
                {
                    RecordRead();
                    return ["urn:opcfoundation.org:Tests:ReadDrain:candidate"];
                }
            }

            /// <inheritdoc/>
            public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deletes);
                RecordCleanup();
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public override async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref m_disposals);
                RecordCleanup();
                await base.DisposeAsync().ConfigureAwait(false);
                Volatile.Write(ref m_disposed, 1);
            }

            /// <summary>
            /// Records a getter without acquiring any production lock.
            /// </summary>
            private void RecordRead()
            {
                Interlocked.Increment(ref m_reads);
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    Interlocked.Increment(ref m_postDisposalReads);
                }
            }

            /// <summary>
            /// Records a physical cleanup callback overlapping the held getter.
            /// </summary>
            private void RecordCleanup()
            {
                if (m_gate.IsReadPending)
                {
                    Interlocked.Increment(ref m_cleanupDuringRead);
                }
            }

            /// <summary>The test-owned getter gate.</summary>
            private readonly OneShotConformanceReadGate m_gate;

            /// <summary>The getter invocation count.</summary>
            private int m_reads;

            /// <summary>The getter count after disposal.</summary>
            private int m_postDisposalReads;

            /// <summary>The address-space deletion count.</summary>
            private int m_deletes;

            /// <summary>The disposal count.</summary>
            private int m_disposals;

            /// <summary>The completed-disposal marker.</summary>
            private int m_disposed;

            /// <summary>The cleanup count while a getter was held.</summary>
            private int m_cleanupDuringRead;
        }
    }
}
