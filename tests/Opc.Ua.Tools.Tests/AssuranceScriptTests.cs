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
    /// Exercises assurance pipeline fixtures for project selection, execution proof, and authenticated evidence
    /// collection.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AssuranceScriptTests
    {
        /// <summary>
        /// Verifies that matrix generation rejects a missing explicitly selected project even when another exists.
        /// </summary>
        [Test]
        public async Task MissingExplicitProjectFailsEvenWhenAnotherExistsAsync()
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root,
                "-File",
                Path.Combine(root, ".azurepipelines", "get-matrix.ps1"),
                "-BuildRoot",
                root,
                "-Files",
                "tests/Opc.Ua.Tools.Tests/Opc.Ua.Tools.Tests.csproj,tests/Missing.Tests.csproj")
                .ConfigureAwait(false);

            Assert.That(exitCode, Is.Not.Zero, output);
        }

        /// <summary>
        /// Verifies that evaluated target-framework support controls matrix inclusion and not-applicable reporting.
        /// </summary>
        [TestCase("net48", "tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj", false)]
        [TestCase("net9.0", "tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj", false)]
        [TestCase("netstandard2.1", "tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj", false)]
        [TestCase("net48", "tests/Opc.Ua.ReleaseEvidence.Tests/Opc.Ua.ReleaseEvidence.Tests.csproj", false)]
        [TestCase("net9.0", "tests/Opc.Ua.ReleaseEvidence.Tests/Opc.Ua.ReleaseEvidence.Tests.csproj", false)]
        [TestCase("net10.0", "tests/Opc.Ua.ReleaseEvidence.Tests/Opc.Ua.ReleaseEvidence.Tests.csproj", true)]
        [TestCase("net48", "fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj", false)]
        [TestCase("netstandard2.1", "fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj", false)]
        [TestCase("net8.0", "fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj", true)]
        [TestCase("net9.0", "fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj", true)]
        [TestCase("net10.0", "fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj", true)]
        public async Task MatrixUsesEvaluatedFrameworkApplicabilityAsync(string tfm, string project, bool supported)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File", Path.Combine(root, ".azurepipelines", "get-matrix.ps1"),
                "-BuildRoot", root,
                "-Files", project,
                "-TestHostTfm", tfm, "-LibraryTfm", tfm, "-AllowEmpty").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output.Contains("Not applicable:", StringComparison.Ordinal), Is.EqualTo(!supported));
                Assert.That(
                    output.Contains("jobMatrix;isOutput=true] {}", StringComparison.Ordinal),
                    Is.EqualTo(!supported));
            });
        }

        /// <summary>
        /// Checks that result collection distinguishes actual successful execution from missing or invalid result
        /// files.
        /// </summary>
        [TestCase("missing-trx")]
        [TestCase("zero-trx")]
        [TestCase("ignored-trx")]
        [TestCase("aborted-trx")]
        [TestCase("valid-trx")]
        [TestCase("inconsistent-trx")]
        [TestCase("missing-mtp")]
        [TestCase("valid-mtp")]
        [TestCase("missing-sarif")]
        [TestCase("valid-sarif")]
        [TestCase("failed-sarif")]
        [TestCase("producer-record")]
        public async Task ResultDocumentsProveExecutionAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceResults.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks fuzz-input bucket attribution, output isolation, and handling of missing or empty input sets.
        /// </summary>
        [TestCase("bucket-identity")]
        [TestCase("output-collision")]
        [TestCase("missing-seeds")]
        [TestCase("empty-seeds")]
        [TestCase("empty-regressions")]
        public async Task FuzzInputsKeepTheirBucketIdentityAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceFuzzInputs.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks that replay credit requires observed coverage of the selected targets and their unchanged inputs.
        /// </summary>
        [TestCase("complete")]
        [TestCase("omitted-target")]
        [TestCase("omitted-input")]
        [TestCase("changed-copy")]
        [TestCase("empty-good")]
        [TestCase("empty-regressions")]
        [TestCase("unrelated-skip")]
        public async Task ReplayRequiresObservedTargetInputCoverageAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceReplay.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks that profile collection accepts matching job proofs and rejects missing, duplicate, or misbound
        /// evidence.
        /// </summary>
        [TestCase("complete-security")]
        [TestCase("wrong-source")]
        [TestCase("stale-attempt")]
        [TestCase("forged-na")]
        [TestCase("missing-job")]
        [TestCase("missing-proof")]
        [TestCase("duplicate-job")]
        [TestCase("unrelated-os")]
        [TestCase("unrelated-attempt")]
        [TestCase("wrong-host")]
        [TestCase("full-profile")]
        public async Task ProfileCollectionRejectsUnverifiedCreditAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceCollection.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks that native-image validation rejects apphosts, malformed images, and invalid architecture or exports.
        /// </summary>
        [TestCase("native-image")]
        [TestCase("apphost")]
        [TestCase("wrong-architecture")]
        [TestCase("missing-export")]
        [TestCase("forged-header")]
        [TestCase("forwarded-export")]
        [TestCase("malformed-image")]
        public async Task NativeImageRequiresNativeAotFormatAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceNative.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks that finding dispositions require an authenticated, current review covering the complete alert
        /// population.
        /// </summary>
        [TestCase("signed-review")]
        [TestCase("signed-review-unreported-alert-count")]
        [TestCase("partial-review")]
        [TestCase("partial-alerts")]
        [TestCase("revoked-review")]
        [TestCase("expired-review")]
        [TestCase("source-mismatch")]
        [TestCase("attempt-mismatch")]
        [TestCase("query-mismatch")]
        [TestCase("population-mismatch")]
        [TestCase("unresolved-review")]
        [TestCase("forged-signature")]
        [TestCase("boolean-authentication")]
        [TestCase("missing-review")]
        public async Task FindingReviewRequiresAuthenticatedCompletePopulationAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceDisposition.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks bounded CodeQL evidence production, including extraction, query coverage, source binding, and review.
        /// </summary>
        [TestCase("complete")]
        [TestCase("incomplete-extraction")]
        [TestCase("missing-query-result")]
        [TestCase("changed-source")]
        [TestCase("wrong-analysis-source")]
        [TestCase("review-missing")]
        [TestCase("command-timeout")]
        [Platform("Win")]
        public async Task CodeqlProducerCollectsBoundedActualEvidenceAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceCodeql.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks that native assurance rejects managed substitutes and failed publishing, compilation, or execution.
        /// </summary>
        [TestCase("apphost")]
        [TestCase("publish-failure")]
        [TestCase("compiler-missing")]
        [TestCase("process-crash")]
        [TestCase("historian-apphost")]
        [TestCase("mcp-apphost")]
        [Platform("Win")]
        public async Task NativeProducerRejectsSubstitutionAndFailedExecutionAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceNativeProducer.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks release evidence retrieval against authenticated run identity, artifact integrity, and execution
        /// proofs.
        /// </summary>
        [TestCase("complete")]
        [TestCase("wrong-repository")]
        [TestCase("wrong-sha")]
        [TestCase("wrong-event")]
        [TestCase("wrong-ref")]
        [TestCase("wrong-workflow")]
        [TestCase("wrong-attempt")]
        [TestCase("definition-unverified")]
        [TestCase("push-definition")]
        [TestCase("push-definition-missing")]
        [TestCase("push-definition-contradiction")]
        [TestCase("run-in-progress")]
        [TestCase("missing-current-run")]
        [TestCase("missing-selected-project")]
        [TestCase("stale-artifact")]
        [TestCase("record-old-attempt")]
        [TestCase("record-wrong-source")]
        [TestCase("producer-local-path")]
        [TestCase("zero-result")]
        [TestCase("partial-fuzz-proof")]
        [TestCase("manifest-only-fuzz")]
        [TestCase("forged-na")]
        [TestCase("zip-traversal")]
        [TestCase("hash-mismatch")]
        [TestCase("failed-result")]
        [TestCase("release-branch")]
        [TestCase("native-positive")]
        [TestCase("native-image-mismatch")]
        [TestCase("native-dynamic-code")]
        [TestCase("native-source-mismatch")]
        [TestCase("native-attempt-mismatch")]
        [TestCase("native-missing-report")]
        [TestCase("native-crash")]
        [TestCase("verified-seven")]
        [TestCase("codeql-partial-extraction")]
        [TestCase("codeql-extraction-mismatch")]
        [TestCase("codeql-partial-queries")]
        [TestCase("codeql-missing-config")]
        [TestCase("codeql-source-mismatch")]
        [TestCase("codeql-attempt-mismatch")]
        [TestCase("codeql-missing-review")]
        [TestCase("wait-success")]
        [TestCase("wait-timeout")]
        [TestCase("wait-cancelled")]
        [TestCase("wait-attempt-changed")]
        [TestCase("wait-superseded")]
        public async Task ReleaseRetrievalValidatesAuthenticatedMetadataAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceRetrieval.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        /// <summary>
        /// Checks that workflow discovery includes replay projects affected by corpus, dictionary, or helper changes.
        /// </summary>
        [TestCase("full-discovery")]
        [TestCase("missing-fuzz-project")]
        [TestCase("corpus-change")]
        [TestCase("dictionary-change")]
        [TestCase("helper-change")]
        [TestCase("documentation-only")]
        public async Task ActionsDiscoveryIncludesRelevantReplayProjectsAsync(string scenario)
        {
            string root = FindRepositoryRoot();
            (int exitCode, string output) = await RunAsync(
                root, "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "AssuranceDiscovery.fixture.ps1"),
                "-Scenario", scenario).ConfigureAwait(false);

            Assert.That(exitCode, Is.Zero, output);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Repository root not found.");
        }

        private static async Task<(int ExitCode, string Output)> RunAsync(
            string workingDirectory,
            params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("pwsh")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            return (process.ExitCode,
                await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
        }
    }
}
#endif
