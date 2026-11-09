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
using NUnit.Framework;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleWriteGateAdmissionRegressionTests
    {
        [Test]
        public async Task ExecuteNotificationsCancellationWhileWaitingForWriterReleasesAdmissionAsync()
        {
            var gate = new ScaleWriteGate();
            var writerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWriter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var notificationCancellation = new CancellationTokenSource();
            Task<ServiceResult>? writer = null;
            Task? notification = null;
            int notificationCalls = 0;

            try
            {
                writer = gate.ExecuteAsynchronouslyAsync(
                    async cancellationToken =>
                    {
                        writerEntered.TrySetResult(true);
                        await releaseWriter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        return ServiceResult.Good;
                    },
                    CancellationToken.None).AsTask();

                Assert.That(
                    await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                    Is.True,
                    "The asynchronous writer must own the gate before notification admission starts.");

                // This call begins in the test flow, outside any notification callback, and
                // synchronously registers its admission reservation before returning pending.
                notification = gate.ExecuteNotificationsAsync(
                    _ =>
                    {
                        Interlocked.Increment(ref notificationCalls);
                        return ValueTask.CompletedTask;
                    },
                    notificationCancellation.Token).AsTask();

                Assert.That(notification.IsCompleted, Is.False, "The notification must be waiting for the writer.");
                notificationCancellation.Cancel();
                OperationCanceledException? cancellation = null;
                try
                {
                    await notification.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception)
                {
                    cancellation = exception;
                }
                Assert.That(cancellation, Is.Not.Null, "The pending notification must observe cancellation.");
                Assert.That(
                    Volatile.Read(ref notificationCalls),
                    Is.Zero,
                    "A cancelled waiting notification must not run.");

                releaseWriter.TrySetResult(true);
                ServiceResult writerResult = await writer.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(writerResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));

                int synchronousCalls = 0;
                bool synchronouslyAdmitted = gate.TryExecuteSynchronously(
                    () =>
                    {
                        synchronousCalls++;
                        return "synchronous";
                    },
                    out string synchronousResult);

                Assert.That(synchronouslyAdmitted, Is.True);
                Assert.That(synchronousResult, Is.EqualTo("synchronous"));
                Assert.That(synchronousCalls, Is.EqualTo(1));

                int asynchronousCalls = 0;
                int asynchronousResult = await gate.ExecuteAsynchronouslyAsync(
                        _ =>
                        {
                            Interlocked.Increment(ref asynchronousCalls);
                            return new ValueTask<int>(42);
                        },
                        CancellationToken.None)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                Assert.That(asynchronousResult, Is.EqualTo(42));
                Assert.That(Volatile.Read(ref asynchronousCalls), Is.EqualTo(1));
            }
            finally
            {
                releaseWriter.TrySetResult(true);
                await DrainAsync(writer, notification).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TryExecuteSynchronouslyRollsBackFirstGateWhenSecondDistinctGateRejectsAsync()
        {
            var firstGate = new ScaleWriteGate();
            var secondGate = new ScaleWriteGate();
            var secondWriterEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecondWriter = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstAsyncEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<ServiceResult>? secondWriter = null;
            Task<int>? firstAsyncProbe = null;
            int callbackCalls = 0;
            const int sentinel = 31415;

            try
            {
                secondWriter = secondGate.ExecuteAsynchronouslyAsync(
                    async cancellationToken =>
                    {
                        secondWriterEntered.TrySetResult(true);
                        await releaseSecondWriter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        return ServiceResult.Good;
                    },
                    CancellationToken.None).AsTask();

                Assert.That(
                    await secondWriterEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                    Is.True,
                    "The second gate must be held before attempting multi-gate admission.");

                bool admitted = ScaleWriteGate.TryExecuteSynchronously(
                    new[] { firstGate, secondGate },
                    () =>
                    {
                        callbackCalls++;
                        return sentinel;
                    },
                    out int result);

                Assert.That(admitted, Is.False);
                Assert.That(result, Is.Zero, "Rejected admission must return the default result, not the sentinel.");
                Assert.That(
                    callbackCalls,
                    Is.Zero,
                    "The callback must not run when a distinct gate rejects admission.");

                // Keep the rejecting second gate held while proving rollback released firstGate:
                // a synchronous-only probe would not reveal a leaked synchronous-writer count.
                firstAsyncProbe = firstGate.ExecuteAsynchronouslyAsync(
                    _ =>
                    {
                        firstAsyncEntered.TrySetResult(true);
                        return new ValueTask<int>(73);
                    },
                    CancellationToken.None).AsTask();

                int firstAsyncResult = await firstAsyncProbe.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(firstAsyncResult, Is.EqualTo(73));
                Assert.That(
                    await firstAsyncEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                    Is.True);

                releaseSecondWriter.TrySetResult(true);
                ServiceResult secondWriterResult = await secondWriter
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(secondWriterResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));

                bool admittedAfterRelease = ScaleWriteGate.TryExecuteSynchronously(
                    new[] { firstGate, secondGate },
                    () =>
                    {
                        callbackCalls++;
                        return sentinel;
                    },
                    out int admittedResult);

                Assert.That(admittedAfterRelease, Is.True);
                Assert.That(admittedResult, Is.EqualTo(sentinel));
                Assert.That(
                    callbackCalls,
                    Is.EqualTo(1),
                    "The callback should run exactly once after both gates admit.");
            }
            finally
            {
                releaseSecondWriter.TrySetResult(true);
                await DrainAsync(secondWriter, firstAsyncProbe).ConfigureAwait(false);
            }
        }

        [Test]
        public void TryExecuteSynchronouslyAdmitsRepeatedReferenceOnlyOnce()
        {
            var gate = new ScaleWriteGate();
            int calls = 0;

            bool admitted = ScaleWriteGate.TryExecuteSynchronously(
                new[] { gate, gate, gate },
                () => ++calls,
                out int result);

            Assert.That(admitted, Is.True);
            Assert.That(result, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void TryExecuteSynchronouslyReleasesAdmissionWhenOperationThrows()
        {
            var gate = new ScaleWriteGate();

            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
                () => gate.TryExecuteSynchronously<int>(
                    () => throw new InvalidOperationException("operation failed"),
                    out _));

            Assert.That(failure!.Message, Is.EqualTo("operation failed"));
            Assert.That(gate.TryExecuteSynchronously(() => 42, out int result), Is.True);
            Assert.That(result, Is.EqualTo(42));
        }

        private static async Task DrainAsync(params Task?[] tasks)
        {
            var started = new List<Task>(tasks.Length);
            foreach (Task? task in tasks)
            {
                if (task != null)
                {
                    started.Add(task);
                }
            }
            if (started.Count == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(started).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The cancellation-wait test intentionally cancels its notification task.
            }
        }
    }
}
