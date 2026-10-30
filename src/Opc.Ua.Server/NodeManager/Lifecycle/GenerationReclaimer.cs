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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns retired generation drains, independent cleanup workers, and retryable destruction stages.
    /// </summary>
    internal sealed class GenerationReclaimer : IDisposable
    {
        /// <summary>
        /// Creates a reclaimer with an optional clock for delayed cleanup retries.
        /// </summary>
        /// <param name="timeProvider">The clock used for retry delays.</param>
        internal GenerationReclaimer(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the number of generations still owned by the reclaimer.
        /// </summary>
        internal int Count
        {
            get
            {
                lock (m_sync)
                {
                    return m_pending.Count;
                }
            }
        }

        /// <summary>
        /// Gets the monotonic count of completed cleanup stages.
        /// </summary>
        internal long CleanupProgress => Interlocked.Read(ref m_shutdownCleanupProgress);

        /// <summary>
        /// Captures pending generations without exposing the ownership collection.
        /// </summary>
        /// <returns>The generations still awaiting cleanup.</returns>
        internal RetiredGeneration[] Snapshot()
        {
            lock (m_sync)
            {
                return [.. m_pending];
            }
        }

        /// <summary>
        /// Reserves retirement storage before entering the publication decision.
        /// </summary>
        /// <param name="count">The number of generations the publication may retire.</param>
        internal void Reserve(int count)
        {
            lock (m_sync)
            {
                int capacity = checked(m_pending.Count + count);
                if (m_pending.Capacity < capacity)
                {
                    m_pending.Capacity = capacity;
                }
            }
        }

        /// <summary>
        /// Transfers committed retirement records without starting callbacks under the publication writer.
        /// </summary>
        /// <param name="retired">The records belonging to the committed publication.</param>
        internal void Enqueue(List<RetiredGeneration> retired)
        {
            lock (m_sync)
            {
                m_pending.AddRange(retired);
            }
        }

        /// <summary>
        /// Tests whether the reclaimer still owns an exact retirement record.
        /// </summary>
        /// <param name="retired">The retirement record.</param>
        /// <returns>Whether cleanup remains pending.</returns>
        internal bool Contains(RetiredGeneration retired)
        {
            lock (m_sync)
            {
                return m_pending.Contains(retired);
            }
        }

        /// <summary>
        /// Enables cleanup after publication and readiness have released the writer.
        /// </summary>
        /// <param name="retired">The publication's retirement records.</param>
        internal void Activate(List<RetiredGeneration> retired)
        {
            lock (m_sync)
            {
                foreach (RetiredGeneration generation in retired)
                {
                    generation.Ready = true;
                    StartTransitionFinalization(generation);
                }
            }
        }

        /// <summary>
        /// Starts item-status finalization without enabling physical cleanup before readiness completes.
        /// </summary>
        /// <param name="retired">The records whose logical item transitions have been reconciled.</param>
        internal void FinalizeMonitoredItemTransitions(List<RetiredGeneration> retired)
        {
            foreach (RetiredGeneration generation in retired)
            {
                StartTransitionFinalization(generation);
            }
        }

        /// <summary>
        /// Restarts scheduling after a completed server shutdown.
        /// </summary>
        internal void BeginStartup()
        {
            lock (m_sync)
            {
                if (m_pending.Count != 0)
                {
                    throw new InvalidOperationException("Retired generations still await cleanup.");
                }
                m_stopping = false;
                m_scheduleEpoch++;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopScheduling();
        }

        /// <summary>
        /// Transfers scheduling to the shutdown join without cancelling already admitted workers.
        /// </summary>
        internal void StopScheduling()
        {
            lock (m_sync)
            {
                m_stopping = true;
                m_scheduleEpoch++;
            }
        }

        /// <summary>
        /// Schedules each eligible generation independently, without inheriting request or publication contexts.
        /// </summary>
        internal void Schedule()
        {
            StartWorkers(onlyQuiescent: false);
        }

        /// <summary>
        /// Retries already-quiescent cleanup without making preparation join physical destruction callbacks.
        /// </summary>
        internal void RetryReady()
        {
            StartWorkers(onlyQuiescent: true);
        }

        /// <summary>
        /// Attempts already-quiescent generations, without waiting for unrelated drains or claimed cleanup.
        /// </summary>
        /// <param name="published">Newly retired records whose quiescent workers belong to this publication.</param>
        /// <returns>A task reporting failures from this attempt; retirement tasks also retain those failures.</returns>
        internal async ValueTask ReclaimReadyAsync(List<RetiredGeneration>? published = null)
        {
            Task<Exception?>[] workers = StartWorkers(onlyQuiescent: true);
            if (published is not null)
            {
                lock (m_sync)
                {
                    // An observer may have started this publication's worker between activation and this call.
                    // Join only its quiescent records, never another publication's blocked destruction callback.
                    workers = workers.Concat(published
                        .Where(item => item.Generation.IsQuiescent)
                        .Select(item => item.Worker)
                        .OfType<Task<Exception?>>())
                        .Distinct()
                        .ToArray();
                }
            }
            Exception?[] results = await Task.WhenAll(workers).ConfigureAwait(false);
            Exception[] failures = results.OfType<Exception>().ToArray();
            if (failures.Length == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures.Length != 0)
            {
                throw new AggregateException("Retired NodeManager cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Stops scheduling, joins admitted cleanup, and independently retries remaining shutdown stages.
        /// </summary>
        /// <param name="ct">Cancels the shutdown wait, not an already admitted cleanup callback.</param>
        /// <returns>A task that completes only after every retired generation has been reclaimed.</returns>
        internal async ValueTask CompleteShutdownAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Task<Exception?>[] workers;
            lock (m_sync)
            {
                m_stopping = true;
                m_scheduleEpoch++;
                workers = new Task<Exception?>[m_pending.Count];
                for (int ii = 0; ii < m_pending.Count; ii++)
                {
                    RetiredGeneration retired = m_pending[ii];
                    if (retired.Worker is not { IsCompleted: false })
                    {
                        retired.LastAttemptFailed = false;
                        retired.Worker = StartWorker(retired);
                    }
                    workers[ii] = retired.Worker;
                }
            }
            Exception?[] results = await Task.WhenAll(workers).WaitAsync(ct).ConfigureAwait(false);
            Exception[] failures = results.OfType<Exception>().ToArray();
            if (failures.Length != 0)
            {
                throw new AggregateException("Retired NodeManager shutdown cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Claims one worker per generation. A blocked drain or destruction never owns another generation's claim.
        /// </summary>
        /// <param name="onlyQuiescent">Whether to leave non-quiescent generations for background scheduling.</param>
        /// <returns>The workers started by this attempt, not workers already admitted by another caller.</returns>
        private Task<Exception?>[] StartWorkers(bool onlyQuiescent)
        {
            lock (m_sync)
            {
                if (m_stopping || m_pending.Count == 0)
                {
                    return [];
                }
                var workers = new List<Task<Exception?>>();
                foreach (RetiredGeneration retired in m_pending)
                {
                    if (!retired.Ready ||
                        retired.Worker is { IsCompleted: false } ||
                        !onlyQuiescent && retired.LastAttemptFailed ||
                        onlyQuiescent && !retired.Generation.IsQuiescent ||
                        retired.AllowActiveMonitoredItems &&
                            NodeManagerLifecycle.HasActiveMonitoredItems(retired.Server, retired.NodeManager))
                    {
                        continue;
                    }

                    retired.LastAttemptFailed = false;
                    retired.Worker = StartWorker(retired);
                    workers.Add(retired.Worker);
                }
                return [.. workers];
            }
        }

        /// <summary>
        /// Dispatches one cleanup attempt without carrying caller-owned request or lifecycle admissions.
        /// </summary>
        /// <param name="retired">The claimed retirement record.</param>
        /// <returns>The independently running cleanup attempt.</returns>
        private Task<Exception?> StartWorker(RetiredGeneration retired)
        {
            bool restoreFlow = !ExecutionContext.IsFlowSuppressed();
            Task<Exception?> worker;
            try
            {
                if (restoreFlow)
                {
                    ExecutionContext.SuppressFlow();
                }
                worker = Task.Run(() => ReclaimAsync(retired));
            }
            finally
            {
                if (restoreFlow)
                {
                    ExecutionContext.RestoreFlow();
                }
            }
            return worker;
        }

        /// <summary>
        /// Drains and cleans one exact generation, retaining completed stages and observable failures for retries.
        /// </summary>
        /// <param name="retired">The exclusively claimed generation.</param>
        /// <returns>The failure, if any, reported to the current cleanup caller and diagnostic log.</returns>
        private async Task<Exception?> ReclaimAsync(RetiredGeneration retired)
        {
            try
            {
                Task? finalization = StartTransitionFinalization(retired);
                InvalidateContinuationPoints(retired.Server, retired.Generation);
                await retired.Generation.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false);
                retired.RequestsDrained = true;
                if (finalization is not null)
                {
                    await finalization.ConfigureAwait(false);
                    lock (m_sync)
                    {
                        retired.Transition = null;
                        retired.TransitionFinalization = null;
                    }
                }
                if (retired.MigrateLateMonitoredItemsAsync is { } migrate && !retired.LateMonitoredItemsMigrated)
                {
                    await migrate().ConfigureAwait(false);
                    retired.LateMonitoredItemsMigrated = true;
                }
                await retired.Generation.CompleteSourceReleasesAsync(
                    null, CancellationToken.None, joinActiveCleanup: true).ConfigureAwait(false);
                retired.Generation.DisposeRetiredResources();
                if (await CleanupRetiredGenerationCoreAsync(retired.Server, retired.Host, retired)
                    .ConfigureAwait(false))
                {
                    lock (m_sync)
                    {
                        m_pending.Remove(retired);
                    }
                    retired.CompleteRetirement();
                    RecordShutdownCleanupProgress();
                }
                return null;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                retired.Server.Telemetry.CreateLogger<GenerationReclaimer>().RetiredGenerationCleanupFailed(failure);
                lock (m_sync)
                {
                    retired.LastAttemptFailed = true;
                    if (retired.TransitionFinalization is { IsFaulted: true })
                    {
                        retired.TransitionFinalization = null;
                    }
                    // All callbacks have finished. Release the retry claim before scheduling another attempt.
                    retired.Worker = null;
                }
                ScheduleRetry(retired);
                return failure;
            }
        }

        /// <summary>
        /// Schedules a delayed retry for a failed generation while normal cleanup is active.
        /// </summary>
        /// <param name="retired">The generation whose cleanup failed.</param>
        private void ScheduleRetry(RetiredGeneration retired)
        {
            lock (m_sync)
            {
                if (m_stopping || !m_pending.Contains(retired) || retired.RetryScheduled)
                {
                    return;
                }
                retired.RetryScheduled = true;
                int exponent = Math.Min(retired.RetryAttempt++, 5);
                TimeSpan delay = TimeSpan.FromSeconds(Math.Min(1 << exponent, 30));
                _ = RetryAfterDelayAsync(retired, m_scheduleEpoch, delay);
            }
        }

        /// <summary>
        /// Admits the retry only if shutdown or a newer scheduling epoch did not supersede it.
        /// </summary>
        /// <param name="retired">The generation whose cleanup failed.</param>
        /// <param name="scheduleEpoch">The scheduler epoch captured by the failure.</param>
        /// <param name="delay">The backoff before retrying.</param>
        private async Task RetryAfterDelayAsync(
            RetiredGeneration retired,
            long scheduleEpoch,
            TimeSpan delay)
        {
            await Task.Delay(delay, m_timeProvider, CancellationToken.None).ConfigureAwait(false);
            lock (m_sync)
            {
                retired.RetryScheduled = false;
                if (m_stopping || scheduleEpoch != m_scheduleEpoch ||
                    !retired.Ready || !m_pending.Contains(retired) ||
                    retired.Worker is { IsCompleted: false })
                {
                    return;
                }
                retired.LastAttemptFailed = false;
                retired.Worker = StartWorker(retired);
            }
        }

        /// <summary>
        /// Starts the committed transition independently of unrelated generation admissions.
        /// </summary>
        /// <param name="retired">The retirement whose transitioned monitored nodes must drain.</param>
        /// <returns>The single claimed transition task, or null when no transition remains.</returns>
        private Task? StartTransitionFinalization(RetiredGeneration retired)
        {
            lock (m_sync)
            {
                if (retired.Transition is not { } transition)
                {
                    return null;
                }
                if (retired.TransitionFinalization is { } current)
                {
                    return current;
                }
                bool restoreFlow = !ExecutionContext.IsFlowSuppressed();
                try
                {
                    if (restoreFlow)
                    {
                        ExecutionContext.SuppressFlow();
                    }
                    retired.TransitionFinalization = Task.Run(async () =>
                    {
                        await transition.WaitForPendingNotificationsAsync(CancellationToken.None).ConfigureAwait(false);
                        transition.MarkDeletedItems();
                    });
                }
                finally
                {
                    if (restoreFlow)
                    {
                        ExecutionContext.RestoreFlow();
                    }
                }
                return retired.TransitionFinalization;
            }
        }

        /// <summary>
        /// Invalidates only continuation points owned by the exact retired generation.
        /// </summary>
        /// <param name="server">The owning server instance.</param>
        /// <param name="generation">The generation whose saved continuations have expired.</param>
        private static void InvalidateContinuationPoints(IServerInternal server, NodeManagerGeneration generation)
        {
            // A published initial generation can retire before startup installs the session manager.
            if (server.SessionManager is not { } sessions)
            {
                return;
            }
            foreach (ISession session in sessions.GetSessions())
            {
                if (session.ContinuationPoints is SessionContinuationPoints points)
                {
                    points.RemoveForGeneration(generation);
                }
            }
        }

        /// <summary>
        /// Detaches staging metadata and disposes a reclaimed manager.
        /// </summary>
        /// <param name="nodeManager">The manager to dispose.</param>
        /// <returns>The asynchronous disposal.</returns>
        private static async ValueTask DisposeNodeManagerAsync(IAsyncNodeManager nodeManager)
        {
            ChangePreparer.Detach(nodeManager);
            if (nodeManager is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (nodeManager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Records a successful cleanup stage for shutdown retry progress detection.
        /// </summary>
        private void RecordShutdownCleanupProgress()
        {
            Interlocked.Increment(ref m_shutdownCleanupProgress);
        }

        /// <summary>
        /// Destroys one quiescent retired generation after item handoff and notification finalization.
        /// Invalidation preserves the client's subscription and monitored-item identities.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The retired generation still has undrained requests or active monitored items when detachment is attempted.
        /// </exception>
        private async ValueTask<bool> CleanupRetiredGenerationCoreAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RetiredGeneration retired)
        {
            RetirementCleanupState cleanup = retired.Cleanup;
            if (retired.NeedsDetachment)
            {
                if (retired.AllowActiveMonitoredItems &&
                    NodeManagerLifecycle.HasActiveMonitoredItems(server, retired.NodeManager))
                {
                    return false;
                }

                if (!retired.RequestsDrained)
                {
                    throw new InvalidOperationException(
                        "A retired NodeManager cannot be detached before its requests drain.");
                }
                InvalidateContinuationPoints(server, retired.Generation);

                // A migrating reload moves its items at commit, but a request routed before
                // the commit can still create one on the retired generation afterwards. If
                // it could not be migrated it is invalidated instead of blocking cleanup.
                if (retired.DetachActiveMonitoredItems ||
                    (!retired.AllowActiveMonitoredItems &&
                        NodeManagerLifecycle.HasActiveMonitoredItems(server, retired.NodeManager)))
                {
                    await NodeManagerLifecycle.DetachActiveMonitoredItemsAsync(
                            server,
                            retired.NodeManager)
                        .ConfigureAwait(false);
                }
                NodeManagerLifecycle.EnsureNoActiveMonitoredItems(server, retired.NodeManager);
                if (!cleanup.NotificationsFinalized)
                {
                    await host.FinalizeRetiredGenerationNotificationsAsync(
                            retired.NodeManager, CancellationToken.None)
                        .ConfigureAwait(false);
                    cleanup.NotificationsFinalized = true;
                    RecordShutdownCleanupProgress();
                }
                await host.GenerationReconciler.UnbindRetiredAsync(
                    retired.Generation,
                    unsubscribeAllEvents: false,
                    CancellationToken.None)
                    .ConfigureAwait(false);
                host.ReleaseRetiringEventGeneration(retired.Generation);
                retired.NeedsDetachment = false;
                if (!cleanup.Detached)
                {
                    cleanup.Detached = true;
                    RecordShutdownCleanupProgress();
                }
            }
            else if (!cleanup.Detached)
            {
                cleanup.Detached = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.ReferencesRemoved)
            {
                if (retired.PendingReferences.Count > 0)
                {
                    await server.NodeManager
                        .RemoveReferencesAsync(
                            retired.PendingReferences,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    retired.PendingReferences.Clear();
                }
                cleanup.ReferencesRemoved = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Destroyed)
            {
                await host.DestroyAddressSpaceAsync(
                        retired.NodeManager,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!cleanup.NotificationsFinalized)
                {
                    cleanup.NotificationsFinalized = true;
                    RecordShutdownCleanupProgress();
                }
                cleanup.Destroyed = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.DestroyedExternalReferencesRemoved)
            {
                await host.RemoveDestroyedExternalReferencesAsync(
                    retired.NodeManager,
                    CancellationToken.None).ConfigureAwait(false);
                cleanup.DestroyedExternalReferencesRemoved = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Disposed)
            {
                host.ReleaseRetiringEventGeneration(retired.Generation);
                await DisposeNodeManagerAsync(retired.NodeManager).ConfigureAwait(false);
                cleanup.Disposed = true;
                RecordShutdownCleanupProgress();
            }
            return cleanup.Disposed;
        }

        /// <summary>
        /// Retains one exact generation and the successful cleanup stages across attempts.
        /// </summary>
        internal sealed class RetiredGeneration
        {
            /// <summary>
            /// Captures the retired generation and its owning server independently of lifecycle publication.
            /// </summary>
            /// <param name="server">The server instance that owns the generation.</param>
            /// <param name="host">The generation's host.</param>
            /// <param name="generation">The exact generation being retired.</param>
            /// <param name="pendingReferences">References deferred until generation quiescence.</param>
            /// <param name="needsDetachment">Whether binding detachment remains pending.</param>
            /// <param name="allowActiveMonitoredItems">Whether retained monitored items postpone cleanup.</param>
            /// <param name="detachActiveMonitoredItems">Whether retained items must be invalidated.</param>
            public RetiredGeneration(
                IServerInternal server,
                IDynamicNodeManagerHost host,
                NodeManagerGeneration generation,
                List<LocalReference> pendingReferences,
                bool needsDetachment,
                bool allowActiveMonitoredItems = false,
                bool detachActiveMonitoredItems = false)
            {
                Server = server ?? throw new ArgumentNullException(nameof(server));
                Host = host ?? throw new ArgumentNullException(nameof(host));
                Generation = generation ?? throw new ArgumentNullException(nameof(generation));
                NodeManager = generation.NodeManager;
                PendingReferences = pendingReferences;
                NeedsDetachment = needsDetachment;
                AllowActiveMonitoredItems = allowActiveMonitoredItems;
                DetachActiveMonitoredItems = detachActiveMonitoredItems;
            }

            /// <summary>
            /// Gets the owning server, which remains valid until shutdown joins reclamation.
            /// </summary>
            internal IServerInternal Server { get; }

            /// <summary>
            /// Gets the exact host that published this generation.
            /// </summary>
            internal IDynamicNodeManagerHost Host { get; }

            /// <summary>
            /// Gets or sets whether post-publication readiness has released cleanup.
            /// </summary>
            internal bool Ready { get; set; }

            /// <summary>
            /// Gets or sets the current independently admitted cleanup attempt.
            /// </summary>
            internal Task<Exception?>? Worker { get; set; }

            /// <summary>
            /// Gets or sets whether an explicit retry must admit the next cleanup attempt.
            /// </summary>
            internal bool LastAttemptFailed { get; set; }

            /// <summary>
            /// Gets or sets whether a delayed retry has been scheduled.
            /// </summary>
            internal bool RetryScheduled { get; set; }

            /// <summary>
            /// Gets or sets the number of failed attempts used to calculate retry backoff.
            /// </summary>
            internal int RetryAttempt { get; set; }

            /// <summary>
            /// Gets the retired manager.
            /// </summary>
            public IAsyncNodeManager NodeManager { get; }

            /// <summary>
            /// Gets the exact generation whose view epochs and admitted callbacks must drain.
            /// </summary>
            public NodeManagerGeneration Generation { get; }

            /// <summary>
            /// Gets references retained until the exact generation drains.
            /// </summary>
            public List<LocalReference> PendingReferences { get; }

            /// <summary>
            /// Gets or sets whether the manager still requires detachment.
            /// </summary>
            public bool NeedsDetachment { get; set; }

            /// <summary>
            /// Gets or sets whether all requests that could still call this generation
            /// have drained since generation admission and continuation points were cut off.
            /// </summary>
            public bool RequestsDrained { get; set; }

            /// <summary>
            /// Gets whether this generation was retired by a shadow reload and may still
            /// own active monitored items. Cleanup is deferred rather than rejected while
            /// this holds true and monitored items remain.
            /// </summary>
            public bool AllowActiveMonitoredItems { get; }

            /// <summary>
            /// Gets whether active monitored items should be detached and marked deleted
            /// before destruction. Prepared batches cut off these sources before readiness;
            /// destruction still waits for requests using this generation to drain.
            /// </summary>
            public bool DetachActiveMonitoredItems { get; }

            /// <summary>
            /// Gets or sets the committed monitored-item transition whose deleted items are marked once the
            /// notifications admitted on their monitored nodes have drained.
            /// </summary>
            public NodeManagerLifecycle.MonitoredItemTransition? Transition { get; set; }

            /// <summary>
            /// Gets or sets the running finalization of <see cref="Transition"/>. It waits only for the transitioned
            /// nodes, never for the whole generation, so unrelated admitted work does not delay invalidation.
            /// </summary>
            public Task? TransitionFinalization { get; set; }

            /// <summary>
            /// Gets or sets the committed late migration operation, which admits a target after the source drains.
            /// </summary>
            public Func<ValueTask>? MigrateLateMonitoredItemsAsync { get; set; }

            /// <summary>
            /// Gets or sets the slot policy revision captured by this committed retirement.
            /// </summary>
            internal long LateMigrationRevision { get; set; }

            /// <summary>
            /// Gets or sets whether late monitored items have been migrated.
            /// </summary>
            public bool LateMonitoredItemsMigrated { get; set; }

            /// <summary>
            /// Gets the completed cleanup stages retained for retries.
            /// </summary>
            public RetirementCleanupState Cleanup { get; } = new();

            /// <summary>
            /// Gets the eventual result of retirement; individual failed attempts are reported by cleanup callers.
            /// </summary>
            public Task Retirement => m_retirementCompletion.Task;

            /// <summary>
            /// Publishes successful reclamation unless an earlier attempt already reported failure.
            /// </summary>
            public void CompleteRetirement()
            {
                MigrateLateMonitoredItemsAsync = null;
                m_retirementCompletion.TrySetResult(true);
            }

            /// <summary>The durable retirement outcome.</summary>
            private readonly TaskCompletionSource<bool> m_retirementCompletion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Records completed destruction stages so retries never replay successful callbacks.
        /// </summary>
        internal sealed class RetirementCleanupState
        {
            /// <summary>
            /// Gets or sets whether bindings have been detached.
            /// </summary>
            public bool Detached { get; set; }

            /// <summary>
            /// Gets or sets whether admitted notifications have finalized.
            /// </summary>
            public bool NotificationsFinalized { get; set; }

            /// <summary>
            /// Gets or sets whether deferred references have been removed.
            /// </summary>
            public bool ReferencesRemoved { get; set; }

            /// <summary>
            /// Gets or sets whether the address space has been destroyed.
            /// </summary>
            public bool Destroyed { get; set; }

            /// <summary>
            /// Gets or sets whether destroyed external references have been removed.
            /// </summary>
            public bool DestroyedExternalReferencesRemoved { get; set; }

            /// <summary>
            /// Gets or sets whether disposal has completed.
            /// </summary>
            public bool Disposed { get; set; }
        }

        /// <summary>Protects retirement ownership and worker admission, never cleanup callbacks.</summary>
        private readonly Lock m_sync = new();

        /// <summary>The clock used to schedule bounded cleanup retry delays.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>The retirement records exclusively owned by this reclaimer.</summary>
        private readonly List<RetiredGeneration> m_pending = [];

        /// <summary>Invalidates delayed retries when shutdown or a restart changes scheduling ownership.</summary>
        private long m_scheduleEpoch;

        /// <summary>The monotonic count of successful cleanup stages.</summary>
        private long m_shutdownCleanupProgress;

        /// <summary>Prevents new background attempts once shutdown or disposal starts.</summary>
        private bool m_stopping;

    }
}
