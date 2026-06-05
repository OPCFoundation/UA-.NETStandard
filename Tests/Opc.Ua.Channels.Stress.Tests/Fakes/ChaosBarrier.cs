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

namespace Opc.Ua.Channels.Stress.Tests.Fakes
{
    /// <summary>
    /// Deterministic asynchronous barrier used to release chaos-test callers together.
    /// </summary>
    public sealed class ChaosBarrier
    {
        /// <summary>
        /// Initializes a new barrier with the expected participant count.
        /// </summary>
        /// <param name="expectedParticipants">Number of arrivals required to release the barrier.</param>
        public ChaosBarrier(int expectedParticipants)
        {
            if (expectedParticipants <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedParticipants));
            }

            m_expectedParticipants = expectedParticipants;
        }

        /// <summary>
        /// Gets the number of callers that have arrived at the barrier.
        /// </summary>
        public int ArrivedCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_arrivedCount;
                }
            }
        }

        /// <summary>
        /// Releases callers that are waiting for an explicit release.
        /// </summary>
        public void Release()
        {
            m_release.TrySetResult(true);
        }

        /// <summary>
        /// Signals arrival and waits until the configured number of callers has arrived.
        /// </summary>
        /// <param name="ct">Cancellation token for this caller's wait.</param>
        /// <returns>A task that completes when the barrier releases.</returns>
        public Task SignalAndWaitAsync(CancellationToken ct = default)
        {
            Task waitTask = SignalArrival(releaseWhenExpected: true);
            return WithCancellationAsync(waitTask, ct);
        }

        /// <summary>
        /// Signals arrival and waits until <see cref="Release"/> is called.
        /// </summary>
        /// <param name="ct">Cancellation token for this caller's wait.</param>
        /// <returns>A task that completes when the barrier is released.</returns>
        public Task SignalAndWaitForReleaseAsync(CancellationToken ct = default)
        {
            Task waitTask = SignalArrival(releaseWhenExpected: false);
            return WithCancellationAsync(waitTask, ct);
        }

        /// <summary>
        /// Waits until the configured number of callers has arrived.
        /// </summary>
        /// <param name="ct">Cancellation token for this caller's wait.</param>
        /// <returns>A task that completes when all expected callers have arrived.</returns>
        public Task WaitUntilArrivedAsync(CancellationToken ct = default)
        {
            return WithCancellationAsync(m_arrival.Task, ct);
        }

        private static Task WithCancellationAsync(Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled || task.IsCompleted)
            {
                return task;
            }

            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled(ct);
            }

            return WaitWithCancellationAsync(task, ct);
        }

        private Task SignalArrival(bool releaseWhenExpected)
        {
            Task waitTask;
            bool shouldRelease;
            lock (m_lock)
            {
                m_arrivedCount++;
                if (m_arrivedCount >= m_expectedParticipants)
                {
                    m_arrival.TrySetResult(true);
                }

                shouldRelease = releaseWhenExpected &&
                    m_arrivedCount >= m_expectedParticipants &&
                    !m_release.Task.IsCompleted;
                waitTask = m_release.Task;
            }

            if (shouldRelease)
            {
                m_release.TrySetResult(true);
            }

            return waitTask;
        }

        private static async Task WaitWithCancellationAsync(Task task, CancellationToken ct)
        {
            var cancellation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = ct.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancellation,
                useSynchronizationContext: false);

            if (task != await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
            }

            await task.ConfigureAwait(false);
        }

        private readonly Lock m_lock = new();
        private readonly TaskCompletionSource<bool> m_arrival = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int m_expectedParticipants;
        private int m_arrivedCount;
    }
}
