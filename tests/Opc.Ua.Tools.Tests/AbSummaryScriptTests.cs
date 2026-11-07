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

#if NET10_0
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Checks the A/B summary against paired and incomplete harness result blocks.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AbSummaryScriptTests
    {
        /// <summary>
        /// Creates a result file location for one test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), $"ab-summary-{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_directory);
            m_results = Path.Combine(m_directory, "results.txt");
        }

        /// <summary>
        /// Removes the result file location.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        /// <summary>
        /// Counts distinguish a delivery below 93% from a rounded 93.0% report.
        /// </summary>
        [Test]
        public async Task FivePairsReportCountsDifferencesMediansAndRangesAsync()
        {
            long[] baseline = [9299, 9400, 9600, 9800, 9200];
            long[] candidate = [9300, 9500, 9700, 9900, 9100];
            var input = new StringBuilder();
            for (int index = 0; index < baseline.Length; index++)
            {
                AppendResult(input, "A", index + 1, baseline[index], 10000, 1000 + (index * 10), 3 + index);
                AppendResult(input, "B", index + 1, candidate[index], 10000, 1100 + (index * 10), 2 + index);
            }
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(5).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(
                    output,
                    Does.Contain("round=1 side=A rate=1000 rateUnit=notif/s received=9299 expected=10000 " +
                        "delivery=92.99% source=counts"));
                Assert.That(
                    output,
                    Does.Contain("round=1 side=B rate=1100 rateUnit=notif/s received=9300 expected=10000 " +
                        "delivery=93% source=counts"));
                Assert.That(
                    output,
                    Does.Contain("round=1 side=A RESULT scenario=sub security=none sessions=1 --scenario sub"));
                Assert.That(output, Does.Contain("round=1 side=B-A rate=100 deliveryPoints=0.01 serverCpuUs=-1"));
                Assert.That(output, Does.Match(@"(?m)^\s*n\s*:\s*5/5\s*$"));
                Assert.That(output, Does.Match(@"(?m)^\s*deliv\s*:\s*94 \[92-98\] -> 95 \[91-99\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*cpu\s*:\s*5 \[3-7\] -> 4 \[2-6\] \(change=-20%\)"));
                Assert.That(output, Does.Match(@"(?m)^\s*tput\s*:\s*1020 \[1000-1040\] -> 1120 \[1100-1140\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*p50\s*:\s*1 \[1-1\] -> 1 \[1-1\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*p90\s*:\s*2 \[2-2\] -> 2 \[2-2\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*p99\s*:\s*3 \[3-3\] -> 3 \[3-3\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*alloc\s*:\s*10 \[10-10\] -> 10 \[10-10\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*calloc\s*:\s*20 \[20-20\] -> 20 \[20-20\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*ccpu\s*:\s*1 \[1-1\] -> 1 \[1-1\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*pause\s*:\s*2 \[2-2\] -> 2 \[2-2\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*cpause\s*:\s*4 \[4-4\] -> 4 \[4-4\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*cont\s*:\s*3 \[3-3\] -> 3 \[3-3\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*ccont\s*:\s*5 \[5-5\] -> 5 \[5-5\]"));
                Assert.That(output, Does.Contain("Exit status is available only when recorded"));
            });
        }

        /// <summary>
        /// Request, event and native sample workloads retain their rate and CPU units.
        /// </summary>
        [TestCase("read", "req", "request", false)]
        [TestCase("events", "events", "event notification", true)]
        [TestCase("native-counters", "samples", "sample", false)]
        public async Task OtherWorkloadRatesAndCpuUnitsAreReportedAsync(
            string scenario,
            string rateUnit,
            string cpuUnit,
            bool includeDelivery)
        {
            var input = new StringBuilder();
            AppendResult(
                input, "A", 1, 90, 100, 1000, 3,
                rateUnit: rateUnit, scenario: scenario, cpuUnit: cpuUnit, includeDelivery: includeDelivery,
                includeClientProcess: scenario != "native-counters",
                clientCpuUnit: scenario == "native-counters" ? "sample, native workers" : cpuUnit);
            AppendResult(
                input, "B", 1, 92, 100, 1100, 2,
                rateUnit: rateUnit, scenario: scenario, cpuUnit: cpuUnit, includeDelivery: includeDelivery,
                includeClientProcess: scenario != "native-counters",
                clientCpuUnit: scenario == "native-counters" ? "sample, native workers" : cpuUnit);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain($"rateUnit={rateUnit}/s"));
                Assert.That(output, Does.Contain($"cpuUnit=[{cpuUnit}]"));
                if (!includeDelivery)
                {
                    Assert.That(output, Does.Contain("received=n/a expected=n/a delivery=n/a source=unavailable"));
                }
                if (scenario == "native-counters")
                {
                    Assert.That(output, Does.Not.Match(@"(?m)^\s*cpause\s*:"));
                    Assert.That(output, Does.Contain("round=1 side=A rate=1000 rateUnit=samples/s"));
                    Assert.That(output, Does.Not.Contain("rateUnit=notif/s"));
                    Assert.That(output, Does.Contain("clientCpuUnit=[sample, native workers]"));
                }
            });
        }

        /// <summary>
        /// The median of an even number of rounds is the average of the middle values.
        /// </summary>
        [Test]
        public async Task EvenRoundCountAveragesTheMiddleValuesAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 1);
            AppendResult(input, "B", 1, 92, 100, 1000, 2);
            AppendResult(input, "A", 2, 94, 100, 1000, 5);
            AppendResult(input, "B", 2, 96, 100, 1000, 10);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(2).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Match(@"(?m)^\s*cpu\s*:\s*3 \[1-5\] -> 6 \[2-10\]"));
                Assert.That(output, Does.Match(@"(?m)^\s*deliv\s*:\s*92 \[90-94\] -> 94 \[92-96\]"));
            });
        }

        /// <summary>
        /// A zero baseline has no defined relative change.
        /// </summary>
        [Test]
        public async Task ZeroBaselineReportsUnavailableChangeAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 0);
            AppendResult(input, "B", 1, 92, 100, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Match(@"(?m)^\s*cpu\s*:\s*0 \[0-0\] -> 2 \[2-2\] \(change=n/a\)"));
            });
        }

        /// <summary>
        /// Raw CPU ticks and received counts expose a difference hidden by rounded per-operation values.
        /// </summary>
        [TestCase("sub", "notif", "notification")]
        [TestCase("events", "events", "event notification")]
        public async Task RawCpuTicksExposeRoundedPerOperationDifferencesAsync(
            string scenario,
            string rateUnit,
            string cpuUnit)
        {
            var input = new StringBuilder();
            AppendResult(
                input, "A", 1, 1000000, 1000000, 1000, 1,
                rateUnit: rateUnit, scenario: scenario, cpuUnit: cpuUnit, serverCpuTicks: 10000000);
            AppendResult(
                input, "B", 1, 1000000, 1000000, 1100, 1,
                rateUnit: rateUnit, scenario: scenario, cpuUnit: cpuUnit, serverCpuTicks: 10000001);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireRawCpu: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(
                    output,
                    Does.Contain("serverCpuTicks=10000001 serverCpuUs=1.0000001 " +
                        "cpuSource=ticks/counts reportedCpuUs=1"));
                Assert.That(output, Does.Contain("serverCpuUs=0.0000001"));
                Assert.That(output, Does.Contain("serverCpuDirection=increased"));
                Assert.That(output, Does.Match(@"(?m)^\s*cpu\s*:\s*1 \[1-1\] -> 1\.0000001"));
                Assert.That(output, Does.Not.Contain("[reported-per-op input]"));
            });
        }

        /// <summary>
        /// The paired direction remains visible when the displayed CPU difference rounds to zero.
        /// </summary>
        [TestCase(999999999L, "decreased")]
        [TestCase(1000000000L, "unchanged")]
        [TestCase(1000000001L, "increased")]
        public async Task RawCpuDirectionUsesUnroundedRatiosAsync(long candidateTicks, string direction)
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 100000000, 100000000, 1000, 1, serverCpuTicks: 1000000000);
            AppendResult(input, "B", 1, 100000000, 100000000, 1100, 1, serverCpuTicks: candidateTicks);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireRawCpu: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Match($"serverCpuUs=-?0 serverCpuDirection={direction}"));
            });
        }

        /// <summary>
        /// Exact direction does not overflow when both cross-products exceed Int64.
        /// </summary>
        [Test]
        public async Task RawCpuDirectionHandlesLargeCrossProductsAsync()
        {
            var input = new StringBuilder();
            AppendResult(
                input, "A", 1, long.MaxValue - 1, long.MaxValue - 1, 1000, 0.1m,
                serverCpuTicks: long.MaxValue - 2);
            AppendResult(
                input, "B", 1, long.MaxValue, long.MaxValue, 1100, 0.1m,
                serverCpuTicks: long.MaxValue - 1);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireRawCpu: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain("serverCpuUs=0 serverCpuDirection=increased"));
            });
        }

        /// <summary>
        /// Requiring raw CPU rejects a side that only has a rounded per-operation value.
        /// </summary>
        [Test]
        public async Task MissingRequiredRawCpuFailsAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 100, 100, 1000, 3, serverCpuTicks: 3000);
            AppendResult(input, "B", 1, 100, 100, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireRawCpu: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("Incomplete result for side=B round=1"));
                Assert.That(output, Does.Contain("missing raw per-notification CPU"));
            });
        }

        /// <summary>
        /// A comparison using a legacy CPU value identifies its rounded input.
        /// </summary>
        [Test]
        public async Task MixedCpuSourcesAreIdentifiedAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 100, 100, 1000, 3, serverCpuTicks: 3000);
            AppendResult(input, "B", 1, 100, 100, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain("cpuSource=ticks/counts"));
                Assert.That(output, Does.Contain("cpuSource=reported-per-op"));
                Assert.That(output, Does.Contain("[reported-per-op input]"));
                Assert.That(output, Does.Contain("serverCpuDirection=unavailable"));
            });
        }

        /// <summary>
        /// A matching unit alone does not establish a supported notification workload.
        /// </summary>
        [Test]
        public async Task UnsupportedScenarioCannotProvideRawNotificationCpuAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 100, 100, 1000, 3, scenario: "counters", serverCpuTicks: 3000);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireRawCpu: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("missing raw per-notification CPU"));
            });
        }

        /// <summary>
        /// A zero received count cannot be used as a CPU-per-notification denominator.
        /// </summary>
        [Test]
        public async Task ZeroReceivedCountCannotProvideRawCpuAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 0, 100, 1000, 3, serverCpuTicks: 3000);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireRawCpu: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("missing raw per-notification CPU"));
            });
        }

        /// <summary>
        /// A malformed tick count is not interpreted as a rounded-only report.
        /// </summary>
        [TestCase("")]
        [TestCase("-1")]
        [TestCase("invalid")]
        public async Task MalformedCpuTicksFailAsync(string ticks)
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 100, 100, 1000, 3);
            input.Replace("cpuMs=100", $"cpuMs=100 cpuTicks={ticks}");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("server CPU tick count is malformed"));
            });
        }

        /// <summary>
        /// Duplicate tick fields are not silently reduced to one CPU measurement.
        /// </summary>
        [Test]
        public async Task DuplicateCpuTicksFailAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 100, 100, 1000, 3, serverCpuTicks: 3000);
            input.Replace("cpuTicks=3000", "cpuTicks=3000 cpuTicks=4000");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("multiple CPU tick counts"));
            });
        }

        /// <summary>
        /// Older result blocks retain their percentage without inventing counts.
        /// </summary>
        [Test]
        public async Task LegacyResultsIdentifyRoundedDeliveryAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 9299, 10000, 1000, 3, includeCounts: false);
            AppendResult(input, "B", 1, 9300, 10000, 1100, 2, includeCounts: false);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(
                    output,
                    Does.Contain("received=n/a expected=n/a delivery=93% source=reported-rounded"));
                Assert.That(output, Does.Match(@"(?m)^\s*deliv\s*:\s*93 \[93-93\] -> 93 \[93-93\]"));
                Assert.That(output, Does.Contain("[reported-rounded input]"));
                Assert.That(output, Does.Contain("exit=n/a"));
            });
        }

        /// <summary>
        /// Recorded successful exits are included for both sides of a required pair.
        /// </summary>
        [Test]
        public async Task RequiredSuccessfulExitStatusesAreReportedAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3, exitCode: 0);
            AppendResult(input, "B", 1, 92, 100, 1100, 2, exitCode: 0);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireExitStatus: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Match(@"(?m)^round=1 side=A .* exit=0\r?$"));
                Assert.That(output, Does.Match(@"(?m)^round=1 side=B .* exit=0\r?$"));
            });
        }

        /// <summary>
        /// Nonzero process exits reject even an otherwise complete result block.
        /// </summary>
        [TestCase(1)]
        [TestCase(124)]
        [TestCase(127)]
        [TestCase(137)]
        public async Task NonzeroExitStatusFailsAsync(int failedExit)
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3, exitCode: 0);
            AppendResult(input, "B", 1, 92, 100, 1100, 2, exitCode: failedExit);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireExitStatus: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain($"Run exited with code {failedExit} for side=B round=1"));
            });
        }

        /// <summary>
        /// A failed process exit is reported even if it did not write a result.
        /// </summary>
        [Test]
        public async Task FailedExitWithoutResultReportsExitAsync()
        {
            var input = new StringBuilder();
            input.AppendLine("### side=A round=1 --scenario sub");
            input.AppendLine("EXIT code=124");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireExitStatus: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("Run exited with code 124 for side=A round=1"));
            });
        }

        /// <summary>
        /// A required exit status cannot be supplied by the other side of a pair.
        /// </summary>
        [Test]
        public async Task MissingRequiredExitStatusFailsAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3, exitCode: 0);
            AppendResult(input, "B", 1, 92, 100, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1, requireExitStatus: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("Incomplete result for side=B round=1"));
                Assert.That(output, Does.Contain("missing exit status"));
            });
        }

        /// <summary>
        /// Duplicate exit records cannot be presented as one process outcome.
        /// </summary>
        [Test]
        public async Task DuplicateExitStatusFailsAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3, exitCode: 0);
            input.AppendLine("EXIT code=0");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("multiple exit statuses"));
            });
        }

        /// <summary>
        /// Exit records must contain one shell status in the valid range.
        /// </summary>
        [TestCase("EXIT code=-1")]
        [TestCase("EXIT code=256")]
        [TestCase("EXIT code=abc")]
        [TestCase("EXIT code=0 ignored")]
        public async Task MalformedExitStatusFailsAsync(string status)
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            input.AppendLine(status);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("malformed exit status"));
            });
        }

        /// <summary>
        /// The runner records and propagates the actual status of each harness invocation.
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(124, 1)]
        [Platform("Linux,MacOSX")]
        public async Task RunnerRecordsAndPropagatesExitStatusesAsync(int candidateExit, int expectedExit)
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The runner integration test uses Unix executables.");
            }

            string tools = Path.Combine(m_directory, "tools with spaces");
            Directory.CreateDirectory(tools);
            string timeout = Path.Combine(tools, "timeout");
            string baseline = Path.Combine(tools, "baseline harness");
            string candidate = Path.Combine(tools, "candidate harness");
            string scenarios = Path.Combine(m_directory, "scenarios.txt");
            await WriteExecutableAsync(timeout, "#!/bin/sh\nshift\nexec \"$@\"\n").ConfigureAwait(false);
            await WriteExecutableAsync(baseline, CreateHarnessScript(0)).ConfigureAwait(false);
            await WriteExecutableAsync(candidate, CreateHarnessScript(candidateExit)).ConfigureAwait(false);
            await File.WriteAllTextAsync(scenarios, "--scenario sub\n").ConfigureAwait(false);

            using Process process = CreateRunnerProcess(scenarios, "1");
            process.StartInfo.Environment["BASELINE"] = baseline;
            process.StartInfo.Environment["CANDIDATE"] = candidate;
            process.StartInfo.Environment["PATH"] =
                tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            (int runnerExit, string runnerOutput) = await RunProcessAsync(process).ConfigureAwait(false);
            string results = await File.ReadAllTextAsync(m_results).ConfigureAwait(false);
            (int summaryExit, string summaryOutput) = await InvokeAsync(1, requireExitStatus: true)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(runnerExit, Is.EqualTo(expectedExit), runnerOutput);
                Assert.That(results.Split("EXIT code=", StringSplitOptions.None).Length - 1, Is.EqualTo(2));
                Assert.That(results, Does.Contain("EXIT code=0"));
                Assert.That(results, Does.Contain($"EXIT code={candidateExit}"));
                Assert.That(results, Does.Contain("done"));
                if (candidateExit == 0)
                {
                    Assert.That(summaryExit, Is.Zero, summaryOutput);
                }
                else
                {
                    Assert.That(summaryExit, Is.Not.Zero, summaryOutput);
                    Assert.That(summaryOutput, Does.Contain($"Run exited with code {candidateExit} for side=B"));
                }
            });
        }

        /// <summary>
        /// Invalid round counts are rejected before the existing result file is replaced.
        /// </summary>
        [TestCase("0")]
        [TestCase("1x")]
        [TestCase("100001")]
        [Platform("Linux,MacOSX")]
        public async Task RunnerRejectsInvalidRoundCountsWithoutReplacingResultsAsync(string rounds)
        {
            string scenarios = Path.Combine(m_directory, "scenarios.txt");
            await File.WriteAllTextAsync(scenarios, "--scenario sub\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(m_results, "existing results").ConfigureAwait(false);
            using Process process = CreateRunnerProcess(scenarios, rounds);

            (int exitCode, string output) = await RunProcessAsync(process).ConfigureAwait(false);
            string results = await File.ReadAllTextAsync(m_results).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(2), output);
                Assert.That(output, Does.Contain("ROUNDS must be an integer from 1 through 100000"));
                Assert.That(results, Is.EqualTo("existing results"));
            });
        }

        /// <summary>
        /// An unreadable scenario list cannot replace the existing result file.
        /// </summary>
        [Test]
        [Platform("Linux,MacOSX")]
        public async Task RunnerRejectsMissingScenarioListWithoutReplacingResultsAsync()
        {
            string scenarios = Path.Combine(m_directory, "missing-scenarios.txt");
            await File.WriteAllTextAsync(m_results, "existing results").ConfigureAwait(false);
            using Process process = CreateRunnerProcess(scenarios, "1");

            (int exitCode, string output) = await RunProcessAsync(process).ConfigureAwait(false);
            string results = await File.ReadAllTextAsync(m_results).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(2), output);
                Assert.That(output, Does.Contain("Cannot read scenario list:"));
                Assert.That(results, Is.EqualTo("existing results"));
            });
        }

        /// <summary>
        /// A comparison with counts on only one side still identifies the rounded input.
        /// </summary>
        [Test]
        public async Task MixedDeliverySourcesAreIdentifiedAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 9299, 10000, 1000, 3, includeCounts: false);
            AppendResult(input, "B", 1, 9300, 10000, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain("delivery=93% source=reported-rounded"));
                Assert.That(output, Does.Contain("received=9300 expected=10000 delivery=93% source=counts"));
                Assert.That(output, Does.Contain("[reported-rounded input]"));
            });
        }

        /// <summary>
        /// A legacy percentage with invariant thousands separators is retained.
        /// </summary>
        [Test]
        public async Task LegacyGroupedPercentageIsParsedAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 1000, 100, 1000, 3, includeCounts: false);
            AppendResult(input, "B", 1, 1100, 100, 1100, 2, includeCounts: false);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain("delivery=1000% source=reported-rounded"));
                Assert.That(output, Does.Contain("delivery=1100% source=reported-rounded"));
            });
        }

        /// <summary>
        /// A zero expected count does not describe a delivery percentage.
        /// </summary>
        [Test]
        public async Task ZeroExpectedCountHasNoDeliveryPercentageAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 2000, 0, 1000, 3);
            AppendResult(input, "B", 1, 1000, 0, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain("received=2000 expected=0 delivery=n/a source=counts"));
                Assert.That(output, Does.Contain("received=1000 expected=0 delivery=n/a source=counts"));
                Assert.That(output, Does.Contain("deliveryPoints=n/a"));
                Assert.That(output, Does.Not.Contain("NaN"));
                Assert.That(output, Does.Not.Contain("Infinity"));
            });
        }

        /// <summary>
        /// A missing or duplicate side cannot be presented as a pair.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingOrDuplicateSideFailsAsync(bool duplicate)
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            if (duplicate)
            {
                AppendResult(input, "A", 1, 91, 100, 1000, 3);
                AppendResult(input, "B", 1, 92, 100, 1000, 2);
            }
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("round=1 needs exactly one A and one B result"));
            });
        }

        /// <summary>
        /// A missing result line is not silently discarded from the summary.
        /// </summary>
        [Test]
        public async Task IncompleteBlockFailsAsync()
        {
            var input = new StringBuilder();
            input.AppendLine("### side=A round=1 --scenario sub");
            input.AppendLine("RESULT scenario=sub");
            input.AppendLine("  client: notif/s=1000 received/expected=90.0 % received=90 expected=100");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("Incomplete result for side=A round=1"));
                Assert.That(output, Does.Contain("missing server, server per-op, client per-op"));
            });
        }

        /// <summary>
        /// The requested round count detects a complete-looking partial file.
        /// </summary>
        [Test]
        public async Task MissingExpectedRoundsFailAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            AppendResult(input, "B", 1, 92, 100, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(5).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("has 1 rounds; expected 5"));
            });
        }

        /// <summary>
        /// A gap in the sequence is detected even without an expected round count.
        /// </summary>
        [Test]
        public async Task GapInRoundSequenceFailsAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            AppendResult(input, "B", 1, 92, 100, 1100, 2);
            AppendResult(input, "A", 3, 91, 100, 1000, 3);
            AppendResult(input, "B", 3, 93, 100, 1100, 2);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(0).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("is missing round 2"));
            });
        }

        /// <summary>
        /// A missing completion marker leaves the number of attempted rounds unknown.
        /// </summary>
        [Test]
        public async Task MissingDoneMarkerFailsAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            AppendResult(input, "B", 1, 92, 100, 1100, 2);
            await File.WriteAllTextAsync(m_results, input.ToString()).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("missing the done marker"));
            });
        }

        /// <summary>
        /// A client-reported failure cannot be summarized as a successful round.
        /// </summary>
        [Test]
        public async Task ReportedClientFailureFailsAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3, clientFailure: true);
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("Client reported FAIL for side=A round=1"));
            });
        }

        /// <summary>
        /// A single count field is not interpreted as a complete delivery record.
        /// </summary>
        [Test]
        public async Task MissingDeliveryCountFailsAsync()
        {
            var input = new StringBuilder();
            input.AppendLine("### side=A round=1 --scenario sub");
            input.AppendLine("RESULT scenario=sub");
            input.AppendLine("  client: notif/s=1000 received/expected=90.0 % received=90");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("both nonnegative received and expected values"));
            });
        }

        /// <summary>
        /// Different rate units cannot be compared as one scenario.
        /// </summary>
        [Test]
        public async Task MismatchedRateUnitsFailAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            AppendResult(input, "B", 1, 92, 100, 1100, 2, rateUnit: "req");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("incompatible rate or CPU units"));
            });
        }

        /// <summary>
        /// Client CPU costs with different denominators cannot be compared.
        /// </summary>
        [Test]
        public async Task MismatchedClientCpuUnitsFailAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            AppendResult(input, "B", 1, 92, 100, 1100, 2, clientCpuUnit: "sample");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("incompatible rate or CPU units"));
            });
        }

        /// <summary>
        /// Differing workload descriptions cannot be reported as a matched comparison.
        /// </summary>
        [Test]
        public async Task MismatchedResultLinesFailAsync()
        {
            var input = new StringBuilder();
            AppendResult(input, "A", 1, 90, 100, 1000, 3);
            AppendResult(input, "B", 1, 92, 100, 1100, 2, security: "encrypt");
            await WriteResultsAsync(input).ConfigureAwait(false);

            (int exitCode, string output) = await InvokeAsync(1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("round=1 has mismatched RESULT lines"));
            });
        }

        private static void AppendResult(
            StringBuilder input,
            string side,
            int round,
            long received,
            long expected,
            int rate,
            decimal cpu,
            bool includeCounts = true,
            bool clientFailure = false,
            string rateUnit = "notif",
            string security = "none",
            string scenario = "sub",
            string cpuUnit = "notification",
            bool includeDelivery = true,
            bool includeClientProcess = true,
            string? clientCpuUnit = null,
            int? exitCode = null,
            long? serverCpuTicks = null)
        {
            input.AppendLine(FormattableString.Invariant($"### side={side} round={round} --scenario {scenario}"));
            input.AppendLine($"RESULT scenario={scenario} security={security} sessions=1 --scenario {scenario}");
            input.Append(FormattableString.Invariant($"  client: {rateUnit}/s={rate}"));
            if (scenario == "native-counters")
            {
                input.Append(" notif/s=250");
            }
            if (includeDelivery)
            {
                double ratio = expected == 0 ? 0 : (double)received / expected;
                string percentage = ratio.ToString("P1", CultureInfo.InvariantCulture);
                input.Append($" received/expected={percentage}");
                if (includeCounts)
                {
                    input.Append(FormattableString.Invariant($" received={received} expected={expected}"));
                }
            }
            input.Append(" writeErrors=0 latency n=1 p50=1.000ms p90=2.000ms p99=3.000ms");
            input.AppendLine(clientFailure ? " FAIL" : string.Empty);
            string serverCpuMs = serverCpuTicks.HasValue
                ? TimeSpan.FromTicks(serverCpuTicks.Value).TotalMilliseconds.ToString(
                    "F0", CultureInfo.InvariantCulture)
                : "100";
            input.Append($"  server: cpuMs={serverCpuMs}");
            if (serverCpuTicks.HasValue)
            {
                input.Append(FormattableString.Invariant($" cpuTicks={serverCpuTicks.Value}"));
            }
            input.AppendLine(" pauseMs=2 contention=3");
            if (includeClientProcess)
            {
                input.AppendLine("  client-process: pauseMs=4 contention=5");
            }
            input.AppendLine(FormattableString.Invariant($"  per-op: allocB=10 cpuUs={cpu:F6}  ({cpuUnit})"));
            input.AppendLine($"  client-per-op: allocB=20 cpuUs=1.0  ({clientCpuUnit ?? cpuUnit})");
            if (exitCode.HasValue)
            {
                input.AppendLine(FormattableString.Invariant($"EXIT code={exitCode.Value}"));
            }
        }

        private static string CreateHarnessScript(int exitCode)
        {
            return "#!/bin/sh\n" +
                "out=\n" +
                "while [ \"$#\" -gt 0 ]; do\n" +
                "  if [ \"$1\" = --out ]; then shift; out=$1; fi\n" +
                "  shift\n" +
                "done\n" +
                "printf '%s\\n' \\\n" +
                "  'RESULT scenario=sub security=none sessions=1 --scenario sub' \\\n" +
                "  '  client: notif/s=1000 received/expected=90.0 % received=90 expected=100' \\\n" +
                "  '  server: cpuMs=100 pauseMs=2 contention=3' \\\n" +
                "  '  per-op: allocB=10 cpuUs=3.0  (notification)' \\\n" +
                "  '  client-per-op: allocB=20 cpuUs=1.0  (notification)' >> \"$out\"\n" +
                FormattableString.Invariant($"exit {exitCode}\n");
        }

        private static async Task WriteExecutableAsync(string path, string content)
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The runner integration test uses Unix executables.");
            }

            await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        private Process CreateRunnerProcess(string scenarios, string rounds)
        {
            var process = new Process();
            process.StartInfo.FileName = "/bin/bash";
            process.StartInfo.WorkingDirectory = m_directory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.ArgumentList.Add(Path.Combine(FindRepositoryRoot(), "perf", "ab.sh"));
            process.StartInfo.Environment["SCENARIOS"] = scenarios;
            process.StartInfo.Environment["ROUNDS"] = rounds;
            process.StartInfo.Environment["OUT"] = m_results;
            return process;
        }

        private async Task WriteResultsAsync(StringBuilder input)
        {
            input.AppendLine("done");
            await File.WriteAllTextAsync(m_results, input.ToString()).ConfigureAwait(false);
        }

        private async Task<(int ExitCode, string Output)> InvokeAsync(
            int expectedRounds,
            bool requireExitStatus = false,
            bool requireRawCpu = false)
        {
            string root = FindRepositoryRoot();
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(root, "perf", "ab-summary.ps1"));
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(m_results);
            process.StartInfo.ArgumentList.Add("-ExpectedRounds");
            process.StartInfo.ArgumentList.Add(expectedRounds.ToString(CultureInfo.InvariantCulture));
            if (requireExitStatus)
            {
                process.StartInfo.ArgumentList.Add("-RequireExitStatus");
            }
            if (requireRawCpu)
            {
                process.StartInfo.ArgumentList.Add("-RequireRawCpu");
            }

            return await RunProcessAsync(process).ConfigureAwait(false);
        }

        private static async Task<(int ExitCode, string Output)> RunProcessAsync(Process process)
        {
            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The process exited between the check and termination.
                }
                await process.WaitForExitAsync().ConfigureAwait(false);
                throw;
            }

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            return (process.ExitCode, PowerShellScriptOutput.Normalize(output + error));
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "perf", "ab-summary.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private string m_directory = string.Empty;
        private string m_results = string.Empty;
    }
}
#endif
