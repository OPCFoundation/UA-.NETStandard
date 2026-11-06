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
 *
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

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies balanced concurrent and cross-thread accounting.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class StripedCounterTests
    {
        /// <summary>
        /// Concurrent workers can retain and exit many entries without losing the outstanding count.
        /// </summary>
        [Test]
        public async Task ConcurrentEntriesCanBeBalancedByTheirWorkers()
        {
            const int workerCount = 8;
            const int entriesPerWorker = 64;
            var counter = new StripedCounter();
            Assert.That(counter.Count, Is.Zero);

            int enteredWorkers = 0;
            var allEntriesRecorded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowExits = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exitRounds = new TaskCompletionSource<bool>[entriesPerWorker];
            var continueRounds = new TaskCompletionSource<bool>[entriesPerWorker];
            var exitedWorkers = new int[entriesPerWorker];
            for (int round = 0; round < entriesPerWorker; round++)
            {
                exitRounds[round] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                continueRounds[round] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            var workers = new Task[workerCount];

            for (int worker = 0; worker < workerCount; worker++)
            {
                workers[worker] = Task.Run(async () =>
                {
                    var slots = new int[entriesPerWorker];
                    for (int entry = 0; entry < slots.Length; entry++)
                    {
                        slots[entry] = counter.Enter();
                    }

                    if (Interlocked.Increment(ref enteredWorkers) == workerCount)
                    {
                        allEntriesRecorded.TrySetResult(true);
                    }
                    await allowExits.Task.ConfigureAwait(false);

                    for (int round = 0; round < slots.Length; round++)
                    {
                        counter.Exit(slots[round]);
                        if (Interlocked.Increment(ref exitedWorkers[round]) == workerCount)
                        {
                            exitRounds[round].TrySetResult(true);
                        }
                        await continueRounds[round].Task.ConfigureAwait(false);
                    }
                });
            }

            try
            {
                await allEntriesRecorded.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(counter.Count, Is.EqualTo((long)workerCount * entriesPerWorker));
                allowExits.TrySetResult(true);

                for (int round = 0; round < entriesPerWorker; round++)
                {
                    await exitRounds[round].Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(
                        counter.Count,
                        Is.EqualTo((long)workerCount * (entriesPerWorker - round - 1)));
                    continueRounds[round].TrySetResult(true);
                }

                await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                allowExits.TrySetResult(true);
                foreach (TaskCompletionSource<bool> continueRound in continueRounds)
                {
                    continueRound.TrySetResult(true);
                }

                await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }

            Assert.That(counter.Count, Is.Zero);
        }

        /// <summary>
        /// A slot returned on the entry thread remains the correct slot for exit on another thread.
        /// </summary>
        [Test]
        public void EntryReturnedSlotCanBeExitedOnAnotherThread()
        {
            var counter = new StripedCounter();
            int entryThreadId = Environment.CurrentManagedThreadId;
            int slot = counter.Enter();

            Assert.That(counter.Count, Is.EqualTo(1));
            counter.Enter(slot);
            Assert.That(counter.Count, Is.EqualTo(2));

            Exception? exitException = null;
            int exitThreadId = 0;
            var exitThread = new Thread(() =>
            {
                exitThreadId = Environment.CurrentManagedThreadId;
                try
                {
                    counter.Exit(slot);
                }
                catch (Exception exception)
                {
                    exitException = exception;
                }
            })
            {
                IsBackground = true
            };

            exitThread.Start();
            Assert.That(exitThread.Join(TimeSpan.FromSeconds(10)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(exitThreadId, Is.Not.EqualTo(entryThreadId));
                Assert.That(exitException, Is.Null);
                Assert.That(counter.Count, Is.EqualTo(1));
            });
            counter.Exit(slot);
            Assert.That(counter.Count, Is.Zero);
        }
    }
}
