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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Regression tests for the package family/version policy shared between
    /// version.targets (build time) and .azurepipelines/package-version-policy.ps1
    /// + validate-nuget-package-set.ps1 (validation time). See
    /// docs/ReleaseProcess.md for the model these scripts implement.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed partial class PackageVersionPolicyTests
    {
        [Test]
        public async Task TestPreviewPackageIdClassifiesEveryFamilyAndControlIdAsync()
        {
            // One representative library, one Debug variant, and the four
            // additional tool projects the plan calls out, plus IDs that must
            // NOT be treated as preview (the general MCP host and its other
            // libraries, and unrelated core/companion packages).
            (string Id, bool ExpectedPreview)[] cases =
            [
                ("OPCFoundation.NetStandard.Opc.Ua.XRegistry", true),
                ("OPCFoundation.NetStandard.Opc.Ua.XRegistry.Client", true),
                ("OPCFoundation.NetStandard.Opc.Ua.XRegistry.Debug", true),
                ("OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Robotics.Server", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Redundancy.Kubernetes", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Positioning.Client", true),
                ("OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Server", true),
                ("OPCFoundation.NetStandard.Opc.Ua.ISA95.Client", true),
                ("OPCFoundation.NetStandard.Opc.Ua.AI.Inference", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Di.Server", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision", true),
                ("OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector", true),
                ("OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer", true),
                ("OPCFoundation.NetStandard.Opc.Ua.OpenUsd.Connector.Viewer.Debug", true),
                ("OPCFoundation.NetStandard.Opc.Ua.Core", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Types", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Server", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Client", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Gds", false),
                ("OPCFoundation.NetStandard.Opc.Ua.PubSub", false),
                // The general MCP host and its non-family extensions must
                // never be swept in by a loose "starts with Mcp" match.
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Core", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Diagnostics", false),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.PubSub", false),
                // A package that merely starts with the same prefix text
                // must not be matched (family boundary is a dot, not a
                // substring).
                ("OPCFoundation.NetStandard.Opc.Ua.AIToolkit", false),
            ];

            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                $cases = @({{string.Join(",", cases.Select(c => $"'{c.Id}'"))}})
                $results = foreach ($id in $cases) {
                    [pscustomobject]@{ id = $id; isPreview = (Test-PreviewPackageId -PackageId $id) }
                }
                $results | ConvertTo-Json -Depth 3 -AsArray
                """).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.GetArrayLength(), Is.EqualTo(cases.Length));
                for (int i = 0; i < cases.Length; i++)
                {
                    JsonElement entry = result[i];
                    Assert.That(
                        entry.GetProperty("id").GetString(),
                        Is.EqualTo(cases[i].Id));
                    Assert.That(
                        entry.GetProperty("isPreview").GetBoolean(),
                        Is.EqualTo(cases[i].ExpectedPreview),
                        $"Package id '{cases[i].Id}' classified incorrectly.");
                }
            });
        }

        [TestCase("2.0.0-preview.6", "2.0.0-preview.6", Description = "Already preview: unchanged")]
        [TestCase("2.0.0-preview.1.gabc123def0", "2.0.0-preview.1.gabc123def0", Description = "Already preview with commit id: unchanged")]
        [TestCase("2.0.1-preview.3+build5", "2.0.1-preview.3+build5", Description = "Already preview with build metadata: unchanged")]
        [TestCase("2.0.0-rc.1", "2.0.0-preview.rc.1", Description = "Other prerelease label: preview-prefixed")]
        public async Task ConvertToPreviewPackageVersionIsIdempotentForExistingPrereleaseAsync(
            string input,
            string expected)
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{ actual = (ConvertTo-PreviewPackageVersion -Version '{{input}}' -PreviewPackageBuildNumber '999') } |
                    ConvertTo-Json
                """).ConfigureAwait(false);

            Assert.That(result.GetProperty("actual").GetString(), Is.EqualTo(expected));
        }

        [TestCase("2.0.0", "42", "2.0.0-preview.42", Description = "Exact stable: numbered suffix appended")]
        [TestCase("2.0.1", "7", "2.0.1-preview.7", Description = "Different stable base: same mechanism")]
        [TestCase("2.0.0+gabc123def0", "42", "2.0.0-preview.42+gabc123def0", Description = "Build metadata only: suffix inserted before '+'")]
        public async Task ConvertToPreviewPackageVersionSynthesizesNumberedSuffixForStableAsync(
            string input,
            string buildNumber,
            string expected)
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{ actual = (ConvertTo-PreviewPackageVersion -Version '{{input}}' -PreviewPackageBuildNumber '{{buildNumber}}') } |
                    ConvertTo-Json
                """).ConfigureAwait(false);

            Assert.That(result.GetProperty("actual").GetString(), Is.EqualTo(expected));
        }

        [Test]
        public async Task GetExpectedPackageVersionAppliesPolicyOnlyToPreviewFamiliesAsync()
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{
                    preview = (Get-ExpectedPackageVersion -PackageId 'OPCFoundation.NetStandard.Opc.Ua.XRegistry' -BaseVersion '2.0.0')
                    core = (Get-ExpectedPackageVersion -PackageId 'OPCFoundation.NetStandard.Opc.Ua.Core' -BaseVersion '2.0.0')
                    previewDev = (Get-ExpectedPackageVersion -PackageId 'OPCFoundation.NetStandard.Opc.Ua.Di' -BaseVersion '2.0.0-preview.9')
                    coreDev = (Get-ExpectedPackageVersion -PackageId 'OPCFoundation.NetStandard.Opc.Ua.Core' -BaseVersion '2.0.0-preview.9')
                } | ConvertTo-Json
                """).ConfigureAwait(false);

            string previewBuildNumber = await GetPreviewPackageBuildNumberAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(
                    result.GetProperty("preview").GetString(),
                    Is.EqualTo($"2.0.0-preview.{previewBuildNumber}"),
                    "A selected family with a stable base version must get the committed numbered preview suffix.");
                Assert.That(
                    result.GetProperty("core").GetString(),
                    Is.EqualTo("2.0.0"),
                    "An unaffected package must keep the exact stable base version.");
                Assert.That(
                    result.GetProperty("previewDev").GetString(),
                    Is.EqualTo("2.0.0-preview.9"),
                    "A selected family with an already-preview base version reuses it unchanged.");
                Assert.That(
                    result.GetProperty("coreDev").GetString(),
                    Is.EqualTo("2.0.0-preview.9"),
                    "An unaffected package with a preview base version also reuses it unchanged (everyone shares the root version while it is prerelease).");
            });
        }

        [TestCase("2.0.0", true)]
        [TestCase("2.1.5", true)]
        [TestCase("2.0.0-preview.6", false)]
        [TestCase("2.0.0-preview.1.gabc123def0", false)]
        [TestCase("2.0.0+gabc123def0", false)]
        [TestCase("2.0.0-rc.1", false)]
        [TestCase("2.0.0.7", false)]
        [TestCase("2.0", false)]
        [TestCase("2.0.00", false)]
        [TestCase("2.0.0.0-preview.1", false)]
        public async Task TestStablePackageVersionAsync(string version, bool expectedStable)
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{ actual = (Test-StablePackageVersion -Version '{{version}}') } | ConvertTo-Json
                """).ConfigureAwait(false);

            Assert.That(result.GetProperty("actual").GetBoolean(), Is.EqualTo(expectedStable));
        }

        [TestCase("refs/heads/release/2.0", true, Description = "Canonical two-component release line")]
        [TestCase("refs/heads/release/2.1", true, Description = "A later canonical release line")]
        [TestCase("refs/heads/release/10.42", true, Description = "Multi-digit components")]
        [TestCase("refs/heads/release/2.0.0", false, Description = "Retired three-component naming")]
        [TestCase("refs/heads/master", false, Description = "master is never a canonical release branch")]
        [TestCase("refs/heads/release/2.0-hotfix", false, Description = "Non-canonical suffix")]
        [TestCase("refs/heads/release/2", false, Description = "Missing minor component")]
        [TestCase("refs/tags/2.0.0", false, Description = "A tag is not a branch ref")]
        public async Task TestCanonicalReleaseBranchRefAsync(string ruleRef, bool expectedCanonical)
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{ actual = (Test-CanonicalReleaseBranchRef -Ref '{{ruleRef}}') } | ConvertTo-Json
                """).ConfigureAwait(false);

            Assert.That(result.GetProperty("actual").GetBoolean(), Is.EqualTo(expectedCanonical));
        }

        [TestCase("refs/heads/release/2.0", "2.0.0", true)]
        [TestCase("refs/heads/release/2.0", "2.0.17", true)]
        [TestCase("refs/heads/release/2.1", "2.1.0", true)]
        [TestCase("refs/heads/release/2.0", "2.1.0", false)]
        [TestCase("refs/heads/release/2.1", "2.0.9", false)]
        [TestCase("refs/heads/release/2.0.0", "2.0.0", false)]
        [TestCase("refs/heads/master", "2.0.0", false)]
        [TestCase("refs/heads/release/2.0", "2.0.0.7", false)]
        [TestCase("refs/heads/release/2.0", "2.0.0-preview.6", false)]
        public async Task TestCanonicalReleaseBranchForPackageVersionAsync(
            string ruleRef,
            string version,
            bool expectedMatch)
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{ actual = (Test-CanonicalReleaseBranchForPackageVersion -Ref '{{ruleRef}}' -Version '{{version}}') } |
                    ConvertTo-Json
                """).ConfigureAwait(false);

            Assert.That(result.GetProperty("actual").GetBoolean(), Is.EqualTo(expectedMatch));
        }

        [Test]
        public async Task GetPreviewPackageBuildNumberMatchesCommittedPropsFileAsync()
        {
            string repositoryRoot = FindRepositoryRoot();
            string propsPath = Path.Combine(repositoryRoot, "preview-version.props");
            Assert.That(File.Exists(propsPath), Is.True, "preview-version.props must exist at the repository root.");

            string propsContent = await File.ReadAllTextAsync(propsPath).ConfigureAwait(false);
            System.Text.RegularExpressions.Match match = PreviewPackageBuildNumberRegex().Match(propsContent);
            Assert.That(match.Success, Is.True, "preview-version.props must define PreviewPackageBuildNumber.");

            string expected = match.Groups["value"].Value.Trim();
            string actual = await GetPreviewPackageBuildNumberAsync().ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [System.Text.RegularExpressions.GeneratedRegex(
            "<PreviewPackageBuildNumber[^>]*>(?<value>[^<]+)</PreviewPackageBuildNumber>")]
        private static partial System.Text.RegularExpressions.Regex PreviewPackageBuildNumberRegex();

        /// <summary>
        /// version.targets and package-version-policy.ps1 must independently
        /// agree on the exact same set of preview-only package IDs;
        /// otherwise a build could produce a version the validation script
        /// then rejects (or vice versa). This does not re-derive the list
        /// from version.targets (that would just duplicate its own
        /// source-of-truth risk); it pins the specific family/tool coverage
        /// the plan requires, so a change to either file that silently drops
        /// or adds a family is caught here.
        /// </summary>
        [Test]
        public void PreviewFamilyListMatchesDocumentedScope()
        {
            string repositoryRoot = FindRepositoryRoot();
            string versionTargetsPath = Path.Combine(repositoryRoot, "version.targets");
            Assert.That(File.Exists(versionTargetsPath), Is.True);
            string versionTargets = File.ReadAllText(versionTargetsPath);

            string[] expectedFamilyTokens =
            [
                "Opc.Ua.XRegistry",
                "Opc.Ua.WotCon",
                "Opc.Ua.Vision",
                "Opc.Ua.Robotics",
                "Opc.Ua.Redundancy",
                "Opc.Ua.Positioning",
                "Opc.Ua.OpenUsd",
                "Opc.Ua.ISA95",
                "Opc.Ua.AI",
                "Opc.Ua.Di",
                "Opc.Ua.Mcp.Robotics",
                "Opc.Ua.Mcp.Vision",
                "Opc.Ua.OpenUsd.Connector",
                "Opc.Ua.OpenUsd.Connector.Viewer",
            ];

            Assert.Multiple(() =>
            {
                foreach (string token in expectedFamilyTokens)
                {
                    Assert.That(
                        versionTargets.Contains(token, StringComparison.Ordinal),
                        Is.True,
                        $"version.targets no longer mentions '{token}'; keep it and package-version-policy.ps1's " +
                        "Test-PreviewPackageId in sync (see docs/ReleaseProcess.md).");
                }
            });
        }

        private static string PolicyScriptPath { get; } = Path.Combine(
            FindRepositoryRoot(),
            ".azurepipelines",
            "package-version-policy.ps1").Replace('\\', '/');

        private static async Task<string> GetPreviewPackageBuildNumberAsync()
        {
            JsonElement result = await RunPolicyScriptAsync(
                $$"""
                . '{{PolicyScriptPath}}'
                @{ actual = (Get-PreviewPackageBuildNumber) } | ConvertTo-Json
                """).ConfigureAwait(false);
            return result.GetProperty("actual").GetString()!;
        }

        private static async Task<JsonElement> RunPolicyScriptAsync(string script)
        {
            string scriptPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "package-version-policy-fixtures",
                $"{Guid.NewGuid():N}.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            await File.WriteAllTextAsync(scriptPath, script).ConfigureAwait(false);

            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "pwsh";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-File");
                process.StartInfo.ArgumentList.Add(scriptPath);

                Assert.That(process.Start(), Is.True);
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    $"package-version-policy.ps1 script failed:\n{output}\n{error}");

                return JsonDocument.Parse(output).RootElement;
            }
            finally
            {
                File.Delete(scriptPath);
            }
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, "version.targets")))
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
