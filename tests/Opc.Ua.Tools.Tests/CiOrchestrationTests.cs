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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Executes bounded CI orchestration contracts without building test workloads.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CiOrchestrationTests
    {
        [TestCase("selected-success")]
        [TestCase("selected-skipped")]
        [TestCase("selected-failure")]
        [TestCase("selected-cancelled")]
        [TestCase("unselected-skipped")]
        [TestCase("unselected-failure")]
        [TestCase("missing-job")]
        [TestCase("unknown-job")]
        [TestCase("empty-matrix")]
        [TestCase("missing-matrix")]
        public Task SummaryRequiresTheSelectedJobsAsync(string scenario)
        {
            return RunFixtureAsync(scenario);
        }

        [TestCase("coverage-complete")]
        [TestCase("coverage-missing-batch")]
        [TestCase("coverage-missing-project")]
        [TestCase("coverage-missing-summary")]
        [TestCase("coverage-legacy")]
        [TestCase("coverage-restricted")]
        [TestCase("coverage-unverified-skip")]
        [TestCase("coverage-failed-project")]
        [TestCase("coverage-private-scope")]
        public Task CoverageRequiresEveryApplicableProjectFragmentAsync(string scenario)
        {
            return RunFixtureAsync(scenario);
        }

        [TestCase("proof-completed")]
        [TestCase("proof-process-failure")]
        [TestCase("proof-aborted")]
        [TestCase("proof-run-error")]
        [TestCase("proof-pending-counter")]
        [TestCase("proof-missing-counter")]
        [TestCase("proof-zero-execution")]
        [TestCase("proof-mtp-fallback")]
        [TestCase("proof-replay-empty-regressions")]
        [TestCase("proof-replay-skipped-known-input")]
        public Task StrictProofsRetainHashesWithoutPrivateDetailsAsync(string scenario)
        {
            return RunFixtureAsync(scenario);
        }

        [TestCase("runner-private-publication")]
        [TestCase("runner-stale-results")]
        [TestCase("runner-legacy-coverage")]
        [TestCase("runner-restricted")]
        [TestCase("runner-pinned-framework")]
        [TestCase("runner-unverified-skip")]
        [TestCase("runner-private-failure")]
        [TestCase("runner-process-output-capture")]
        public Task SharedRunnerPreservesScopeAndPrivacyAsync(string scenario)
        {
            return RunFixtureAsync(scenario);
        }

        [TestCase("selection-required-projects")]
        [TestCase("selection-missing-replay")]
        public Task SelectionKeepsTheFixedProfileAndAdditionalPubSubReplayAsync(string scenario)
        {
            return RunFixtureAsync(scenario);
        }

        [TestCase("public-inputs-committed")]
        [TestCase("public-inputs-untracked-overlay")]
        [TestCase("public-inputs-modified-overlay")]
        public Task PublicReplayCannotCreditPrivateOverlaysAsync(string scenario)
        {
            return RunFixtureAsync(scenario);
        }

        [Test]
        public Task WorkflowsUseTheSharedRunnerAndExactAssuranceIdentitiesAsync()
        {
            return RunFixtureAsync("workflow-contracts");
        }

        private static async Task RunFixtureAsync(string scenario)
        {
            string? root = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(root) && !File.Exists(Path.Combine(root, "azure-pipelines.yml")))
            {
                root = Directory.GetParent(root)?.FullName;
            }
            Assert.That(root, Is.Not.Null, "Cannot locate the repository.");
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root!;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(
                root!, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "CiOrchestration.fixture.ps1"));
            process.StartInfo.ArgumentList.Add("-Scenario");
            process.StartInfo.ArgumentList.Add(scenario);
            Assert.That(process.Start(), Is.True);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync().ConfigureAwait(false);
                throw;
            }
            string output = PowerShellScriptOutput.Normalize(await outputTask.ConfigureAwait(false));
            string error = PowerShellScriptOutput.Normalize(await errorTask.ConfigureAwait(false));
            Assert.That(process.ExitCode, Is.Zero, output + error);
            Assert.That(output, Does.Contain($"PASS: {scenario}"));
        }
    }
}
#endif
