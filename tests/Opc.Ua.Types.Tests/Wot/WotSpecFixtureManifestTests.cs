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
        [Test]
        public void ManifestPinsEveryEmbeddedExample()
        {
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();
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
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

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
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

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
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

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
            using JsonDocument manifest = JsonDocument.Parse(ReadEmbeddedExample(ManifestFileName));
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
            using JsonDocument context = JsonDocument.Parse(
                ReadEmbeddedExample("opc-ua-wot-binding.context.jsonld"));
            Assert.That(
                context.RootElement.GetProperty("@id").GetString(),
                Is.EqualTo(WotVocabulary.BindingContext));

            string[] missing = context.RootElement.GetProperty("@context").EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name.StartsWith("uav:", StringComparison.Ordinal) &&
                    !WotBindingConformance.IsKnownTerm(name))
                .ToArray();
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
                .Select(name => name.Substring(
                    name.IndexOf(ResourcePrefix, StringComparison.Ordinal) + ResourcePrefix.Length))
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
        public static WotSpecFixtureManifest Load()
        {
            Assembly assembly = typeof(WotSpecFixtureManifest).Assembly;
            string resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(
                    "Wot.Assets." + WotSpecFixtureManifestTests.ManifestFileName,
                    StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The fixture manifest is not embedded.");
            using JsonDocument document = JsonDocument.Parse(stream);
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
