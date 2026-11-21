/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Performs one push onboarding attempt using authenticated device identity and retained lifecycle state.
    /// </summary>
    public interface IDeviceRegistrarPushProcessor
    {
        /// <summary>
        /// Processes a detached endpoint registration. Completion means the attempt finished, not queue acceptance.
        /// Implementations validate actual channel identity, honor software approval, and recover persisted steps
        /// before repeating credential operations. They must observe shutdown cancellation and report failure by
        /// throwing. Discovery metadata does not authorize the device or its managed applications.
        /// </summary>
        ValueTask ProcessAsync(ApplicationDescription application, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Runs a bounded, single-consumer push queue under the host's startup and shutdown lifetime.
    /// When a work store is configured, accepted work survives restart and interrupted work is reconciled before retry.
    /// Without a work store, acceptance remains in memory and failed attempts are not automatically repeated.
    /// </summary>
    // The gate remains available after disposal so late admissions return BadServerHalted rather than
    // ObjectDisposedException.
    // TODO: Replace the semaphore with an admission primitive that can be disposed without changing that contract.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2213:Disposable fields should be disposed",
        Justification = "The gate must remain usable for the established post-disposal BadServerHalted response.")]
    public sealed class DeviceRegistrarPushScheduler : BackgroundService, IDeviceRegistrarPushScheduler
    {
        /// <summary>
        /// Borrows an injectable processor and creates a queue with the specified pending-work capacity.
        /// The running attempt does not consume a pending-work slot.
        /// </summary>
        public DeviceRegistrarPushScheduler(
            IDeviceRegistrarPushProcessor processor,
            ITelemetryContext telemetry,
            int capacity = 32)
            : this(processor, telemetry, capacity, null)
        {
        }

        /// <summary>
        /// Borrows a durable work store and a processor that reconciles interrupted attempts before retry.
        /// The work store must have one exclusive scheduler owner for its entire lifetime.
        /// </summary>
        public DeviceRegistrarPushScheduler(
            IDeviceRegistrarPushProcessor processor,
            ITelemetryContext telemetry,
            int capacity,
            IDeviceRegistrarPushWorkStore? workStore)
        {
            m_processor = processor ?? throw new ArgumentNullException(nameof(processor));
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (capacity < 1 || capacity > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            m_logger = telemetry.CreateLogger<DeviceRegistrarPushScheduler>();
            m_capacity = capacity;
            m_workStore = workStore;
            m_recoveryProcessor = processor as IDeviceRegistrarPushRecoveryProcessor;
            m_queue = workStore == null
                ? Channel.CreateBounded<QueueEntry>(new BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                })
                : Channel.CreateUnbounded<QueueEntry>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
        }

        /// <summary>
        /// Starts this queue once. A stopped queue cannot be restarted or accept more work.
        /// </summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await m_acceptance.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_disposalState) != 0 ||
                    Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The push scheduler can only be started once.");
                }
                if (m_workStore != null)
                {
                    if (m_recoveryProcessor == null)
                    {
                        throw new InvalidOperationException(
                            "A durable push scheduler requires an interrupted-work recovery processor.");
                    }
                    ArrayOf<DeviceRegistrarPushWorkItem> items =
                        await m_workStore.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (items.Count > m_capacity + 1)
                    {
                        throw new InvalidDataException("The durable push work queue exceeds its configured capacity.");
                    }
                    for (int index = 0; index < items.Count; index++)
                    {
                        DeviceRegistrarPushWorkItem item = items[index];
                        if (item.Id == Guid.Empty ||
                            item.State is not (
                                DeviceRegistrarPushWorkState.Pending or
                                DeviceRegistrarPushWorkState.Processing or
                                DeviceRegistrarPushWorkState.Interrupted))
                        {
                            throw new InvalidDataException("The durable push work queue contains invalid records.");
                        }
                        DeviceRegistrarPushWorkState state = item.State;
                        if (state == DeviceRegistrarPushWorkState.Processing)
                        {
                            await m_workStore.MarkInterruptedAsync(item.Id, cancellationToken).ConfigureAwait(false);
                            state = DeviceRegistrarPushWorkState.Interrupted;
                        }
                        if (!m_queue.Writer.TryWrite(new QueueEntry(item.Id, item.Application, state)))
                        {
                            throw new InvalidOperationException("The durable push work queue is closed.");
                        }
                    }
                }
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                CompleteAcceptance();
                throw;
            }
            finally
            {
                m_acceptance.Release();
                TryCompleteDisposal();
            }
        }

        /// <summary>
        /// Accepts a detached description only while running, or rejects a full queue without waiting.
        /// The caller's cancellation token governs acceptance, not the lifetime of an already accepted attempt.
        /// A failed durable write is accepted only if read-back confirms the same unique pending item.
        /// </summary>
        public async ValueTask EnqueueAsync(
            ApplicationDescription application,
            CancellationToken cancellationToken = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationDescription snapshot = SnapshotApplication(application);
            await m_acceptance.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_state) != 1 || Volatile.Read(ref m_disposalState) != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }
                Guid id = Guid.NewGuid();
                if (m_workStore != null)
                {
                    try
                    {
                        await m_workStore.EnqueueAsync(id, snapshot, m_capacity, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception publicationError)
                    {
                        bool confirmed;
                        try
                        {
                            confirmed = await IsPendingPublicationConfirmedAsync(id, snapshot).ConfigureAwait(false);
                        }
                        catch (Exception reconciliationError)
                        {
                            throw new AggregateException(
                                "Accepting push work and confirming its publication both failed.",
                                publicationError, reconciliationError);
                        }
                        if (!confirmed)
                        {
                            throw;
                        }
                        m_logger.PushPublicationConfirmed(publicationError, snapshot.ApplicationUri!);
                    }
                    if (!m_queue.Writer.TryWrite(new QueueEntry(
                        id, snapshot, DeviceRegistrarPushWorkState.Pending)))
                    {
                        throw new InvalidOperationException("The durable push work queue is closed.");
                    }
                }
                else if (!m_queue.Writer.TryWrite(new QueueEntry(
                    id, snapshot, DeviceRegistrarPushWorkState.Pending)))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
            }
            finally
            {
                m_acceptance.Release();
                TryCompleteDisposal();
            }
        }

        /// <summary>
        /// Closes acceptance and requests cancellation of the running attempt and queued work.
        /// The host's stop token bounds the wait; processors must cooperate with cancellation.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await CompleteAcceptanceAsync().ConfigureAwait(false);
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Requests disposal without blocking an admitted acceptance operation.
        /// Queue closure and worker cancellation wait until the acceptance holder has finished.
        /// The host should stop the worker before disposal so asynchronous cleanup can finish.
        /// </summary>
        // Base disposal cancels the worker, so it must wait until an admitted durable acceptance has queued its work.
        // TODO: Move this deferral into a host lifecycle abstraction that can safely drain before base disposal.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2215:Dispose methods should call base class dispose",
            Justification = "Calling base.Dispose here would cancel the worker before an admitted acceptance is queued.")]
        public override void Dispose()
        {
            Interlocked.CompareExchange(ref m_disposalState, 1, 0);
            TryCompleteDisposal();
        }

        /// <summary>
        /// Processes accepted work serially and records failed or interrupted attempts without losing observation.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (await m_queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref m_state) != 1)
                    {
                        break;
                    }
                    if (!m_queue.Reader.TryRead(out QueueEntry? entry))
                    {
                        continue;
                    }
                    ApplicationDescription application = entry.Application;
                    string applicationUri = application.ApplicationUri ??
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    try
                    {
                        DeviceRegistrarPushWorkState priorState = entry.State;
                        if (m_workStore != null)
                        {
                            priorState = await m_workStore.BeginAttemptAsync(entry.Id, stoppingToken)
                                .ConfigureAwait(false);
                            if (priorState != DeviceRegistrarPushWorkState.Pending)
                            {
                                await m_recoveryProcessor!.ReconcileInterruptedAsync(application, stoppingToken)
                                    .ConfigureAwait(false);
                            }
                        }
                        await m_processor.ProcessAsync(application, stoppingToken).ConfigureAwait(false);
                        if (m_workStore != null)
                        {
                            await m_workStore.CompleteAsync(entry.Id, CancellationToken.None).ConfigureAwait(false);
                        }
                        m_logger.PushAttemptCompleted(applicationUri);
                    }
                    catch (OperationCanceledException error) when (stoppingToken.IsCancellationRequested)
                    {
                        m_logger.PushAttemptInterrupted(applicationUri);
                        if (m_workStore != null)
                        {
                            try
                            {
                                await m_workStore.MarkInterruptedAsync(entry.Id, CancellationToken.None)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception stateError)
                            {
                                throw new AggregateException(error, stateError);
                            }
                        }
                        throw;
                    }
                    catch (Exception error)
                    {
                        if (m_workStore != null)
                        {
                            try
                            {
                                await m_workStore.MarkInterruptedAsync(entry.Id, CancellationToken.None)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception stateError)
                            {
                                error = new AggregateException(error, stateError);
                            }
                        }
                        m_logger.PushAttemptFailed(error, applicationUri);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown cancels processing; configured work stores retain entries for restart recovery.
            }
            finally
            {
                await CompleteAcceptanceAsync().ConfigureAwait(false);
                int discarded = 0;
                if (m_workStore == null)
                {
                    while (m_queue.Reader.TryRead(out _))
                    {
                        discarded++;
                    }
                }
                if (discarded != 0)
                {
                    m_logger.PushRegistrationsDiscarded(discarded);
                }
            }
        }

        /// <summary>
        /// Atomically excludes new registrations before completing the queue.
        /// </summary>
        private void CompleteAcceptance()
        {
            Interlocked.Exchange(ref m_state, 2);
            m_queue.Writer.TryComplete();
        }

        /// <summary>
        /// Completes requested disposal only when no acceptance operation owns the semaphore.
        /// Every acceptance release retries this nonblocking acquisition to avoid missing a concurrent request.
        /// </summary>
        private void TryCompleteDisposal()
        {
            if (Volatile.Read(ref m_disposalState) != 1 || !m_acceptance.Wait(0))
            {
                return;
            }
            try
            {
                if (Interlocked.CompareExchange(ref m_disposalState, 2, 1) == 1)
                {
                    CompleteAcceptance();
                    base.Dispose();
                }
            }
            finally
            {
                m_acceptance.Release();
            }
        }

        /// <summary>
        /// Waits for any durable acceptance write before closing the queue.
        /// </summary>
        private async ValueTask CompleteAcceptanceAsync()
        {
            await m_acceptance.WaitAsync().ConfigureAwait(false);
            try
            {
                CompleteAcceptance();
            }
            finally
            {
                m_acceptance.Release();
                TryCompleteDisposal();
            }
        }

        /// <summary>
        /// Detaches and validates application metadata before it enters the queue.
        /// </summary>
        private static ApplicationDescription SnapshotApplication(ApplicationDescription application)
        {
            return DeviceRegistrarPushApplicationSnapshot.Create(application);
        }

        /// <summary>
        /// Confirms the exact pending item after an uncertain write without repeating acceptance or using its token.
        /// </summary>
        private async ValueTask<bool> IsPendingPublicationConfirmedAsync(Guid id, ApplicationDescription application)
        {
            ArrayOf<DeviceRegistrarPushWorkItem> items = await m_workStore!.ReadAsync(CancellationToken.None)
                .ConfigureAwait(false);
            DeviceRegistrarPushWorkItem? published = null;
            for (int index = 0; index < items.Count; index++)
            {
                DeviceRegistrarPushWorkItem item = items[index];
                if (item.Id == id)
                {
                    if (published != null)
                    {
                        return false;
                    }
                    published = item;
                }
            }
            return published != null &&
                published.State == DeviceRegistrarPushWorkState.Pending &&
                application.IsEqual(SnapshotApplication(published.Application));
        }

        /// <summary>
        /// The queued work identifier, application snapshot, and persisted prior state.
        /// </summary>
        private sealed record QueueEntry(
            Guid Id,
            ApplicationDescription Application,
            DeviceRegistrarPushWorkState State);

        /// <summary>The host-owned processor responsible for persistent provisioning state.</summary>
        private readonly IDeviceRegistrarPushProcessor m_processor;

        /// <summary>The bounded queue of independently owned endpoint descriptions.</summary>
        private readonly Channel<QueueEntry> m_queue;

        /// <summary>The optional durable work provider.</summary>
        private readonly IDeviceRegistrarPushWorkStore? m_workStore;

        /// <summary>The recovery boundary required when durable acceptance is enabled.</summary>
        private readonly IDeviceRegistrarPushRecoveryProcessor? m_recoveryProcessor;

        /// <summary>The source-generated worker diagnostics.</summary>
        private readonly ILogger<DeviceRegistrarPushScheduler> m_logger;

        /// <summary>Serializes durable acceptance writes and queue closure.</summary>
        private readonly SemaphoreSlim m_acceptance = new(1, 1);

        /// <summary>The maximum pending work accepted by this scheduler.</summary>
        private readonly int m_capacity;

        /// <summary>Zero before startup, one while accepting, and two after stopping.</summary>
        private int m_state;

        /// <summary>Zero before disposal, one while requested, and two once teardown starts.</summary>
        private int m_disposalState;
    }

    /// <summary>
    /// Reports queue outcomes with classified application identities.
    /// </summary>
    internal static partial class DeviceRegistrarPushSchedulerLog
    {
        /// <summary>
        /// Records a successfully completed processor invocation.
        /// </summary>
        [LoggerMessage(
            EventId = GdsServerCommonEventIds.DeviceRegistrarPushScheduler + 0,
            Level = LogLevel.Information,
            Message = "Push onboarding processor completed an attempt for {ApplicationUri}.")]
        public static partial void PushAttemptCompleted(
            this ILogger logger, [ApplicationIdentity] string applicationUri);

        /// <summary>
        /// Records a processor failure that requires host inspection or explicit resubmission.
        /// </summary>
        [LoggerMessage(
            EventId = GdsServerCommonEventIds.DeviceRegistrarPushScheduler + 1,
            Level = LogLevel.Error,
            Message = "Push onboarding attempt for {ApplicationUri} failed; it was not automatically retried.")]
        public static partial void PushAttemptFailed(
            this ILogger logger, Exception error, [ApplicationIdentity] string applicationUri);

        /// <summary>
        /// Records cancellation of a running attempt without declaring its remote effects undone.
        /// </summary>
        [LoggerMessage(
            EventId = GdsServerCommonEventIds.DeviceRegistrarPushScheduler + 2,
            Level = LogLevel.Warning,
            Message = "Push onboarding attempt for {ApplicationUri} was interrupted by shutdown; " +
                "recover retained state.")]
        public static partial void PushAttemptInterrupted(
            this ILogger logger, [ApplicationIdentity] string applicationUri);

        /// <summary>
        /// Records accepted in-memory work that was not processed before shutdown.
        /// </summary>
        [LoggerMessage(
            EventId = GdsServerCommonEventIds.DeviceRegistrarPushScheduler + 3,
            Level = LogLevel.Warning,
            Message = "Push scheduler discarded {Count} queued registrations during shutdown; " +
                "resubmission is required.")]
        public static partial void PushRegistrationsDiscarded(this ILogger logger, int count);

        /// <summary>
        /// Preserves the write failure when the exact pending publication was independently confirmed.
        /// </summary>
        [LoggerMessage(
            EventId = GdsServerCommonEventIds.DeviceRegistrarPushScheduler + 4,
            Level = LogLevel.Warning,
            Message = "Push work publication for {ApplicationUri} was confirmed after the acceptance write failed.")]
        public static partial void PushPublicationConfirmed(
            this ILogger logger, Exception error, [ApplicationIdentity] string applicationUri);
    }
}
