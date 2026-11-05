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
 *
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Keeps the local WoT regression fixtures stable and records the official
    /// specification baseline separately from their provenance.
    /// </summary>
    /// <remarks>
    /// The specification examples are mirrored byte-for-byte from the pinned
    /// upstream baseline. UA-only regression inputs are retained separately.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Category("WotSpecExamples")]
    [Parallelizable]
    public sealed class WotSpecFixtureManifestTests
    {
        /// <summary>
        /// Verifies the current source copies independently of the frozen implementation fixtures.
        /// </summary>
        [TestCase("CurrentSpecification", "f3440825b4fe1a316b270e01452239fb3f3bfe57")]
        [TestCase("SharedResolution", "76e084ec2149481a5d8e73a4fefc4a724e404937")]
        public void CurrentSourceManifestMatchesEveryCopiedByte(string directory, string commit)
        {
            string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Wot", directory);
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(folder, "source-manifest.json")));
            Assert.That(manifest.RootElement.GetProperty("commit").GetString(), Is.EqualTo(commit));
            JsonElement[] files = [.. manifest.RootElement.GetProperty("files").EnumerateArray()];
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName)
                .Where(name => name != "source-manifest.json"),
                Is.EquivalentTo(files.Select(file => file.GetProperty("name").GetString())));
            foreach (JsonElement entry in files)
            {
                string name = entry.GetProperty("name").GetString()!;
                byte[] bytes = File.ReadAllBytes(Path.Combine(folder, name));
                Assert.That(bytes, Has.Length.EqualTo(entry.GetProperty("bytes").GetInt32()), name);
                Assert.That(Sha256Hex(bytes), Is.EqualTo(entry.GetProperty("sha256").GetString()), name);
            }
        }

        /// <summary>
        /// Verifies that every current example retains its exact source bytes when read.
        /// </summary>
        [Test]
        public void CurrentSpecificationExamplesPreserveTheirSourceBytes()
        {
            string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Wot", "CurrentSpecification");
            string[] files =
                [.. Directory.GetFiles(folder, "*.jsonld").Where(path => char.IsDigit(Path.GetFileName(path)[0]))];
            Assert.That(files, Has.Length.EqualTo(39));
            foreach (string file in files)
            {
                byte[] bytes = File.ReadAllBytes(file);
                using var document = WotDocument.Parse(bytes);
                Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object), file);
                Assert.That(document.Utf8Json.ToArray(), Is.EqualTo(bytes), file);
            }
        }

        /// <summary>
        /// Exercises the established standalone mapping surface with the current source examples.
        /// </summary>
        [TestCase("03-nodeset-preservation-envelope.jsonld")]
        [TestCase("04-type-reference-modelling-rule.jsonld")]
        [TestCase("05-native-node-model.jsonld")]
        [TestCase("06-anchored-paths-and-device-identity.jsonld")]
        [TestCase("08-projection-resolved.jsonld")]
        [TestCase("14-asset-instance-resolved.jsonld")]
        [TestCase("20-asset-model-resolved.jsonld")]
        [TestCase("23-datatype-definitions.jsonld")]
        [TestCase("24-localized-text-fallback.jsonld")]
        [TestCase("25-reference-type-node.jsonld")]
        [TestCase("26-data-type-node.jsonld")]
        [TestCase("27-event-type-models.jsonld")]
        [TestCase("28-channel-monitoring-type.jsonld")]
        [TestCase("36-declaration-axis-model.jsonld")]
        [TestCase("39-native-annotations.jsonld")]
        public void CurrentStandaloneSpecificationExampleConvertsAndImports(string name)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Wot", "CurrentSpecification", name));
            using WotDocument document = ReadCurrentExampleWithContext(name);
            WotConversionResult<Export.UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document,
                new WotNodeSetConverterOptions { ConformanceMode = WotConformanceMode.Strict });
            Assert.That(result.Success, Is.True, name + ": " + string.Join("; ", result.Diagnostics));
            WotNodeSetImportTests.AssertImportable(result.Value!, name);
            Assert.That(File.ReadAllBytes(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Wot", "CurrentSpecification", name)), Is.EqualTo(bytes));
        }

        /// <summary>
        /// Verifies that context-dependent examples are not returned as complete standalone conversions.
        /// </summary>
        [TestCase("01-opcua-td-pump.jsonld", WotDiagnosticCode.EventSelectionUnresolved)]
        [TestCase("02-thing-model-pump.jsonld", WotDiagnosticCode.EventSelectionUnresolved)]
        [TestCase("21-condition-limit-alarm.jsonld", WotDiagnosticCode.EventSelectionUnresolved)]
        [TestCase("22-type-binding-and-instance-reference.jsonld", WotDiagnosticCode.UnresolvedTypeBinding)]
        [TestCase("07-projection-predictive-maintenance.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("09-asset-instance.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("10-group-process-data.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("11-group-condition-data.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("12-group-alarms.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("13-group-management.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("15-asset-model.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("16-model-group-process-data.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("17-model-group-condition-data.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("18-model-group-alarms.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        [TestCase("19-model-group-management.jsonld", WotDiagnosticCode.ProjectionManifestInvalid)]
        public void CurrentContextDependentExampleDoesNotReturnAnIncompleteNodeSet(
            string name, WotDiagnosticCode expected)
        {
            using WotDocument document = ReadCurrentExampleWithContext(name);
            WotConversionResult<Export.UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document,
                new WotNodeSetConverterOptions { ConformanceMode = WotConformanceMode.Strict });
            Assert.That(result.Success, Is.False, name);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == expected && diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True,
                name + ": " + string.Join("; ", result.Diagnostics));
        }

        /// <summary>
        /// Records the converter boundary for newer examples without treating a diagnosed rejection as conformance.
        /// </summary>
        [Test]
        public void CurrentAdditionalExamplesExposeTheirConversionBoundary()
        {
            string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Wot", "CurrentSpecification");
            string[] files = [.. Directory.GetFiles(folder, "*.jsonld")
                .Where(path => char.IsDigit(Path.GetFileName(path)[0]) &&
                    int.Parse(Path.GetFileName(path)[..2], CultureInfo.InvariantCulture) >= 28)
                .OrderBy(path => path, StringComparer.Ordinal)];
            var outcomes = new List<string>();
            foreach (string file in files)
            {
                using WotDocument document = ReadCurrentExampleWithContext(Path.GetFileName(file));
                WotConversionResult<Export.UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document,
                    new WotNodeSetConverterOptions { ConformanceMode = WotConformanceMode.Strict });
                int number = int.Parse(Path.GetFileName(file)[..2], CultureInfo.InvariantCulture);
                if (number is 28 or 36 or 39)
                {
                    Assert.That(result.Success, Is.True, file + ": " + string.Join("; ", result.Diagnostics));
                    WotNodeSetImportTests.AssertImportable(result.Value!, file);
                    outcomes.Add(Path.GetFileName(file) + ": converts and imports");
                }
                else
                {
                    WotDiagnosticCode expected = number is 31 or 33
                        ? WotDiagnosticCode.ProjectionManifestInvalid
                        : number == 35 ? WotDiagnosticCode.EventSelectionUnresolved
                        : WotDiagnosticCode.UnknownVocabularyTerm;
                    Assert.That(result.Success, Is.False, file);
                    Assert.That(result.Diagnostics.Any(diagnostic =>
                        diagnostic.Severity == WotDiagnosticSeverity.Error && diagnostic.Code == expected), Is.True,
                        file + ": " + string.Join("; ", result.Diagnostics));
                    outcomes.Add(Path.GetFileName(file) +
                        ": " +
                        string.Join("; ",
                            result.Diagnostics.Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error)
                                .Select(diagnostic => diagnostic.Code).Distinct()));
                }
            }
            string report = Path.Combine(
                Path.GetTempPath(), "ua-current-spec-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(report, outcomes);
            TestContext.AddTestAttachment(
                report, "Current example conversion boundaries; not a conformance certificate.");
        }

        /// <summary>
        /// Uses the current mirrored context at the same acquisition seam as the frozen regression pipeline.
        /// </summary>
        private static WotDocument ReadCurrentExampleWithContext(string name)
        {
            string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Wot", "CurrentSpecification");
            JsonNode context = JsonNode.Parse(File.ReadAllBytes(
                Path.Combine(folder, "opc-ua-wot-binding.context.jsonld")))!["@context"]!;
            return WotDocument.Parse(WotSpecExampleTests.WithLocalBindingContext(
                File.ReadAllBytes(Path.Combine(folder, name)), context));
        }

        [Test]
        public void ManifestPinsEveryEmbeddedExample()
        {
            var manifest = WotSpecFixtureManifest.Load();
            IReadOnlyList<string> embedded = EmbeddedExampleNames();

            Assert.Multiple(() =>
            {
                Assert.That(
                    embedded,
                    Is.EqualTo(manifest.Files.Select(file => file.Name)).AsCollection);
                Assert.That(embedded, Has.Count.EqualTo(manifest.FixtureCount));
            });
        }

        [Test]
        public void EveryEmbeddedExampleMatchesItsRecordedHash()
        {
            var manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                foreach (WotSpecFixtureEntry entry in manifest.Files)
                {
                    byte[] bytes = ReadEmbeddedExample(entry.Name);
                    Assert.That(bytes, Has.Length.EqualTo(entry.Bytes), entry.Name);
                    Assert.That(Sha256Hex(bytes), Is.EqualTo(entry.Sha256), entry.Name);
                }
            });
        }

        [Test]
        public void ManifestPinsTheOfficialSpecificationBaseline()
        {
            var manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                Assert.That(
                    manifest.Repository,
                    Is.EqualTo("https://github.com/OPCF-Members/OPC10100-WoT.git"));
                Assert.That(
                    manifest.Commit,
                    Is.EqualTo("0b06c3e3b5d6a427193a17321abb6d1da4a2bae0"));
                Assert.That(
                    manifest.BindingRevision,
                    Is.EqualTo(WotBindingConformance.CurrentRevision));
            });
        }

        [Test]
        public void FixtureNumberingHasNoGaps()
        {
            var manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                for (int ii = 0; ii < manifest.Files.Count; ii++)
                {
                    string name = manifest.Files[ii].Name;
                    Assert.That(name, Does.Match("^[0-9]{2}-[a-z0-9-]+[.]jsonld$"));
                    Assert.That(
                        name,
                        Does.StartWith((ii + 1).ToString("00", CultureInfo.InvariantCulture) + "-"));
                }
            });
        }

        [Test]
        public void BindingContextMatchesItsCanonicalHash()
        {
            using var manifest = JsonDocument.Parse(ReadEmbeddedExample(ManifestFileName));
            JsonElement entry = manifest.RootElement.GetProperty("context");
            byte[] bytes = ReadEmbeddedExample(entry.GetProperty("name").GetString()!);
            Assert.Multiple(() =>
            {
                Assert.That(bytes, Has.Length.EqualTo(entry.GetProperty("bytes").GetInt32()));
                Assert.That(Sha256Hex(bytes), Is.EqualTo(entry.GetProperty("sha256").GetString()));
            });
        }

        [Test]
        public void LocalContextFixtureUsesKnownTerms()
        {
            using var context = JsonDocument.Parse(
                ReadEmbeddedExample("opc-ua-wot-binding.context.jsonld"));
            Assert.That(
                context.RootElement.GetProperty("@id").GetString(),
                Is.EqualTo(WotVocabulary.BindingContext));

            string[] missing = [.. context.RootElement.GetProperty("@context").EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name.StartsWith("uav:", StringComparison.Ordinal) &&
                    !WotBindingConformance.IsKnownTerm(name))];
            Assert.That(missing, Is.Empty);
        }

        internal static string Sha256Hex(byte[] bytes)
        {
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(bytes);
#else
            using var algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(bytes);
#endif
            var text = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        internal static IReadOnlyList<string> EmbeddedExampleNames()
        {
            return [.. typeof(WotSpecFixtureManifestTests).Assembly
                .GetManifestResourceNames()
                .Where(name => name.Contains(ResourcePrefix, StringComparison.Ordinal) &&
                    name.EndsWith(".jsonld", StringComparison.Ordinal) &&
                    char.IsDigit(name[name.IndexOf(ResourcePrefix, StringComparison.Ordinal) + ResourcePrefix.Length]))
                .Select(name => name[
                    (name.IndexOf(ResourcePrefix, StringComparison.Ordinal) + ResourcePrefix.Length)..])
                .OrderBy(name => name, StringComparer.Ordinal)];
        }

        internal static byte[] ReadEmbeddedExample(string name)
        {
            Assembly assembly = typeof(WotSpecFixtureManifestTests).Assembly;
            string resource = assembly.GetManifestResourceNames()
                .Single(resourceName => resourceName.EndsWith(
                    ResourcePrefix + name,
                    StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        internal const string ManifestFileName = "spec-examples.manifest.json";
        private const string ResourcePrefix = "Wot.Assets.";
    }

    /// <summary>
    /// One local regression fixture and the bytes recorded for it.
    /// </summary>
    internal sealed record WotSpecFixtureEntry(string Name, int Bytes, string Sha256);

    /// <summary>
    /// The official normative baseline and the local regression fixture inventory.
    /// </summary>
    internal sealed record WotSpecFixtureManifest(
        string Repository,
        string Commit,
        string BindingRevision,
        int FixtureCount,
        IReadOnlyList<WotSpecFixtureEntry> Files)
    {
        /// <summary>
        /// Reads the manifest embedded beside the local regression fixtures.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public static WotSpecFixtureManifest Load()
        {
            Assembly assembly = typeof(WotSpecFixtureManifest).Assembly;
            string resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(
                    "Wot.Assets." + WotSpecFixtureManifestTests.ManifestFileName,
                    StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The fixture manifest is not embedded.");
            using var document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            JsonElement baseline = root.GetProperty("baseline");
            var files = new List<WotSpecFixtureEntry>();

            foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
            {
                files.Add(new WotSpecFixtureEntry(
                    file.GetProperty("name").GetString()!,
                    file.GetProperty("bytes").GetInt32(),
                    file.GetProperty("sha256").GetString()!));
            }

            return new WotSpecFixtureManifest(
                baseline.GetProperty("repository").GetString()!,
                baseline.GetProperty("commit").GetString()!,
                baseline.GetProperty("bindingRevision").GetString()!,
                root.GetProperty("fixtureCount").GetInt32(),
                files);
        }
    }
}
