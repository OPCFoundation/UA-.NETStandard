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
    /// Runs a throttled concurrent stress workload and records operation outcomes.
    /// </summary>
    public sealed class StressRunner : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a concurrent stress workload runner.
        /// </summary>
        /// <param name="operations">The operations workers randomly execute.</param>
        /// <param name="concurrency">The number of worker tasks to run.</param>
        /// <param name="targetOpsPerSecond">The aggregate target operation rate.</param>
        /// <param name="telemetry">Optional telemetry context reserved for future stress diagnostics.</param>
        /// <param name="timeProvider">The time provider used for monotonic timing and delays.</param>
        public StressRunner(
            IReadOnlyList<Func<CancellationToken, Task>> operations,
            int concurrency,
            int targetOpsPerSecond,
            ITelemetryContext? telemetry = null,
            TimeProvider? timeProvider = null)
        {
            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }
            if (operations.Count == 0)
            {
                throw new ArgumentException("At least one stress operation is required.", nameof(operations));
            }
            if (concurrency <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(concurrency));
            }
            if (targetOpsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetOpsPerSecond));
            }

            for (int ii = 0; ii < operations.Count; ii++)
            {
                if (operations[ii] == null)
                {
                    throw new ArgumentException("Stress operations cannot contain null delegates.", nameof(operations));
                }
            }

            _ = telemetry;

            m_operations = operations;
            m_concurrency = concurrency;
            m_targetOpsPerSecond = targetOpsPerSecond;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the total number of operations that completed with an outcome.
        /// </summary>
        public long TotalOpsAttempted => Interlocked.Read(ref m_totalOpsAttempted);

        /// <summary>
        /// Gets the total number of operations that completed successfully.
        /// </summary>
        public long TotalOpsSucceeded => Interlocked.Read(ref m_totalOpsSucceeded);

        /// <summary>
        /// Gets the total number of operations that completed with a failure.
        /// </summary>
        public long TotalOpsFailed => Interlocked.Read(ref m_totalOpsFailed);

        /// <summary>
        /// Gets a capped reservoir sample of operation latencies.
        /// </summary>
        public IReadOnlyList<TimeSpan> LatencySamples
        {
            get
            {
                lock (m_latencyLock)
                {
                    return m_latencySamples.ToArray();
                }
            }
        }

        /// <summary>
        /// Gets failure counts grouped by OPC UA status code.
        /// </summary>
        public IReadOnlyDictionary<StatusCode, long> FailureCounts
        {
            get
            {
                lock (m_failureLock)
                {
                    return new Dictionary<StatusCode, long>(m_failureCounts);
                }
            }
        }

        /// <summary>
        /// Gets the fraction of attempted operations that failed.
        /// </summary>
        public double FailureRate => TotalOpsAttempted == 0 ? 0 : (double)TotalOpsFailed / TotalOpsAttempted;

        /// <summary>
        /// Gets p50, p95, and p99 latency percentiles from the current reservoir sample.
        /// </summary>
        public (TimeSpan p50, TimeSpan p95, TimeSpan p99) LatencyPercentiles
        {
            get
            {
                TimeSpan[] samples;
                lock (m_latencyLock)
                {
                    samples = [.. m_latencySamples];
                }

                if (samples.Length == 0)
                {
                    return (TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
                }

                Array.Sort(samples);
                return (
                    GetPercentile(samples, 50),
                    GetPercentile(samples, 95),
                    GetPercentile(samples, 99));
            }
        }

        /// <summary>
        /// Starts all stress worker tasks.
        /// </summary>
        /// <param name="ct">Cancellation token linked to the worker lifetime.</param>
        /// <returns>A task that completes when workers have been scheduled.</returns>
        public Task StartAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            lock (m_stateLock)
            {
                ThrowIfDisposed();
                if (m_workers != null)
                {
                    throw new InvalidOperationException("The stress runner has already been started.");
                }

                m_startTimestamp = m_timeProvider.GetTimestamp();
                m_stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                CancellationToken runToken = m_stopCts.Token;
                var workers = new Task[m_concurrency];
                for (int ii = 0; ii < workers.Length; ii++)
                {
                    workers[ii] = Task.Run(() => RunWorkerAsync(runToken), CancellationToken.None);
                }

                m_workers = workers;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the stress workers and waits for them to exit.
        /// </summary>
        /// <returns>A task that completes when all workers have stopped.</returns>
        public async Task StopAsync()
        {
            Task[]? workers;
            CancellationTokenSource? stopCts;
            lock (m_stateLock)
            {
                workers = m_workers;
                stopCts = m_stopCts;
                if (workers == null || stopCts == null || m_stopRequested)
                {
                    return;
                }

                m_stopRequested = true;
            }

            stopCts.Cancel();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the stress workers and releases cancellation resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            await StopAsync().ConfigureAwait(false);

            CancellationTokenSource? stopCts;
            lock (m_stateLock)
            {
                stopCts = m_stopCts;
                m_stopCts = null;
            }

            stopCts?.Dispose();
            GC.SuppressFinalize(this);
        }

        private static TimeSpan GetPercentile(TimeSpan[] sortedSamples, int percentile)
        {
            int index = (int)Math.Ceiling(percentile / 100.0 * sortedSamples.Length) - 1;
            index = Math.Clamp(index, 0, sortedSamples.Length - 1);
            return sortedSamples[index];
        }

        private async Task RunWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    await WaitForPaceAsync(ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    Func<CancellationToken, Task> operation = PickOperation();
                    await ExecuteOperationAsync(operation, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        private Func<CancellationToken, Task> PickOperation()
        {
            int index = UnsecureRandom.Shared.Next(m_operations.Count);
            return m_operations[index];
        }

        private async Task WaitForPaceAsync(CancellationToken ct)
        {
            long ticket = Interlocked.Increment(ref m_pacerTickets) - 1;
            var targetElapsed = TimeSpan.FromSeconds((double)ticket / m_targetOpsPerSecond);
            TimeSpan elapsed = m_timeProvider.GetElapsedTime(m_startTimestamp);
            TimeSpan delay = targetElapsed - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await TimeProviderExtensions.Delay(m_timeProvider, delay, ct).ConfigureAwait(false);
            }
        }

        private async Task ExecuteOperationAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct)
        {
            long startTimestamp = m_timeProvider.GetTimestamp();
            try
            {
                await operation(ct).ConfigureAwait(false);
                RecordSuccess(m_timeProvider.GetElapsedTime(startTimestamp));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ServiceResultException ex)
            {
                RecordFailure(ex.StatusCode, m_timeProvider.GetElapsedTime(startTimestamp));
            }
            catch (Exception)
            {
                RecordFailure(StatusCodes.Bad, m_timeProvider.GetElapsedTime(startTimestamp));
            }
        }

        private void RecordSuccess(TimeSpan latency)
        {
            long attempted = Interlocked.Increment(ref m_totalOpsAttempted);
            Interlocked.Increment(ref m_totalOpsSucceeded);
            RecordLatency(latency, attempted);
        }

        private void RecordFailure(StatusCode statusCode, TimeSpan latency)
        {
            long attempted = Interlocked.Increment(ref m_totalOpsAttempted);
            Interlocked.Increment(ref m_totalOpsFailed);

            lock (m_failureLock)
            {
                m_failureCounts.TryGetValue(statusCode, out long count);
                m_failureCounts[statusCode] = count + 1;
            }

            RecordLatency(latency, attempted);
        }

        private void RecordLatency(TimeSpan latency, long sampleNumber)
        {
            lock (m_latencyLock)
            {
                if (m_latencySamples.Count < MaxLatencySamples)
                {
                    m_latencySamples.Add(latency);
                    return;
                }

                long replacementIndex = (long)(UnsecureRandom.Shared.NextDouble() * sampleNumber);
                if (replacementIndex < MaxLatencySamples)
                {
                    m_latencySamples[(int)replacementIndex] = latency;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(StressRunner));
            }
        }

        private const int MaxLatencySamples = 1000;

        private readonly IReadOnlyList<Func<CancellationToken, Task>> m_operations;
        private readonly int m_concurrency;
        private readonly int m_targetOpsPerSecond;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_failureLock = new();
        private readonly Lock m_latencyLock = new();
        private readonly Lock m_stateLock = new();
        private readonly Dictionary<StatusCode, long> m_failureCounts = [];
        private readonly List<TimeSpan> m_latencySamples = new(MaxLatencySamples);
        private CancellationTokenSource? m_stopCts;
        private Task[]? m_workers;
        private bool m_stopRequested;
        private long m_disposed;
        private long m_pacerTickets;
        private long m_startTimestamp;
        private long m_totalOpsAttempted;
        private long m_totalOpsFailed;
        private long m_totalOpsSucceeded;
    }
}
