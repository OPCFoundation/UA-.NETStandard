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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Scheduling
{
    /// <summary>
    /// Default <see cref="IPubSubScheduler"/> implementation backed by
    /// <see cref="TimeProvider.CreateTimer"/>. One periodic callback per
    /// registered <see cref="PubSubSchedule"/>; back-pressure is enforced
    /// by skipping a tick if the prior callback is still in-flight (the
    /// runtime logs the skip via the supplied logger).
    /// </summary>
    /// <remarks>
    /// Implements the periodic scheduling abstraction required by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Periodic publishing</see>. The scheduler intentionally
    /// uses a single dedicated timer per schedule rather than a shared
    /// scheduler queue: per Part 14 §6.4.1 each WriterGroup owns its own
    /// publishing cadence independent of every other group.
    /// </remarks>
    public sealed class PubSubScheduler : IPubSubScheduler
    {
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<PubSubScheduler> m_logger;

        /// <summary>
        /// Initializes a new <see cref="PubSubScheduler"/>.
        /// </summary>
        /// <param name="telemetry">
        /// Telemetry context used to create the contextual logger. May be
        /// <see langword="null"/> in which case a no-op logger is used.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used to drive periodic callbacks. Defaults to
        /// <see cref="TimeProvider.System"/> when <see langword="null"/>.
        /// </param>
        public PubSubScheduler(
            ITelemetryContext? telemetry = null,
            TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<PubSubScheduler>();
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncDisposable> ScheduleAsync(
            PubSubSchedule schedule,
            Func<CancellationToken, ValueTask> action,
            CancellationToken cancellationToken = default)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (schedule.Period <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "Schedule.Period must be positive.",
                    nameof(schedule));
            }

            cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA2000 // Caller owns the returned IAsyncDisposable.
            var registration = new ScheduledTimer(m_timeProvider, schedule, action, m_logger);
#pragma warning restore CA2000
            registration.Start();
            return new ValueTask<IAsyncDisposable>(registration);
        }

        private sealed class ScheduledTimer : IAsyncDisposable
        {
            private readonly TimeProvider m_timeProvider;
            private readonly PubSubSchedule m_schedule;
            private readonly Func<CancellationToken, ValueTask> m_action;
            private readonly ILogger m_logger;
            private readonly CancellationTokenSource m_cts = new();
            private readonly Lock m_gate = new();
            private ITimer? m_timer;
            private Task m_currentRun = Task.CompletedTask;
            private bool m_disposed;

            public ScheduledTimer(
                TimeProvider timeProvider,
                PubSubSchedule schedule,
                Func<CancellationToken, ValueTask> action,
                ILogger logger)
            {
                m_timeProvider = timeProvider;
                m_schedule = schedule;
                m_action = action;
                m_logger = logger;
            }

            public void Start()
            {
                TimeSpan dueTime = m_schedule.PublishingOffset > TimeSpan.Zero
                    ? m_schedule.PublishingOffset
                    : m_schedule.Period;
                m_timer = m_timeProvider.CreateTimer(
                    static state => ((ScheduledTimer)state!).OnTick(),
                    this,
                    dueTime,
                    m_schedule.Period);
            }

            private void OnTick()
            {
                Task previous;
                Task next;
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    if (!m_currentRun.IsCompleted)
                    {
                        m_logger.SchedulerTickSkippedPriorCallbackStillRunning();
                        return;
                    }
                    previous = m_currentRun;
                    next = RunActionAsync();
                    m_currentRun = next;
                }
                _ = previous;
            }

            private async Task RunActionAsync()
            {
                try
                {
                    await m_action(m_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    m_logger.ScheduledCallbackThrew(ex);
                }
            }

            public async ValueTask DisposeAsync()
            {
                Task running;
                ITimer? timer;
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    timer = m_timer;
                    m_timer = null;
                    running = m_currentRun;
                }
                if (timer is not null)
                {
                    await timer.DisposeAsync().ConfigureAwait(false);
                }
                try
                {
                    m_cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                try
                {
                    await running.ConfigureAwait(false);
                }
                catch
                {
                }
                m_cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="PubSubScheduler"/>.
    /// </summary>
    internal static partial class PubSubSchedulerLog
    {
        [LoggerMessage(EventId = PubSubEventIds.PubSubScheduler + 0, Level = LogLevel.Debug,
            Message = "Scheduler tick skipped — prior callback still running.")]
        public static partial void SchedulerTickSkippedPriorCallbackStillRunning(this ILogger logger);

        [LoggerMessage(EventId = PubSubEventIds.PubSubScheduler + 1, Level = LogLevel.Error,
            Message = "Scheduled callback threw.")]
        public static partial void ScheduledCallbackThrew(this ILogger logger, Exception exception);
    }

}
