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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Regression tests for .azurepipelines/validate-nuget-package-set.ps1:
    /// the script nuget-publish.yml and release.yml both rely on to accept
    /// only an intentional mix of a stable (or preview) root version and the
    /// numbered preview-only package families. See docs/ReleaseProcess.md.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class NuGetPackageSetValidationTests
    {
        private static readonly string[] s_autoIdPackages =
        [
            "OPCFoundation.NetStandard.Opc.Ua.AutoID",
            "OPCFoundation.NetStandard.Opc.Ua.AutoID.Client",
            "OPCFoundation.NetStandard.Opc.Ua.AutoID.Machinery.Server",
            "OPCFoundation.NetStandard.Opc.Ua.AutoID.Server",
            "OPCFoundation.NetStandard.Opc.Ua.Mcp.AutoID"
        ];

        /// <summary>
        /// The approved suite adds exactly twelve package identities and never a separate WMTP package.
        /// </summary>
        [Test]
        public async Task ExpectedPackageInventoryContainsTheTwelveCompanionSuitePackagesAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "expected-packages.txt");
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            string[] prefixes =
            [
                "OPCFoundation.NetStandard.Opc.Ua.MachineTool",
                "OPCFoundation.NetStandard.Opc.Ua.GMS",
                "OPCFoundation.NetStandard.Opc.Ua.CranesHoists",
                "OPCFoundation.NetStandard.Opc.Ua.WMTP",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.MachineTool",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.GMS",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.CranesHoists",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.WMTP"
            ];
            string[] actual =
            [
                .. lines.Select(line => line.Trim()).Where(
                    id => prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal)))
            ];
            string[] expected =
            [
                "OPCFoundation.NetStandard.Opc.Ua.MachineTool",
                "OPCFoundation.NetStandard.Opc.Ua.MachineTool.Client",
                "OPCFoundation.NetStandard.Opc.Ua.MachineTool.Server",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.MachineTool",
                "OPCFoundation.NetStandard.Opc.Ua.GMS",
                "OPCFoundation.NetStandard.Opc.Ua.GMS.Client",
                "OPCFoundation.NetStandard.Opc.Ua.GMS.Server",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.GMS",
                "OPCFoundation.NetStandard.Opc.Ua.CranesHoists",
                "OPCFoundation.NetStandard.Opc.Ua.CranesHoists.Client",
                "OPCFoundation.NetStandard.Opc.Ua.CranesHoists.Server",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.CranesHoists"
            ];

            Assert.That(actual, Is.EquivalentTo(expected));
            Assert.That(actual, Has.Length.EqualTo(12));
        }

        /// <summary>
        /// AutoID ships the model, server, client, optional Machinery integration and MCP packages.
        /// </summary>
        [Test]
        public async Task ExpectedPackageInventoryContainsTheFiveAutoIdPackagesAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "expected-packages.txt");
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            string[] actual =
            [
                .. lines.Select(line => line.Trim()).Where(
                    id => id.Contains("AutoID", StringComparison.Ordinal))
            ];

            Assert.That(
                actual,
                Is.EquivalentTo(s_autoIdPackages));
        }

        /// <summary>
        /// Requires the release inventory to include the intended Vision package boundaries.
        /// </summary>
        [Test]
        public async Task ExpectedPackageInventoryIncludesTheCompleteVisionFamilyAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "expected-packages.txt");
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            string[] actual =
            [
                .. lines.Select(line => line.Trim()).Where(
                    id =>
                        id is "OPCFoundation.NetStandard.Opc.Ua.Vision" or
                            "OPCFoundation.NetStandard.Opc.Ua.MachineVision" or
                            "OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision" ||
                        id.StartsWith("OPCFoundation.NetStandard.Opc.Ua.Vision.", StringComparison.Ordinal) ||
                        id.StartsWith("OPCFoundation.NetStandard.Opc.Ua.MachineVision.", StringComparison.Ordinal))
            ];
            string[] expected =
            [
                "OPCFoundation.NetStandard.Opc.Ua.Vision",
                "OPCFoundation.NetStandard.Opc.Ua.Vision.Client",
                "OPCFoundation.NetStandard.Opc.Ua.Vision.Server",
                "OPCFoundation.NetStandard.Opc.Ua.MachineVision",
                "OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client",
                "OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server",
                "OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd",
                "OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision"
            ];

            Assert.That(actual, Is.EquivalentTo(expected));
        }

        /// <summary>
        /// The managed desktop tool ships its selector and every supported runtime payload.
        /// </summary>
        [Test]
        public async Task ExpectedPackageInventoryIncludesTheCompleteLensToolAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "expected-packages.txt");
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            const string prefix = "OPCFoundation.NetStandard.Opc.Ua.Lens";
            string[] actual = [.. lines.Select(line => line.Trim())
                .Where(id => id == prefix || id.StartsWith(prefix + ".", StringComparison.Ordinal))];
            string[] expected = [prefix, prefix + ".win-x64", prefix + ".linux-x64", prefix + ".osx-arm64"];

            Assert.That(actual, Is.EquivalentTo(expected));
            XDocument project = XDocument.Load(
                Path.Combine(FindRepositoryRoot(), "tools", "Opc.Ua.Lens", "Opc.Ua.Lens.csproj"));
            string[] runtimeIdentifiers = project.Descendants("ToolPackageRuntimeIdentifiers").Single().Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(project.Descendants("RuntimeIdentifiers").Single().Value,
                Is.EqualTo("$(ToolPackageRuntimeIdentifiers)"));
            Assert.That(expected.Skip(1), Is.EquivalentTo(runtimeIdentifiers.Select(rid => prefix + "." + rid)));
        }

        /// <summary>
        /// Each plastics/rubber family ships exactly its model, client and server packages.
        /// </summary>
        [TestCase("Extrusion")]
        [TestCase("PlasticsRubber")]
        public async Task ExpectedPackageInventoryIncludesOnlyModelClientAndServerAsync(string family)
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "expected-packages.txt");
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            string prefix = "OPCFoundation.NetStandard.Opc.Ua." + family;
            string[] actual = [.. lines.Select(line => line.Trim())
                .Where(id => id == prefix || id.StartsWith(prefix + ".", StringComparison.Ordinal))];

            Assert.That(actual, Is.EquivalentTo(new[] { prefix, prefix + ".Client", prefix + ".Server" }));
        }

        /// <summary>
        /// Granulate-drying functionality ships in the PlasticsRubber family, not standalone packages.
        /// </summary>
        [Test]
        public async Task ExpectedPackageInventoryHasNoStandaloneGranulateDryingPackagesAsync()
        {
            string path = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "expected-packages.txt");
            string[] lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);

            Assert.That(
                lines.Any(
                    line => line.Trim().StartsWith(
                        "OPCFoundation.NetStandard.Opc.Ua.GranulateDrying",
                        StringComparison.Ordinal)),
                Is.False);
        }

        [Test]
        public async Task AcceptsAUniformPreviewPackageSetAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0-preview.1.gabc123def0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", "2.0.0-preview.1.gabc123def0");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Zero, result.Output);
                    Assert.That(result.Manifest!.Value.GetProperty("channel").GetString(), Is.EqualTo("preview"));
                    Assert.That(
                        result.Manifest.Value.GetProperty("basePackageVersion").GetString(),
                        Is.EqualTo("2.0.0-preview.1.gabc123def0"));
                });
        }

        [Test]
        public async Task AcceptsAStableCoreWithNumberedPreviewFamilyReleaseSetAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.0").ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Zero, result.Output);
                    Assert.That(result.Manifest!.Value.GetProperty("channel").GetString(), Is.EqualTo("stable"));
                    string[] actualVersions = [.. result.Manifest.Value.GetProperty("packageVersions").EnumerateArray()
                        .Select(e => e.GetString()!)
                        .OrderBy(v => v, StringComparer.Ordinal)];
                    string[] expectedVersions = ["2.0.0", PreviewFamilyVersion];
                    Assert.That(actualVersions, Is.EqualTo(expectedVersions.OrderBy(v => v, StringComparer.Ordinal)));
                });
        }

        /// <summary>
        /// Industrial, WoT, xRegistry and Vision MCP modules follow a stable root; Robotics remains preview.
        /// </summary>
        [TestCase("")]
        [TestCase(".Debug")]
        public async Task AcceptsStableIndustrialMcpPackagesAsync(string suffix)
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core" + suffix, "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Mcp.Robotics" + suffix, PreviewFamilyVersion);
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision" + suffix, "2.0.0");
            string[] families =
                ["AMB", "Machinery", "Scales", "Pumps", "Di", "ISA95", "Positioning", "WotCon", "XRegistry"];
            foreach (string family in families)
            {
                fixture.AddPackage($"OPCFoundation.NetStandard.Opc.Ua.Mcp.{family}{suffix}", "2.0.0");
            }

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.0").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Manifest!.Value.GetProperty("channel").GetString(), Is.EqualTo("stable"));
            Assert.That(result.Manifest.Value.GetProperty("basePackageVersion").GetString(), Is.EqualTo("2.0.0"));
        }

        [Test]
        public async Task AcceptsADebugOnlyStableReleaseSetAnchoredOnCoreDebugAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry.Debug", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.0").ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(
                        result.ExitCode,
                        Is.Zero,
                        "A Debug-configuration-only package set must anchor on Core.Debug, not fail looking for plain Core.\n" +
                        result.Output);
                    Assert.That(result.Manifest!.Value.GetProperty("debugPackageCount").GetInt32(), Is.EqualTo(2));
                });
        }

        [Test]
        public async Task RejectsASelectedFamilyPackageThatIsAccidentallyStableAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("do not match the preview policy"));
                    Assert.That(result.Output, Does.Contain("OPCFoundation.NetStandard.Opc.Ua.XRegistry=2.0.0"));
                });
        }

        [Test]
        public async Task RejectsAnUnaffectedPackageThatDoesNotMatchTheRootVersionAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Server", "1.9.9");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("do not match the preview policy"));
                });
        }

        [Test]
        public async Task RejectsAMissingCoreAnchorPackageAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("Core (or .Core.Debug)"));
                });
        }

        [Test]
        public async Task AcceptsBothCoreAndCoreDebugTogetherWhenTheyAgreeOnVersionAsync()
        {
            // nuget-publish.yml's "publish" job aggregates the Release and
            // Debug matrix legs into one directory before validating and
            // promoting them, so Core and Core.Debug legitimately coexist
            // there - this must not be rejected as long as they agree.
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Zero, result.Output);
                    Assert.That(
                        result.Manifest!.Value.GetProperty("basePackageVersion").GetString(),
                        Is.EqualTo("2.0.0"));
                });
        }

        [Test]
        public async Task RejectsCoreAndCoreDebugDisagreeingOnVersionAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.1");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("disagree on the package version"));
                });
        }

        [Test]
        public async Task RejectsDuplicatePackageIdentitiesAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0", fileNameSuffix: "-dup");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("Duplicate package ID/version"));
                });
        }

        [Test]
        public async Task RejectsAnUnexpectedExpectedVersionMismatchAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.1").ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("Expected base package version '2.0.1'"));
                });
        }

        [Test]
        public async Task RequireDebugRejectsAReleaseOnlySetAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(requireDebug: true).ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("does not contain any .Debug package IDs"));
                });
        }

        [Test]
        public async Task RequireDebugAcceptsACombinedReleaseAndDebugSetAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry.Debug", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(requireDebug: true).ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Zero, result.Output);
                    Assert.That(result.Manifest!.Value.GetProperty("debugPackageCount").GetInt32(), Is.EqualTo(2));
                    Assert.That(result.Manifest.Value.GetProperty("packageCount").GetInt32(), Is.EqualTo(4));
                });
        }

        /// <summary>
        /// Counts a RID-specific Debug tool payload as Debug in a mixed package set.
        /// </summary>
        [TestCase("win-x64")]
        [TestCase("linux-x64")]
        [TestCase("osx-arm64")]
        public async Task RequireDebugAcceptsARuntimeSpecificDebugToolPayloadAsync(string runtimeIdentifier)
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage($"OPCFoundation.NetStandard.Opc.Ua.Lens.Debug.{runtimeIdentifier}", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync(requireDebug: true).ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Zero, result.Output);
                    Assert.That(result.Manifest!.Value.GetProperty("debugPackageCount").GetInt32(), Is.EqualTo(1));
                    Assert.That(result.Manifest.Value.GetProperty("packageCount").GetInt32(), Is.EqualTo(2));
                });
        }

        /// <summary>
        /// Does not let a RID-specific Debug payload masquerade as a Release package.
        /// </summary>
        [TestCase("win-x64")]
        [TestCase("linux-x64")]
        [TestCase("osx-arm64")]
        public async Task RequireDebugRejectsADebugOnlySetWithRuntimeToolPayloadAsync(string runtimeIdentifier)
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");
            fixture.AddPackage($"OPCFoundation.NetStandard.Opc.Ua.Lens.Debug.{runtimeIdentifier}", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync(requireDebug: true).ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("does not contain any Release package IDs"));
                });
        }

        [Test]
        public async Task RejectsAnOrphanedSymbolPackageAsync()
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            // A .snupkg whose corresponding .nupkg never exists.
            fixture.AddOrphanedSymbolPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.Not.Zero);
                    Assert.That(result.Output, Does.Contain("Symbol packages without matching packages"));
                });
        }

        private readonly record struct ValidationResult(int ExitCode, string Output, JsonElement? Manifest);

        /// <summary>
        /// The version a preview-only family carries in a stable "2.0.0"
        /// package set, composed from the committed PreviewPackageBuildNumber
        /// rather than hardcoded. preview-version.props mandates re-picking
        /// that number for every new stable base version, and a literal would
        /// silently repurpose the negative tests below: the preview-policy
        /// check runs before the RequireDebug and orphaned-symbol checks, so
        /// they would keep throwing - just for the wrong reason.
        /// </summary>
        private static string PreviewFamilyVersion { get; } =
            $"2.0.0-preview.{ReadPreviewPackageBuildNumber()}";

        private static string ReadPreviewPackageBuildNumber()
        {
            string propsPath = Path.Combine(FindRepositoryRoot(), "preview-version.props");
            XElement? element = XDocument.Load(propsPath)
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "PreviewPackageBuildNumber");
            if (element is null || string.IsNullOrWhiteSpace(element.Value))
            {
                throw new InvalidOperationException(
                    $"'{propsPath}' does not define a PreviewPackageBuildNumber value.");
            }
            return element.Value.Trim();
        }

        private sealed class PackageSetFixture : IDisposable
        {
            private readonly string _directory;
            private readonly List<string> _addedFiles = [];

            private PackageSetFixture(string directory)
            {
                _directory = directory;
            }

            public static PackageSetFixture Create()
            {
                string directory = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "nuget-package-set-fixtures",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                return new PackageSetFixture(directory);
            }

            public void AddPackage(string id, string version, string fileNameSuffix = "")
            {
                string fileName = $"{id}{fileNameSuffix}.{version}.nupkg";
                string path = Path.Combine(_directory, fileName);
                WriteNupkg(path, id, version);
                _addedFiles.Add(path);
            }

            public void AddOrphanedSymbolPackage(string id, string version)
            {
                string fileName = $"{id}.{version}.snupkg";
                string path = Path.Combine(_directory, fileName);
                WriteNupkg(path, id, version);
                _addedFiles.Add(path);
            }

            private static void WriteNupkg(string path, string id, string version)
            {
                using FileStream stream = File.Create(path);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                ZipArchiveEntry entry = archive.CreateEntry($"{id}.nuspec");
                using StreamWriter writer = new(entry.Open());
                writer.Write(
                    $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
                      <metadata>
                        <id>{id}</id>
                        <version>{version}</version>
                      </metadata>
                    </package>
                    """);
            }

            public async Task<ValidationResult> ValidateAsync(
                string? expectedVersion = null,
                bool requireDebug = false)
            {
                string manifestPath = Path.Combine(_directory, "manifest.json");
                string scriptPath = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "validate-nuget-package-set.ps1");

                using var process = new Process();
                process.StartInfo.FileName = "pwsh";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
                process.StartInfo.ArgumentList.Add("-NoProfile");
                process.StartInfo.ArgumentList.Add("-File");
                process.StartInfo.ArgumentList.Add(scriptPath);
                process.StartInfo.ArgumentList.Add("-PackageDirectory");
                process.StartInfo.ArgumentList.Add(_directory);
                process.StartInfo.ArgumentList.Add("-ManifestPath");
                process.StartInfo.ArgumentList.Add(manifestPath);
                if (!string.IsNullOrEmpty(expectedVersion))
                {
                    process.StartInfo.ArgumentList.Add("-ExpectedVersion");
                    process.StartInfo.ArgumentList.Add(expectedVersion);
                }
                if (requireDebug)
                {
                    process.StartInfo.ArgumentList.Add("-RequireDebug");
                }

                Assert.That(process.Start(), Is.True);
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                string combined = PowerShellScriptOutput.Normalize(output + error);

                JsonElement? manifest = null;
                if (File.Exists(manifestPath))
                {
                    string manifestJson = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                    manifest = JsonDocument.Parse(manifestJson).RootElement;
                }

                return new ValidationResult(process.ExitCode, combined, manifest);
            }

            public void Dispose()
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".azurepipelines", "validate-nuget-package-set.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        [Test]
        public async Task AcceptsPromotedVisionAtStableRootAsync()
        {
            (string Id, string Version)[] packages =
            [
                ("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Client", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Client.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Server", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Server.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd", PreviewFamilyVersion),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Debug", PreviewFamilyVersion)
            ];
            using var fixture = PackageSetFixture.Create();
            foreach ((string id, string version) in packages)
            {
                fixture.AddPackage(id, version);
            }

            ValidationResult result = await fixture.ValidateAsync(
                expectedVersion: "2.0.0",
                requireDebug: true).ConfigureAwait(false);

            AssertStableVisionManifest(result, packages, expectedPackageCount: 17, expectedDebugCount: 8);
        }

        [Test]
        public async Task AcceptsRetainedVisionOpenUsdDescendantsAsync()
        {
            (string Id, string Version)[] packages =
            [
                ("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Child", PreviewFamilyVersion),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Child.Debug", PreviewFamilyVersion)
            ];
            using var fixture = PackageSetFixture.Create();
            foreach ((string id, string version) in packages)
            {
                fixture.AddPackage(id, version);
            }

            ValidationResult result = await fixture.ValidateAsync(
                expectedVersion: "2.0.0",
                requireDebug: true).ConfigureAwait(false);

            AssertStableVisionManifest(result, packages, expectedPackageCount: 3, expectedDebugCount: 1);
        }

        [Test]
        public async Task AcceptsPromotedVisionWithDebugCoreAnchorAsync()
        {
            (string Id, string Version)[] packages =
            [
                ("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Client.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.Server.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision.Debug", "2.0.0"),
                ("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Debug", PreviewFamilyVersion)
            ];
            using var fixture = PackageSetFixture.Create();
            foreach ((string id, string version) in packages)
            {
                fixture.AddPackage(id, version);
            }

            // RequireDebug requires a mixed Release/Debug set, whereas this
            // scenario deliberately proves the supported Debug-only anchor.
            ValidationResult result = await fixture.ValidateAsync(
                expectedVersion: "2.0.0").ConfigureAwait(false);

            AssertStableVisionManifest(result, packages, expectedPackageCount: 9, expectedDebugCount: 9);
        }

        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.Client")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.Client.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.Server")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.Server.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.MachineVision")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision.Debug")]
        public async Task RejectsForcedPreviewForPromotedVisionAtStableRootAsync(string packageId)
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage(packageId, PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(
                expectedVersion: "2.0.0").ConfigureAwait(false);

            AssertVisionPolicyRejection(result, packageId, PreviewFamilyVersion, "2.0.0");
        }

        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Debug")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Child")]
        [TestCase("OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd.Child.Debug")]
        public async Task RejectsStableVisionOpenUsdAtStableRootAsync(string packageId)
        {
            using var fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage(packageId, "2.0.0");

            ValidationResult result = await fixture.ValidateAsync(
                expectedVersion: "2.0.0").ConfigureAwait(false);

            AssertVisionPolicyRejection(result, packageId, "2.0.0", PreviewFamilyVersion);
        }

        private static void AssertStableVisionManifest(
            ValidationResult result,
            (string Id, string Version)[] expectedPackages,
            int expectedPackageCount,
            int expectedDebugCount)
        {
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Manifest.HasValue, Is.True, "Successful validation must write the manifest.");
            JsonElement manifest = result.Manifest!.Value;
            (string? Id, string? Version, string? Type, string? File)[] archives =
                [.. manifest.GetProperty("archives").EnumerateArray().Select(
                    archive => (
                        archive.GetProperty("id").GetString(),
                        archive.GetProperty("version").GetString(),
                        archive.GetProperty("type").GetString(),
                        archive.GetProperty("file").GetString()))];

            Assert.Multiple(
                () =>
                {
                    Assert.That(manifest.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
                    Assert.That(manifest.GetProperty("basePackageVersion").GetString(), Is.EqualTo("2.0.0"));
                    Assert.That(manifest.GetProperty("channel").GetString(), Is.EqualTo("stable"));
                    Assert.That(manifest.GetProperty("packageCount").GetInt32(), Is.EqualTo(expectedPackageCount));
                    Assert.That(manifest.GetProperty("debugPackageCount").GetInt32(), Is.EqualTo(expectedDebugCount));
                    Assert.That(manifest.GetProperty("symbolPackageCount").GetInt32(), Is.Zero);
                    Assert.That(
                        manifest.GetProperty("packageVersions").EnumerateArray().Select(version => version.GetString()),
                        Is.EquivalentTo(["2.0.0", PreviewFamilyVersion]));
                    Assert.That(archives, Has.Length.EqualTo(expectedPackageCount));
                    Assert.That(archives, Is.Unique);
                    Assert.That(
                        archives,
                        Is.EquivalentTo(
                            expectedPackages.Select(
                                package => (
                                    package.Id,
                                    package.Version,
                                    "package",
                                    $"{package.Id}.{package.Version}.nupkg"))));
                });
        }

        private static void AssertVisionPolicyRejection(
            ValidationResult result,
            string packageId,
            string actualVersion,
            string expectedVersion)
        {
            Assert.Multiple(
                () =>
                {
                    Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
                    Assert.That(
                        result.Output,
                        Does.Contain(
                            "The package set contains versions that do not match the preview policy: " +
                            $"{packageId}={actualVersion} (expected {expectedVersion})."));
                    Assert.That(result.Manifest, Is.Null, "A rejected package set must not produce a manifest.");
                });
        }
    }
}
#endif
