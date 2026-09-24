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
    /// Regression tests for .azurepipelines/nuget/validate-package-set.ps1:
    /// the script nuget-publish.yml and release.yml both rely on to accept
    /// only an intentional mix of a stable (or preview) root version and the
    /// numbered preview-only package families. See docs/ReleaseProcess.md.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class NuGetPackageSetValidationTests
    {
        [Test]
        public async Task AcceptsAUniformPreviewPackageSetAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0-preview.1.gabc123def0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", "2.0.0-preview.1.gabc123def0");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
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
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.0").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Manifest!.Value.GetProperty("channel").GetString(), Is.EqualTo("stable"));
                string[] actualVersions = result.Manifest.Value.GetProperty("packageVersions").EnumerateArray()
                    .Select(e => e.GetString()!)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToArray();
                string[] expectedVersions = ["2.0.0", PreviewFamilyVersion];
                Assert.That(actualVersions, Is.EqualTo(expectedVersions.OrderBy(v => v, StringComparer.Ordinal)));
            });
        }

        [Test]
        public async Task AcceptsADebugOnlyStableReleaseSetAnchoredOnCoreDebugAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry.Debug", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.0").ConfigureAwait(false);

            Assert.Multiple(() =>
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
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("do not match the preview policy"));
                Assert.That(result.Output, Does.Contain("OPCFoundation.NetStandard.Opc.Ua.XRegistry=2.0.0"));
            });
        }

        [Test]
        public async Task RejectsAnUnaffectedPackageThatDoesNotMatchTheRootVersionAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Server", "1.9.9");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("do not match the preview policy"));
            });
        }

        [Test]
        public async Task RejectsAMissingCoreAnchorPackageAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
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
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
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
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.1");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("disagree on the package version"));
            });
        }

        [Test]
        public async Task RejectsDuplicatePackageIdentitiesAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0", fileNameSuffix: "-dup");

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("Duplicate package ID/version"));
            });
        }

        [Test]
        public async Task RejectsAnUnexpectedExpectedVersionMismatchAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");

            ValidationResult result = await fixture.ValidateAsync(expectedVersion: "2.0.1").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("Expected base package version '2.0.1'"));
            });
        }

        [Test]
        public async Task RequireDebugRejectsAReleaseOnlySetAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(requireDebug: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("does not contain any .Debug package IDs"));
            });
        }

        [Test]
        public async Task RequireDebugAcceptsACombinedReleaseAndDebugSetAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core.Debug", "2.0.0");
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry.Debug", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync(requireDebug: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Manifest!.Value.GetProperty("debugPackageCount").GetInt32(), Is.EqualTo(2));
                Assert.That(result.Manifest.Value.GetProperty("packageCount").GetInt32(), Is.EqualTo(4));
            });
        }

        [Test]
        public async Task RejectsAnOrphanedSymbolPackageAsync()
        {
            using PackageSetFixture fixture = PackageSetFixture.Create();
            fixture.AddPackage("OPCFoundation.NetStandard.Opc.Ua.Core", "2.0.0");
            // A .snupkg whose corresponding .nupkg never exists.
            fixture.AddOrphanedSymbolPackage("OPCFoundation.NetStandard.Opc.Ua.XRegistry", PreviewFamilyVersion);

            ValidationResult result = await fixture.ValidateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
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
                string scriptPath = Path.Combine(FindRepositoryRoot(), ".azurepipelines", "nuget", "validate-package-set.ps1");

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
                if (File.Exists(Path.Combine(current, ".azurepipelines", "nuget", "validate-package-set.ps1")))
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
