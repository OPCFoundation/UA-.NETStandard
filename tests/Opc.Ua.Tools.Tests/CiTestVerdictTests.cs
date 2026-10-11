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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Contract tests for Get-TestRunVerdict in .github/scripts/get-test-verdict.ps1,
    /// the rule that decides whether a continuous integration test leg reports
    /// green. It deliberately tolerates a non-zero test-host exit after a fully
    /// green run, because the host can stall or crash during process exit once
    /// every test and teardown has already completed. That tolerance has to stay
    /// narrow: if it widened to cover a run that recorded real failures, a broken
    /// suite would merge silently. Both directions are asserted here.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CiTestVerdictTests
    {
        /// <summary>
        /// A clean run passes and is not flagged as tolerated.
        /// </summary>
        [Test]
        public async Task CleanRunPassesAsync()
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1, total: 128, passed: 128, failed: 0, exitCode: 0, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.True);
                Assert.That(verdict.Tolerated, Is.False);
                Assert.That(verdict.Reason, Is.Empty);
            });
        }

        /// <summary>
        /// The case this rule exists for: every recorded test passed, yet the
        /// host exited non-zero because it died during process exit. Observed on
        /// run 35714133848, job 'test-windows-net48 (5/30)', where
        /// Opc.Ua.Client.Tests reported 256 passed and 0 failed and the host
        /// still exited 1. Failing that would report a false red.
        /// </summary>
        [TestCase(1)]
        [TestCase(-1)]
        [TestCase(134)]
        public async Task AtExitHostFailureIsToleratedAsync(int exitCode)
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1, total: 256, passed: 256, failed: 0, exitCode: exitCode, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.True);
                // Never silently: the reason reaches the job summary and a
                // warning annotation so a host that keeps dying stays visible.
                Assert.That(verdict.Tolerated, Is.True);
                Assert.That(verdict.Reason, Does.Contain("256"));
                Assert.That(verdict.Reason, Does.Contain(exitCode.ToString(CultureInfo.InvariantCulture)));
            });
        }

        /// <summary>
        /// The tolerance must not widen to cover a run that recorded failures.
        /// A non-zero failed counter is rejected whatever the exit code says -
        /// including exit code 0, which would otherwise let a broken suite
        /// through on the strength of a lying host.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public async Task RecordedFailuresAreNeverToleratedAsync(int exitCode)
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1, total: 256, passed: 253, failed: 3, exitCode: exitCode, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Tolerated, Is.False);
                Assert.That(verdict.Reason, Does.Contain("3 test(s) failed"));
            });
        }

        /// <summary>
        /// VSTest has more non-passing counters than failed/error/timeout. A
        /// partial run can contain passing tests and still be inconclusive,
        /// disconnected, not runnable, or unfinished. The executor must sum the
        /// complete set before asking for a verdict.
        /// </summary>
        [Test]
        public async Task EveryNonPassingTrxCounterIsCountedAsync()
        {
            string[] nonPassing =
            [
                "failed",
                "error",
                "timeout",
                "aborted",
                "passedButRunAborted",
                "inconclusive",
                "notRunnable",
                "disconnected",
                "warning",
                "completed",
                "inProgress",
                "pending"
            ];
            var counters = new Dictionary<string, int> { ["total"] = 20, ["passed"] = 8 };
            foreach (string counter in nonPassing)
            {
                counters[counter] = 1;
            }

            TrxResults results = await MeasureAsync(new TrxFile("run.trx", counters, [])).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results.Files, Is.EqualTo(1));
                Assert.That(results.Total, Is.EqualTo(20));
                Assert.That(results.Passed, Is.EqualTo(8));
                Assert.That(results.Failed, Is.EqualTo(nonPassing.Length));
                Assert.That(results.FixtureFailures, Is.Empty);
            });
        }

        /// <summary>
        /// A fixture whose setup or teardown fails - in this repository usually
        /// the assembly-level certificate leak check in [OneTimeTearDown] - fails
        /// after its tests passed. No TRX counter records it; the NUnit adapter
        /// reports it as run messages instead, and the host exits non-zero.
        /// Observed on master for Opc.Ua.Gds.Tests and Opc.Ua.Server.Tests on
        /// every leg, accepted as an at-exit stall until this rule existed.
        /// </summary>
        [Test]
        public async Task FixtureSetupAndTeardownFailuresAreReadFromTheRunMessagesAsync()
        {
            var counters = new Dictionary<string, int> { ["total"] = 10, ["passed"] = 10 };

            TrxResults results = await MeasureAsync(
                new TrxFile(
                    "teardown.trx",
                    counters,
                    [
                        ("Warning", "ClientTest FinishAsync\n\tTEST 13:48:23 validation suppressed"),
                        ("Error", "TearDown failed for test fixture Opc.Ua.Gds.Tests.LeakDetectionSetup"),
                        ("Error", "One or more child tests were ignored\nCertificate leak detected: 1 instance(s)")
                    ]),
                new TrxFile(
                    "setup.trx",
                    counters,
                    [
                        ("Error", "Setup failed for test fixture Opc.Ua.Server.Tests.ServerFixture"),
                        ("Error", "OneTimeSetUp: System.InvalidOperationException : the server did not start")
                    ])).ConfigureAwait(false);

            string[] expected =
            [
                "TearDown failed for test fixture Opc.Ua.Gds.Tests.LeakDetectionSetup",
                "Setup failed for test fixture Opc.Ua.Server.Tests.ServerFixture"
            ];
            Assert.Multiple(() =>
            {
                Assert.That(results.Files, Is.EqualTo(2));
                Assert.That(results.Total, Is.EqualTo(20));
                Assert.That(results.Failed, Is.Zero);
                Assert.That(results.FixtureFailures, Is.EquivalentTo(expected));
            });
        }

        /// <summary>
        /// Other run messages are not fixture failures. A host that crashes at
        /// exit leaves "Test host process crashed" behind, and test output that
        /// merely mentions a teardown must not count either; otherwise the
        /// at-exit tolerance would no longer apply to the case it exists for.
        /// </summary>
        [Test]
        public async Task OtherRunMessagesAreNotFixtureFailuresAsync()
        {
            TrxResults results = await MeasureAsync(
                new TrxFile(
                    "crash.trx",
                    new Dictionary<string, int> { ["total"] = 332, ["passed"] = 332 },
                    [
                        ("Error", "The active test run was aborted. Reason: Test host process crashed"),
                        ("Warning", "PushTest Run\n\tTEST 14:08:56 TearDown failed for test fixture Other.Fixture")
                    ])).ConfigureAwait(false);

            Assert.That(results.FixtureFailures, Is.Empty);
        }

        /// <summary>
        /// A failed fixture setup or teardown is rejected whatever the exit code
        /// says, and the reason names the fixture so the job summary shows it.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public async Task FixtureSetupOrTeardownFailureIsRejectedAsync(int exitCode)
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1,
                total: 1314,
                passed: 1258,
                failed: 0,
                exitCode: exitCode,
                timedOut: false,
                fixtureFailures: ["TearDown failed for test fixture Opc.Ua.Gds.Tests.LeakDetectionSetup"])
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Tolerated, Is.False);
                Assert.That(verdict.Reason, Does.Contain("Opc.Ua.Gds.Tests.LeakDetectionSetup"));
            });
        }

        /// <summary>
        /// The executor has to hand the fixture failures it measured to the
        /// verdict; a rule that never receives them protects nothing.
        /// </summary>
        [Test]
        public async Task ExecutorPassesFixtureFailuresToTheVerdictAsync()
        {
            string executor = Path.Combine(FindRepositoryRoot(), ".github", "scripts", "run-dotnet-tests.ps1");
            string source = await File.ReadAllTextAsync(executor).ConfigureAwait(false);

            Assert.That(source, Does.Contain("-FixtureFailures $results.FixtureFailures"));
        }

        /// <summary>
        /// A run that produced no TRX at all is a broken run, not an empty one.
        /// Every mainline project runs on VSTest and must emit one, so silence
        /// here means the suite stopped executing.
        /// </summary>
        [Test]
        public async Task MissingResultsAreRejectedAsync()
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 0, total: 0, passed: 0, failed: 0, exitCode: 0, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Reason, Does.Contain("No TRX"));
            });
        }

        /// <summary>
        /// A TRX that records zero tests is a discovery failure. Reporting it as
        /// a pass is how a matrix silently stops testing anything.
        /// </summary>
        [Test]
        public async Task ZeroRecordedTestsAreRejectedAsync()
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1, total: 0, passed: 0, failed: 0, exitCode: 0, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Reason, Does.Contain("No tests were recorded"));
            });
        }

        /// <summary>
        /// A suite whose tests were all skipped records Total &gt; 0 with nothing
        /// executed. Accepting that would let a project that silently stopped
        /// running anything - a broken category filter, a disabled fixture, an
        /// unmet runtime precondition - report green while verifying nothing.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        public async Task AllSkippedRunIsRejectedAsync(int exitCode)
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1, total: 200, passed: 0, failed: 0, exitCode: exitCode, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Tolerated, Is.False);
                Assert.That(verdict.Reason, Does.Contain("200"));
                Assert.That(verdict.Reason, Does.Contain("skipped or not executed"));
            });
        }

        /// <summary>
        /// A partially skipped suite still passes: the contract is that at least
        /// one test executed, not that every recorded test ran.
        /// </summary>
        [Test]
        public async Task PartiallySkippedRunPassesAsync()
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1, total: 200, passed: 1, failed: 0, exitCode: 0, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.True);
                Assert.That(verdict.Tolerated, Is.False);
            });
        }

        /// <summary>
        /// A timeout is never benign, even when the partial results look clean:
        /// the executor killed a process that was still running, so the run did
        /// not complete.
        /// </summary>
        [Test]
        public async Task TimeoutIsRejectedEvenWithCleanResultsAsync()
        {
            Verdict verdict = await InvokeAsync(
                trxFileCount: 1,
                total: 256,
                passed: 256,
                failed: 0,
                exitCode: -1,
                timedOut: true,
                timeoutMinutes: 45).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Tolerated, Is.False);
                Assert.That(verdict.Reason, Does.Contain("45-minute"));
            });
        }

        /// <summary>
        /// Tolerated is only ever reported alongside a pass, and a tolerated
        /// verdict always carries a reason - otherwise the condition would not
        /// reach the job summary.
        /// </summary>
        [Test]
        public async Task ToleratedAlwaysImpliesPassedWithAReasonAsync()
        {
            foreach ((int trx, int total, int passed, int failed, int exit, bool timedOut) in new[]
            {
                (1, 128, 128, 0, 0, false),
                (1, 128, 128, 0, 1, false),
                (1, 128, 124, 4, 1, false),
                (0, 0, 0, 0, 1, false),
                (1, 0, 0, 0, 0, false),
                (1, 128, 0, 0, 0, false),
                (1, 128, 0, 0, 1, false),
                (1, 128, 128, 0, -1, true)
            })
            {
                Verdict verdict = await InvokeAsync(trx, total, passed, failed, exit, timedOut, 45)
                    .ConfigureAwait(false);

                if (verdict.Tolerated)
                {
                    Assert.That(verdict.Passed, Is.True, "A tolerated verdict must also be a pass.");
                    Assert.That(verdict.Reason, Is.Not.Empty, "A tolerated verdict must explain itself.");
                }
                if (!verdict.Passed)
                {
                    Assert.That(verdict.Reason, Is.Not.Empty, "A failing verdict must explain itself.");
                }
            }
        }

        private static async Task<Verdict> InvokeAsync(
            int trxFileCount,
            int total,
            int passed,
            int failed,
            int exitCode,
            bool timedOut,
            int timeoutMinutes = 30,
            string[]? fixtureFailures = null)
        {
            string script = Path.Combine(FindRepositoryRoot(), ".github", "scripts", "get-test-verdict.ps1");

            // Dot-source the helper and emit the verdict as JSON, which is what
            // makes the rule testable without building or running any project.
            string command = string.Format(
                CultureInfo.InvariantCulture,
                ". {0}; Get-TestRunVerdict -TrxFileCount {1} -Total {2} -Passed {3} -Failed {4} -ExitCode {5} " +
                "-TimedOut ${6} -TimeoutMinutes {7}{8} | ConvertTo-Json -Compress",
                Quote(script),
                trxFileCount,
                total,
                passed,
                failed,
                exitCode,
                timedOut ? "true" : "false",
                timeoutMinutes,
                fixtureFailures == null
                    ? string.Empty
                    : " -FixtureFailures @(" + string.Join(",", fixtureFailures.Select(Quote)) + ")");

            string output = await RunPowerShellAsync(command, "Get-TestRunVerdict").ConfigureAwait(false);
            Verdict? verdict = JsonSerializer.Deserialize<Verdict>(output.Trim(), s_json);
            Assert.That(verdict, Is.Not.Null, $"No verdict was emitted: {output}");
            return verdict!;
        }

        /// <summary>
        /// Writes each TRX into a fresh directory and measures it with
        /// Measure-TestResults, the way the executor reads a project's results.
        /// </summary>
        private static async Task<TrxResults> MeasureAsync(
            params TrxFile[] trxFiles)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CiTestVerdictTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                foreach (TrxFile trxFile in trxFiles)
                {
                    WriteTrx(Path.Combine(directory, trxFile.FileName), trxFile.Counters, trxFile.RunInfos);
                }

                string script = Path.Combine(FindRepositoryRoot(), ".github", "scripts", "get-test-verdict.ps1");
                string command = string.Format(
                    CultureInfo.InvariantCulture,
                    ". {0}; Measure-TestResults {1} | ConvertTo-Json -Compress",
                    Quote(script),
                    Quote(directory));

                string output = await RunPowerShellAsync(command, "Measure-TestResults").ConfigureAwait(false);
                TrxResults? results = JsonSerializer.Deserialize<TrxResults>(output.Trim(), s_json);
                Assert.That(results, Is.Not.Null, $"No results were emitted: {output}");
                return results!;
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Writes a TRX with the given counters and run messages, the two parts
        /// of a VSTest result file that Measure-TestResults reads.
        /// </summary>
        private static void WriteTrx(
            string path,
            Dictionary<string, int> counters,
            (string Outcome, string Text)[] runInfos)
        {
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var document = new XDocument(
                new XElement(
                    ns + "TestRun",
                    new XElement(
                        ns + "ResultSummary",
                        new XAttribute("outcome", "Completed"),
                        new XElement(
                            ns + "Counters",
                            counters.Select(counter => new XAttribute(
                                counter.Key,
                                counter.Value.ToString(CultureInfo.InvariantCulture)))),
                        new XElement(
                            ns + "RunInfos",
                            runInfos.Select(runInfo => new XElement(
                                ns + "RunInfo",
                                new XAttribute("computerName", "ci"),
                                new XAttribute("outcome", runInfo.Outcome),
                                new XElement(ns + "Text", runInfo.Text)))))));
            document.Save(path);
        }

        private static async Task<string> RunPowerShellAsync(string command, string function)
        {
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = FindRepositoryRoot();
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(command);

            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            Assert.That(
                process.ExitCode,
                Is.Zero,
                $"{function} failed: {PowerShellScriptOutput.Normalize(output + error)}");
            return output;
        }

        /// <summary>
        /// Quotes a value as a single-quoted PowerShell string literal.
        /// </summary>
        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".github", "scripts", "get-test-verdict.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        /// <summary>
        /// The verdict emitted by Get-TestRunVerdict. Deserialized by reflection, hence public.
        /// </summary>
        public sealed record Verdict(bool Passed, bool Tolerated, string Reason);

        /// <summary>
        /// A TRX for Measure-TestResults: its counters and its run messages.
        /// </summary>
        private sealed record TrxFile(
            string FileName,
            Dictionary<string, int> Counters,
            (string Outcome, string Text)[] RunInfos);

        /// <summary>
        /// The result of Measure-TestResults. Deserialized by reflection, hence public.
        /// </summary>
        public sealed record TrxResults(int Files, int Total, int Passed, int Failed, string[] FixtureFailures);

        private static readonly JsonSerializerOptions s_json = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
#endif
