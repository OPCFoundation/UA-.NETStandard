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
    /// Exercises container evidence collection and publication boundaries through isolated PowerShell fixtures.
    /// </summary>
    [TestFixture]
    public sealed class ContainerEvidencePipelineTests
    {
        /// <summary>
        /// Confirms each fixture enforces artifact identity, platform scope, and explicit incomplete outcomes.
        /// </summary>
        /// <param name="scenario">The isolated evidence scenario.</param>
        [TestCase("valid-pump")]
        [TestCase("valid-dual-platform")]
        [TestCase("record-request")]
        [TestCase("recorded-root-mismatch")]
        [TestCase("wrong-digest")]
        [TestCase("missing-platform")]
        [TestCase("attestation-descriptor")]
        [TestCase("wrong-attestation-subject")]
        [TestCase("private-path-filter")]
        [TestCase("private-field-filter")]
        [TestCase("active-incomplete")]
        [TestCase("required-stable")]
        [TestCase("required-four-part-version")]
        [TestCase("required-context-version")]
        [TestCase("required-preview")]
        [TestCase("baseline-failure")]
        [TestCase("pr-no-publish")]
        [TestCase("catalog-membership")]
        [TestCase("pump-membership")]
        [TestCase("unsupported-spdx")]
        [TestCase("aggregate-observed")]
        [TestCase("path-traversal")]
        public async Task ContainerEvidencePreservesBoundariesAsync(string scenario)
        {
            await RunFixtureAsync("ContainerEvidencePipeline.fixture.ps1", scenario).ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves image selection, registry and platform contracts while sharing the Docker producer.
        /// </summary>
        /// <param name="scenario">The workflow selection or invalid-input scenario.</param>
        [TestCase("master")]
        [TestCase("release")]
        [TestCase("docker-branch")]
        [TestCase("pull-request")]
        [TestCase("manual-pump")]
        [TestCase("fork")]
        [TestCase("invalid-push-ref")]
        [TestCase("invalid-event")]
        [TestCase("missing-group")]
        [TestCase("wrong-producer")]
        [TestCase("missing-dockerfile")]
        [TestCase("duplicate-image")]
        [TestCase("duplicate-cross-group-image")]
        [TestCase("empty-platforms")]
        [TestCase("unsafe-dockerfile")]
        [TestCase("missing-project")]
        [TestCase("missing-project-field")]
        [TestCase("unsafe-project")]
        [TestCase("foreign-project")]
        [TestCase("project-paths")]
        [TestCase("workflow-wiring")]
        public async Task ContainerWorkflowPreservesImageContractsAsync(string scenario)
        {
            await RunFixtureAsync("ContainerWorkflow.fixture.ps1", scenario).ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps every catalog image on its approved SDK and runtime digests with the SDK matching global.json.
        /// </summary>
        [Test]
        public async Task ContainerSdkPinsMatchRepositorySdkAsync()
        {
            await RunFixtureAsync("ContainerWorkflow.fixture.ps1", "sdk-pins").ConfigureAwait(false);
        }

        /// <summary>
        /// Keeps local source builds native and routes each final image through its selected publish stage.
        /// </summary>
        [Test]
        public async Task ContainerDockerStagesUseNativeBuildAndSelectedPayloadAsync()
        {
            await RunFixtureAsync("ContainerWorkflow.fixture.ps1", "docker-stages").ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the native publisher and payload checks with synthetic dotnet output and no real builds.
        /// </summary>
        /// <param name="scenario">The publication, identity, or payload boundary to exercise.</param>
        [TestCase("native-publish")]
        [TestCase("native-publish-failure")]
        [TestCase("native-missing-assembly")]
        [TestCase("native-invalid-version")]
        [TestCase("native-consumer-rerun")]
        [TestCase("native-source-mismatch")]
        [TestCase("native-repository-mismatch")]
        [TestCase("native-run-mismatch")]
        [TestCase("native-attempt-mismatch")]
        [TestCase("native-version-mismatch")]
        [TestCase("native-file-version-mismatch")]
        [TestCase("native-project-mismatch")]
        [TestCase("native-dockerfile-mismatch")]
        [TestCase("native-duplicate-entry")]
        [TestCase("native-file-mismatch")]
        [TestCase("native-file-missing")]
        [TestCase("native-file-extra")]
        public async Task ContainerNativePublicationBindsSourceAndPayloadAsync(string scenario)
        {
            await RunFixtureAsync("ContainerWorkflow.fixture.ps1", scenario).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes the summary gate and rejects failed or skipped publication of selected images.
        /// </summary>
        /// <param name="scenario">The expected or invalid job-result combination.</param>
        [TestCase("summary-success")]
        [TestCase("summary-docs-skipped")]
        [TestCase("summary-selected-publish-skipped")]
        [TestCase("summary-selected-images-skipped")]
        [TestCase("summary-publish-failed")]
        [TestCase("summary-selection-failed")]
        [TestCase("summary-discover-cancelled")]
        [TestCase("summary-invalid-relevance")]
        public async Task ContainerSummaryRequiresSelectedPublicationAsync(string scenario)
        {
            await RunFixtureAsync("ContainerWorkflow.fixture.ps1", scenario).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes branch sanitization and release-line alias guards without registry or network access.
        /// </summary>
        /// <param name="scenario">The branch, version, or release-precedence boundary.</param>
        [TestCase("tag-preview")]
        [TestCase("tag-injection")]
        [TestCase("tag-invalid-version")]
        [TestCase("tag-newest-release")]
        [TestCase("tag-maintenance-release")]
        [TestCase("tag-unknown-release-line")]
        public async Task ContainerTagsPreserveBranchAndReleaseOwnershipAsync(string scenario)
        {
            await RunFixtureAsync("ContainerWorkflow.fixture.ps1", scenario).ConfigureAwait(false);
        }

        private static async Task RunFixtureAsync(string fixture, string scenario)
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            string repositoryRoot = directory?.FullName ??
                throw new DirectoryNotFoundException("Repository root is required for pipeline fixtures.");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("pwsh")
                {
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (string argument in new[]
            {
                "-NoLogo", "-NoProfile", "-File",
                Path.Combine(repositoryRoot, "tests", "Opc.Ua.Tools.Tests", "Fixtures", fixture),
                "-Scenario", scenario
            })
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
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            string output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, output);
        }
    }
}
#endif
