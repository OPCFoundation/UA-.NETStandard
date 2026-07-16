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

namespace Opc.Ua.Stress.Tests.Channels.Helpers
{
    /// <summary>
    /// A deterministic schedule of chaos events generated from a seed.
    /// </summary>
    public sealed class ChaosSchedule
    {
        /// <summary>
        /// Initializes a deterministic chaos event schedule.
        /// </summary>
        /// <param name="seed">The random seed used to generate events.</param>
        /// <param name="totalDuration">The maximum schedule duration.</param>
        /// <param name="kinds">The event kinds to select from uniformly.</param>
        /// <param name="minInterval">The minimum inter-event interval.</param>
        /// <param name="maxInterval">The maximum inter-event interval.</param>
        public ChaosSchedule(
            int seed,
            TimeSpan totalDuration,
            IReadOnlyList<ChaosEventKind> kinds,
            TimeSpan minInterval,
            TimeSpan maxInterval)
        {
            if (totalDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(totalDuration));
            }
            if (kinds == null)
            {
                throw new ArgumentNullException(nameof(kinds));
            }
            if (kinds.Count == 0)
            {
                throw new ArgumentException("At least one chaos event kind is required.", nameof(kinds));
            }
            if (minInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(minInterval));
            }
            if (maxInterval < minInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInterval));
            }

            Seed = seed;
            TotalDuration = totalDuration;
            Events = GenerateEvents(seed, totalDuration, kinds, minInterval, maxInterval);
        }

        /// <summary>
        /// Gets the generated events sorted by dispatch time.
        /// </summary>
        public IReadOnlyList<ChaosEvent> Events { get; }

        /// <summary>
        /// Gets the seed used to generate this schedule.
        /// </summary>
        public int Seed { get; }

        /// <summary>
        /// Gets the maximum schedule duration.
        /// </summary>
        public TimeSpan TotalDuration { get; }

        private static ChaosEvent[] GenerateEvents(
            int seed,
            TimeSpan totalDuration,
            IReadOnlyList<ChaosEventKind> kinds,
            TimeSpan minInterval,
            TimeSpan maxInterval)
        {
            if (totalDuration == TimeSpan.Zero)
            {
                return [];
            }

            double averageIntervalTicks = (minInterval.Ticks + (double)maxInterval.Ticks) / 2;
            int eventCount = (int)Math.Min(
                int.MaxValue,
                Math.Floor(totalDuration.Ticks / averageIntervalTicks));
            var random = new Random(seed);
            var events = new List<ChaosEvent>(eventCount);
            TimeSpan at = TimeSpan.Zero;

            for (int ii = 0; ii < eventCount; ii++)
            {
                at += NextInterval(random, minInterval, maxInterval);
                if (at > totalDuration)
                {
                    break;
                }

                ChaosEventKind kind = kinds[Next(random, kinds.Count)];
                events.Add(CreateEvent(random, at, kind, minInterval, maxInterval));
            }

            return [.. events];
        }

        private static ChaosEvent CreateEvent(
            Random random,
            TimeSpan at,
            ChaosEventKind kind,
            TimeSpan minInterval,
            TimeSpan maxInterval)
        {
            return kind switch
            {
                ChaosEventKind.BlockAccept => new ChaosEvent(
                    at,
                    kind,
                    SpanParam: NextInterval(random, minInterval, maxInterval)),
                ChaosEventKind.StallForwarding => new ChaosEvent(
                    at,
                    kind,
                    IntParam: GetStallMilliseconds(random, minInterval, maxInterval)),
                _ => new ChaosEvent(at, kind)
            };
        }

        private static TimeSpan NextInterval(Random random, TimeSpan minInterval, TimeSpan maxInterval)
        {
            long intervalRange = maxInterval.Ticks - minInterval.Ticks;
            if (intervalRange == 0)
            {
                return minInterval;
            }

            long ticks = minInterval.Ticks + (long)Math.Floor(NextDouble(random) * (intervalRange + 1.0));
            return TimeSpan.FromTicks(ticks);
        }

        private static int GetStallMilliseconds(Random random, TimeSpan minInterval, TimeSpan maxInterval)
        {
            double milliseconds = NextInterval(random, minInterval, maxInterval).TotalMilliseconds;
            return (int)Math.Clamp(milliseconds, 1, int.MaxValue);
        }

        // Deterministic stress schedules require seeded pseudo-random generation rather than cryptographic randomness.
#pragma warning disable CA5394
        private static int Next(Random random, int maxValue)
        {
            return random.Next(maxValue);
        }

        private static double NextDouble(Random random)
        {
            return random.NextDouble();
        }
#pragma warning restore CA5394
    }

    /// <summary>
    /// Dispatches chaos events at their scheduled offsets.
    /// </summary>
    public sealed class ChaosScheduleRunner : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a chaos schedule runner.
        /// </summary>
        /// <param name="schedule">The schedule to run.</param>
        /// <param name="dispatcher">The event dispatcher invoked for each event.</param>
        /// <param name="timeProvider">The time provider used for delays.</param>
        public ChaosScheduleRunner(
            ChaosSchedule schedule,
            Func<ChaosEvent, CancellationToken, Task> dispatcher,
            TimeProvider? timeProvider = null)
        {
            m_schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            m_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Runs the schedule until all events are dispatched or cancellation is requested.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when the schedule stops.</returns>
        public async Task RunAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            long startTimestamp = m_timeProvider.GetTimestamp();

            try
            {
                foreach (ChaosEvent chaosEvent in m_schedule.Events)
                {
                    TimeSpan elapsed = m_timeProvider.GetElapsedTime(startTimestamp);
                    TimeSpan delay = chaosEvent.At - elapsed;
                    if (delay > TimeSpan.Zero)
                    {
                        await m_timeProvider.Delay(delay, ct).ConfigureAwait(false);
                    }

                    ct.ThrowIfCancellationRequested();
                    await m_dispatcher(chaosEvent, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        /// <summary>
        /// Marks the runner as disposed.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref m_disposed, 1);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ChaosScheduleRunner));
            }
        }

        private readonly ChaosSchedule m_schedule;
        private readonly Func<ChaosEvent, CancellationToken, Task> m_dispatcher;
        private readonly TimeProvider m_timeProvider;
        private long m_disposed;
    }

    /// <summary>
    /// A single scheduled chaos event.
    /// </summary>
    /// <param name="At">The offset from schedule start.</param>
    /// <param name="Kind">The kind of chaos action to dispatch.</param>
    /// <param name="IntParam">An integer parameter for event kinds that need one.</param>
    /// <param name="SpanParam">A time span parameter for event kinds that need one.</param>
    public sealed record ChaosEvent(
        TimeSpan At,
        ChaosEventKind Kind,
        int IntParam = 0,
        TimeSpan SpanParam = default);

    /// <summary>
    /// Kinds of chaos actions L3 stress tests can dispatch.
    /// </summary>
    public enum ChaosEventKind
    {
        /// <summary>
        /// Drop all active proxy connections.
        /// </summary>
        DropAllConnections,

        /// <summary>
        /// Block the proxy accept loop for <see cref="ChaosEvent.SpanParam"/>.
        /// </summary>
        BlockAccept,

        /// <summary>
        /// Stall forwarding for <see cref="ChaosEvent.IntParam"/> milliseconds.
        /// </summary>
        StallForwarding,

        /// <summary>
        /// Rotate the client certificate used by the workload.
        /// </summary>
        RotateClientCertificate,

        /// <summary>
        /// Trigger failover in the test workload.
        /// </summary>
        TriggerFailover,

        /// <summary>
        /// Request asynchronous reconnects for all sessions.
        /// </summary>
        ReconnectAllAsync
    }
}
