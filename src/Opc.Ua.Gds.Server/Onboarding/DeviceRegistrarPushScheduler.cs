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
    /// Acceptance is in-memory only; production hosts needing durable acceptance supply their own scheduler.
    /// Failed attempts are reported, not automatically repeated after an uncertain remote outcome.
    /// </summary>
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
            m_queue = Channel.CreateBounded<ApplicationDescription>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }

        /// <summary>
        /// Starts this queue once. A stopped queue cannot be restarted or accept more work.
        /// </summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
            {
                throw new InvalidOperationException("The push scheduler can only be started once.");
            }
            try
            {
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                CompleteAcceptance();
                throw;
            }
        }

        /// <summary>
        /// Accepts a detached description only while running, or rejects a full queue without waiting.
        /// The caller's cancellation token governs acceptance, not the lifetime of an already accepted attempt.
        /// </summary>
        public ValueTask EnqueueAsync(
            ApplicationDescription application,
            CancellationToken cancellationToken = default)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationDescription snapshot = CoreUtils.Clone(application)!;
            snapshot.DiscoveryUrls = application.DiscoveryUrls.IsNull
                ? default
                : application.DiscoveryUrls.Span.ToArray().ToArrayOf();
            if (!Uri.TryCreate(snapshot.ApplicationUri, UriKind.Absolute, out _) ||
                snapshot.DiscoveryUrls.IsEmpty ||
                (snapshot.ApplicationType != ApplicationType.Server &&
                    snapshot.ApplicationType != ApplicationType.ClientAndServer))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            for (int i = 0; i < snapshot.DiscoveryUrls.Count; i++)
            {
                if (!Uri.TryCreate(snapshot.DiscoveryUrls[i], UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
            }
            lock (m_acceptance)
            {
                if (Volatile.Read(ref m_state) != 1)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }
                if (!m_queue.Writer.TryWrite(snapshot))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
            }
            return default;
        }

        /// <summary>
        /// Closes acceptance and requests cancellation of the running attempt and queued work.
        /// The host's stop token bounds the wait; processors must cooperate with cancellation.
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            CompleteAcceptance();
            return base.StopAsync(cancellationToken);
        }

        /// <summary>
        /// Closes acceptance before disposing the host-owned cancellation source.
        /// The host should stop the worker before disposal so asynchronous cleanup can finish.
        /// </summary>
        public override void Dispose()
        {
            CompleteAcceptance();
            base.Dispose();
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
                    if (!m_queue.Reader.TryRead(out ApplicationDescription? application))
                    {
                        continue;
                    }
                    string applicationUri = application.ApplicationUri ??
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    try
                    {
                        await m_processor.ProcessAsync(application, stoppingToken).ConfigureAwait(false);
                        m_logger.PushAttemptCompleted(applicationUri);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        m_logger.PushAttemptInterrupted(applicationUri);
                        throw;
                    }
                    catch (Exception error)
                    {
                        m_logger.PushAttemptFailed(error, applicationUri);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown cancels processing; queued registrations are not durable.
            }
            finally
            {
                CompleteAcceptance();
                int discarded = 0;
                while (m_queue.Reader.TryRead(out _))
                {
                    discarded++;
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
            lock (m_acceptance)
            {
                Interlocked.Exchange(ref m_state, 2);
                m_queue.Writer.TryComplete();
            }
        }

        /// <summary>The host-owned processor responsible for persistent provisioning state.</summary>
        private readonly IDeviceRegistrarPushProcessor m_processor;

        /// <summary>The bounded queue of independently owned endpoint descriptions.</summary>
        private readonly Channel<ApplicationDescription> m_queue;

        /// <summary>The source-generated worker diagnostics.</summary>
        private readonly ILogger<DeviceRegistrarPushScheduler> m_logger;

        /// <summary>Serializes only queue acceptance and closure, never provider calls.</summary>
        private readonly Lock m_acceptance = new();

        /// <summary>Zero before startup, one while accepting, and two after stopping.</summary>
        private int m_state;
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
    }
}
