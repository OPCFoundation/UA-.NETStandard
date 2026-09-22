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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
            Verdict verdict = await InvokeAsync(trxFileCount: 1, total: 128, failed: 0, exitCode: 0, timedOut: false)
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
            Verdict verdict = await InvokeAsync(trxFileCount: 1, total: 256, failed: 0, exitCode: exitCode, timedOut: false)
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
            Verdict verdict = await InvokeAsync(trxFileCount: 1, total: 256, failed: 3, exitCode: exitCode, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Tolerated, Is.False);
                Assert.That(verdict.Reason, Does.Contain("3 test(s) failed"));
            });
        }

        /// <summary>
        /// A run that produced no TRX at all is a broken run, not an empty one.
        /// Every mainline project runs on VSTest and must emit one, so silence
        /// here means the suite stopped executing.
        /// </summary>
        [Test]
        public async Task MissingResultsAreRejectedAsync()
        {
            Verdict verdict = await InvokeAsync(trxFileCount: 0, total: 0, failed: 0, exitCode: 0, timedOut: false)
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
            Verdict verdict = await InvokeAsync(trxFileCount: 1, total: 0, failed: 0, exitCode: 0, timedOut: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(verdict.Passed, Is.False);
                Assert.That(verdict.Reason, Does.Contain("No tests were recorded"));
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
            foreach ((int trx, int total, int failed, int exit, bool timedOut) in new[]
            {
                (1, 128, 0, 0, false),
                (1, 128, 0, 1, false),
                (1, 128, 4, 1, false),
                (0, 0, 0, 1, false),
                (1, 0, 0, 0, false),
                (1, 128, 0, -1, true)
            })
            {
                Verdict verdict = await InvokeAsync(trx, total, failed, exit, timedOut, 45).ConfigureAwait(false);

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
            int failed,
            int exitCode,
            bool timedOut,
            int timeoutMinutes = 30)
        {
            string root = FindRepositoryRoot();
            string script = Path.Combine(root, ".github", "scripts", "get-test-verdict.ps1");

            // Dot-source the helper and emit the verdict as JSON, which is what
            // makes the rule testable without building or running any project.
            string command = string.Format(
                CultureInfo.InvariantCulture,
                ". '{0}'; Get-TestRunVerdict -TrxFileCount {1} -Total {2} -Failed {3} -ExitCode {4} " +
                "-TimedOut ${5} -TimeoutMinutes {6} | ConvertTo-Json -Compress",
                script.Replace("'", "''", StringComparison.Ordinal),
                trxFileCount,
                total,
                failed,
                exitCode,
                timedOut ? "true" : "false",
                timeoutMinutes);

            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
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
                $"Get-TestRunVerdict failed: {PowerShellScriptOutput.Normalize(output + error)}");

            Verdict? verdict = JsonSerializer.Deserialize<Verdict>(output.Trim(), s_json);
            Assert.That(verdict, Is.Not.Null, $"No verdict was emitted: {output}");
            return verdict!;
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

        private static readonly JsonSerializerOptions s_json = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
#endif
