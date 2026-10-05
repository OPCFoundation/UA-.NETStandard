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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Fuzzing.Tests
{
    [TestFixture]
    [Category("Fuzzing")]
    [NonParallelizable]
    public sealed class FuzzOraclesTests : IDisposable
    {
        [SetUp]
        public void SetUp()
        {
            FuzzableCode.Reset();
            m_inputs = new TestInputDirectory();
        }

        [TearDown]
        public void Dispose()
        {
            m_inputs?.Dispose();
        }

        [Test]
        public void AllocationOracleReportsADecodeOverBudget()
        {
            Assume.That(FuzzOracles.IsAllocationMeasurementSupported, Is.True);
            long overBudget = FuzzOracles.GetAllocationBudget(16) + (1024 * 1024);

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(
                () => FuzzOracles.MeasureAllocation("Allocating", 16, () => AllocateChunks(overBudget)));

            Assert.That(ex.Kind, Is.EqualTo(ResourceFindingKind.Allocation));
            Assert.That(ex.Message, Does.Contain("Allocating"));
        }

        [Test]
        public void AllocationOracleAcceptsADecodeWithinBudget()
        {
            long withinBudget = FuzzOracles.GetAllocationBudget(1024) / 4;

            int chunks = FuzzOracles.MeasureAllocation("Allocating", 1024, () => AllocateChunks(withinBudget));

            Assert.That(chunks, Is.Positive);
        }

        [Test]
        public void AllocationOracleReportsADecodeThatThrowsAfterAllocating()
        {
            // A decoder that allocates from a length prefix and then runs out of input
            // fails with the documented error, which the fuzz targets swallow.
            Assume.That(FuzzOracles.IsAllocationMeasurementSupported, Is.True);
            long overBudget = FuzzOracles.GetAllocationBudget(16) + (1024 * 1024);

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(
                () => FuzzOracles.MeasureAllocation("Allocating", 16, () =>
                {
                    _ = AllocateChunks(overBudget);
                    throw new FormatException("End of input.");
                }));

            Assert.That(ex.Kind, Is.EqualTo(ResourceFindingKind.Allocation));
            Assert.That(ex.InnerException, Is.TypeOf<FormatException>());
        }

        [Test]
        public void AllocationOracleKeepsTheExceptionOfADecodeWithinBudget()
        {
            var expected = new FormatException("End of input.");

            FormatException ex = Assert.Throws<FormatException>(
                () => FuzzOracles.MeasureAllocation("Throwing", 16, () => throw expected));

            Assert.That(ex, Is.SameAs(expected));
        }

        [Test]
        public void TimeBudgetGrowsWithTheInputSize()
        {
            long small = FuzzOracles.GetTimeBudgetMilliseconds(0);
            long large = FuzzOracles.GetTimeBudgetMilliseconds(1024 * 1024);

            Assert.That(small, Is.Positive);
            Assert.That(large, Is.GreaterThan(small));
        }

        [Test]
        public void TimeOracleReportsATargetThatStaysOverBudget()
        {
            int runs = 0;

            ResourceBudgetException ex = Assert.Throws<ResourceBudgetException>(
                () => FuzzOracles.RunTarget("Slow", 16, () =>
                {
                    runs++;
                    Thread.Sleep(200);
                }, budget: 50));

            Assert.That(ex.Kind, Is.EqualTo(ResourceFindingKind.Time));
            Assert.That(ex.Message, Does.Contain("Slow"));
            Assert.That(runs, Is.EqualTo(FuzzOracles.TimeConfirmationRuns + 1));
        }

        [Test]
        public void TimeOracleIgnoresASlowRunThatIsNotConfirmed()
        {
            // JIT, a collection or a noisy neighbour slow down a single run only.
            int runs = 0;

            FuzzOracles.RunTarget("Warming", 16, () =>
            {
                if (runs++ == 0)
                {
                    Thread.Sleep(200);
                }
            }, budget: 50);

            Assert.That(runs, Is.EqualTo(2));
        }

        [Test]
        public void TimeOracleRunsATargetWithinBudgetOnce()
        {
            int runs = 0;

            FuzzOracles.RunTarget("Fast", 16, () => runs++, budget: 60_000);

            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void RunTargetUsesAWorkerThreadAndPropagatesExceptions()
        {
            int caller = Environment.CurrentManagedThreadId;
            int worker = caller;
            var expected = new InvalidOperationException("Injected fuzz target failure.");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => FuzzOracles.RunTarget("Throwing", 16, () =>
                {
                    worker = Environment.CurrentManagedThreadId;
                    throw expected;
                }));

            Assert.That(ex, Is.SameAs(expected));
            Assert.That(worker, Is.Not.EqualTo(caller));
        }

        [Test]
        public async Task StackOracleTurnsNearLimitRecursionIntoACrashAsync()
        {
            // The target recurses about 400 KB deep. That fits the stack of the replay host's
            // main thread, so only the 256 KB fuzz worker makes the input a finding.
            string file = await m_inputs.WriteAsync("deep", FuzzableCode.DeepRecursionInput.ToArray())
                .ConfigureAwait(false);

            (int exitCode, bool timedOut, string standardOutput, _) = await FuzzProcessWatchdog.RunAsync(
                CreateReplayStartInfo(file, workerStackKilobytes: null),
                TimeSpan.FromSeconds(60)).ConfigureAwait(false);

            Assert.That(timedOut, Is.False);
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(standardOutput, Does.Not.Contain(FuzzableCode.TargetCompleted));
        }

        [Test]
        public async Task SameRecursionCompletesOnTheCallingThreadAsync()
        {
            string file = await m_inputs.WriteAsync("deep", FuzzableCode.DeepRecursionInput.ToArray())
                .ConfigureAwait(false);

            (int exitCode, bool timedOut, string standardOutput, string standardError) =
                await FuzzProcessWatchdog.RunAsync(
                    CreateReplayStartInfo(file, workerStackKilobytes: 0),
                    TimeSpan.FromSeconds(60)).ConfigureAwait(false);

            Assert.That(timedOut, Is.False);
            Assert.That(exitCode, Is.Zero, standardError);
            Assert.That(standardOutput, Does.Contain(FuzzableCode.TargetCompleted));
        }

        private static int AllocateChunks(long bytes)
        {
            const int chunkSize = 64 * 1024;
            int chunks = 0;
            for (long allocated = 0; allocated < bytes; allocated += chunkSize)
            {
                byte[] chunk = new byte[chunkSize];
                GC.KeepAlive(chunk);
                chunks++;
            }
            return chunks;
        }

        private static ProcessStartInfo CreateReplayStartInfo(string input, int? workerStackKilobytes)
        {
            string assembly = typeof(Program).Assembly.Location;
#if NETFRAMEWORK
            string executable = assembly;
            string prefix = string.Empty;
#else
            string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            string prefix = $"\"{assembly}\" ";
#endif
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"{prefix}--replay {nameof(FuzzableCode.DeepRecursionSpanTarget)} \"{input}\"",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.Environment.Remove(FuzzOracles.WorkerStackVariable);
            if (workerStackKilobytes != null)
            {
                startInfo.Environment[FuzzOracles.WorkerStackVariable] =
                    workerStackKilobytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return startInfo;
        }

        private TestInputDirectory m_inputs;
    }
}
