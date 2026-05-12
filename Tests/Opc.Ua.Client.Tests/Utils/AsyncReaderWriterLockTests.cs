/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.AsyncPrimitives
{
    /// <summary>
    /// Unit tests for the local hand-rolled
    /// <see cref="AsyncReaderWriterLock"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public sealed class AsyncReaderWriterLockTests
    {
        [Test]
        public async Task ReadersDoNotMutuallyExcludeAsync()
        {
            using var rwLock = new AsyncReaderWriterLock();

            // Acquire two readers concurrently — neither should
            // block the other.
            using AsyncReaderWriterLock.Releaser r1 =
                await rwLock.ReaderLockAsync().ConfigureAwait(false);
            using AsyncReaderWriterLock.Releaser r2 =
                await rwLock.ReaderLockAsync().ConfigureAwait(false);

            // No assertion needed — passing means the second
            // ReaderLockAsync did not block on the first.
            Assert.Pass();
        }

        [Test]
        public async Task WriterExcludesReadersAsync()
        {
            using var rwLock = new AsyncReaderWriterLock();

            AsyncReaderWriterLock.Releaser writer =
                await rwLock.WriterLockAsync().ConfigureAwait(false);

            Task<AsyncReaderWriterLock.Releaser> readerTask =
                rwLock.ReaderLockAsync().AsTask();

            // Reader must not complete while writer is held.
            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(readerTask.IsCompleted, Is.False,
                "Reader must wait while writer holds the lock.");

            writer.Dispose();

            using AsyncReaderWriterLock.Releaser reader =
                await readerTask.ConfigureAwait(false);
            // Reader proceeded after writer released.
        }

        [Test]
        public async Task WriterWaitsForReadersToDrainAsync()
        {
            using var rwLock = new AsyncReaderWriterLock();

            AsyncReaderWriterLock.Releaser r1 =
                await rwLock.ReaderLockAsync().ConfigureAwait(false);
            AsyncReaderWriterLock.Releaser r2 =
                await rwLock.ReaderLockAsync().ConfigureAwait(false);

            Task<AsyncReaderWriterLock.Releaser> writerTask =
                rwLock.WriterLockAsync().AsTask();

            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(writerTask.IsCompleted, Is.False,
                "Writer must not acquire while readers are active.");

            r1.Dispose();
            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(writerTask.IsCompleted, Is.False,
                "Writer must wait for ALL readers to drain, not just " +
                "the first.");

            r2.Dispose();

            using AsyncReaderWriterLock.Releaser writer =
                await writerTask.ConfigureAwait(false);
            // Writer acquired after last reader drained.
        }

        [Test]
        [Repeat(50)]
        [CancelAfter(30_000)]
        public async Task LastReaderNextReaderRaceDoesNotAdmitWriterEarlyAsync(
            CancellationToken ct)
        {
            // Stress test the race the rubber-duck review identified:
            // a naive `Interlocked` reader counter without serialized
            // pairing of counter + drain signal could let a writer
            // observe drain==signaled while a fresh reader has just
            // entered. This test runs many tight reader cycles
            // alongside one writer that asserts m_activeReaders == 0
            // (via a probe) when it acquires.
            using var rwLock = new AsyncReaderWriterLock();
            int activeReaders = 0;
            int maxObservedReadersInsideWriter = 0;

            // Start a churn of readers in tight loops.
            using var churnCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);
            var churn = new Task[8];
            for (int i = 0; i < churn.Length; i++)
            {
                churn[i] = Task.Run(async () =>
                {
                    while (!churnCts.IsCancellationRequested)
                    {
                        using AsyncReaderWriterLock.Releaser r =
                            await rwLock.ReaderLockAsync(churnCts.Token).ConfigureAwait(false);
                        Interlocked.Increment(ref activeReaders);
                        await Task.Yield();
                        Interlocked.Decrement(ref activeReaders);
                    }
                }, churnCts.Token);
            }

            // Run several writer acquisitions interleaved with the churn.
            for (int round = 0; round < 20; round++)
            {
                using AsyncReaderWriterLock.Releaser w = await rwLock
                    .WriterLockAsync(ct).ConfigureAwait(false);
                int observed = Volatile.Read(ref activeReaders);
                if (observed > maxObservedReadersInsideWriter)
                {
                    maxObservedReadersInsideWriter = observed;
                }
                Assert.That(observed, Is.Zero,
                    $"Writer #{round} observed {observed} active " +
                    "readers while holding the writer lock — the " +
                    "reader-counter / drain-signal pairing is racy.");
                await Task.Yield();
            }

            await churnCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(churn).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }

            Assert.That(maxObservedReadersInsideWriter, Is.Zero);
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task WriterCancellationWhileDrainingReleasesSemaphoreAsync(
            CancellationToken testCt)
        {
            using var rwLock = new AsyncReaderWriterLock();

            // Hold a reader so the writer must wait for drain.
            AsyncReaderWriterLock.Releaser reader =
                await rwLock.ReaderLockAsync(testCt).ConfigureAwait(false);

            using var writerCts = new CancellationTokenSource();
            Task<AsyncReaderWriterLock.Releaser> writerTask =
                rwLock.WriterLockAsync(writerCts.Token).AsTask();

            // Ensure the writer has reached the drain wait.
            await Task.Delay(50, testCt).ConfigureAwait(false);
            Assert.That(writerTask.IsCompleted, Is.False);

            // Cancel the writer while draining.
            await writerCts.CancelAsync().ConfigureAwait(false);
            Assert.That(
                () => writerTask,
                Throws.InstanceOf<OperationCanceledException>());

            // The cancelled writer must have released the writer-entry
            // semaphore — the next writer should be able to acquire
            // immediately once the reader leaves.
            reader.Dispose();
            using AsyncReaderWriterLock.Releaser nextWriter =
                await rwLock.WriterLockAsync(testCt).ConfigureAwait(false);
            // If the cancelled writer leaked the semaphore, the test
            // would hang here (caught by [CancelAfter]).
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task WriterCanReacquireAfterReleaseAsync(
            CancellationToken ct)
        {
            using var rwLock = new AsyncReaderWriterLock();

            using (AsyncReaderWriterLock.Releaser w1 =
                await rwLock.WriterLockAsync(ct).ConfigureAwait(false))
            {
                // hold and release
            }

            using AsyncReaderWriterLock.Releaser w2 =
                await rwLock.WriterLockAsync(ct).ConfigureAwait(false);

            // would deadlock if the previous writer leaked.
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task WriterIsNotReentrantAndDeadlocksWithSelfAsync(CancellationToken ct)
        {
            // Sanity: reentrancy is intentionally NOT supported. A
            // writer that asks for the writer lock again on the same
            // logical flow must NOT silently succeed (which would
            // indicate accidental reentrancy). It deadlocks; we
            // detect by short timeout.
            using var rwLock = new AsyncReaderWriterLock();
            using AsyncReaderWriterLock.Releaser outer =
                await rwLock.WriterLockAsync(ct).ConfigureAwait(false);

            using var innerCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct);
            innerCts.CancelAfter(TimeSpan.FromMilliseconds(300));

            Assert.That(
                async () => await rwLock
                    .WriterLockAsync(innerCts.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task ManyParallelReadersAdmittedAsync(CancellationToken ct)
        {
            // Spin up 32 readers in parallel; all should hold the lock
            // simultaneously without blocking each other.
            using var rwLock = new AsyncReaderWriterLock();
            var startGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int holdingCount = 0;
            int peakHolding = 0;
            const int kReaders = 32;
            var releaseGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var readers = new Task[kReaders];
            for (int i = 0; i < kReaders; i++)
            {
                readers[i] = Task.Run(async () =>
                {
                    using AsyncReaderWriterLock.Releaser r =
                    await rwLock.ReaderLockAsync(ct).ConfigureAwait(false);
                    int now = Interlocked.Increment(ref holdingCount);
                    int peak;
                    do
                    {
                        peak = Volatile.Read(ref peakHolding);
                        if (now <= peak)
                        {
                            break;
                        }
                    } while (Interlocked.CompareExchange(ref peakHolding, now, peak) != peak);
                    await releaseGate.Task.ConfigureAwait(false);
                    Interlocked.Decrement(ref holdingCount);
                }, ct);
            }

            // Give all readers time to acquire.
            await Task.Delay(200, ct).ConfigureAwait(false);
            releaseGate.SetResult(true);
            await Task.WhenAll(readers).ConfigureAwait(false);

            Assert.That(peakHolding, Is.EqualTo(kReaders),
                "All 32 readers should have held the lock simultaneously.");
        }
    }
}
