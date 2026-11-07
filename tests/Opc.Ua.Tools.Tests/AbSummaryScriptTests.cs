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
                Assert.That(output, Does.Contain("does not establish process exit status"));
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
            int cpu,
            bool includeCounts = true,
            bool clientFailure = false,
            string rateUnit = "notif",
            string security = "none",
            string scenario = "sub",
            string cpuUnit = "notification",
            bool includeDelivery = true,
            bool includeClientProcess = true,
            string? clientCpuUnit = null)
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
            input.AppendLine("  server: cpuMs=100 pauseMs=2 contention=3");
            if (includeClientProcess)
            {
                input.AppendLine("  client-process: pauseMs=4 contention=5");
            }
            input.AppendLine(FormattableString.Invariant($"  per-op: allocB=10 cpuUs={cpu}.0  ({cpuUnit})"));
            input.AppendLine($"  client-per-op: allocB=20 cpuUs=1.0  ({clientCpuUnit ?? cpuUnit})");
        }

        private async Task WriteResultsAsync(StringBuilder input)
        {
            input.AppendLine("done");
            await File.WriteAllTextAsync(m_results, input.ToString()).ConfigureAwait(false);
        }

        private async Task<(int ExitCode, string Output)> InvokeAsync(int expectedRounds)
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
