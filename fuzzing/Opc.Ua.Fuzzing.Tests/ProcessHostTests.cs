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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace Opc.Ua.Fuzzing.Tests
{
    [TestFixture]
    [Category("Fuzzing")]
    [NonParallelizable]
    public sealed class ProcessHostTests : IDisposable
    {
        [SetUp]
        public void SetUp()
        {
            m_inputs = new TestInputDirectory();
        }

        [TearDown]
        public void Dispose()
        {
            m_inputs?.Dispose();
        }

        [TestCase(nameof(FuzzableCode.ThrowingStreamTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingStringTarget))]
        [TestCase(nameof(FuzzableCode.ThrowingSpanTarget))]
        public async Task InjectedTargetFailureProducesANonzeroHostExitAsync(string target)
        {
            string file = await m_inputs.WriteAsync("failing input", [0x41]).ConfigureAwait(false);

            (int exitCode, bool timedOut, string output, string error) =
                await RunHostAsync(target, file).ConfigureAwait(false);

            Assert.That(timedOut, Is.False, error);
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Does.Contain("Injected fuzz target failure.").And.Contain(target));
            Assert.That(output, Is.Empty);
        }

        [Test]
        public async Task MissingTargetPreservesTheHostUsageExitCodeAsync()
        {
            string file = Path.Combine(m_inputs.DirectoryPath, "not-read");

            (int exitCode, bool timedOut, string output, string error) =
                await RunHostAsync("MissingTarget", file).ConfigureAwait(false);

            Assert.That(timedOut, Is.False, error);
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error, Does.Contain("The fuzzing function MissingTarget was not found."));
            Assert.That(output, Is.Empty);
        }

        [Test]
        public async Task MissingInputFileProducesANonzeroHostExitAsync()
        {
            string file = Path.Combine(m_inputs.DirectoryPath, "missing input");

            (int exitCode, bool timedOut, string output, string error) =
                await RunHostAsync(nameof(FuzzableCode.SpanTarget), file).ConfigureAwait(false);

            Assert.That(timedOut, Is.False, error);
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Does.Contain(nameof(FileNotFoundException)).And.Contain(file));
            Assert.That(output, Is.Empty);
        }

        [Test]
        public async Task EmptyCorpusProducesANonzeroHostExitAsync()
        {
            (int exitCode, bool timedOut, string output, string error) =
                await RunHostAsync(nameof(FuzzableCode.SpanTarget), m_inputs.DirectoryPath).ConfigureAwait(false);

            Assert.That(timedOut, Is.False, error);
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(error, Does.Contain("Replay corpus is empty:").And.Contain(m_inputs.DirectoryPath));
            Assert.That(output, Is.Empty);
        }

        [Test]
        public async Task SuccessfulReplayPreservesZeroExitAndActuallyInvokesTheTargetAsync()
        {
            string file = await m_inputs.WriteAsync("successful input", FuzzableCode.CompletedInput.ToArray())
                .ConfigureAwait(false);

            (int exitCode, bool timedOut, string output, string error) =
                await RunHostAsync(nameof(FuzzableCode.HangingSpanTarget), file).ConfigureAwait(false);

            Assert.That(timedOut, Is.False, error);
            Assert.That(exitCode, Is.Zero);
            Assert.That(output, Is.EqualTo(FuzzableCode.TargetCompleted + Environment.NewLine));
            Assert.That(error, Is.Empty);
        }

        [Test]
        public async Task SharedWatchdogDrainsBothRedirectedStreamsBeforeProcessExitAsync()
        {
            string file = await m_inputs.WriteAsync("large output input", FuzzableCode.LargeOutputInput.ToArray())
                .ConfigureAwait(false);

            (int exitCode, bool timedOut, string output, string error) =
                await RunHostAsync(nameof(FuzzableCode.HangingSpanTarget), file).ConfigureAwait(false);

            Assert.That(timedOut, Is.False);
            Assert.That(exitCode, Is.Zero);
            Assert.That(output, Is.EqualTo(new string('o', FuzzableCode.OutputLength)));
            Assert.That(error, Is.EqualTo(new string('e', FuzzableCode.OutputLength)));
        }

        [Test]
        public async Task SharedWatchdogTerminatesAHangingCallbackAfterItHasEnteredAsync()
        {
            string file = await m_inputs.WriteAsync("hanging input", FuzzableCode.HangingInput.ToArray())
                .ConfigureAwait(false);
            ProcessStartInfo startInfo = CreateReplayStartInfo(nameof(FuzzableCode.HangingSpanTarget), file);
            var elapsed = Stopwatch.StartNew();

            (int exitCode, bool timedOut, string output, string error) =
                await FuzzProcessWatchdog.RunAsync(startInfo, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

            Assert.That(output, Is.EqualTo(FuzzableCode.HangingTargetEntered + Environment.NewLine));
            Assert.That(error, Is.Empty);
            Assert.That(timedOut, Is.True);
            Assert.That(exitCode, Is.Not.Zero);
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)),
                "The shared watchdog must honor the short budget, not the area's 30-second default.");
        }

        [Test]
        public async Task SharedWatchdogRejectsNullStartInformationAsync()
        {
            Func<Task> run = async () =>
                _ = await FuzzProcessWatchdog.RunAsync(null, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await Assert.ThatAsync(run, Throws.TypeOf<ArgumentNullException>()
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo("startInfo")).ConfigureAwait(false);
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(-TimeSpan.TicksPerMillisecond)]
        [TestCase(-TimeSpan.TicksPerSecond)]
        [TestCase(((long)int.MaxValue * TimeSpan.TicksPerMillisecond) + 1)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public async Task SharedWatchdogRejectsInvalidTimeoutBeforeStartingOrChangingTheProcessAsync(long ticks)
        {
            ProcessStartInfo startInfo = CreateMissingExecutableStartInfo();
            TimeSpan timeout = TimeSpan.FromTicks(ticks);
            Func<Task> run = async () =>
                _ = await FuzzProcessWatchdog.RunAsync(startInfo, timeout).ConfigureAwait(false);

            await Assert.ThatAsync(run, Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo("timeout")).ConfigureAwait(false);

            Assert.That(startInfo.UseShellExecute, Is.True);
            Assert.That(startInfo.RedirectStandardOutput, Is.False);
            Assert.That(startInfo.RedirectStandardError, Is.False);
        }

        [TestCase(5000)]
        [TestCase(int.MaxValue)]
        public async Task SharedWatchdogPropagatesLaunchFailureForAllowedTimeoutsAsync(int milliseconds)
        {
            ProcessStartInfo startInfo = CreateMissingExecutableStartInfo();
            Func<Task> run = async () =>
                _ = await FuzzProcessWatchdog.RunAsync(startInfo, TimeSpan.FromMilliseconds(milliseconds))
                    .ConfigureAwait(false);

            await Assert.ThatAsync(run, Throws.TypeOf<Win32Exception>()
                .With.Property(nameof(Win32Exception.NativeErrorCode)).EqualTo(2)).ConfigureAwait(false);

            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
        }

        private static Task<(int ExitCode, bool TimedOut, string Output, string Error)> RunHostAsync(
            string target,
            string input)
        {
            return FuzzProcessWatchdog.RunAsync(CreateReplayStartInfo(target, input), TimeSpan.FromSeconds(15));
        }

        private static ProcessStartInfo CreateReplayStartInfo(string target, string input)
        {
            string assembly = typeof(Program).Assembly.Location;
#if NETFRAMEWORK
            string executable = assembly;
            string prefix = string.Empty;
#else
            string executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            string prefix = $"\"{assembly}\" ";
#endif
            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"{prefix}--replay {target} \"{input}\"",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private ProcessStartInfo CreateMissingExecutableStartInfo()
        {
            return new ProcessStartInfo
            {
                FileName = Path.Combine(m_inputs.DirectoryPath, "missing-replay-host.exe"),
                UseShellExecute = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
        }

        private TestInputDirectory m_inputs;
    }
}
