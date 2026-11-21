// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Periodically polls every operational <see cref="DataSetReader"/> in a
    /// <see cref="ReaderGroup"/> and faults any reader whose
    /// <see cref="DataSetReader.MessageReceiveTimeout"/> has elapsed
    /// without a successful dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the receive-timeout supervisor described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.6">
    /// Part 14 §6.2.9.6 DataSetReader Status</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6.3">
    /// §9.1.6.3 ReaderGroup state transitions</see>: on timeout the
    /// reader transitions to <see cref="PubSubState.Error"/> with
    /// status <see cref="StatusCodes.BadTimeout"/>; recovery to
    /// <see cref="PubSubState.Operational"/> happens automatically when
    /// the next valid dispatch resets the receive clock.
    /// </para>
    /// <para>
    /// Polling is driven by <see cref="IPubSubScheduler"/> at a fixed
    /// cadence so a single shared deterministic clock can be used in
    /// tests via <c>FakeTimeProvider</c>.
    /// </para>
    /// </remarks>
    internal sealed class DataSetReaderTimeoutWatcher : IAsyncDisposable
    {
        private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(1);
        private readonly ArrayOf<DataSetReader> m_readers;
        private readonly IPubSubScheduler m_scheduler;
        private readonly IPubSubDiagnostics m_diagnostics;
        private readonly ILogger m_logger;
        private IAsyncDisposable? m_schedule;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="DataSetReaderTimeoutWatcher"/>.
        /// </summary>
        /// <param name="readers">Readers to supervise.</param>
        /// <param name="scheduler">Scheduler driving periodic polls.</param>
        /// <param name="diagnostics">Diagnostics counter sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="pollInterval">Override poll interval (test seam).</param>
        public DataSetReaderTimeoutWatcher(
            ArrayOf<DataSetReader> readers,
            IPubSubScheduler scheduler,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeSpan? pollInterval = null)
        {
            if (scheduler is null)
            {
                throw new ArgumentNullException(nameof(scheduler));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_readers = readers;
            m_scheduler = scheduler;
            m_diagnostics = diagnostics;
            m_logger = telemetry.CreateLogger<DataSetReaderTimeoutWatcher>();
            PollInterval = pollInterval ?? s_pollInterval;
        }

        /// <summary>
        /// Effective poll interval. Defaults to one second.
        /// </summary>
        public TimeSpan PollInterval { get; }

        /// <summary>
        /// Starts the periodic poll. Idempotent; subsequent calls are
        /// ignored until <see cref="DisposeAsync"/>.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            if (m_schedule is not null || m_disposed)
            {
                return;
            }
            var schedule = new PubSubSchedule(
                PollInterval,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero);
            m_schedule = await m_scheduler.ScheduleAsync(
                schedule,
                PollOnceAsync,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Polls every reader exactly once. Public for deterministic
        /// tests so the watcher can be driven without a real scheduler.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public ValueTask PollOnceAsync(CancellationToken cancellationToken = default)
        {
            foreach (DataSetReader reader in m_readers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.State.State is PubSubState.Disabled
                    or PubSubState.Error)
                {
                    continue;
                }
                if (!reader.IsReceiveTimedOut())
                {
                    continue;
                }
                m_diagnostics.Increment(PubSubDiagnosticsCounterKind.MessageReceiveTimeouts);
                m_diagnostics.RecordError(
                    StatusCodes.BadTimeout,
                    $"DataSetReader '{reader.Name}' MessageReceiveTimeout elapsed.");
                bool transitioned = reader.State.TryFault(
                    StatusCodes.BadTimeout,
                    PubSubStateTransitionReason.Fatal);
                if (transitioned)
                {
                    m_logger.DataSetReaderFaultedOnMessageReceiveTimeout(reader.Name, reader.MessageReceiveTimeout);
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            IAsyncDisposable? schedule = m_schedule;
            m_schedule = null;
            if (schedule is not null)
            {
                await schedule.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="DataSetReaderTimeoutWatcher"/>.
    /// </summary>
    internal static partial class DataSetReaderTimeoutWatcherLog
    {
        [LoggerMessage(EventId = PubSubEventIds.DataSetReaderTimeoutWatcher + 0, Level = LogLevel.Warning,
            Message = "DataSetReader {Reader} faulted on MessageReceiveTimeout (>{Timeout}).")]
        public static partial void DataSetReaderFaultedOnMessageReceiveTimeout(
            this ILogger logger,
            string reader,
            TimeSpan timeout);
    }

}
