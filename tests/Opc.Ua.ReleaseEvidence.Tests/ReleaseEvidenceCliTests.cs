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

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

[assembly: CLSCompliant(true)]

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Exercises the CLI and its on-disk evidence boundary, not implementation helpers.
    /// </summary>
    [TestFixture]
    public sealed class ReleaseEvidenceCliTests
    {
        /// <summary>
        /// Verifies that CLI help lists the offline capture, NuGet, and evaluation commands.
        /// </summary>
        [Test]
        public async Task HelpDescribesOfflineEvidenceCommandsAsync()
        {
            (int code, string output) = await RunAsync("--help").ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            Assert.That(output, Does.Contain("capture").And.Contain("nuget").And.Contain("evaluate"));
        }

        /// <summary>
        /// Verifies that unknown commands and missing required arguments return a fatal-input exit with diagnostics.
        /// </summary>
        [TestCase("unknown-command")]
        [TestCase("evaluate")]
        [TestCase("capture")]
        public async Task InvalidCommandArgumentsReturnFatalInputExitAsync(string command)
        {
            (int code, string output) = await RunAsync(command).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(2), output);
            Assert.That(output, Is.Not.Empty);
        }

        /// <summary>
        /// Verifies that a legacy manifest produces a blocking incomplete report under the active evidence contract.
        /// </summary>
        [Test]
        public async Task LegacyManifestIsRejectedByTheActiveContractAsync()
        {
            string work = CreateWorkspace();
            try
            {
                string result = Path.Combine(work, "result.json");
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(),
                    "--evidence", Fixture("v1.json"), "--expected", Fixture("expected.json"),
                    "--output", result).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                Assert.That(File.Exists(result), Is.True, output);
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result).ConfigureAwait(false));
                Assert.That(document.RootElement.GetProperty("status").GetString(), Is.EqualTo("incomplete"));
                Assert.That(document.RootElement.GetProperty("unmetControls").ToString(),
                    Does.Contain("EVIDENCE_SCHEMA"));
                Assert.That(document.RootElement.GetProperty("blocking").GetBoolean(), Is.True);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that stage and channel govern blocking while incomplete evidence never claims verified readiness.
        /// </summary>
        [TestCase("pilot", "stable", 0)]
        [TestCase("required", "stable", 1)]
        [TestCase("required", "preview", 0)]
        [TestCase("required", "development", 0)]
        public async Task PolicyStageControlsBlockingButNeverFakesReadinessAsync(
            string stage, string channel, int expectedExit)
        {
            string work = CreateWorkspace();
            try
            {
                CopyContracts(work);
                string policyPath = Path.Combine(work, ".azurepipelines", "release", "policy.json");
                JsonNode policy = JsonNode.Parse(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false))!;
                policy["stage"] = stage;
                policy["publisherBoundaryVerified"] = true;
                await File.WriteAllTextAsync(policyPath, policy.ToJsonString()).ConfigureAwait(false);
                JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected.json"))
                    .ConfigureAwait(false))!;
                expected["release"]!["channel"] = channel;
                string expectedPath = Path.Combine(work, "expected.json");
                await File.WriteAllTextAsync(expectedPath, expected.ToJsonString()).ConfigureAwait(false);
                JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("v2.json"))
                    .ConfigureAwait(false))!;
                envelope["release"]!["channel"] = channel;
                string evidencePath = Path.Combine(work, "evidence.json");
                await File.WriteAllTextAsync(evidencePath, envelope.ToJsonString()).ConfigureAwait(false);
                string result = Path.Combine(work, "result.json");
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", work, "--evidence", evidencePath,
                    "--expected", expectedPath, "--output", result).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(expectedExit), output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(result).ConfigureAwait(false));
                Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("incomplete"));
                Assert.That(report.RootElement.GetProperty("unmetControls").ToString(),
                    Does.Contain("ASSURANCE_COMPLETE").And.Contain("ARTIFACT_MEMBERSHIP")
                        .And.Contain("PRODUCER_NOT_READY").And.Contain("PUBLISHER_BOUNDARY"));
                Assert.That(report.RootElement.GetProperty("externalVerificationPerformed").GetBoolean(), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that required-stage controls leave the deferred 1.5 release line advisory without claiming
        /// completeness.
        /// </summary>
        [Test]
        public async Task NewRequiredControlsDoNotEnrollTheDeferredReleaseLineAsync()
        {
            string work = CreateWorkspace();
            try
            {
                CopyContracts(work);
                string policyPath = Path.Combine(work, ".azurepipelines", "release", "policy.json");
                JsonNode policy = JsonNode.Parse(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false))!;
                policy["stage"] = "required";
                await File.WriteAllTextAsync(policyPath, policy.ToJsonString()).ConfigureAwait(false);
                foreach (string fixture in new[] { "expected.json", "v2.json" })
                {
                    JsonNode value = JsonNode.Parse(await File.ReadAllTextAsync(Fixture(fixture))
                        .ConfigureAwait(false))!;
                    value["release"]!["version"] = "1.5.378";
                    foreach (JsonNode? artifact in value["artifacts"]!.AsArray())
                    {
                        artifact!["version"] = "1.5.378";
                    }
                    await File.WriteAllTextAsync(Path.Combine(work, fixture), value.ToJsonString())
                        .ConfigureAwait(false);
                }
                string resultPath = Path.Combine(work, "result.json");
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", work, "--evidence", Path.Combine(work, "v2.json"),
                    "--expected", Path.Combine(work, "expected.json"), "--output", resultPath).ConfigureAwait(false);
                Assert.That(code, Is.Zero, output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false));
                Assert.That(report.RootElement.GetProperty("blocking").GetBoolean(), Is.False);
                Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("incomplete"));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that malformed, duplicate-version, unsupported, or incomplete evidence JSON returns a fatal-input
        /// exit.
        /// </summary>
        [TestCase("{")]
        [TestCase(/*lang=json,strict*/ "{\"schemaVersion\":2,\"schemaVersion\":1}")]
        [TestCase(/*lang=json,strict*/ "{\"schemaVersion\":3}")]
        [TestCase(/*lang=json,strict*/ "{\"schemaVersion\":2}")]
        public async Task InvalidEvidenceCannotPretendSuccessAsync(string json)
        {
            string work = CreateWorkspace();
            try
            {
                string evidence = Path.Combine(work, "evidence.json");
                await File.WriteAllTextAsync(evidence, json).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", evidence,
                    "--expected", Fixture("expected.json"), "--output", Path.Combine(work, "result.json"))
                    .ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that capture preserves target-specific dependency graphs and nonpackable contributors with exact
        /// digests.
        /// </summary>
        [Test]
        public async Task CaptureFreezesTargetGraphsAndNonpackableContributorsAsync()
        {
            string work = CreateWorkspace();
            try
            {
                await PrepareCaptureAsync(work).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "capture", "--repository-root", FindRoot(),
                    "--request", Path.Combine(work, "capture.json"), "--output", Path.Combine(work, "frozen"))
                    .ConfigureAwait(false);
                Assert.That(code, Is.Zero, output);
                string path = Path.Combine(work, "frozen", "build-inputs.json");
                Assert.That(File.Exists(path), Is.True, output);
                using var captured = JsonDocument.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
                JsonElement mappings = captured.RootElement.GetProperty("mappings");
                Assert.That(mappings.GetArrayLength(), Is.EqualTo(2));
                Assert.That(mappings.ToString(), Does.Contain("Contoso.Dependency").And.Contain("1.2.3")
                    .And.Contain("2.3.4").And.Contain("net8.0").And.Contain("net10.0").And.Contain("Contributor.dll"));
                foreach (JsonElement mapping in mappings.EnumerateArray())
                {
                    string asset = mapping.GetProperty("assets").GetProperty("path").GetString()!;
                    byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(work, "frozen", asset))
                        .ConfigureAwait(false);
                    Assert.That(mapping.GetProperty("assets").GetProperty("digest").GetString(),
                        Is.EqualTo("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes))));
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that invented checkout state or evaluated package versions prevent frozen-input output.
        /// </summary>
        [TestCase("sha")]
        [TestCase("cleanliness")]
        [TestCase("ref")]
        [TestCase("version")]
        public async Task CaptureRejectsInventedCheckoutOrEvaluatedVersionAsync(string mutation)
        {
            string work = CreateWorkspace();
            try
            {
                await PrepareCaptureAsync(work).ConfigureAwait(false);
                string requestPath = Path.Combine(work, "capture.json");
                JsonNode request = JsonNode.Parse(await File.ReadAllTextAsync(requestPath).ConfigureAwait(false))!;
                switch (mutation)
                {
                    case "sha":
                        request["source"]!["actualSha"] = new string('4', 40);
                        break;
                    case "cleanliness":
                        request["source"]!["trackedClean"] = !request["source"]!["trackedClean"]!.GetValue<bool>();
                        break;
                    case "ref":
                        request["source"]!["actualRef"] = "refs/heads/release-evidence-nonexistent-ref";
                        break;
                    case "version":
                        request["version"] = "2.0.1";
                        break;
                }
                await File.WriteAllTextAsync(requestPath, request.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "capture", "--repository-root", FindRoot(), "--request", requestPath,
                    "--output", Path.Combine(work, "frozen")).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
                Assert.That(File.Exists(Path.Combine(work, "frozen", "build-inputs.json")), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that missing or null expectation members produce an input error without a report or null-reference
        /// failure.
        /// </summary>
        [TestCase("release", false)]
        [TestCase("release", true)]
        [TestCase("artifacts", false)]
        [TestCase("artifacts", true)]
        public async Task MalformedExpectationReturnsInputErrorWithoutReportAsync(string field, bool explicitNull)
        {
            string work = CreateWorkspace();
            try
            {
                JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected.json"))
                    .ConfigureAwait(false))!;
                if (explicitNull)
                {
                    expected[field] = null;
                }
                else
                {
                    expected.AsObject().Remove(field);
                }
                string expectedPath = Path.Combine(work, "expected.json");
                string reportPath = Path.Combine(work, "result.json");
                await File.WriteAllTextAsync(expectedPath, expected.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", Fixture("v2.json"),
                    "--expected", expectedPath, "--output", reportPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
                Assert.That(output, Does.Contain("Invalid input:").And.Not.Contain("NullReferenceException"));
                Assert.That(File.Exists(reportPath), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that NuGet sidecars preserve package and manifest bytes while describing private payloads and TFM
        /// graphs.
        /// </summary>
        [Test]
        public async Task NugetSidecarsPreserveSignedBytesPrivatePayloadsAndTargetSpecificGraphsAsync()
        {
            string work = CreateWorkspace();
            try
            {
                await PrepareCaptureAsync(work).ConfigureAwait(false);
                (int captureCode, string captureOutput) = await RunAsync(
                    "capture", "--repository-root", FindRoot(), "--request", Path.Combine(work, "capture.json"),
                    "--output", Path.Combine(work, "frozen")).ConfigureAwait(false);
                Assert.That(captureCode, Is.Zero, captureOutput);
                string package = await CreateGeneratorPackageAsync(work).ConfigureAwait(false);
                byte[] before = await File.ReadAllBytesAsync(package).ConfigureAwait(false);
                JsonNode context = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(work, "expected.json"))
                    .ConfigureAwait(false))!;
                JsonNode manifest = JsonNode.Parse(
                    await File.ReadAllTextAsync(Fixture("v1.json")).ConfigureAwait(false))!;
                manifest["commit"] = context["source"]!["actualSha"]!.DeepClone();
                manifest["ref"] = context["source"]!["actualRef"]!.DeepClone();
                manifest["packageCount"] = 1;
                manifest["archives"] = JsonNode.Parse($$"""
                    [{"id":"Fixture.Generator","version":"2.0.0","type":"package",
                    "file":"Fixture.Generator.2.0.0.nupkg",
                    "sha256":"{{Convert.ToHexStringLower(SHA256.HashData(before))}}"}]
                    """);
                string manifestPath = Path.Combine(work, "release-manifest.json");
                await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "nuget", "--repository-root", FindRoot(), "--packages", Path.Combine(work, "packages"),
                    "--inputs", Path.Combine(work, "frozen"), "--context", Path.Combine(work, "expected.json"),
                    "--manifest", manifestPath,
                    "--output", Path.Combine(work, "evidence")).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                Assert.That(await File.ReadAllBytesAsync(package).ConfigureAwait(false), Is.EqualTo(before));
                Assert.That(await File.ReadAllBytesAsync(Path.Combine(work, "evidence", "release-manifest.json"))
                    .ConfigureAwait(false),
                    Is.EqualTo(await File.ReadAllBytesAsync(manifestPath).ConfigureAwait(false)));
                string inventoryPath = Directory.GetFiles(Path.Combine(work, "evidence"), "*.inventory.json",
                    SearchOption.AllDirectories)[0];
                using var inventory = JsonDocument.Parse(
                    await File.ReadAllTextAsync(inventoryPath).ConfigureAwait(false));
                Assert.That(inventory.RootElement.GetProperty("consumerDependencies").ToString(),
                    Does.Contain("[1.0,2.0)").And.Contain("[3.0,4.0)"));
                Assert.That(inventory.RootElement.GetProperty("payloads").ToString(),
                    Does.Contain("Contoso.Dependency").And.Contain("Contributor").And.Contain("sha256:"));
                Assert.That(inventory.RootElement.GetProperty("resolvedGraphs").ToString(),
                    Does.Contain("1.2.3").And.Contain("2.3.4").And.Contain("net8.0").And.Contain("net10.0"));
                string bomPath = Directory.GetFiles(Path.Combine(work, "evidence"), "*.cdx.json",
                    SearchOption.AllDirectories)[0];
                using var bom = JsonDocument.Parse(await File.ReadAllTextAsync(bomPath).ConfigureAwait(false));
                Assert.That(bom.RootElement.GetProperty("specVersion").GetString(), Is.EqualTo("1.6"));
                Assert.That(bom.RootElement.GetProperty("metadata").GetProperty("component").GetProperty("hashes")[0]
                    .GetProperty("content").GetString(),
                    Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(before))));
                Assert.That(bom.RootElement.GetProperty("components").ToString(),
                    Does.Contain("MIT OR Apache-2.0").And.Contain("build-only").And.Contain("shipped"));
                using var envelope = JsonDocument.Parse(await File.ReadAllTextAsync(
                    Path.Combine(work, "evidence", "release-evidence.json")).ConfigureAwait(false));
                Assert.That(envelope.RootElement.GetProperty("assurance").GetProperty("expected").GetInt32(),
                    Is.EqualTo(7));
                Assert.That(envelope.RootElement.GetProperty("assurance").GetProperty("missing").GetInt32(),
                    Is.EqualTo(7));
                Assert.That(envelope.RootElement.GetProperty("assessment").GetProperty("status").GetString(),
                    Is.EqualTo("incomplete"));
                (int evaluateCode, string evaluateOutput) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence",
                    Path.Combine(work, "evidence", "release-evidence.json"), "--expected",
                    Path.Combine(work, "expected.json"), "--artifacts-root", Path.Combine(work, "packages"),
                    "--output", Path.Combine(work, "report.json"))
                    .ConfigureAwait(false);
                Assert.That(evaluateCode, Is.EqualTo(1), evaluateOutput);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies honest inventory classification of unowned native payloads, Debug packages, symbols, and
        /// metapackages.
        /// </summary>
        [TestCase("unknown-native")]
        [TestCase("debug")]
        [TestCase("symbols")]
        [TestCase("metapackage")]
        public async Task ArchiveVariantsHaveHonestInventoriesAsync(string variant)
        {
            string work = CreateWorkspace();
            try
            {
                await PrepareCaptureAsync(work).ConfigureAwait(false);
                if (variant == "debug")
                {
                    string requestPath = Path.Combine(work, "capture.json");
                    JsonNode request = JsonNode.Parse(await File.ReadAllTextAsync(requestPath).ConfigureAwait(false))!;
                    request["configuration"] = "Debug";
                    await File.WriteAllTextAsync(requestPath, request.ToJsonString()).ConfigureAwait(false);
                }
                (int captureCode, string captureOutput) = await RunAsync(
                    "capture", "--repository-root", FindRoot(), "--request", Path.Combine(work, "capture.json"),
                    "--output", Path.Combine(work, "frozen")).ConfigureAwait(false);
                Assert.That(captureCode, Is.Zero, captureOutput);
                string package = await CreateGeneratorPackageAsync(work).ConfigureAwait(false);
                string desiredId = variant switch
                {
                    "debug" => "Fixture.Generator.Debug",
                    "metapackage" => "OPCFoundation.NetStandard.Opc.Ua",
                    _ => "Fixture.Generator"
                };
                if (variant is "debug" or "metapackage")
                {
                    using ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Update);
                    archive.GetEntry("Fixture.Generator.nuspec")!.Delete();
                    string nuspec = (await File.ReadAllTextAsync(Fixture("Generator.nuspec")).ConfigureAwait(false))
                        .Replace("<id>Fixture.Generator</id>", "<id>" + desiredId + "</id>", StringComparison.Ordinal);
                    using (var writer = new StreamWriter(archive.CreateEntry("Fixture.Generator.nuspec").Open()))
                    {
                        await writer.WriteAsync(nuspec).ConfigureAwait(false);
                    }
                    if (variant == "metapackage")
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries.Where(e =>
                            e.FullName.EndsWith(".dll", StringComparison.Ordinal)).ToArray())
                        {
                            entry.Delete();
                        }
                    }
                }
                if (variant == "unknown-native")
                {
                    using ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Update);
                    using var writer = new StreamWriter(
                        archive.CreateEntry("runtimes/linux-x64/native/unknown.so").Open());
                    await writer.WriteAsync("unowned-native-content").ConfigureAwait(false);
                }
                if (variant == "symbols")
                {
                    File.Copy(package, Path.ChangeExtension(package, ".snupkg"));
                }
                (int code, string output) = await GenerateAsync(work).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                string[] inventoryPaths = Directory.GetFiles(
                    Path.Combine(work, "evidence"), "*.inventory.json", SearchOption.AllDirectories);
                string path = inventoryPaths.Single(p => p.Contains(
                    variant == "symbols" ? ".nuget-symbols." : ".nuget-package.", StringComparison.Ordinal));
                using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
                Assert.That(inventory.RootElement.GetProperty("artifact").GetProperty("id").GetString(),
                    Is.EqualTo(desiredId));
                if (variant == "unknown-native")
                {
                    Assert.That(inventory.RootElement.GetProperty("unmetControls").ToString(),
                        Does.Contain("INVENTORY_COMPLETE"));
                    Assert.That(inventory.RootElement.GetProperty("payloads").ToString(), Does.Contain("unowned"));
                }
                if (variant == "metapackage")
                {
                    Assert.That(inventory.RootElement.GetProperty("resolvedGraphs").GetArrayLength(), Is.Zero);
                    Assert.That(inventory.RootElement.GetProperty("artifact").GetProperty("scopes").GetProperty("tfms")
                        .GetArrayLength(), Is.Zero);
                    Assert.That(inventory.RootElement.GetProperty("unmetControls").GetArrayLength(), Is.Zero);
                }
                if (variant == "debug")
                {
                    Assert.That(inventory.RootElement.GetProperty("artifact").GetProperty("configuration").GetString(),
                        Is.EqualTo("Debug"));
                }
                if (variant == "symbols")
                {
                    Assert.That(inventory.RootElement.GetProperty("artifact").GetProperty("kind").GetString(),
                        Is.EqualTo("nuget-symbols"));
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that unsafe archive entry paths are rejected without extracting files or writing an evidence
        /// envelope.
        /// </summary>
        [TestCase("../escape.dll")]
        [TestCase("/absolute.dll")]
        [TestCase("C:/outside.dll")]
        [TestCase("folder\\escape.dll")]
        public async Task NugetRejectsUnsafeArchivePathsWithoutExtractionAsync(string entryPath)
        {
            string work = CreateWorkspace();
            try
            {
                await PrepareCaptureAsync(work).ConfigureAwait(false);
                (int captureCode, string captureOutput) = await RunAsync(
                    "capture", "--repository-root", FindRoot(), "--request", Path.Combine(work, "capture.json"),
                    "--output", Path.Combine(work, "frozen")).ConfigureAwait(false);
                Assert.That(captureCode, Is.Zero, captureOutput);
                string package = await CreateGeneratorPackageAsync(work).ConfigureAwait(false);
                using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Update))
                {
                    archive.CreateEntry(entryPath);
                }
                (int code, string output) = await GenerateAsync(work).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
                Assert.That(File.Exists(Path.Combine(work, "escape.dll")), Is.False);
                Assert.That(File.Exists(Path.Combine(work, "evidence", "release-evidence.json")), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that changing frozen dependency-graph bytes causes reconciliation to fail with an integrity
        /// diagnostic.
        /// </summary>
        [Test]
        public async Task AlteredFrozenGraphCannotBeReconciledAsync()
        {
            string work = CreateWorkspace();
            try
            {
                await PrepareCaptureAsync(work).ConfigureAwait(false);
                (int captureCode, string captureOutput) = await RunAsync(
                    "capture", "--repository-root", FindRoot(), "--request", Path.Combine(work, "capture.json"),
                    "--output", Path.Combine(work, "frozen")).ConfigureAwait(false);
                Assert.That(captureCode, Is.Zero, captureOutput);
                await CreateGeneratorPackageAsync(work).ConfigureAwait(false);
                string graph = Directory.GetFiles(Path.Combine(work, "frozen", "graphs"))[0];
                await File.AppendAllTextAsync(graph, " ").ConfigureAwait(false);
                (int code, string output) = await GenerateAsync(work).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
                Assert.That(output, Does.Contain("Frozen input bytes changed"));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that equivalent normalized stable NuGet versions do not create a false release-identity mismatch.
        /// </summary>
        [TestCase("2.0")]
        [TestCase("2.0.0")]
        [TestCase("2.0.0.0")]
        [TestCase("2.0.0+build.15")]
        public async Task EquivalentStableNugetVersionsDoNotInventIdentityMismatchAsync(string version)
        {
            string work = CreateWorkspace();
            try
            {
                JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected.json"))
                    .ConfigureAwait(false))!;
                expected["release"]!["version"] = version;
                string expectedPath = Path.Combine(work, "expected.json");
                await File.WriteAllTextAsync(expectedPath, expected.ToJsonString()).ConfigureAwait(false);
                string reportPath = Path.Combine(work, "result.json");
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", Fixture("v2.json"),
                    "--expected", expectedPath, "--output", reportPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                Assert.That(await File.ReadAllTextAsync(reportPath).ConfigureAwait(false),
                    Does.Not.Contain("Actual version/group/channel contradict"));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that a completed job with zero executed tests is a blocking baseline failure under the active
        /// required policy.
        /// </summary>
        [Test]
        public async Task ZeroExecutedAssuranceIsBaselineFailureEvenDuringPilotAsync()
        {
            string work = CreateWorkspace();
            try
            {
                JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("v2.json"))
                    .ConfigureAwait(false))!;
                envelope["assurance"]!["jobs"] = JsonNode.Parse("""
                    [{
                      "id":"core-security","profile":"security-net10",
                      "project":"tests/Opc.Ua.Core.Security.Tests/Opc.Ua.Core.Security.Tests.csproj",
                      "configuration":"Release","host":"windows","hostTfm":"net10.0","libraryTfm":"net10.0",
                      "platform":"windows/amd64","shard":"all","filter":"","selected":true,
                      "status":"completed","inputIds":[],
                      "counts":{"total":0,"executed":0,"passed":0,"failed":0,"skipped":0}
                    }]
                    """);
                string evidencePath = Path.Combine(work, "evidence.json");
                await File.WriteAllTextAsync(evidencePath, envelope.ToJsonString()).ConfigureAwait(false);
                string reportPath = Path.Combine(work, "result.json");
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", evidencePath,
                    "--expected", Fixture("expected.json"), "--output", reportPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(false));
                Assert.That(report.RootElement.GetProperty("baselineFailed").GetBoolean(), Is.True);
                Assert.That(report.RootElement.GetProperty("unmetControls").ToString(),
                    Does.Contain("ASSURANCE_COMPLETE").And.Contain("EVIDENCE_FRESHNESS"));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that locally asserted passing results cannot authenticate producer identity or complete assurance.
        /// </summary>
        [TestCase("pilot", 0)]
        [TestCase("required", 1)]
        public async Task SelfAssertedPassingAssuranceNeverAuthenticatesItsProducerAsync(
            string stage, int expectedExit)
        {
            string work = CreateWorkspace();
            try
            {
                CopyContracts(work);
                string policyPath = Path.Combine(work, ".azurepipelines", "release", "policy.json");
                JsonNode policy = JsonNode.Parse(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false))!;
                policy["stage"] = stage;
                await File.WriteAllTextAsync(policyPath, policy.ToJsonString()).ConfigureAwait(false);
                JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("v2.json"))
                    .ConfigureAwait(false))!;
                JsonNode profiles = JsonNode.Parse(await File.ReadAllTextAsync(
                    Path.Combine(work, ".azurepipelines", "assurance", "profiles.json")).ConfigureAwait(false))!;
                var jobs = new JsonArray();
                foreach (JsonNode? profile in profiles["profiles"]!.AsArray())
                {
                    foreach (JsonNode? definition in profile!["jobs"]!.AsArray())
                    {
                        string id = definition!["id"]!.GetValue<string>();
                        JsonNode counts = JsonNode.Parse(definition["kind"]!.GetValue<string>() == "analysis"
                            ? /*lang=json,strict*/ """{"analyzedProjects":1,"findings":0,"unresolvedFindings":0}"""
                            : /*lang=json,strict*/ """{"total":1,"executed":1,"passed":1,"failed":0,"skipped":0}""")!;
                        var job = new JsonObject
                        {
                            ["id"] = id,
                            ["profile"] = profile["id"]!.DeepClone(),
                            ["project"] = definition["project"]!.DeepClone(),
                            ["shard"] = "all",
                            ["selected"] = true,
                            ["status"] = "completed",
                            ["inputIds"] = new JsonArray(),
                            ["sourceSha"] = envelope["source"]!["actualSha"]!.DeepClone(),
                            ["producer"] = envelope["producer"]!.DeepClone(),
                            ["counts"] = counts.DeepClone(),
                            ["resultDocument"] = id + ".json"
                        };
                        foreach (string field in new[]
                        {
                            "configuration", "host", "hostTfm", "libraryTfm", "platform", "filter"
                        })
                        {
                            job[field] = profile[field]!.DeepClone();
                        }
                        var summary = new JsonObject
                        {
                            ["schemaVersion"] = 1,
                            ["kind"] = definition["kind"]!.GetValue<string>() == "analysis" ? "sarif" : "trx",
                            ["status"] = "completed",
                            ["counts"] = counts,
                            ["documents"] = JsonNode.Parse("""
                                [{"digest":"sha256:4444444444444444444444444444444444444444444444444444444444444444"}]
                                """)
                        };
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(summary.ToJsonString());
                        await File.WriteAllBytesAsync(Path.Combine(work, id + ".json"), bytes).ConfigureAwait(false);
                        envelope["documents"]!.AsArray().Add(new JsonObject
                        {
                            ["type"] = "assurance-summary",
                            ["format"] = "json",
                            ["version"] = "1",
                            ["path"] = id + ".json",
                            ["digest"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)),
                            ["subject"] = JsonNode.Parse("""
                                {"kind":"nuget-package","id":"Example",
                                "digest":"sha256:3333333333333333333333333333333333333333333333333333333333333333"}
                                """)
                        });
                        jobs.Add(job);
                    }
                }
                envelope["assurance"]!["jobs"] = jobs;
                foreach (string field in new[] { "expected", "selected", "completed" })
                {
                    envelope["assurance"]![field] = jobs.Count;
                }
                string evidencePath = Path.Combine(work, "evidence.json");
                string resultPath = Path.Combine(work, "result.json");
                await File.WriteAllTextAsync(evidencePath, envelope.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", work, "--evidence", evidencePath,
                    "--expected", Fixture("expected.json"), "--output", resultPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(expectedExit), output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false));
                Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("incomplete"));
                Assert.That(report.RootElement.GetProperty("baselineFailed").GetBoolean(), Is.False);
                Assert.That(report.RootElement.GetProperty("externalVerificationPerformed").GetBoolean(), Is.False);
                Assert.That(report.RootElement.GetProperty("unmetControls").ToString(),
                    Does.Contain("ASSURANCE_COMPLETE").And.Contain("PRODUCER_IDENTITY")
                        .And.Contain("EVIDENCE_FRESHNESS"));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that empty-regression skips avoid baseline failure only when bound replay counts justify them.
        /// </summary>
        [TestCase(true, 1)]
        [TestCase(false, 1)]
        public async Task EmptyRegressionSkipsRequireBoundReplayProofAsync(bool validProof, int expectedExit)
        {
            string work = CreateWorkspace();
            try
            {
                JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("v2.json"))
                    .ConfigureAwait(false))!;
                JsonNode counts = JsonNode.Parse("""
                    {"total":4,"executed":2,"passed":2,"failed":0,"skipped":2,
                    "expectedTargets":1,"executedTargets":1,"expectedInputs":1,"executedInputs":1}
                    """)!;
                JsonNode summary = JsonNode.Parse("""
                    {"schemaVersion":1,"kind":"fuzz-replay","status":"completed",
                    "documents":[{"digest":"sha256:1111111111111111111111111111111111111111111111111111111111111111"}],
                    "replay":{
                        "inventoryDigest":"sha256:2222222222222222222222222222222222222222222222222222222222222222",
                        "targetDigest":"sha256:3333333333333333333333333333333333333333333333333333333333333333",
                        "executionDigest":"sha256:4444444444444444444444444444444444444444444444444444444444444444",
                        "expectedPairs":1,"executedPairs":1,"allowedEmptyRegressionSkips":2}}
                    """)!;
                summary["counts"] = counts.DeepClone();
                if (!validProof)
                {
                    summary["replay"]!["allowedEmptyRegressionSkips"] = 1;
                }
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(summary.ToJsonString());
                await File.WriteAllBytesAsync(Path.Combine(work, "replay.json"), bytes).ConfigureAwait(false);
                envelope["documents"] = new JsonArray(new JsonObject
                {
                    ["type"] = "assurance-summary",
                    ["format"] = "json",
                    ["version"] = "1",
                    ["path"] = "replay.json",
                    ["digest"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    ["subject"] = new JsonObject
                    {
                        ["kind"] = "nuget-package",
                        ["id"] = "Example",
                        ["digest"] = envelope["artifacts"]![0]!["digest"]!.DeepClone()
                    }
                });
                JsonNode job = JsonNode.Parse("""
                    {"id":"fuzz-network","profile":"fuzz-replay-net10",
                    "project":"fuzzing/Opc.Ua.Network.Fuzz.Tests/Opc.Ua.Network.Fuzz.Tests.csproj",
                    "configuration":"Release","host":"windows","hostTfm":"net10.0","libraryTfm":"net10.0",
                    "platform":"windows/amd64","shard":"all","filter":"","selected":true,
                    "status":"completed","inputIds":[],"resultDocument":"replay.json"}
                    """)!;
                job["counts"] = counts;
                envelope["assurance"]!["jobs"] = new JsonArray(job);
                envelope["assurance"]!["expected"] = 1;
                envelope["assurance"]!["selected"] = 1;
                envelope["assurance"]!["completed"] = 1;
                string evidencePath = Path.Combine(work, "evidence.json");
                string reportPath = Path.Combine(work, "result.json");
                await File.WriteAllTextAsync(evidencePath, envelope.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", evidencePath,
                    "--expected", Fixture("expected.json"), "--output", reportPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(expectedExit), output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath).ConfigureAwait(false));
                Assert.That(report.RootElement.GetProperty("baselineFailed").GetBoolean(), Is.EqualTo(!validProof));
                Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("incomplete"),
                    "Locally coherent replay proof does not supply authenticated release authority.");
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that source, producer, policy, and document mismatches yield specific evidence findings.
        /// </summary>
        [TestCase("source", "Actual source does not match")]
        [TestCase("run", "Producer run, attempt or definition differs")]
        [TestCase("policy", "Protected policy bytes/identity differ")]
        [TestCase("document", "Missing or altered document")]
        public async Task MismatchedEvidenceRecordsSpecificUnmetControlsAsync(string mutation, string finding)
        {
            string work = CreateWorkspace();
            try
            {
                JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("v2.json"))
                    .ConfigureAwait(false))!;
                switch (mutation)
                {
                    case "source":
                        envelope["source"]!["actualSha"] = new string('4', 40);
                        break;
                    case "run":
                        envelope["producer"]!["attempt"] = 9;
                        break;
                    case "policy":
                        envelope["policy"]!["stage"] = "required";
                        break;
                    case "document":
                        envelope["documents"] = JsonNode.Parse("""
                            [{"type":"sbom","format":"CycloneDX","version":"1.6","path":"wrong.json",
                            "digest":"sha256:5555555555555555555555555555555555555555555555555555555555555555",
                            "subject":{"kind":"nuget-package","id":"Example",
                            "digest":"sha256:3333333333333333333333333333333333333333333333333333333333333333"}}]
                            """);
                        await File.WriteAllTextAsync(Path.Combine(work, "wrong.json"), "{}").ConfigureAwait(false);
                        break;
                }
                string evidencePath = Path.Combine(work, "evidence.json");
                string resultPath = Path.Combine(work, "result.json");
                await File.WriteAllTextAsync(evidencePath, envelope.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", evidencePath,
                    "--expected", Fixture("expected.json"), "--output", resultPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                Assert.That(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false), Does.Contain(finding));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies SBOM schema and artifact-subject content instead of accepting a document's format labels alone.
        /// </summary>
        [TestCase("none", null)]
        [TestCase("schema", "CycloneDX 1.6 schema validation failed")]
        [TestCase("subject", "SBOM content disagrees with its artifact subject")]
        public async Task EvaluationValidatesBomContentBeyondSelfLabelsAsync(string mutation, string? finding)
        {
            string work = CreateWorkspace();
            try
            {
                JsonNode envelope = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("v2.json"))
                    .ConfigureAwait(false))!;
                JsonNode bom = JsonNode.Parse("""
                    {"bomFormat":"CycloneDX","specVersion":"1.6","version":1,"metadata":{"component":{
                    "type":"library","name":"Example","version":"2.0.0","hashes":[{
                    "alg":"SHA-256","content":"3333333333333333333333333333333333333333333333333333333333333333"}]}}}
                    """)!;
                if (mutation == "schema")
                {
                    bom["components"] = "not-an-array";
                }
                if (mutation == "subject")
                {
                    bom["metadata"]!["component"]!["hashes"]![0]!["content"] = new string('4', 64);
                }
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(bom.ToJsonString());
                await File.WriteAllBytesAsync(Path.Combine(work, "bom.json"), bytes).ConfigureAwait(false);
                envelope["documents"] = new JsonArray(new JsonObject
                {
                    ["type"] = "sbom",
                    ["format"] = "CycloneDX",
                    ["version"] = "1.6",
                    ["path"] = "bom.json",
                    ["digest"] = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    ["subject"] = JsonNode.Parse("""
                        {"kind":"nuget-package","id":"Example",
                        "digest":"sha256:3333333333333333333333333333333333333333333333333333333333333333"}
                        """)
                });
                string evidencePath = Path.Combine(work, "evidence.json");
                string resultPath = Path.Combine(work, "result.json");
                await File.WriteAllTextAsync(evidencePath, envelope.ToJsonString()).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", evidencePath,
                    "--expected", Fixture("expected.json"), "--output", resultPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false));
                if (finding == null)
                {
                    Assert.That(report.RootElement.GetProperty("unmetControls").ToString(),
                        Does.Not.Contain("INVENTORY_COMPLETE"));
                }
                else
                {
                    Assert.That(report.RootElement.GetProperty("findings").ToString(), Does.Contain(finding));
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that a conflicting development expectation cannot downgrade a claimed stable release's blocking
        /// policy.
        /// </summary>
        [Test]
        public async Task ConflictingDevelopmentExpectationCannotDowngradeClaimedStableReleaseAsync()
        {
            string work = CreateWorkspace();
            try
            {
                CopyContracts(work);
                string policyPath = Path.Combine(work, ".azurepipelines", "release", "policy.json");
                JsonNode policy = JsonNode.Parse(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false))!;
                policy["stage"] = "required";
                await File.WriteAllTextAsync(policyPath, policy.ToJsonString()).ConfigureAwait(false);
                JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected.json"))
                    .ConfigureAwait(false))!;
                expected["release"]!["channel"] = "development";
                string expectedPath = Path.Combine(work, "expected.json");
                await File.WriteAllTextAsync(expectedPath, expected.ToJsonString()).ConfigureAwait(false);
                string resultPath = Path.Combine(work, "result.json");
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", work, "--evidence", Fixture("v2.json"),
                    "--expected", expectedPath, "--output", resultPath).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                Assert.That(await File.ReadAllTextAsync(resultPath).ConfigureAwait(false),
                    Does.Contain("RELEASE_INTENT").And.Contain("\"blocking\": true"));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that evaluation rejects an output path equal to its evidence input and preserves the original
        /// bytes.
        /// </summary>
        [Test]
        public async Task EvaluationCannotOverwriteItsEvidenceInputAsync()
        {
            string work = CreateWorkspace();
            try
            {
                string evidence = Path.Combine(work, "evidence.json");
                byte[] original = await File.ReadAllBytesAsync(Fixture("v2.json")).ConfigureAwait(false);
                await File.WriteAllBytesAsync(evidence, original).ConfigureAwait(false);
                (int code, string output) = await RunAsync(
                    "evaluate", "--repository-root", FindRoot(), "--evidence", evidence,
                    "--expected", Fixture("expected.json"), "--output", evidence).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
                Assert.That(await File.ReadAllBytesAsync(evidence).ConfigureAwait(false), Is.EqualTo(original));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies OCI descriptor integrity and binds attestation references and predicates to the runnable image
        /// subject.
        /// </summary>
        [TestCase("none", null)]
        [TestCase("descriptor-size", "OCI descriptor size")]
        [TestCase("missing-layer", "Missing or altered OCI blob")]
        [TestCase("reference-subject", "Attestation reference does not name a runnable sibling")]
        [TestCase("statement-subject", "Statement subject does not match the runnable manifest")]
        [TestCase("spdx-subject", "SPDX document describes an undefined subject")]
        [TestCase("predicate-type", "Predicate type differs from its descriptor")]
        public async Task OciTraversalBindsAttestationLayersToRunnableSubjectsAsync(string mutation, string? finding)
        {
            string work = CreateWorkspace();
            try
            {
                string blobs = Path.Combine(work, "layout", "blobs", "sha256");
                Directory.CreateDirectory(blobs);
                JsonObject config = await BlobDescriptorAsync(blobs, /*lang=json,strict*/ """
                    {"architecture":"amd64","os":"linux","config":{"Labels":{
                    "org.opencontainers.image.version":"2.0.0",
                    "org.opencontainers.image.revision":"1111111111111111111111111111111111111111"}}}
                    """, "application/vnd.oci.image.config.v1+json").ConfigureAwait(false);
                var manifest = new JsonObject
                {
                    ["schemaVersion"] = 2,
                    ["mediaType"] = "application/vnd.oci.image.manifest.v1+json",
                    ["config"] = config,
                    ["layers"] = new JsonArray()
                };
                JsonObject runnable = await BlobDescriptorAsync(
                    blobs, manifest.ToJsonString(), "application/vnd.oci.image.manifest.v1+json")
                    .ConfigureAwait(false);
                string manifestDigest = runnable["digest"]!.GetValue<string>();
                runnable["platform"] = JsonNode.Parse("""{"architecture":"amd64","os":"linux"}""");
                string subjectDigest = mutation == "statement-subject" ? new string('4', 64) : manifestDigest[7..];
                JsonNode statement = JsonNode.Parse($$$"""
                    {"_type":"https://in-toto.io/Statement/v0.1",
                    "subject":[{"name":"pkg:docker/fixture","digest":{"sha256":"{{{subjectDigest}}}"}}],
                    "predicateType":"https://spdx.dev/Document","predicate":{
                    "spdxVersion":"SPDX-2.3","dataLicense":"CC0-1.0","SPDXID":"SPDXRef-DOCUMENT",
                    "name":"fixture","documentNamespace":"https://example.invalid/spdx/fixture",
                    "creationInfo":{"creators":["Tool: golden-fixture"],"created":"2026-09-08T00:00:00Z"},
                    "documentDescribes":["SPDXRef-Image"],"packages":[{
                    "SPDXID":"SPDXRef-Image","name":"fixture","downloadLocation":"NOASSERTION","filesAnalyzed":false}],
                    "relationships":[{"spdxElementId":"SPDXRef-DOCUMENT","relationshipType":"DESCRIBES",
                    "relatedSpdxElement":"SPDXRef-Image"}]}}
                    """)!;
                if (mutation == "spdx-subject")
                {
                    statement["predicate"]!["documentDescribes"] = new JsonArray("SPDXRef-Missing");
                }
                JsonObject layer = await BlobDescriptorAsync(
                    blobs, statement.ToJsonString(), "application/vnd.in-toto+json").ConfigureAwait(false);
                layer["annotations"] = new JsonObject
                {
                    ["in-toto.io/predicate-type"] = mutation == "predicate-type"
                        ? "https://slsa.dev/provenance/v0.2" : "https://spdx.dev/Document"
                };
                if (mutation == "missing-layer")
                {
                    File.Delete(Path.Combine(blobs, layer["digest"]!.GetValue<string>()[7..]));
                }
                JsonObject attestationConfig = await BlobDescriptorAsync(
                    blobs, /*lang=json,strict*/ """{"architecture":"unknown","os":"unknown","config":{}}""",
                    "application/vnd.oci.image.config.v1+json").ConfigureAwait(false);
                var attestationManifest = new JsonObject
                {
                    ["schemaVersion"] = 2,
                    ["mediaType"] = "application/vnd.oci.image.manifest.v1+json",
                    ["config"] = attestationConfig,
                    ["layers"] = new JsonArray(layer)
                };
                JsonObject attestation = await BlobDescriptorAsync(
                    blobs, attestationManifest.ToJsonString(), "application/vnd.oci.image.manifest.v1+json")
                    .ConfigureAwait(false);
                attestation["platform"] = JsonNode.Parse("""{"architecture":"unknown","os":"unknown"}""");
                attestation["annotations"] = new JsonObject
                {
                    ["vnd.docker.reference.type"] = "attestation-manifest",
                    ["vnd.docker.reference.digest"] = mutation == "reference-subject"
                        ? "sha256:" + new string('4', 64) : manifestDigest
                };
                if (mutation == "descriptor-size")
                {
                    runnable["size"] = 1;
                }
                var indexDocument = new JsonObject
                {
                    ["schemaVersion"] = 2,
                    ["mediaType"] = "application/vnd.oci.image.index.v1+json",
                    ["manifests"] = new JsonArray(runnable, attestation)
                };
                string index = await WriteBlobAsync(blobs, indexDocument.ToJsonString()).ConfigureAwait(false);
                JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected.json"))
                    .ConfigureAwait(false))!;
                expected["release"]!["group"] = "pump";
                string context = Path.Combine(work, "context.json");
                await File.WriteAllTextAsync(context, expected.ToJsonString()).ConfigureAwait(false);
                string request = Path.Combine(work, "oci.json");
                await File.WriteAllTextAsync(request, $$"""
                    {"images":[{"id":"ghcr.io/opcfoundation/pumpdeviceintegrationserver",
                    "layout":"layout","rootDigest":"{{index}}"}]}
                    """).ConfigureAwait(false);
                string result = Path.Combine(work, "oci-result.json");
                (int code, string output) = await RunAsync(
                    "oci", "--repository-root", FindRoot(), "--context", context,
                    "--request", request, "--output", result).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(result).ConfigureAwait(false));
                if (mutation == "descriptor-size")
                {
                    Assert.That(report.RootElement.GetProperty("artifacts").GetArrayLength(), Is.EqualTo(1));
                    Assert.That(report.RootElement.GetProperty("artifacts").ToString(),
                        Does.Contain("oci-index").And.Not.Contain("oci-manifest"));
                }
                else
                {
                    Assert.That(report.RootElement.GetProperty("artifacts").GetArrayLength(), Is.EqualTo(2));
                    Assert.That(report.RootElement.GetProperty("artifacts").ToString(),
                        Does.Contain("oci-index").And.Contain("oci-manifest").And.Contain("linux/amd64"));
                }
                Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("incomplete"));
                Assert.That(report.RootElement.GetProperty("unmetControls").ToString(),
                    Does.Contain("SIGNATURE_VERIFIED").And.Contain("INVENTORY_COMPLETE"));
                if (finding != null)
                {
                    Assert.That(report.RootElement.GetProperty("findings").ToString(), Does.Contain(finding));
                }
                else
                {
                    Assert.That(report.RootElement.GetProperty("unmetControls").ToString(),
                        Does.Not.Contain("ARTIFACT_INTEGRITY").And.Not.Contain("ARTIFACT_MEMBERSHIP"));
                    Assert.That(report.RootElement.GetProperty("attestations")[0]
                        .GetProperty("subjectDigest").GetString(), Is.EqualTo(manifestDigest));
                    Assert.That(report.RootElement.GetProperty("attestations")[0]
                        .GetProperty("formatVersion").GetString(), Is.EqualTo("SPDX-2.3"));
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        private static async Task<string> WriteBlobAsync(string root, string json)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await File.WriteAllBytesAsync(Path.Combine(root, hash), bytes).ConfigureAwait(false);
            return "sha256:" + hash;
        }

        private static async Task<JsonObject> BlobDescriptorAsync(string root, string json, string mediaType)
        {
            string digest = await WriteBlobAsync(root, json).ConfigureAwait(false);
            return new JsonObject
            {
                ["mediaType"] = mediaType,
                ["digest"] = digest,
                ["size"] = new FileInfo(Path.Combine(root, digest[7..])).Length
            };
        }

        private static Task<(int Code, string Output)> GenerateAsync(string work)
        {
            return RunAsync(
                "nuget", "--repository-root", FindRoot(), "--packages", Path.Combine(work, "packages"),
                "--inputs", Path.Combine(work, "frozen"), "--context", Path.Combine(work, "expected.json"),
                "--output", Path.Combine(work, "evidence"));
        }

        private static async Task<string> CreateGeneratorPackageAsync(string work)
        {
            string directory = Path.Combine(work, "packages");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "Fixture.Generator.2.0.0.nupkg");
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(Fixture("Generator.nuspec"), "Fixture.Generator.nuspec");
                archive.CreateEntryFromFile(Path.Combine(work, "bin", "net10.0", "Generator.dll"),
                    "analyzers/dotnet/roslyn4.14/cs/Generator.dll");
                archive.CreateEntryFromFile(Path.Combine(work, "bin", "net8.0", "Contributor.dll"),
                    "analyzers/dotnet/roslyn4.0/cs/Contributor.dll");
                archive.CreateEntryFromFile(
                    Path.Combine(work, "cache", "contoso.dependency", "2.3.4", "lib", "net10.0",
                        "Contoso.Dependency.dll"),
                    "analyzers/dotnet/roslyn4.14/cs/Contoso.Dependency.dll");
                using var license = new StreamWriter(archive.CreateEntry("LICENSE.txt").Open());
                await license.WriteAsync("Synthetic non-SPDX license fixture.").ConfigureAwait(false);
            }
            return path;
        }

        private static async Task PrepareCaptureAsync(string work)
        {
            File.Copy(Fixture("Generator.csproj"), Path.Combine(work, "Generator.csproj"));
            File.Copy(Fixture("Contributor.csproj"), Path.Combine(work, "Contributor.csproj"));
            foreach ((string version, string tfm) in new[] { ("1.2.3", "net8.0"), ("2.3.4", "net10.0") })
            {
                string cache = Path.Combine(work, "cache", "contoso.dependency", version);
                Directory.CreateDirectory(Path.Combine(cache, "lib", tfm));
                await File.WriteAllTextAsync(Path.Combine(cache, "lib", tfm, "Contoso.Dependency.dll"), version)
                    .ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(cache, "contoso.dependency.nuspec"),
                    "<package><metadata><id>Contoso.Dependency</id><version>" +
                    version +
                    "</version><license type=\"expression\">MIT OR Apache-2.0</license></metadata></package>")
                    .ConfigureAwait(false);
                Directory.CreateDirectory(Path.Combine(work, "bin", tfm));
                await File.WriteAllTextAsync(Path.Combine(work, "bin", tfm, "Generator.dll"), "generator-" + tfm)
                    .ConfigureAwait(false);
            }
            await File.WriteAllTextAsync(Path.Combine(work, "bin", "net8.0", "Contributor.dll"), "contributor")
                .ConfigureAwait(false);
            JsonNode assets = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("project.assets.json"))
                .ConfigureAwait(false))!;
            assets["packageFolders"]![Path.Combine(work, "cache") + Path.DirectorySeparatorChar] = new JsonObject();
            await File.WriteAllTextAsync(Path.Combine(work, "project.assets.json"), assets.ToJsonString())
                .ConfigureAwait(false);
            JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected.json"))
                .ConfigureAwait(false))!;
            (int gitCode, string sha) = await ExecuteAsync("git", "rev-parse", "HEAD").ConfigureAwait(false);
            Assert.That(gitCode, Is.Zero);
            expected["source"]!["actualSha"] = sha.Trim();
            (int refCode, string references) = await ExecuteAsync(
                "git", "for-each-ref", "--points-at", sha.Trim(), "--format=%(refname)").ConfigureAwait(false);
            Assert.That(refCode, Is.Zero);
            string[] checkoutRefs = references.Split(
                '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.That(checkoutRefs, Is.Not.Empty, "Capture fixtures require a named ref for the actual checkout.");
            expected["source"]!["actualRef"] = checkoutRefs[0];
            (_, string status) = await ExecuteAsync("git", "status", "--porcelain", "--untracked-files=no")
                .ConfigureAwait(false);
            expected["source"]!["trackedClean"] = string.IsNullOrWhiteSpace(status);
            var request = new JsonObject
            {
                ["source"] = expected["source"]!.DeepClone(),
                ["producer"] = expected["producer"]!.DeepClone(),
                ["version"] = "2.0.0",
                ["configuration"] = "Release",
                ["projects"] = new JsonArray(Path.GetRelativePath(FindRoot(), Path.Combine(work, "Generator.csproj"))
                    .Replace('\\', '/'))
            };
            await File.WriteAllTextAsync(Path.Combine(work, "capture.json"), request.ToJsonString())
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(work, "expected.json"), expected.ToJsonString())
                .ConfigureAwait(false);
        }

        private static void CopyContracts(string work)
        {
            string directory = Path.Combine(work, ".azurepipelines");
            foreach ((string folder, string file) in new[]
            {
                ("release", "policy.json"), ("release", "artifacts.json"), ("assurance", "profiles.json"),
                ("release", "evidence.schema.json"), ("nuget", "expected-packages.txt")
            })
            {
                Directory.CreateDirectory(Path.Combine(directory, folder));
                File.Copy(Path.Combine(FindRoot(), ".azurepipelines", folder, file),
                    Path.Combine(directory, folder, file));
            }
        }

        private static string CreateWorkspace()
        {
            string path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, ".work", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string Fixture(string name)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
        }

        private static Task<(int Code, string Output)> RunAsync(params string[] arguments)
        {
            string root = FindRoot();
            string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            string tool = Path.Combine(root, "tools", "Opc.Ua.ReleaseEvidence", "bin", configuration, "net10.0",
                "Opc.Ua.ReleaseEvidence.dll");
            return ExecuteAsync("dotnet", [tool, .. arguments]);
        }

        private static async Task<(int Code, string Output)> ExecuteAsync(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = FindRoot()
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
        }

        private static string FindRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
}
