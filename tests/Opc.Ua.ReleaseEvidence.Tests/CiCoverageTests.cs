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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Exercises capture, inventory, sidecar, and assessment boundaries in process with real local artifact bytes.
    /// </summary>
    [TestFixture]
    public sealed class CiCoverageTests
    {
        /// <summary>
        /// Verifies evaluated project closure, target-specific ownership, frozen bytes, and immutable capture output.
        /// </summary>
        [Test]
        public async Task CapturePreservesEvaluatedClosureAndImmutableInputsAsync()
        {
            using var work = new CiEvidenceWorkspace();
            CaptureRequest request = await work.PrepareCaptureAsync().ConfigureAwait(false);
            var capture = new BuildCapture(work.Files, new ProcessRunner());
            int code = await capture.CaptureAsync(
                CiEvidenceWorkspace.RepositoryRoot, work.At("capture.json"), work.At("captured"),
                CancellationToken.None).ConfigureAwait(false);
            FrozenBundle bundle = await work.Files.ReadModelAsync(
                work.At("captured/build-inputs.json"), EvidenceJsonContext.Default.FrozenBundle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(code, Is.Zero);
            Assert.That(bundle.Source, Is.EqualTo(request.Source));
            Assert.That((bundle.Producer.System, bundle.Producer.Workflow, bundle.Producer.DefinitionSha,
                bundle.Producer.RunId, bundle.Producer.Attempt, bundle.Producer.Job),
                Is.EqualTo((request.Producer.System, request.Producer.Workflow, request.Producer.DefinitionSha,
                    request.Producer.RunId, request.Producer.Attempt, request.Producer.Job)));
            Assert.That(bundle.Producer.Tools, Is.EqualTo(request.Producer.Tools));
            Assert.That(bundle.Configuration, Is.EqualTo("Release"));
            Assert.That(bundle.Mappings, Has.Length.EqualTo(2));
            ProjectMapping generator = bundle.Mappings.Single(m => m.IsPackable);
            ProjectMapping contributor = bundle.Mappings.Single(m => !m.IsPackable);
            Assert.That(generator.PackageId, Is.EqualTo("Fixture.Generator"));
            Assert.That(generator.Tfms, Is.EqualTo(s_expectedTfms));
            Assert.That(generator.References, Is.EqualTo([contributor.Project]));
            Assert.That(generator.Components.Select(c => (c.Id, c.Version, c.Target)),
                Is.EquivalentTo(s_expectedRestoredComponents));
            Assert.That(generator.Components.All(c => c.Project == generator.Project), Is.True);
            Assert.That(generator.Payloads.Count(p => p.Origin == "package"), Is.EqualTo(2));
            Assert.That(generator.Payloads.Single(p => p.File == "Generator.pdb").Roslyn, Is.EqualTo("4.14"));
            Assert.That(contributor.Payloads.Single(p => p.File == "Contributor.dll").Target, Is.EqualTo("net8.0"));
            Assert.That(bundle.Contracts, Has.Length.EqualTo(5));
            foreach (FrozenFile frozen in bundle.Contracts.Concat(bundle.Mappings.Select(m => m.Assets)))
            {
                byte[] bytes = await File.ReadAllBytesAsync(
                    Path.Combine(work.At("captured"), frozen.Path)).ConfigureAwait(false);
                Assert.That(frozen.Digest, Is.EqualTo(CiEvidenceWorkspace.Hash(bytes)), frozen.Path);
                Assert.That(frozen.Size, Is.EqualTo(bytes.LongLength), frozen.Path);
            }
            byte[] original = await File.ReadAllBytesAsync(work.At("captured/build-inputs.json"))
                .ConfigureAwait(false);
            Assert.That(() => capture.CaptureAsync(
                CiEvidenceWorkspace.RepositoryRoot, work.At("capture.json"), work.At("captured"),
                CancellationToken.None), Throws.TypeOf<InvalidDataException>().With.Message.Contains("immutable"));
            Assert.That(await File.ReadAllBytesAsync(work.At("captured/build-inputs.json")).ConfigureAwait(false),
                Is.EqualTo(original));
        }

        /// <summary>
        /// Verifies that unsupported restored graphs and mismatched cache identities cannot become frozen evidence.
        /// </summary>
        [TestCase("assets-version", "Unsupported NuGet assets format.")]
        [TestCase("cache-identity", "Package cache nuspec does not match the restored identity.")]
        public async Task CaptureRejectsUnsupportedOrInconsistentRestoredInputsAsync(string scenario, string message)
        {
            using var work = new CiEvidenceWorkspace();
            await work.PrepareCaptureAsync().ConfigureAwait(false);
            if (scenario == "assets-version")
            {
                JsonObject assets = await work.ReadObjectAsync("project.assets.json").ConfigureAwait(false);
                assets["version"] = 2;
                await work.WriteJsonAsync("project.assets.json", assets).ConfigureAwait(false);
            }
            else
            {
                await File.WriteAllTextAsync(work.At("cache/contoso.dependency/1.2.3/contoso.dependency.nuspec"),
                    """
                    <package><metadata><id>Different.Dependency</id><version>1.2.3</version></metadata></package>
                    """).ConfigureAwait(false);
            }
            Assert.That(() => new BuildCapture(work.Files, new ProcessRunner()).CaptureAsync(
                CiEvidenceWorkspace.RepositoryRoot, work.At("capture.json"), work.At("captured"),
                CancellationToken.None), Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(message));
            Assert.That(File.Exists(work.At("captured/build-inputs.json")), Is.False);
        }

        /// <summary>
        /// Verifies actual payload ownership and CycloneDX dependency edges without confusing build and consumer scope.
        /// </summary>
        [Test]
        public async Task ReconciledCycloneDxPreservesPayloadsLicensesAndScopedDependenciesAsync()
        {
            using var work = new CiEvidenceWorkspace();
            await work.PreparePackageAsync().ConfigureAwait(false);
            PackageInventory inventory = await new PackageReconciler(work.Files).ReconcileAsync(
                work.ArchivePath, "2.0.0", work.Mappings, [], CancellationToken.None).ConfigureAwait(false);

            Assert.That(inventory.UnmetControls, Is.Empty);
            Assert.That(inventory.Artifact.Id, Is.EqualTo("Fixture.Library"));
            Assert.That(inventory.Artifact.Digest, Is.EqualTo(CiEvidenceWorkspace.Hash(
                await File.ReadAllBytesAsync(work.ArchivePath).ConfigureAwait(false))));
            Assert.That(inventory.Artifact.Size, Is.EqualTo(new FileInfo(work.ArchivePath).Length));
            Assert.That(inventory.Artifact.Scopes.Tfms, Is.EqualTo(s_expectedTfms));
            Assert.That(inventory.Artifact.Scopes.Rids, Is.EqualTo(s_expectedRids));
            Assert.That(inventory.Artifact.Scopes.Roslyn, Is.EqualTo(s_expectedRoslyn));
            Assert.That(inventory.ConsumerDependencies, Is.EqualTo(
            [
                new ConsumerDependency("Contoso.Dependency", "[2.0.0,3.0.0)", "net10.0"),
                new ConsumerDependency("Contoso.Dependency", "[1.0.0,2.0.0)", "net8.0")
            ]));
            Assert.That(inventory.ExternalPrerequisites, Is.EqualTo(s_expectedPrerequisites));
            Assert.That(inventory.Licenses, Is.EqualTo(
            [
                new LicenseRecord("file", "LICENSE.txt", CiEvidenceWorkspace.Hash(work.Entries["LICENSE.txt"]))
            ]));
            Assert.That(inventory.Payloads, Has.Length.EqualTo(work.Entries.Count));
            foreach (InventoryPayload payload in inventory.Payloads)
            {
                Assert.That(payload.Digest, Is.EqualTo(CiEvidenceWorkspace.Hash(work.Entries[payload.Path])),
                    payload.Path);
            }
            Assert.That(inventory.Payloads.Single(p => p.Path.EndsWith("Contributor.dll", StringComparison.Ordinal))
                .Owner, Is.EqualTo("Fixture.Contributor"));
            Assert.That(inventory.Payloads.Single(p => p.Path.EndsWith("native.so", StringComparison.Ordinal))
                .Classification, Is.EqualTo("native"));
            Assert.That(inventory.Payloads.Single(p => p.Path.EndsWith(".pdb", StringComparison.Ordinal))
                .Classification, Is.EqualTo("symbols"));

            using var bom = JsonDocument.Parse(CycloneDxInventory.Serialize(inventory));
            JsonElement root = bom.RootElement;
            Assert.That(root.GetProperty("bomFormat").GetString(), Is.EqualTo("CycloneDX"));
            Assert.That(root.GetProperty("specVersion").GetString(), Is.EqualTo("1.6"));
            JsonElement component = root.GetProperty("metadata").GetProperty("component");
            Assert.That(component.GetProperty("purl").GetString(), Is.EqualTo("pkg:nuget/Fixture.Library@2.0.0"));
            Assert.That(component.GetProperty("hashes")[0].GetProperty("content").GetString(),
                Is.EqualTo(inventory.Artifact.Digest[7..]));
            JsonElement license = component.GetProperty("licenses")[0].GetProperty("license");
            Assert.That(license.GetProperty("name").GetString(), Is.EqualTo("License file: LICENSE.txt"));
            Assert.That(Property(license, "license-file-digest"), Is.EqualTo(inventory.Licenses[0].Digest));
            JsonElement[] components = [.. root.GetProperty("components").EnumerateArray()];
            JsonElement shipped = components.Single(c => c.GetProperty("bom-ref").GetString() ==
                "resolved:src/Library.csproj:net10.0:Contoso.Dependency:2.3.4");
            JsonElement buildOnly = components.Single(c => c.GetProperty("bom-ref").GetString() ==
                "resolved:src/Library.csproj:net8.0:Contoso.Dependency:1.2.3");
            Assert.That(Property(shipped, "ownership"), Is.EqualTo("shipped"));
            Assert.That(Property(buildOnly, "ownership"), Is.EqualTo("build-only"));
            Assert.That(shipped.GetProperty("licenses")[0].GetProperty("expression").GetString(),
                Is.EqualTo("MIT OR Apache-2.0"));
            Assert.That(buildOnly.GetProperty("licenses")[0].GetProperty("license").GetProperty("url").GetString(),
                Is.EqualTo("https://example.invalid/license"));
            Assert.That(components.Any(c => c.GetProperty("bom-ref").GetString() == "payload:LICENSE.txt"), Is.False);
            JsonElement project = components.Single(c => c.GetProperty("bom-ref").GetString() ==
                "resolved:src/Library.csproj:net10.0:Fixture.Library:2.0.0");
            Assert.That(project.TryGetProperty("purl", out _), Is.False);
            JsonElement[] dependencies = [.. root.GetProperty("dependencies").EnumerateArray()];
            Assert.That(dependencies.Single(d => d.GetProperty("ref").GetString() ==
                project.GetProperty("bom-ref").GetString()).GetProperty("dependsOn")
                .EnumerateArray().Select(d => d.GetString()), Is.EqualTo(s_expectedProjectDependencies));
            string?[] rootReferences = [.. dependencies.Single(d => d.GetProperty("ref").GetString() ==
                "artifact:" + inventory.Artifact.Digest).GetProperty("dependsOn")
                .EnumerateArray().Select(d => d.GetString())];
            Assert.That(rootReferences, Does.Contain(shipped.GetProperty("bom-ref").GetString()));
            Assert.That(rootReferences, Does.Not.Contain(buildOnly.GetProperty("bom-ref").GetString()));
            Assert.That(rootReferences, Does.Contain("consumer:net8.0:Contoso.Dependency"));
            Assert.That(rootReferences, Does.Contain("prerequisite:Microsoft.AspNetCore.App"));
        }

        /// <summary>
        /// Verifies metadata-only packages retain declared consumer ranges without inventing resolved runtime payloads.
        /// </summary>
        [Test]
        public async Task MetapackageInventoryRetainsDeclarationsWithoutInventingShippedComponentsAsync()
        {
            using var work = new CiEvidenceWorkspace();
            await work.PreparePackageAsync().ConfigureAwait(false);
            string path = work.At("packages/metadata-only.nupkg");
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (string name in new[] { "Fixture.Library.nuspec", "LICENSE.txt", "README.txt" })
                {
                    await using Stream stream = archive.CreateEntry(name).Open();
                    await stream.WriteAsync(work.Entries[name]).ConfigureAwait(false);
                }
            }
            PackageInventory inventory = await new PackageReconciler(work.Files).ReconcileAsync(
                path, "2.0.0", [], ["fixture.library"], CancellationToken.None).ConfigureAwait(false);
            Assert.That(inventory.UnmetControls, Is.Empty);
            Assert.That(inventory.Artifact.Id, Is.EqualTo("Fixture.Library"));
            Assert.That(inventory.Artifact.Configuration, Is.EqualTo("Release"));
            Assert.That(inventory.Artifact.Scopes.Tfms, Is.Empty);
            Assert.That(inventory.ResolvedGraphs, Is.Empty);
            Assert.That(inventory.Payloads, Has.Length.EqualTo(3));
            Assert.That(inventory.Payloads.All(p => p.Classification == "metadata" && p.Owner == null), Is.True);
            Assert.That(inventory.ConsumerDependencies, Is.EqualTo(
            [
                new ConsumerDependency("Contoso.Dependency", "[2.0.0,3.0.0)", "net10.0"),
                new ConsumerDependency("Contoso.Dependency", "[1.0.0,2.0.0)", "net8.0")
            ]));
            using var bom = JsonDocument.Parse(CycloneDxInventory.Serialize(inventory));
            JsonElement[] components = [.. bom.RootElement.GetProperty("components").EnumerateArray()];
            Assert.That(components, Has.Length.EqualTo(4));
            Assert.That(components.Select(c => Property(c, "ownership")),
                Is.EquivalentTo(s_expectedMetapackageOwnership));
            Assert.That(components.All(c => !c.TryGetProperty("version", out _)), Is.True);
        }

        /// <summary>
        /// Verifies that ambiguity and missing license bytes remain explicit inventory failures with preserved hashes.
        /// </summary>
        [TestCase("managed", "lib/net10.0/Library.dll", "unowned")]
        [TestCase("native", "runtimes/linux-x64/native/native.so", "native-unowned")]
        [TestCase("license", "LICENSE.txt", "metadata")]
        public async Task ReconciliationDoesNotInventOwnershipOrLicenseContentAsync(
            string scenario, string path, string classification)
        {
            using var work = new CiEvidenceWorkspace();
            await work.PreparePackageAsync(includeLicense: scenario != "license").ConfigureAwait(false);
            ProjectMapping[] mappings = work.Mappings;
            if (scenario != "license")
            {
                PayloadCandidate candidate = mappings[0].Payloads.Single(p =>
                    Path.GetFileName(p.File) == Path.GetFileName(path));
                mappings =
                [
                    mappings[0] with
                    {
                        Payloads = [.. mappings[0].Payloads, candidate with { Owner = "Different.Owner" }]
                    },
                    mappings[1]
                ];
            }
            PackageInventory inventory = await new PackageReconciler(work.Files).ReconcileAsync(
                work.ArchivePath, "2.0.0", mappings, [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(inventory.UnmetControls, Is.EqualTo(s_incompleteInventoryControls));
            if (scenario == "license")
            {
                Assert.That(inventory.Licenses.Single().Digest, Is.Null);
                Assert.That(inventory.Payloads.Any(p => p.Path == path), Is.False);
            }
            else
            {
                InventoryPayload payload = inventory.Payloads.Single(p => p.Path == path);
                Assert.That(payload.Classification, Is.EqualTo(classification));
                Assert.That(payload.Owner, Is.Null);
                Assert.That(payload.Version, Is.Null);
                Assert.That(payload.Digest, Is.EqualTo(CiEvidenceWorkspace.Hash(work.Entries[path])));
                using var bom = JsonDocument.Parse(CycloneDxInventory.Serialize(inventory));
                JsonElement component = bom.RootElement.GetProperty("components").EnumerateArray().Single(c =>
                    c.GetProperty("bom-ref").GetString() == "payload:" + path);
                Assert.That(Property(component, "ownership"), Is.EqualTo("unknown"));
                Assert.That(Property(component, "classification"), Is.EqualTo(classification));
            }
        }

        /// <summary>
        /// Verifies archive identity and contributor closure rejection before any inventory can be returned.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The fixture scenario is unsupported.</exception>
        [TestCase("version", "Archive nuspec version differs")]
        [TestCase("unmapped", "No evaluated package mapping")]
        [TestCase("duplicate-owner", "multiple evaluated")]
        [TestCase("missing-contributor", "contributing project is missing")]
        public async Task ReconciliationRequiresExactVersionAndUnambiguousProjectClosureAsync(
            string scenario, string message)
        {
            using var work = new CiEvidenceWorkspace();
            await work.PreparePackageAsync().ConfigureAwait(false);
            ProjectMapping[] mappings = scenario switch
            {
                "unmapped" => [],
                "duplicate-owner" => [.. work.Mappings, work.Mappings[0] with { Configuration = "Debug" }],
                "missing-contributor" => [work.Mappings[0]],
                "version" => work.Mappings,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            Assert.That(() => new PackageReconciler(work.Files).ReconcileAsync(
                work.ArchivePath, scenario == "version" ? "2.0.1" : "2.0.0", mappings, [],
                CancellationToken.None), Throws.TypeOf<InvalidDataException>().With.Message.Contains(message));
        }

        /// <summary>
        /// Verifies that symbol payloads require evaluated symbol settings and retain their project ownership.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task SymbolArchivesRequireEvaluatedSymbolOptInAsync(bool includeSymbols)
        {
            using var work = new CiEvidenceWorkspace();
            await work.PreparePackageAsync(symbols: true).ConfigureAwait(false);
            ProjectMapping[] mappings =
            [
                work.Mappings[0] with { IncludeSymbols = includeSymbols },
                work.Mappings[1]
            ];
            PackageInventory inventory = await new PackageReconciler(work.Files).ReconcileAsync(
                work.ArchivePath, "2.0.0", mappings, [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(inventory.Artifact.Kind, Is.EqualTo("nuget-symbols"));
            Assert.That(inventory.Payloads.Single(p => p.Classification != "metadata").Path,
                Is.EqualTo("lib/net10.0/Library.pdb"));
            Assert.That(inventory.Payloads.Single(p => p.Classification != "metadata").Owner,
                Is.EqualTo("Fixture.Library"));
            string[] expectedControls = includeSymbols ? [] : ["ARTIFACT_MEMBERSHIP"];
            Assert.That(inventory.UnmetControls, Is.EqualTo(expectedControls));
        }

        /// <summary>
        /// Verifies digest-bound sidecars and source mappings while retaining original archives and manifest bytes.
        /// </summary>
        [TestCase("development", 0)]
        [TestCase("stable", 1)]
        public async Task NugetGenerationPreservesBytesAndReportsUnauthenticatedControlsAsync(
            string channel, int expectedExit)
        {
            using var work = new CiEvidenceWorkspace();
            EvaluationExpectation context = await work.PrepareNugetAsync(channel).ConfigureAwait(false);
            byte[] archive = await File.ReadAllBytesAsync(work.ArchivePath).ConfigureAwait(false);
            byte[] manifest = await File.ReadAllBytesAsync(work.At("manifest.json")).ConfigureAwait(false);
            int code = await work.GenerateAsync().ConfigureAwait(false);
            EvidenceEnvelope envelope = await work.ReadEnvelopeAsync().ConfigureAwait(false);
            SourceInputsRecord source = await work.Files.ReadModelAsync(
                work.At("evidence/source-inputs.json"), EvidenceJsonContext.Default.SourceInputsRecord,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(code, Is.EqualTo(expectedExit));
            Assert.That(await File.ReadAllBytesAsync(work.ArchivePath).ConfigureAwait(false), Is.EqualTo(archive));
            Assert.That(await File.ReadAllBytesAsync(work.At("evidence/release-manifest.json")).ConfigureAwait(false),
                Is.EqualTo(manifest));
            Assert.That(source.Source, Is.EqualTo(context.Source));
            Assert.That(source.Mappings, Has.Length.EqualTo(2));
            Assert.That(source.Mappings.Single(m => m.IsPackable).HasPdb, Is.True);
            Assert.That(source.Mappings.Single(m => !m.IsPackable).Project, Is.EqualTo("src/Contributor.csproj"));
            Assert.That(source.BuildInputDigests, Is.EqualTo(
            [
                CiEvidenceWorkspace.Hash(await File.ReadAllBytesAsync(work.At("frozen/build-inputs.json"))
                    .ConfigureAwait(false))
            ]));
            Assert.That(envelope.Artifacts, Has.Length.EqualTo(1));
            Assert.That(envelope.Artifacts[0].Digest, Is.EqualTo(CiEvidenceWorkspace.Hash(archive)));
            Assert.That(envelope.Assessment.Status, Is.EqualTo("incomplete"));
            Assert.That(envelope.Assessment.UnmetControls, Does.Contain("SIGNATURE_VERIFIED"));
            Assert.That(envelope.Assessment.UnmetControls, Does.Contain("PROVENANCE_VERIFIED"));
            Assert.That(envelope.Assessment.UnmetControls, Does.Contain("ARTIFACT_MEMBERSHIP"));
            Assert.That(envelope.Assurance.Expected, Is.EqualTo(7));
            Assert.That(envelope.Assurance.Missing, Is.EqualTo(7));
            Assert.That(envelope.Assurance.Selected, Is.Zero);
            Assert.That(envelope.Assurance.Completed, Is.Zero);
            Assert.That(envelope.Documents.Count(d => d.Type == "inventory"), Is.EqualTo(1));
            Assert.That(envelope.Documents.Count(d => d.Type == "sbom"), Is.EqualTo(1));
            foreach (DocumentRecord document in envelope.Documents)
            {
                byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(work.At("evidence"), document.Path))
                    .ConfigureAwait(false);
                Assert.That(document.Digest, Is.EqualTo(CiEvidenceWorkspace.Hash(bytes)), document.Path);
            }
            byte[] original = await File.ReadAllBytesAsync(work.At("evidence/release-evidence.json"))
                .ConfigureAwait(false);
            Assert.That(() => work.GenerateAsync(),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("empty"));
            Assert.That(await File.ReadAllBytesAsync(work.At("evidence/release-evidence.json")).ConfigureAwait(false),
                Is.EqualTo(original));
        }

        /// <summary>
        /// Verifies that changed frozen inputs or duplicate mappings stop sidecar publication.
        /// </summary>
        [TestCase("bytes", "Frozen input bytes changed")]
        [TestCase("size", "Frozen input bytes changed")]
        [TestCase("source", "different source/version/build attempt")]
        [TestCase("attempt", "different source/version/build attempt")]
        [TestCase("duplicate", "Duplicate project/configuration")]
        public async Task NugetGenerationRejectsChangedOrDuplicatedFrozenInputsAsync(string scenario, string message)
        {
            using var work = new CiEvidenceWorkspace();
            await work.PrepareNugetAsync().ConfigureAwait(false);
            FrozenBundle bundle = await work.Files.ReadModelAsync(
                work.At("frozen/build-inputs.json"), EvidenceJsonContext.Default.FrozenBundle,
                CancellationToken.None).ConfigureAwait(false);
            switch (scenario)
            {
                case "bytes":
                    await File.AppendAllTextAsync(work.At("frozen/graphs/project.assets.json"), " ")
                        .ConfigureAwait(false);
                    break;
                case "size":
                    bundle = bundle with
                    {
                        Contracts =
                        [
                            bundle.Contracts[0] with { Size = bundle.Contracts[0].Size + 1 },
                            .. bundle.Contracts.Skip(1)
                        ]
                    };
                    break;
                case "source":
                    bundle = bundle with { Source = bundle.Source with { ActualSha = new string('f', 40) } };
                    break;
                case "attempt":
                    bundle = bundle with { Producer = bundle.Producer with { Attempt = bundle.Producer.Attempt + 1 } };
                    break;
            }
            await work.WriteModelAsync("frozen/build-inputs.json", bundle, EvidenceJsonContext.Default.FrozenBundle)
                .ConfigureAwait(false);
            string[] inputs = scenario == "duplicate"
                ? [work.At("frozen"), work.At("frozen")] : [work.At("frozen")];
            Assert.That(() => new NugetEvidence(work.Files, new PackageReconciler(work.Files)).GenerateAsync(
                work.Root, work.At("packages"), inputs, work.At("context.json"), work.At("manifest.json"),
                work.At("evidence"), CancellationToken.None),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains(message));
            Assert.That(File.Exists(work.At("evidence/release-evidence.json")), Is.False);
            Assert.That(Directory.GetFiles(work.At("evidence"), "*", SearchOption.AllDirectories), Is.Empty);
        }

        /// <summary>
        /// Verifies local document validation separately from independent authentication and identifies exact failures.
        /// </summary>
        [TestCase("valid", "", "")]
        [TestCase("missing", "ARTIFACT_INTEGRITY", "Missing or altered document:")]
        [TestCase("subject", "ARTIFACT_INTEGRITY", "A document is bound to the wrong artifact subject.")]
        [TestCase("bom-version", "INVENTORY_COMPLETE", "The NuGet SBOM is not CycloneDX JSON 1.6.")]
        [TestCase("bom-schema", "INVENTORY_COMPLETE", "CycloneDX 1.6 schema validation failed.")]
        [TestCase("bom-hash", "ARTIFACT_INTEGRITY", "SBOM content disagrees with its artifact subject.")]
        [TestCase("inventory-identity", "ARTIFACT_INTEGRITY", "Inventory content disagrees with its document subject.")]
        [TestCase("unowned", "INVENTORY_COMPLETE", "Actual inventory has unknown ownership or unmet controls.")]
        public async Task EvaluatorChecksDocumentContentRatherThanOnlyRecordedDigestsAsync(
            string scenario, string control, string detail)
        {
            using var work = new CiEvidenceWorkspace();
            EvaluationExpectation expected = await work.PrepareNugetAsync().ConfigureAwait(false);
            Assert.That(await work.GenerateAsync().ConfigureAwait(false), Is.Zero);
            EvidenceEnvelope envelope = await work.ReadEnvelopeAsync().ConfigureAwait(false);
            DocumentRecord document = envelope.Documents.Single(d =>
                d.Type == (scenario is "inventory-identity" or "unowned" ? "inventory" : "sbom"));
            DocumentRecord replacement = document;
            string relative = "evidence/" + document.Path;
            if (scenario == "missing")
            {
                await File.WriteAllTextAsync(work.At(relative), "{}").ConfigureAwait(false);
            }
            else if (scenario == "subject")
            {
                replacement = document with { Subject = document.Subject with { Id = "Different.Package" } };
            }
            else if (scenario != "valid")
            {
                JsonObject payload = await work.ReadObjectAsync(relative).ConfigureAwait(false);
                switch (scenario)
                {
                    case "bom-version":
                        payload["specVersion"] = "1.5";
                        break;
                    case "bom-schema":
                        payload["unexpectedProperty"] = true;
                        break;
                    case "bom-hash":
                        payload["metadata"]!["component"]!["hashes"]![0]!["content"] = new string('0', 64);
                        break;
                    case "inventory-identity":
                        payload["artifact"]!["id"] = "Different.Package";
                        break;
                    case "unowned":
                        JsonObject unowned = payload["payloads"]!.AsArray().Single(p =>
                            p!["path"]!.GetValue<string>() == "lib/net10.0/Library.dll")!.AsObject();
                        unowned["classification"] = "unowned";
                        unowned.Remove("owner");
                        unowned.Remove("version");
                        break;
                }
                await work.WriteJsonAsync(relative, payload).ConfigureAwait(false);
                replacement = document with
                {
                    Digest = CiEvidenceWorkspace.Hash(await File.ReadAllBytesAsync(work.At(relative))
                        .ConfigureAwait(false))
                };
            }
            envelope = envelope with
            {
                Documents = [.. envelope.Documents.Select(d => d == document ? replacement : d)]
            };
            (int code, EvaluationReport report) = await work.EvaluateAsync(
                envelope, expected with { Artifacts = envelope.Artifacts }).ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            Assert.That(report.Status, Is.EqualTo("incomplete"));
            Assert.That(report.Blocking, Is.False);
            Assert.That(report.BaselineFailed, Is.False);
            Assert.That(report.ExternalVerificationPerformed, Is.False);
            Assert.That(report.InputEvidenceDigest, Is.EqualTo(CiEvidenceWorkspace.Hash(
                await File.ReadAllBytesAsync(work.At("evidence/release-evidence.json")).ConfigureAwait(false))));
            if (scenario == "valid")
            {
                Assert.That(report.Findings.Where(f =>
                    f.Code == "INVENTORY_COMPLETE" ||
                    f.Detail.Contains("document", StringComparison.Ordinal) ||
                    f.Detail.Contains("SBOM", StringComparison.Ordinal)), Is.Empty);
            }
            else
            {
                Assert.That(report.Findings.Any(f => f.Code == control &&
                    f.Detail.StartsWith(detail, StringComparison.Ordinal)), Is.True, detail);
            }
        }

        /// <summary>
        /// Verifies that actual failed, empty, or skipped assurance executions block even a development assessment.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The fixture scenario is unsupported.</exception>
        [TestCase("failed", true)]
        [TestCase("empty", true)]
        [TestCase("skipped", true)]
        [TestCase("unknown", false)]
        public async Task EvaluatorPreservesObservedAssuranceFailuresInDevelopmentAsync(string scenario, bool blocking)
        {
            using var work = new CiEvidenceWorkspace();
            EvaluationExpectation expected = await work.PrepareNugetAsync().ConfigureAwait(false);
            await work.GenerateAsync().ConfigureAwait(false);
            EvidenceEnvelope envelope = await work.ReadEnvelopeAsync().ConfigureAwait(false);
            ProfilesConfiguration profiles = await work.Files.ReadModelAsync(
                work.At(".azurepipelines/assurance/profiles.json"),
                EvidenceJsonContext.Default.ProfilesConfiguration, CancellationToken.None).ConfigureAwait(false);
            ProfileConfiguration profile = profiles.Profiles.Single(p => p.Id == "security-net10");
            ProfileJob definition = profile.Jobs[0];
            CountsRecord? counts = scenario switch
            {
                "failed" => new CountsRecord(Total: 1, Executed: 1, Passed: 0, Failed: 1, Skipped: 0),
                "empty" => new CountsRecord(Total: 0, Executed: 0, Passed: 0, Failed: 0, Skipped: 0),
                "skipped" => new CountsRecord(Total: 2, Executed: 1, Passed: 1, Failed: 0, Skipped: 1),
                "unknown" => null,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var job = new JobRecord(
                definition.Id, profile.Id, definition.Project, profile.Configuration, profile.Host,
                profile.HostTfm, profile.LibraryTfm, profile.Platform, "all", profile.Filter, true, "completed", [],
                envelope.Source.ActualSha, envelope.Producer, Counts: counts);
            envelope = envelope with
            {
                Assurance = new AssuranceRecord(
                    envelope.Assurance.Profiles, 1, 1, 1, 0, 0, 0, [job], envelope.Assurance.InputIdentities)
            };
            (int code, EvaluationReport report) = await work.EvaluateAsync(
                envelope, expected with { Artifacts = envelope.Artifacts }).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(blocking ? 1 : 0));
            Assert.That(report.Blocking, Is.EqualTo(blocking));
            Assert.That(report.BaselineFailed, Is.EqualTo(blocking));
            Assert.That(report.ExternalVerificationPerformed, Is.False);
            Assert.That(report.Findings, Does.Contain(new Finding("ASSURANCE_COMPLETE", blocking
                ? $"Failed, zero or inconsistent executed results: {job.Id}."
                : $"Applicable result counts are unknown: {job.Id}.")));
        }

        /// <summary>
        /// Verifies that legacy evidence remains incomplete and only the requested release channel changes blocking.
        /// </summary>
        [TestCase("stable", 1)]
        [TestCase("development", 0)]
        public async Task LegacyEvaluationNeverClaimsAuthenticationOrContractCompletionAsync(string channel, int code)
        {
            using var work = new CiEvidenceWorkspace();
            EvaluationExpectation expected = await work.ReadExpectationAsync().ConfigureAwait(false);
            expected = expected with { Release = expected.Release with { Channel = channel } };
            await work.WriteModelAsync("context.json", expected, EvidenceJsonContext.Default.EvaluationExpectation)
                .ConfigureAwait(false);
            int actual = await new EvidenceEvaluator(work.Files).EvaluateAsync(
                CiEvidenceWorkspace.RepositoryRoot, CiEvidenceWorkspace.Fixture("v1.json"), work.At("context.json"),
                work.At("report.json"), null, CancellationToken.None).ConfigureAwait(false);
            EvaluationReport report = await work.Files.ReadModelAsync(
                work.At("report.json"), EvidenceJsonContext.Default.EvaluationReport,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(code));
            Assert.That(report.Blocking, Is.EqualTo(code == 1));
            Assert.That(report.Status, Is.EqualTo("incomplete"));
            Assert.That(report.BaselineFailed, Is.False);
            Assert.That(report.ExternalVerificationPerformed, Is.False);
            Assert.That(report.UnmetControls, Is.EqualTo(s_legacyControls));
        }

        /// <summary>
        /// Verifies schema and model rejection without writing a report that could be mistaken for an assessment.
        /// </summary>
        [TestCase(/*lang=json,strict*/ "{\"schemaVersion\":3}", false)]
        [TestCase(/*lang=json,strict*/ "{\"schemaVersion\":2}", false)]
        [TestCase(/*lang=json,strict*/ "{\"schemaVersion\":2,\"schemaVersion\":1}", true)]
        public async Task InvalidEvidenceCannotProduceAnAssessmentReportAsync(string json, bool duplicate)
        {
            using var work = new CiEvidenceWorkspace();
            await File.WriteAllTextAsync(work.At("invalid.json"), json).ConfigureAwait(false);
            var evaluator = new EvidenceEvaluator(work.Files);
            if (duplicate)
            {
                Assert.That(() => evaluator.EvaluateAsync(
                    CiEvidenceWorkspace.RepositoryRoot, work.At("invalid.json"),
                    CiEvidenceWorkspace.Fixture("expected.json"), work.At("report.json"), null,
                    CancellationToken.None), Throws.TypeOf<JsonException>().With.Message.Contains("Duplicate"));
            }
            else
            {
                Assert.That(() => evaluator.EvaluateAsync(
                    CiEvidenceWorkspace.RepositoryRoot, work.At("invalid.json"),
                    CiEvidenceWorkspace.Fixture("expected.json"), work.At("report.json"), null,
                    CancellationToken.None), Throws.TypeOf<InvalidDataException>());
            }
            Assert.That(File.Exists(work.At("report.json")), Is.False);
        }

        private static string? Property(JsonElement component, string name)
        {
            return component.GetProperty("properties").EnumerateArray().Single(p =>
                p.GetProperty("name").GetString() == "opcua:" + name).GetProperty("value").GetString();
        }

        private static readonly string[] s_expectedTfms = ["net10.0", "net8.0"];

        private static readonly (string Id, string Version, string Target)[] s_expectedRestoredComponents =
        [
            ("Contoso.Dependency", "1.2.3", "net8.0"),
            ("Contoso.Dependency", "2.3.4", "net10.0")
        ];

        private static readonly string[] s_expectedRids = ["linux-x64"];
        private static readonly string[] s_expectedRoslyn = ["roslyn4.0", "roslyn4.14"];
        private static readonly string[] s_expectedPrerequisites = ["Microsoft.AspNetCore.App", "System.Xml"];

        private static readonly string[] s_expectedProjectDependencies =
            ["resolved:src/Library.csproj:net10.0:Contoso.Dependency:2.3.4"];

        private static readonly string[] s_expectedMetapackageOwnership =
            ["declared-consumer", "declared-consumer", "external-prerequisite", "external-prerequisite"];

        private static readonly string[] s_incompleteInventoryControls = ["INVENTORY_COMPLETE"];
        private static readonly string[] s_legacyControls = ["EVIDENCE_SCHEMA", "POLICY_IDENTITY"];
    }

    /// <summary>
    /// Owns isolated test artifacts and builds small unsigned inputs without replacing any authentication service.
    /// </summary>
    internal sealed class CiEvidenceWorkspace : IDisposable
    {
        /// <summary>
        /// Creates a unique repository-local fixture directory suitable for confined build capture.
        /// </summary>
        internal CiEvidenceWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(
                RepositoryRoot, "TestResults", "release-evidence-ci", Guid.NewGuid().ToString("N"))).FullName;
        }

        /// <summary>
        /// Gets the fixture directory owned by this instance.
        /// </summary>
        internal string Root { get; }

        /// <summary>
        /// Gets the real bounded evidence-file implementation used by the tests.
        /// </summary>
        internal EvidenceFiles Files { get; } = new();

        /// <summary>
        /// Gets the generated archive path.
        /// </summary>
        internal string ArchivePath { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the archive's known input bytes for independent content assertions.
        /// </summary>
        internal Dictionary<string, byte[]> Entries { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the packable root and nonpackable contributor mappings supplied to reconciliation.
        /// </summary>
        internal ProjectMapping[] Mappings { get; private set; } = [];

        /// <summary>
        /// Gets the repository root from the test location or the runner's working directory.
        /// </summary>
        /// <exception cref="DirectoryNotFoundException">The repository root could not be located.</exception>
        internal static string RepositoryRoot
        {
            get
            {
                foreach (string start in new[]
                {
                    TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory
                })
                {
                    DirectoryInfo? directory = new(start);
                    while (directory != null)
                    {
                        if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                        {
                            return directory.FullName;
                        }
                        directory = directory.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Release evidence fixtures require the repository root.");
            }
        }

        /// <summary>
        /// Removes only the unique fixture directory created by this instance.
        /// </summary>
        public void Dispose()
        {
            Directory.Delete(Root, true);
        }

        /// <summary>
        /// Resolves a fixture-relative path using the host's directory separator.
        /// </summary>
        internal string At(string relative)
        {
            return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Locates an existing checked-in fixture without depending on copied test output.
        /// </summary>
        internal static string Fixture(string name)
        {
            return Path.Combine(RepositoryRoot, "tests", "Opc.Ua.ReleaseEvidence.Tests", "Fixtures", name);
        }

        /// <summary>
        /// Calculates the expected SHA-256 identity directly from known input bytes.
        /// </summary>
        internal static string Hash(ReadOnlySpan<byte> bytes)
        {
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        /// <summary>
        /// Reads the checked-in unsigned expectation through the production model boundary.
        /// </summary>
        internal Task<EvaluationExpectation> ReadExpectationAsync()
        {
            return Files.ReadModelAsync(
                Fixture("expected.json"), EvidenceJsonContext.Default.EvaluationExpectation, CancellationToken.None);
        }

        /// <summary>
        /// Reads a fixture JSON object for a deliberate positive or negative input variation.
        /// </summary>
        internal async Task<JsonObject> ReadObjectAsync(string relative)
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(At(relative)).ConfigureAwait(false))!.AsObject();
        }

        /// <summary>
        /// Writes mutable test input before handing it to the immutable production boundary.
        /// </summary>
        internal Task WriteJsonAsync(string relative, JsonObject value)
        {
            return File.WriteAllTextAsync(At(relative), value.ToJsonString());
        }

        /// <summary>
        /// Serializes test input with the same generated contract metadata used by production readers.
        /// </summary>
        /// <typeparam name="T">The fixture model type described by the generated metadata.</typeparam>
        internal Task WriteModelAsync<T>(string relative, T value, JsonTypeInfo<T> type)
        {
            string path = At(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, type));
        }

        /// <summary>
        /// Prepares real MSBuild-evaluated fixture projects and local restored package bytes without running a build.
        /// </summary>
        internal async Task<CaptureRequest> PrepareCaptureAsync()
        {
            foreach (string project in new[] { "Generator.csproj", "Contributor.csproj" })
            {
                await File.WriteAllBytesAsync(At(project), await File.ReadAllBytesAsync(Fixture(project))
                    .ConfigureAwait(false)).ConfigureAwait(false);
            }
            foreach ((string version, string tfm) in new[] { ("1.2.3", "net8.0"), ("2.3.4", "net10.0") })
            {
                string cache = At("cache/contoso.dependency/" + version);
                Directory.CreateDirectory(Path.Combine(cache, "lib", tfm));
                await File.WriteAllTextAsync(Path.Combine(cache, "lib", tfm, "Contoso.Dependency.dll"), version)
                    .ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(cache, "contoso.dependency.nuspec"), $"""
                    <package><metadata><id>Contoso.Dependency</id><version>{version}</version>
                    <license type="expression">MIT</license></metadata></package>
                    """).ConfigureAwait(false);
                Directory.CreateDirectory(At("bin/" + tfm));
                await File.WriteAllTextAsync(At("bin/" + tfm + "/Generator.dll"), "generator-" + tfm)
                    .ConfigureAwait(false);
            }
            await File.WriteAllTextAsync(At("bin/net10.0/Generator.pdb"), "generator-symbols").ConfigureAwait(false);
            await File.WriteAllTextAsync(At("bin/net8.0/Contributor.dll"), "contributor").ConfigureAwait(false);
            JsonObject assets = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("project.assets.json"))
                .ConfigureAwait(false))!.AsObject();
            assets["packageFolders"]![At("cache") + Path.DirectorySeparatorChar] = new JsonObject();
            await WriteJsonAsync("project.assets.json", assets).ConfigureAwait(false);
            var runner = new ProcessRunner();
            string sha = (await runner.RunAsync(
                RepositoryRoot, "git", ["rev-parse", "HEAD"], CancellationToken.None).ConfigureAwait(false)).Trim();
            string references = await runner.RunAsync(
                RepositoryRoot, "git", ["for-each-ref", "--points-at", sha, "--format=%(refname)"],
                CancellationToken.None).ConfigureAwait(false);
            string[] names = references.Split(
                '\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Assert.That(names, Is.Not.Empty, "Capture requires a named ref pointing to the checkout.");
            string status = await runner.RunAsync(
                RepositoryRoot, "git", ["status", "--porcelain", "--untracked-files=no"],
                CancellationToken.None).ConfigureAwait(false);
            EvaluationExpectation expected = await ReadExpectationAsync().ConfigureAwait(false);
            string projectPath = Path.GetRelativePath(RepositoryRoot, At("Generator.csproj")).Replace('\\', '/');
            var request = new CaptureRequest(
                expected.Source with
                {
                    ActualSha = sha,
                    ActualRef = names[0],
                    TrackedClean = string.IsNullOrWhiteSpace(status)
                }, expected.Producer, "2.0.0", "Release", [projectPath, projectPath]);
            await WriteModelAsync("capture.json", request, EvidenceJsonContext.Default.CaptureRequest)
                .ConfigureAwait(false);
            return request;
        }

        /// <summary>
        /// Creates an archive and explicit candidate ownership for managed, native, symbol, and contributor payloads.
        /// </summary>
        internal async Task PreparePackageAsync(bool symbols = false, bool includeLicense = true)
        {
            byte[] graph = await File.ReadAllBytesAsync(Fixture("project.assets.json")).ConfigureAwait(false);
            Directory.CreateDirectory(At("frozen/graphs"));
            await File.WriteAllBytesAsync(At("frozen/graphs/project.assets.json"), graph).ConfigureAwait(false);
            var assets = new FrozenFile("graphs/project.assets.json", Hash(graph), graph.LongLength);
            byte[] library = "library-net10"u8.ToArray();
            byte[] pdb = "library-symbols"u8.ToArray();
            byte[] dependency = "dependency-2.3.4"u8.ToArray();
            byte[] contributor = "contributor-net8"u8.ToArray();
            byte[] native = "native-linux-x64"u8.ToArray();
            Mappings =
            [
                new ProjectMapping(
                    "src/Library.csproj", "Release", "Fixture.Library", "2.0.0", true, true, "snupkg",
                    ["net8.0", "net10.0"], ["src/Contributor.csproj"], assets,
                    [
                        new ResolvedComponent(
                            "Fixture.Library", "2.0.0", "project", "net10.0",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["contoso.dependency"] = "[2.0.0,3.0.0)"
                            },
                            [new LicenseRecord("file", "LICENSE.txt", Hash("Synthetic license terms."u8))],
                            "src/Library.csproj"),
                        new ResolvedComponent(
                            "Contoso.Dependency", "2.3.4", "package", "net10.0", [],
                            [new LicenseRecord("expression", "MIT OR Apache-2.0")], "src/Library.csproj"),
                        new ResolvedComponent(
                            "Contoso.Dependency", "1.2.3", "package", "net8.0", [],
                            [new LicenseRecord("url", "https://example.invalid/license")], "src/Library.csproj")
                    ],
                    [
                        new PayloadCandidate(
                            "Library.dll", Hash(library), "Fixture.Library", "2.0.0",
                            "project:src/Library.csproj", "net10.0"),
                        new PayloadCandidate(
                            "Library.pdb", Hash(pdb), "Fixture.Library", "2.0.0",
                            "project:src/Library.csproj", "net10.0"),
                        new PayloadCandidate(
                            "Contoso.Dependency.dll", Hash(dependency), "Contoso.Dependency", "2.3.4",
                            "package", "net10.0"),
                        new PayloadCandidate(
                            "native.so", Hash(native), "Contoso.Dependency", "2.3.4",
                            "package", "net10.0/linux-x64")
                    ]),
                new ProjectMapping(
                    "src/Contributor.csproj", "Release", "Fixture.Contributor", "2.0.0", false, false, string.Empty,
                    ["net8.0"], [], assets, [],
                    [new PayloadCandidate(
                        "Contributor.dll", Hash(contributor), "Fixture.Contributor", "2.0.0",
                        "project:src/Contributor.csproj", "net8.0")])
            ];
            Entries.Add("Fixture.Library.nuspec", Encoding.UTF8.GetBytes("""
                <package><metadata><id>Fixture.Library</id><version>2.0.0</version>
                <license type="file">LICENSE.txt</license><readme>README.txt</readme>
                <dependencies>
                  <group targetFramework="net8.0"><dependency id="Contoso.Dependency" version="[1.0.0,2.0.0)" /></group>
                  <group targetFramework="net10.0">
                    <dependency id="Contoso.Dependency" version="[2.0.0,3.0.0)" />
                  </group>
                </dependencies>
                <frameworkAssemblies><frameworkAssembly assemblyName="System.Xml" /></frameworkAssemblies>
                <frameworkReferences><group targetFramework="net10.0">
                  <frameworkReference name="Microsoft.AspNetCore.App" />
                </group></frameworkReferences>
                </metadata></package>
                """));
            if (includeLicense)
            {
                Entries.Add("LICENSE.txt", "Synthetic license terms."u8.ToArray());
            }
            Entries.Add("README.txt", "Synthetic package documentation."u8.ToArray());
            Entries.Add("lib/net10.0/Library.pdb", pdb);
            if (!symbols)
            {
                Entries.Add("lib/net10.0/Library.dll", library);
                Entries.Add("analyzers/dotnet/roslyn4.0/cs/Contributor.dll", contributor);
                Entries.Add("analyzers/dotnet/roslyn4.14/cs/Contoso.Dependency.dll", dependency);
                Entries.Add("runtimes/linux-x64/native/native.so", native);
            }
            Directory.CreateDirectory(At("packages"));
            ArchivePath = At("packages/Fixture.Library.2.0.0." + (symbols ? "snupkg" : "nupkg"));
            using ZipArchive archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create);
            foreach ((string path, byte[] bytes) in Entries)
            {
                await using Stream stream = archive.CreateEntry(path).Open();
                await stream.WriteAsync(bytes).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Prepares frozen inputs and an exact legacy manifest against unmodified copies of the repository contracts.
        /// </summary>
        internal async Task<EvaluationExpectation> PrepareNugetAsync(string channel = "development")
        {
            await PreparePackageAsync().ConfigureAwait(false);
            var contracts = new List<FrozenFile>();
            foreach (string relative in new[]
            {
                ".azurepipelines/release/policy.json", ".azurepipelines/release/artifacts.json",
                ".azurepipelines/assurance/profiles.json", ".azurepipelines/release/evidence.schema.json",
                ".azurepipelines/nuget/expected-packages.txt", "nuget/Opc.Ua.nuspec", "nuget/Opc.Ua.Symbols.nuspec"
            })
            {
                byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(RepositoryRoot, relative))
                    .ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(At(relative))!);
                await File.WriteAllBytesAsync(At(relative), bytes).ConfigureAwait(false);
                string frozen = "contracts/" + Path.GetFileName(relative);
                Directory.CreateDirectory(At("frozen/contracts"));
                await File.WriteAllBytesAsync(At("frozen/" + frozen), bytes).ConfigureAwait(false);
                contracts.Add(new FrozenFile(frozen, Hash(bytes), bytes.LongLength));
            }
            EvaluationExpectation expected = await ReadExpectationAsync().ConfigureAwait(false);
            expected = expected with
            {
                Release = expected.Release with { Channel = channel },
                PolicyDigest = Hash(await File.ReadAllBytesAsync(At(".azurepipelines/release/policy.json"))
                    .ConfigureAwait(false))
            };
            await WriteModelAsync("context.json", expected, EvidenceJsonContext.Default.EvaluationExpectation)
                .ConfigureAwait(false);
            var bundle = new FrozenBundle(
                1, expected.Source, expected.Producer, "2.0.0", "Release", Mappings, [.. contracts], []);
            await WriteModelAsync("frozen/build-inputs.json", bundle, EvidenceJsonContext.Default.FrozenBundle)
                .ConfigureAwait(false);
            var manifest = new ArchiveManifest(
                1, expected.Source.Repository, expected.Producer.Workflow, expected.Producer.RunId,
                expected.Source.ActualRef, expected.Source.ActualSha, "2.0.0", 1, 0, 0,
                [new ArchiveRecord(
                    "Fixture.Library", "2.0.0", "package", Path.GetFileName(ArchivePath),
                    Hash(await File.ReadAllBytesAsync(ArchivePath).ConfigureAwait(false))[7..])]);
            await WriteModelAsync("manifest.json", manifest, EvidenceJsonContext.Default.ArchiveManifest)
                .ConfigureAwait(false);
            return expected;
        }

        /// <summary>
        /// Calls the production generator directly while keeping all outputs within this fixture.
        /// </summary>
        internal Task<int> GenerateAsync()
        {
            return new NugetEvidence(Files, new PackageReconciler(Files)).GenerateAsync(
                Root, At("packages"), [At("frozen")], At("context.json"), At("manifest.json"),
                At("evidence"), CancellationToken.None);
        }

        /// <summary>
        /// Reads the generated evidence envelope with production metadata.
        /// </summary>
        internal Task<EvidenceEnvelope> ReadEnvelopeAsync()
        {
            return Files.ReadModelAsync(
                At("evidence/release-evidence.json"), EvidenceJsonContext.Default.EvidenceEnvelope,
                CancellationToken.None);
        }

        /// <summary>
        /// Evaluates explicitly untrusted test evidence without supplying a trust policy or successful verifier stub.
        /// </summary>
        internal async Task<(int Code, EvaluationReport Report)> EvaluateAsync(
            EvidenceEnvelope envelope, EvaluationExpectation expected)
        {
            await WriteModelAsync(
                "evidence/release-evidence.json", envelope, EvidenceJsonContext.Default.EvidenceEnvelope)
                .ConfigureAwait(false);
            await WriteModelAsync("context.json", expected, EvidenceJsonContext.Default.EvaluationExpectation)
                .ConfigureAwait(false);
            int code = await new EvidenceEvaluator(Files).EvaluateAsync(
                Root, At("evidence/release-evidence.json"), At("context.json"), At("report.json"),
                null, CancellationToken.None).ConfigureAwait(false);
            EvaluationReport report = await Files.ReadModelAsync(
                At("report.json"), EvidenceJsonContext.Default.EvaluationReport, CancellationToken.None)
                .ConfigureAwait(false);
            return (code, report);
        }
    }
}
