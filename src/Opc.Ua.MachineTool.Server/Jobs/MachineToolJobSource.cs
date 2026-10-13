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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Shares one accepted ISA-95 V2 engine state with retained MachineTool production observations.
    /// This bounded reference simulator is single-host and non-durable; it never executes CNC hardware.
    /// </summary>
    public sealed partial class MachineToolJobSource :
        IMachineToolProvider,
        IIsa95JobOrderReceiverV2,
        IIsa95JobResponseProviderV2,
        IIsa95JobResponseReceiverV2,
        IIsa95JobExecutionController,
        IIsa95JobOrderCatalog,
        IIsa95JobResponseCatalog,
        IIsa95JobStatusSourceV2,
        IIsa95JobOrderCatalogChangeSource,
        IIsa95JobResponseChangeSource,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates an isolated engine and explicit program mappings. The optional monitoring observation
        /// supplies raw acquisition data, not production commands. The supplied clock remains borrowed.
        /// </summary>
        public MachineToolJobSource(
            MachineToolJobSourceOptions options,
            MachineToolSnapshot? monitoring = null,
            TimeProvider? timeProvider = null,
            ITelemetryContext? telemetry = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            ValidateOptions();
            foreach (MachineToolJobSourceDescriptor descriptor in options.Jobs)
            {
                ValidateDescriptor(descriptor);
                if (!m_descriptors.TryAdd(descriptor.JobOrderId, descriptor))
                {
                    throw Invalid("Job descriptors must have distinct identifiers.");
                }
            }
            m_monitoring = monitoring ?? new MachineToolSnapshot();
            ValidateMonitoring(m_monitoring);
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_encoding = ServiceMessageContext.Create(telemetry);
            m_encoding.MaxStringLength = options.MaximumTextLength;
            m_encoding.MaxArrayLength = options.MaximumArrayLength;
            m_encoding.MaxByteStringLength = options.MaximumPayloadBytes;
            m_encoding.MaxMessageSize = options.MaximumPayloadBytes;
            m_encoding.MaxEncodingNestingLevels = 32;
            m_engine = new InMemoryIsa95JobControlProvider(new Isa95JobControlProviderOptions
            {
                MaxJobOrders = options.MaximumJobOrders,
                MaxJobResponses = options.MaximumJobResponses
            }, m_clock);
            m_capture = new MachineToolJobSourceCapture { Snapshot = MakeSnapshot(0) };
            m_pending.Enqueue(m_capture);
        }

        /// <inheritdoc/>
        public ushort MaxDownloadableJobOrders => m_engine.MaxDownloadableJobOrders;

        /// <inheritdoc/>
        public async ValueTask<MachineToolSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                return CurrentCapture().Snapshot;
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Captures both representations atomically. During a drain callback this returns that callback's
        /// retained revision; ordinary calls capture the latest accepted revision.
        /// </summary>
        public async ValueTask<MachineToolJobSourceCapture> CaptureAsync(CancellationToken cancellationToken = default)
        {
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                MachineToolJobSourceCapture capture = CurrentCapture();
                return capture with
                {
                    JobOrders = CoreUtils.Clone(capture.JobOrders),
                    JobResponses = CoreUtils.Clone(capture.JobResponses)
                };
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Replaces only supplied base monitoring and optional lifetime statistics, preserving their raw
        /// timestamps and quality. The source assigns revisions and owns ActiveProgram and Production.Jobs.
        /// Equipment and prognosis modules are deliberately outside this small reference adapter.
        /// </summary>
        public async ValueTask SetMonitoringAsync(
            MachineToolSnapshot monitoring, CancellationToken cancellationToken = default)
        {
            CheckReentrancy();
            ValidateMonitoring(monitoring);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                RequireCapacity(ChangeKind.None);
                if (m_monitoring.Operation.PartsProducedInLifetime is ulong previous &&
                    monitoring.Operation.PartsProducedInLifetime is ulong current && current < previous)
                {
                    throw Invalid("The lifetime part count cannot decrease.");
                }
                if (m_monitoring.Production.Statistics is MachineToolProductionStatistics old &&
                    monitoring.Production.Statistics is MachineToolProductionStatistics next &&
                    next.PartsProducedInLifetime < old.PartsProducedInLifetime)
                {
                    throw Invalid("Retained lifetime statistics cannot decrease.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_monitoring = monitoring;
                await CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Accepts explicit aggregate counts for the current run without inferring physical part identities,
        /// quality measurements, machine operation mode, or lifetime equipment counters.
        /// </summary>
        public async ValueTask RecordPartsAsync(
            string jobOrderId,
            MachineToolJobSourcePartCounts counts,
            CancellationToken cancellationToken = default)
        {
            CheckReentrancy();
            ValidateSample(counts.Sample);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                RequireCapacity(ChangeKind.None);
                MachineToolProductionJob job = GetLegacyJob(jobOrderId);
                if (job.PartSets.Count != 1 || job.Phase is not (
                    MachineToolProductionPhase.Running or MachineToolProductionPhase.Interrupted))
                {
                    throw Invalid("Part observations require a declared part set and an executing job.");
                }
                MachineToolProductionPartSet set = job.PartSets[0];
                if (counts.Completed < set.PartsCompletedPerRun ||
                    counts.Completed > set.PartsPlannedPerRun || counts.Good > counts.Completed ||
                    counts.Good < job.PartsGood - job.PartsGoodBeforeCurrentRun)
                {
                    throw Invalid("Current-run part counts must be monotonic and within their declared plan.");
                }
                uint completed = checked(job.PartsCompletedBeforeCurrentRun + counts.Completed);
                uint good = checked(job.PartsGoodBeforeCurrentRun + counts.Good);
                cancellationToken.ThrowIfCancellationRequested();
                m_jobs[jobOrderId] = job with
                {
                    PartsCompleted = completed,
                    PartsGood = good,
                    PartSets = [set with { PartsCompletedPerRun = counts.Completed, Sample = counts.Sample }]
                };
                await CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Reports one explicitly completed simulated run. Intermediate runs use the canonical
        /// RunningToRunning legacy transition while ISA-95 stays Running. The last run delegates Complete
        /// to the existing ISA-95 engine. Declared parts must have been explicitly accounted for first.
        /// </summary>
        public async ValueTask CompleteRunAsync(string jobOrderId, CancellationToken cancellationToken = default)
        {
            CheckReentrancy();
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                MachineToolProductionJob job = GetLegacyJob(jobOrderId);
                RequireCompletedRun(job);
                bool final = job.RunsCompleted + 1 == job.RunsPlanned;
                RequireCapacity(final ? ChangeKind.Status : ChangeKind.None);
                MachineToolProductionJob next = Advance(job, final
                    ? MachineToolProductionPhase.Ended : MachineToolProductionPhase.Running, Sample(now), true);
                if (!final && next.PartSets.Count != 0)
                {
                    next = next with
                    {
                        PartsCompletedBeforeCurrentRun = job.PartsCompleted,
                        PartsGoodBeforeCurrentRun = job.PartsGood,
                        PartSets =
                        [
                            next.PartSets[0] with
                            {
                                PartsCompletedPerRun = 0,
                                Sample = MachineToolSampleInfo.Unavailable
                            }
                        ]
                    };
                }
                else if (!final)
                {
                    next = next with
                    {
                        PartsCompletedBeforeCurrentRun = job.PartsCompleted,
                        PartsGoodBeforeCurrentRun = job.PartsGood
                    };
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (final)
                {
                    m_clock.UtcNow = now;
                    Isa95JobOrderReceiptV2 receipt = await m_engine.TransitionAsync(
                        jobOrderId, Isa95JobExecutionTransition.Complete, cancellationToken).ConfigureAwait(false);
                    if (receipt.ReturnStatus != Isa95JobReturnStatus.Success)
                    {
                        throw new ServiceResultException(receipt.Result);
                    }
                    AccountChanges(ChangeKind.Status);
                }
                m_jobs[jobOrderId] = next;
                await CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Replays retained revisions in order through an application publication callback, for example
        /// runtime.RefreshAsync. ReadAsync and CaptureAsync are pinned to the current callback revision.
        /// No user callback runs under a source gate. Failure retains the head for an idempotent retry.
        /// A callback may read but cannot mutate, drain, or dispose this source recursively.
        /// </summary>
        public async ValueTask DrainAsync(
            Func<CancellationToken, ValueTask> publishAsync,
            CancellationToken cancellationToken = default)
        {
            if (publishAsync == null)
            {
                throw new ArgumentNullException(nameof(publishAsync));
            }
            CheckReentrancy();
            BeginOperation();
            try
            {
                await DrainCoreAsync(publishAsync, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            CheckReentrancy();
            bool owner;
            lock (m_admission)
            {
                owner = !m_disposed;
                if (owner)
                {
                    m_disposed = true;
                    if (m_operations == 0)
                    {
                        m_quiescence.TrySetResult(true);
                    }
                }
            }
            if (!owner)
            {
                await m_disposal.Task.ConfigureAwait(false);
                return;
            }
            try
            {
                try
                {
                    m_lifetime.Cancel();
                }
                finally
                {
                    await m_quiescence.Task.ConfigureAwait(false);
                    m_engine.Dispose();
                    m_pending.Clear();
                    m_jobs.Clear();
                    m_gate.Dispose();
                    m_drainGate.Dispose();
                    m_lifetime.Dispose();
                }
                m_disposal.TrySetResult(true);
            }
            catch (Exception exception)
            {
                m_disposal.TrySetException(exception);
                throw;
            }
        }

        /// <summary>
        /// Drains under an admitted lifetime lease, including time spent waiting for another publisher.
        /// </summary>
        private async ValueTask DrainCoreAsync(
            Func<CancellationToken, ValueTask> publishAsync, CancellationToken cancellationToken)
        {
            CancellationTokenSource operation;
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token);
            }
            finally
            {
                Exit();
            }
            using (operation)
            {
                await m_drainGate.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    while (true)
                    {
                        MachineToolJobSourceCapture capture;
                        await EnterAsync(operation.Token).ConfigureAwait(false);
                        try
                        {
                            ThrowIfDisposed();
                            if (m_pending.Count == 0)
                            {
                                return;
                            }
                            capture = m_pending.Peek();
                        }
                        finally
                        {
                            Exit();
                        }
                        var view = new DrainView(capture);
                        m_readView.Value = view;
                        try
                        {
                            await publishAsync(operation.Token).ConfigureAwait(false);
                            operation.Token.ThrowIfCancellationRequested();
                        }
                        finally
                        {
                            view.Active = false;
                            m_readView.Value = null;
                        }
                        await EnterAsync(operation.Token).ConfigureAwait(false);
                        try
                        {
                            ThrowIfDisposed();
                            m_pending.Dequeue();
                        }
                        finally
                        {
                            Exit();
                        }
                    }
                }
                finally
                {
                    m_drainGate.Release();
                }
            }
        }

        /// <summary>
        /// Returns a pinned delivery view only while that callback is active.
        /// </summary>
        private MachineToolJobSourceCapture CurrentCapture() =>
            m_readView.Value is { Active: true } view ? view.Capture : m_capture;

        /// <summary>
        /// Commits an immutable cross-model revision after an accepted engine or observation change.
        /// </summary>
        private async ValueTask CommitAsync()
        {
            m_capture = new MachineToolJobSourceCapture
            {
                Snapshot = MakeSnapshot(checked(m_capture.Snapshot.Revision + 1)),
                JobOrders = await m_engine.GetJobOrdersV2Async().ConfigureAwait(false),
                JobResponses = await m_engine.GetJobResponsesV2Async().ConfigureAwait(false)
            };
            m_pending.Enqueue(m_capture);
        }

        /// <summary>
        /// Projects the explicitly ordered inventory without touching raw equipment measurements.
        /// </summary>
        private MachineToolSnapshot MakeSnapshot(ulong revision)
        {
            var jobs = new List<MachineToolProductionJob>(m_jobs.Count);
            foreach (MachineToolJobSourceDescriptor descriptor in m_options.Jobs)
            {
                if (m_jobs.TryGetValue(descriptor.JobOrderId, out MachineToolProductionJob? job))
                {
                    jobs.Add(job);
                }
            }
            MachineToolProgram active = m_jobs.TryGetValue(m_activeJob, out MachineToolProductionJob? selected)
                ? selected.Programs[0] : new MachineToolProgram();
            return m_monitoring with
            {
                Revision = revision,
                ActiveProgram = active,
                Production = m_monitoring.Production with { Jobs = jobs.ToArrayOf() }
            };
        }

        /// <summary>
        /// Resolves an accepted job without accepting observations for invented order identities.
        /// </summary>
        private MachineToolProductionJob GetLegacyJob(string jobOrderId)
        {
            if (jobOrderId == null)
            {
                throw new ArgumentNullException(nameof(jobOrderId));
            }
            return m_jobs.TryGetValue(jobOrderId, out MachineToolProductionJob? job)
                ? job : throw Invalid("The job order is not stored.");
        }

        /// <summary>
        /// Rejects normal completion unless explicit current-run observations support it.
        /// </summary>
        private static void RequireCompletedRun(MachineToolProductionJob job)
        {
            if (job.Phase != MachineToolProductionPhase.Running || job.RunsCompleted >= job.RunsPlanned ||
                (job.PartSets.Count != 0 &&
                    job.PartSets[0].PartsCompletedPerRun != job.PartSets[0].PartsPlannedPerRun))
            {
                throw Invalid("A normally completed run requires a running job and all declared parts accounted for.");
            }
        }

        /// <summary>
        /// Advances only a projection transition selected after an explicit operation or execution observation.
        /// </summary>
        private static MachineToolProductionJob Advance(
            MachineToolProductionJob job, MachineToolProductionPhase phase, MachineToolSampleInfo sample,
            bool completedRun = false)
        {
            if (job.Phase == phase && !completedRun)
            {
                return job;
            }
            MachineToolProgram program = job.Programs[0];
            return job with
            {
                Phase = phase,
                TransitionSequence = checked(job.TransitionSequence + 1),
                RunsCompleted = completedRun ? checked(job.RunsCompleted + 1) : job.RunsCompleted,
                Sample = sample,
                Programs =
                [
                    program with
                    {
                        Phase = phase,
                        TransitionSequence = checked(program.TransitionSequence + 1),
                        Sample = sample
                    }
                ]
            };
        }

        /// <summary>
        /// Marks derived reference execution state as substituted, never as a Good hardware measurement.
        /// </summary>
        private static MachineToolSampleInfo Sample(DateTimeOffset timestamp) =>
            new(DateTimeUtc.From(timestamp), StatusCodes.UncertainSubstituteValue);

        /// <summary>
        /// Rejects mutation from callbacks rather than deadlocking on their own delivery.
        /// </summary>
        private void CheckReentrancy()
        {
            if (m_readView.Value is { Active: true })
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A job-source publication callback may read but cannot mutate, drain or dispose its source.");
            }
        }

        /// <summary>
        /// Admits an operation before waiting, so disposal can safely release gates only after all waiters drain.
        /// </summary>
        private async ValueTask EnterAsync(CancellationToken cancellationToken)
        {
            BeginOperation();
            try
            {
                await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        /// <summary>
        /// Releases state serialization before completing the admitted lifetime lease.
        /// </summary>
        private void Exit()
        {
            m_gate.Release();
            EndOperation();
        }

        /// <summary>
        /// Atomically refuses new lifetime leases after disposal begins.
        /// </summary>
        private void BeginOperation()
        {
            lock (m_admission)
            {
                ThrowIfDisposed();
                m_operations++;
            }
        }

        /// <summary>
        /// Completes the disposal barrier only when no admitted operation can access an owned gate.
        /// </summary>
        private void EndOperation()
        {
            lock (m_admission)
            {
                if (--m_operations == 0 && m_disposed)
                {
                    m_quiescence.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Checks the lifetime after entry into the private gate.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(MachineToolJobSource));
            }
        }

        /// <summary>
        /// Creates a precise observation/configuration refusal.
        /// </summary>
        private static ServiceResultException Invalid(string message) =>
            new(StatusCodes.BadInvalidArgument, message);

        /// <summary>Immutable, bounded configuration.</summary>
        private readonly MachineToolJobSourceOptions m_options;

        /// <summary>Application-owned clock, never disposed by this adapter.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>Engine clock captured before gates, avoiding application callbacks under a gate.</summary>
        private readonly CapturedClock m_clock = new();

        /// <summary>Exclusively owned canonical protocol engine.</summary>
        private readonly InMemoryIsa95JobControlProvider m_engine;

        /// <summary>Private bounded protocol encoding context.</summary>
        private readonly ServiceMessageContext m_encoding;

        /// <summary>Serializes accepted state and point-in-time reads; never exposed to applications.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Serializes outside-gate delivery.</summary>
        private readonly SemaphoreSlim m_drainGate = new(1, 1);

        /// <summary>Protects admission counters and subscriber accounting, never user callbacks.</summary>
        private readonly Lock m_admission = new();

        /// <summary>Cancellation for active and waiting publication callbacks.</summary>
        private readonly CancellationTokenSource m_lifetime = new();

        /// <summary>Completes after disposal has drained any active publisher.</summary>
        private readonly TaskCompletionSource<bool> m_disposal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes after all operations admitted before disposal have ended.</summary>
        private readonly TaskCompletionSource<bool> m_quiescence =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Explicit, immutable program and work-master mappings.</summary>
        private readonly Dictionary<string, MachineToolJobSourceDescriptor> m_descriptors =
            new(StringComparer.Ordinal);

        /// <summary>Legacy state derived from accepted operations and explicit execution observations.</summary>
        private readonly Dictionary<string, MachineToolProductionJob> m_jobs = new(StringComparer.Ordinal);

        /// <summary>Bounded history of revisions not yet delivered to the retained production runtime.</summary>
        private readonly Queue<MachineToolJobSourceCapture> m_pending = new();

        /// <summary>Scopes a retained observation to one asynchronous publication call.</summary>
        private readonly AsyncLocal<DrainView?> m_readView = new();

        /// <summary>Latest caller-supplied raw monitoring observation.</summary>
        private MachineToolSnapshot m_monitoring;

        /// <summary>Latest accepted immutable cross-model capture.</summary>
        private MachineToolJobSourceCapture m_capture;

        /// <summary>Program selection changes only on an explicit execution transition.</summary>
        private string m_activeJob = string.Empty;

        /// <summary>Number of admitted operations, including state waiters and whole publication calls.</summary>
        private int m_operations;

        /// <summary>Whether disposal has begun.</summary>
        private volatile bool m_disposed;

        /// <summary>
        /// A delivery scope whose invalidation also prevents escaped asynchronous work from using stale reads.
        /// </summary>
        private sealed class DrainView(MachineToolJobSourceCapture capture)
        {
            /// <summary>
            /// Gets the immutable captured revision.
            /// </summary>
            public MachineToolJobSourceCapture Capture { get; } = capture;

            /// <summary>
            /// Gets or sets whether the publication callback is still active.
            /// </summary>
            public bool Active { get; set; } = true;
        }

        /// <summary>
        /// Supplies the existing engine with one transaction timestamp without calling application code.
        /// </summary>
        private sealed class CapturedClock : TimeProvider
        {
            /// <summary>
            /// Gets or sets the captured transaction time.
            /// </summary>
            public DateTimeOffset UtcNow { get; set; }

            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow() => UtcNow;
        }
    }
}
