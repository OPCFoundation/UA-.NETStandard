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

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// The scan loop shared by the simulated readers: synchronous scans, and an asynchronous scan
    /// that reports through the sink until a termination condition is fulfilled.
    /// </summary>
    internal sealed class InMemoryScanEngine : IDisposable
    {
        /// <summary>
        /// Creates the loop.
        /// </summary>
        /// <param name="snapshot">Returns what one scan cycle sees.</param>
        /// <param name="cycleInterval">
        /// The duration of one scan cycle; <see langword="null"/> selects <see cref="DefaultCycleInterval"/>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
        public InMemoryScanEngine(Func<ArrayOf<ScanResult>> snapshot, TimeSpan? cycleInterval)
        {
            TimeSpan interval = cycleInterval ?? DefaultCycleInterval;
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cycleInterval),
                    interval,
                    "The scan cycle interval must be positive.");
            }
            m_snapshot = snapshot;
            CycleInterval = interval;
        }

        /// <summary>
        /// The scan cycle of a simulated reader that was not given one: one cycle per second, so an
        /// unbounded <c>ScanStart</c> raises at most one scan event per second.
        /// </summary>
        public static TimeSpan DefaultCycleInterval { get; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets the duration of one scan cycle.
        /// </summary>
        public TimeSpan CycleInterval { get; }

        public IAutoIdReaderSink? Sink
        {
            get
            {
                lock (m_lock)
                {
                    return m_sink;
                }
            }
        }

        public void Attach(IAutoIdReaderSink sink)
        {
            lock (m_lock)
            {
                m_sink = sink;
            }
        }

        public async ValueTask DetachAsync()
        {
            await StopAsync().ConfigureAwait(false);
            lock (m_lock)
            {
                m_sink = null;
            }
        }

        /// <summary>
        /// Scans cycle by cycle until the settings are satisfied and returns the last snapshot.
        /// </summary>
        public async ValueTask<AutoIdScanOutcome> ScanAsync(ScanSettings settings, CancellationToken cancellationToken)
        {
            DateTime deadline = settings.Duration > 0
                ? DateTime.UtcNow.AddMilliseconds(Math.Min(settings.Duration, 86_400_000d))
                : DateTime.MaxValue;
            int cycle = 0;
            ArrayOf<ScanResult> found;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                found = m_snapshot();
                cycle++;
                if ((settings.DataAvailable && found.Count > 0) ||
                    (settings.Cycles > 0 && cycle >= settings.Cycles) ||
                    DateTime.UtcNow >= deadline)
                {
                    break;
                }
                await Task.Delay(Next(deadline), cancellationToken).ConfigureAwait(false);
            }
            return new AutoIdScanOutcome(
                found,
                found.Count > 0
                    ? AutoIdOperationStatusEnumeration.SUCCESS
                    : AutoIdOperationStatusEnumeration.NO_IDENTIFIER);
        }

        /// <summary>
        /// Starts the reporting loop.
        /// </summary>
        public AutoIdOperationStatusEnumeration Start(ScanSettings settings)
        {
            IAutoIdReaderSink? sink = Sink;
            if (sink is null)
            {
                return AutoIdOperationStatusEnumeration.DEVICE_NOT_READY;
            }
            lock (m_lock)
            {
                if (m_loop != null)
                {
                    return AutoIdOperationStatusEnumeration.DEVICE_NOT_READY;
                }
                m_cancel = new CancellationTokenSource();
                // Run on the thread pool so the loop can end only after this method published it.
                CancellationTokenSource owner = m_cancel;
                CancellationToken token = owner.Token;
                m_loop = Task.Run(() => RunAsync(sink, settings, owner, token), CancellationToken.None);
            }
            return AutoIdOperationStatusEnumeration.SUCCESS;
        }

        /// <summary>
        /// Stops the reporting loop and waits until it has ended.
        /// </summary>
        public async ValueTask StopAsync()
        {
            Task? loop;
            CancellationTokenSource? cancel;
            lock (m_lock)
            {
                loop = m_loop;
                cancel = m_cancel;
                m_loop = null;
                m_cancel = null;
            }
            if (loop == null)
            {
                return;
            }
            await cancel!.CancelAsync().ConfigureAwait(false);
            try
            {
                await loop.ConfigureAwait(false);
            }
            finally
            {
                cancel.Dispose();
            }
        }

        private async Task RunAsync(
            IAutoIdReaderSink sink,
            ScanSettings settings,
            CancellationTokenSource owner,
            CancellationToken cancellationToken)
        {
            DateTime deadline = settings.Duration > 0
                ? DateTime.UtcNow.AddMilliseconds(Math.Min(settings.Duration, 86_400_000d))
                : DateTime.MaxValue;
            int cycle = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ArrayOf<ScanResult> found = m_snapshot();
                    cycle++;
                    if (found.Count > 0)
                    {
                        await sink.ReportScanAsync(found, cancellationToken).ConfigureAwait(false);
                    }
                    if ((settings.DataAvailable && found.Count > 0) ||
                        (settings.Cycles > 0 && cycle >= settings.Cycles) ||
                        DateTime.UtcNow >= deadline)
                    {
                        // The loop ended on its own: tell the reader, which returns to idle. A
                        // stop by the reader cancels the token instead and needs no report.
                        bool owned;
                        lock (m_lock)
                        {
                            owned = ReferenceEquals(m_cancel, owner);
                            if (owned)
                            {
                                m_loop = null;
                                m_cancel = null;
                            }
                        }
                        if (owned)
                        {
                            owner.Dispose();
                            await sink.CompleteScanAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        return;
                    }
                    await Task.Delay(Next(deadline), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped by the reader or by detaching.
            }
            catch (ObjectDisposedException)
            {
                // The reader shut down while the loop reported.
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The sink rejected a report: end the loop and let the reader return to idle
                // instead of leaving it scanning with a dead loop.
                bool owned;
                lock (m_lock)
                {
                    owned = ReferenceEquals(m_cancel, owner);
                    if (owned)
                    {
                        m_loop = null;
                        m_cancel = null;
                    }
                }
                if (owned)
                {
                    owner.Dispose();
                    try
                    {
                        await sink.CompleteScanAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The reader is already gone.
                    }
                }
            }
        }

        /// <summary>
        /// Cancels a running loop without waiting for it. <see cref="StopAsync"/> is the orderly way.
        /// </summary>
        public void Dispose()
        {
            CancellationTokenSource? cancel;
            lock (m_lock)
            {
                cancel = m_cancel;
                m_cancel = null;
                m_loop = null;
            }
            if (cancel != null)
            {
                cancel.Cancel();
                cancel.Dispose();
            }
        }

        private TimeSpan Next(DateTime deadline)
        {
            if (deadline == DateTime.MaxValue)
            {
                return CycleInterval;
            }
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }
            return remaining < CycleInterval ? remaining : CycleInterval;
        }

        private readonly Func<ArrayOf<ScanResult>> m_snapshot;
        private readonly Lock m_lock = new();
        private IAutoIdReaderSink? m_sink;
        private Task? m_loop;
        private CancellationTokenSource? m_cancel;
    }
}
