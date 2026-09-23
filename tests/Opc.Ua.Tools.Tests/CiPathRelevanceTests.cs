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
using System.Linq;
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
        /// Anything that is not documentation has to be built. These are the
        /// build inputs an extension allow-list would have missed.
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
        public async Task ChangeIsBuildRelevantAsync(string path)
        {
            Assert.That(await EvaluateAsync(path).ConfigureAwait(false), Is.True);
        }

        /// <summary>
        /// The only skippable class is documentation: Markdown anywhere, and the
        /// docs/ tree, which holds nothing but Markdown and images.
        /// </summary>
        [TestCase("README.md", TestName = "RootMarkdown")]
        [TestCase("docs/ReleaseProcess.md", TestName = "DocsMarkdown")]
        [TestCase("docs/images/architecture.png", TestName = "DocsImage")]
        [TestCase("src/Opc.Ua.Client/README.md", TestName = "NestedMarkdown")]
        [TestCase("CONTRIBUTING.MD", TestName = "UppercaseExtension")]
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
        [Test]
        public async Task WorkflowDelegatesTheDecisionToThisScriptAsync()
        {
            string workflow = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "buildandtest.yml");
            string source = await File.ReadAllTextAsync(workflow).ConfigureAwait(false);

            Assert.That(source, Does.Contain("./.github/scripts/get-path-relevance.ps1 -ChangedFile $changed"));
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
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(root, ".github", "scripts", "get-path-relevance.ps1"));
            if (changedFiles.Length != 0)
            {
                // 'pwsh -File' binds one argument per parameter, so an array has
                // to arrive as a single comma-separated value. Omitting the
                // parameter entirely is how an empty diff is expressed.
                process.StartInfo.ArgumentList.Add("-ChangedFile");
                process.StartInfo.ArgumentList.Add(string.Join(',', changedFiles));
            }

            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = PowerShellScriptOutput.Normalize(await standardOutput.ConfigureAwait(false));
            string error = await standardError.ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, output + error);

            string verdict = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
            return bool.Parse(verdict);
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
