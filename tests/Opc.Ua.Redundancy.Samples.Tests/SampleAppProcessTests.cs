// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Exercises output delivery independently of the child process lifetime.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class SampleAppProcessTests
    {
        [Test]
        public async Task ExitedProcessWaitsForBufferedOutputAsync()
        {
            using var releaseOutput = new ManualResetEventSlim();
            var outputEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var process = new SampleAppProcess(
                "buffered-client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                ["--help"],
                writeOutput: _ =>
                {
                    if (outputEntered.TrySetResult(true) &&
                        !releaseOutput.Wait(TimeSpan.FromSeconds(15)))
                    {
                        throw new TimeoutException("The buffered output callback was not released.");
                    }
                });
            await using var lifetime = process.ConfigureAwait(false);
            try
            {
                await outputEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                using var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (!process.HasExited)
                {
                    await Task.Delay(10, exitDeadline.Token).ConfigureAwait(false);
                }

                Task<string?> waiting = process.WaitForLineOrDefaultAsync("--identity", TimeSpan.FromSeconds(10));
                Task completed = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                Assert.That(completed, Is.Not.SameAs(waiting),
                    "Process exit must not complete a line wait while redirected output is still being delivered.");

                releaseOutput.Set();
                string? line = await waiting.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(line, Does.Contain("--identity"));
            }
            finally
            {
                releaseOutput.Set();
            }
        }

        [Test]
        public async Task MissingLineAfterOutputCompletionReturnsNullAsync()
        {
            var process = new SampleAppProcess(
                "missing-line-client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                ["--help"],
                writeOutput: _ => { });
            await using var lifetime = process.ConfigureAwait(false);

            string? line = await process.WaitForLineOrDefaultAsync(
                "IDENTITY HA OK:", TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(line, Is.Null);
            Assert.That(process.ContainsLine("--identity"), Is.True);
            Assert.That(process.HasExited, Is.True);
        }

        /// <summary>
        /// Verifies captured output and bounded tail selection remain available after process disposal.
        /// </summary>
        [Test]
        public async Task CapturedOutputRemainsAvailableAfterProcessDisposalAsync()
        {
            var process = new SampleAppProcess(
                "retained-output-client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                ["--help"],
                writeOutput: _ => { });
            try
            {
                Assert.That(await process.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.True);
            }
            finally
            {
                await process.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(process.GetOutputTail(int.MaxValue), Does.Contain("--identity"));
            Assert.That(process.GetOutputTail(1), Is.EqualTo(process.LastLineContaining(string.Empty)));
            Assert.That(process.GetOutputTail(0), Is.Empty);
        }

        [Test]
        public async Task CancelledLineWaitPropagatesCancellationAsync()
        {
            var process = new SampleAppProcess(
                "cancelled-line-client",
                "Redundancy/RedundantClient",
                "RedundantClient",
                ["--help"],
                writeOutput: _ => { });
            await using var lifetime = process.ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(async () => await process.WaitForLineOrDefaultAsync(
                "IDENTITY HA OK:", TimeSpan.FromSeconds(10), cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
