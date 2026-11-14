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

#if NET10_0_OR_GREATER
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Contract tests for .github/scripts/get-path-relevance.ps1, which decides
    /// whether a pull request's changes are worth building.
    ///
    /// This rule fails dangerously in one direction only. Calling a real change
    /// irrelevant skips the build, test and NativeAOT jobs while the required
    /// 'build-and-test summary' still reports success, so a broken change merges
    /// behind a green check. Calling an irrelevant change relevant only wastes
    /// runner time. The cases below therefore concentrate on inputs that are
    /// easy to forget: generator inputs, test fixtures and build configuration.
    /// </summary>
    [TestFixture]
    public sealed class CiPathRelevanceTests
    {
        /// <summary>
        /// Paths outside the designated documentation locations require a build,
        /// including Markdown files consumed by analyzers and packages.
        /// </summary>
        [TestCase("src/Opc.Ua.Types/BuiltIn/NodeId.cs", TestName = "Source")]
        [TestCase("src/Opc.Ua.WotCon/Design/Opc.Ua.WotCon.NodeSet2.xml", TestName = "GeneratorNodeSet")]
        [TestCase("src/Opc.Ua.WotCon/Design/Opc.Ua.WotCon.NodeSet2.csv", TestName = "GeneratorIdentifiers")]
        [TestCase("tests/Opc.Ua.Types.Tests/Wot/Assets/example.jsonld", TestName = "TestFixture")]
        [TestCase(".editorconfig", TestName = "EditorConfigEnforcedAtBuildTime")]
        [TestCase("Directory.Packages.props", TestName = "CentralPackageVersions")]
        [TestCase("version.json", TestName = "PackageVersion")]
        [TestCase(".github/workflows/images.yml", TestName = "Workflow")]
        [TestCase(".github/scripts/get-ci-matrix.ps1", TestName = "MatrixExpander")]
        [TestCase("fuzzing/Common/Fuzz.Tests/FuzzTargetTestsBase.cs", TestName = "Fuzz")]
        [TestCase("docs.txt", TestName = "FileNamedLikeTheDocsDirectory")]
        [TestCase("src/readme.markdown", TestName = "NotMarkdownByExtension")]
        [TestCase("tools/Opc.Ua.MigrationAnalyzer/AnalyzerReleases.Unshipped.md", TestName = "AnalyzerReleaseInput")]
        [TestCase(@"tools\Opc.Ua.SourceGeneration\AnalyzerReleases.Shipped.md", TestName = "AnalyzerInputWindowsPath")]
        [TestCase("src/Opc.Ua.Client/NugetREADME.md", TestName = "PackageReadme")]
        [TestCase("tools/Opc.Ua.Mcp/McpREADME.md", TestName = "ToolPackageReadme")]
        [TestCase("nuget/MetaPackageREADME.md", TestName = "MetaPackageReadme")]
        [TestCase("src/Opc.Ua.Client/README.md", TestName = "NestedMarkdown")]
        [TestCase("notes.md", TestName = "UnrecognizedRootMarkdown")]
        [TestCase("readme.md", TestName = "DistinctRootDocumentCase")]
        [TestCase("DOCS/Generated.cs", TestName = "DistinctDocumentationDirectoryCase")]
        [TestCase(" ", TestName = "WhitespaceOnlyFileName")]
        public async Task ChangeIsBuildRelevantAsync(string path)
        {
            Assert.That(await EvaluateAsync(path).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// The documentation tree and the designated root documents are skippable.
        /// </summary>
        [TestCase("README.md", TestName = "RootMarkdown")]
        [TestCase("docs/ReleaseProcess.md", TestName = "DocsMarkdown")]
        [TestCase("docs/images/architecture.png", TestName = "DocsImage")]
        [TestCase("CONTRIBUTING.md", TestName = "RootContributionGuide")]
        [TestCase("SECURITY.md", TestName = "RootSecurityPolicy")]
        [TestCase(@"docs\images\architecture.png", TestName = "DocsWindowsPath")]
        public async Task ChangeIsSkippableAsync(string path)
        {
            Assert.That(await EvaluateAsync(path).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// One build-relevant file among documentation changes still has to
        /// build; the rule stops at the first file that matters.
        /// </summary>
        [Test]
        public async Task OneRelevantFileAmongDocumentationStillBuildsAsync()
        {
            bool relevant = await EvaluateAsync("README.md", "docs/DeveloperGuide.md", "src/Opc.Ua.Types/Guid.cs")
                .ConfigureAwait(false);

            Assert.That(relevant, Is.True);
        }

        /// <summary>
        /// A build-relevant path remains relevant regardless of its position among documentation paths.
        /// </summary>
        [TestCase("src/Opc.Ua.Types/Guid.cs", "README.md")]
        [TestCase("README.md", "src/Opc.Ua.Types/Guid.cs")]
        [TestCase("src/it's,code.cs", "README.md")]
        [TestCase("README.md", "src/Opc.Ua.Client/NugetREADME.md")]
        [TestCase("nuget/MetaPackageREADME.md", "docs/Profiles.md")]
        public async Task RelevantFileWithDocumentationStillBuildsAsync(string first, string second)
        {
            Assert.That(await EvaluateAsync(first, second).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// Multiple documentation paths do not require a build.
        /// </summary>
        [Test]
        public async Task MultipleDocumentationFilesAreSkippableAsync()
        {
            Assert.That(await EvaluateAsync("README.md", "docs/images/architecture.png").ConfigureAwait(false),
                Is.False);
        }

        /// <summary>
        /// An empty diff changes nothing, so there is nothing to rebuild.
        /// </summary>
        [Test]
        public async Task EmptyChangeSetIsNotRelevantAsync()
        {
            Assert.That(await EvaluateAsync().ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// The workflow has to call this script rather than carry a second copy
        /// of the rule, because only this one is tested.
        /// </summary>
        [TestCase("buildandtest.yml")]
        [TestCase("nuget-publish.yml")]
        public async Task WorkflowDelegatesTheDecisionToThisScriptAsync(string workflowName)
        {
            string workflow = Path.Combine(FindRepositoryRoot(), ".github", "workflows", workflowName);
            string source = await File.ReadAllTextAsync(workflow).ConfigureAwait(false);

            Assert.That(source, Does.Contain("./.github/scripts/get-path-relevance.ps1 -ChangedFile $changed"));
        }

        /// <summary>
        /// Renames report the old and new paths so removing a build input cannot skip the relevant jobs.
        /// </summary>
        [TestCase("buildandtest.yml")]
        [TestCase("nuget-publish.yml")]
        [TestCase("docker-image.yml")]
        public async Task WorkflowIncludesBothPathsOfRenamesAsync(string workflowName)
        {
            string workflow = Path.Combine(FindRepositoryRoot(), ".github", "workflows", workflowName);
            string source = await File.ReadAllTextAsync(workflow).ConfigureAwait(false);

            Assert.That(source, Does.Contain("git diff --name-only --no-renames"));
        }

        /// <summary>
        /// Runs the build, package, and image relevance steps with available and unavailable Git comparisons.
        /// </summary>
        [Test]
        public async Task WorkflowRelevanceHandlesGitResultsAsync()
        {
            string root = FindRepositoryRoot();
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(
                root, "tests", "Opc.Ua.Tools.Tests", "TestCiWorkflowRelevance.ps1"));

            (int exitCode, string output, string error) = await RunProcessAsync(process).ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero, output + error);
        }

        /// <summary>
        /// An unavailable base ref must fail the gate rather than look like an
        /// empty, documentation-only diff. Both fixed-name required summaries
        /// depend on these internal relevance checks.
        /// </summary>
        [TestCase("buildandtest.yml", "$relevantChanges = [bool]")]
        [TestCase("docker-image.yml", "$relevantChanges = $false")]
        public async Task WorkflowFailsClosedWhenGitDiffFailsAsync(string workflowName, string relevanceDecision)
        {
            string workflow = Path.Combine(FindRepositoryRoot(), ".github", "workflows", workflowName);
            string source = await File.ReadAllTextAsync(workflow).ConfigureAwait(false);
            int diff = source.IndexOf("$changed = @(& git diff --name-only", StringComparison.Ordinal);
            int exitCheck = source.IndexOf("if ($LASTEXITCODE -ne 0)", diff, StringComparison.Ordinal);
            int decision = source.IndexOf(relevanceDecision, diff, StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(diff, Is.GreaterThanOrEqualTo(0));
                Assert.That(exitCheck, Is.GreaterThan(diff), "git diff must be checked immediately.");
                Assert.That(
                    decision,
                    Is.GreaterThan(exitCheck),
                    "Relevance must not be evaluated after a failed git diff.");
                Assert.That(
                    source[exitCheck..decision],
                    Does.Contain("throw"),
                    "A failed diff must fail the gate rather than become an empty change set.");
            });
        }

        private static async Task<bool> EvaluateAsync(params string[] changedFiles)
        {
            string root = FindRepositoryRoot();
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.Environment["OPCUA_TEST_CHANGED_FILES"] = JsonSerializer.Serialize(changedFiles);
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(
                "& ./.github/scripts/get-path-relevance.ps1 -ChangedFile " +
                "@(ConvertFrom-Json -InputObject $env:OPCUA_TEST_CHANGED_FILES)");

            (int exitCode, string output, string error) = await RunProcessAsync(process).ConfigureAwait(false);
            Assert.That(exitCode, Is.Zero, output + error);

            string verdict = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
            return bool.Parse(verdict);
        }

        private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(Process process)
        {
            Assert.That(process.Start(), Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                }
            }

            return (
                process.ExitCode,
                PowerShellScriptOutput.Normalize(await standardOutput.ConfigureAwait(false)),
                await standardError.ConfigureAwait(false));
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".github", "scripts", "get-path-relevance.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }
    }
}
#endif
