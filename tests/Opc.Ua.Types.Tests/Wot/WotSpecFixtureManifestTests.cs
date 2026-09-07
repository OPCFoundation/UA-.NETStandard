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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Pins the vendored WoT Binding specification examples to the source they
    /// were taken from, so a specification change cannot reach the fixtures
    /// without a reviewer seeing it.
    /// </summary>
    /// <remarks>
    /// The fixtures under <c>Wot/Assets</c> are copies of the examples the
    /// WoT Binding draft publishes. They used to be copied by hand with no
    /// record of the source, so they drifted silently: one example gained a
    /// security floor upstream and the copy here kept the superseded text.
    /// <para>
    /// The check has three parts. <see cref="ManifestPinsEveryEmbeddedExample"/>
    /// and the hash tests run everywhere, need no network and no second
    /// checkout - they compare the embedded bytes against
    /// <c>spec-examples.manifest.json</c>, which records the source repository,
    /// branch and commit. <see cref="VendoredExamplesMatchTheSpecificationCheckout"/>
    /// additionally proves byte identity against a sibling <c>spec-drafts</c>
    /// checkout when a contributor has one. <see cref="RegenerateFromSpecCheckout"/>
    /// is the explicit, developer-run step that performs the copy and rewrites
    /// the manifest.
    /// </para>
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
                    Is.EqualTo(manifest.Files.Select(f => f.Name)).AsCollection,
                    "The embedded example set has to be exactly the manifest set, in order. " +
                    RegenerationHint);
                Assert.That(
                    embedded,
                    Has.Count.EqualTo(manifest.ExampleCount),
                    "The manifest records how many examples the specification publishes, so " +
                    "dropping one is a failure rather than a smaller test run. " +
                    RegenerationHint);
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
                    Assert.That(
                        bytes,
                        Has.Length.EqualTo(entry.Bytes),
                        $"'{entry.Name}' is {bytes.Length} bytes, not the recorded " +
                        $"{entry.Bytes}. {DescribeDifference(bytes, entry)}");
                    Assert.That(
                        Sha256Hex(bytes),
                        Is.EqualTo(entry.Sha256),
                        $"'{entry.Name}' does not match the vendored specification bytes. " +
                        DescribeDifference(bytes, entry));
                }
            });
        }

        [Test]
        public void ManifestRecordsTheSourceItWasTakenFrom()
        {
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                Assert.That(manifest.Repository, Is.Not.Empty);
                Assert.That(manifest.Branch, Is.Not.Empty);
                Assert.That(
                    manifest.Commit,
                    Has.Length.EqualTo(40),
                    "The manifest records the exact source commit, not a moving reference.");
                Assert.That(
                    manifest.Commit.All(Uri.IsHexDigit),
                    Is.True,
                    "The recorded commit has to be a full hexadecimal object name.");
                Assert.That(manifest.SourcePath, Is.Not.Empty);
                Assert.That(
                    manifest.BindingRevision,
                    Is.EqualTo(WotBindingConformance.CurrentRevision),
                    "The fixtures track the vocabulary revision this library implements. " +
                    "If the specification moved on, implement the revision first.");
                Assert.That(
                    manifest.LineEnding,
                    Is.EqualTo("lf"),
                    "The fixtures are vendored byte-for-byte, so .gitattributes keeps them " +
                    "at LF on every platform.");
            });
        }

        /// <summary>
        /// Guards the two failure modes this manifest exists to prevent: an
        /// example silently reverting to superseded text, and an example the
        /// specification added never arriving here at all.
        /// </summary>
        [Test]
        public void ManifestLeavesNoGapInTheExampleNumbering()
        {
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                for (int ii = 0; ii < manifest.Files.Count; ii++)
                {
                    string name = manifest.Files[ii].Name;
                    Assert.That(
                        name,
                        Does.Match("^[0-9]{2}-[a-z0-9-]+[.]jsonld$"),
                        "The specification numbers its examples, and the numbering is what " +
                        "makes an omission visible.");
                    Assert.That(
                        name,
                        Does.StartWith((ii + 1).ToString("00", CultureInfo.InvariantCulture) + "-"),
                        "A gap or a truncated tail means an example was dropped rather than " +
                        "synced. " + RegenerationHint);
                }
            });
        }

        /// <summary>
        /// Proves byte identity against the specification source itself, for a
        /// contributor who has the sibling checkout. The manifest hashes cannot
        /// prove this on their own, because a regeneration that used the wrong
        /// source would record the wrong hashes just as consistently.
        /// </summary>
        /// <remarks>
        /// The comparison is against the commit the manifest names, not against
        /// whatever the contributor's working tree currently holds. A checkout
        /// sitting on an older or newer commit says nothing about whether the
        /// fixtures were vendored correctly, and comparing against it turns an
        /// ordinary local state into a failure that no change can fix.
        /// </remarks>
        [Test]
        public void VendoredExamplesMatchTheSpecificationCheckout()
        {
            string? examples = TryFindSpecificationExamples();
            if (examples is null)
            {
                Assert.Ignore(
                    "No specification checkout found. Set " + SpecCheckoutVariable +
                    " or place a 'spec-drafts' checkout beside this repository to run this " +
                    "comparison.");
                return;
            }

            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();
            if (!TryReadPinnedExamples(examples, manifest, out var pinned))
            {
                Assert.Ignore(
                    $"The checkout at '{examples}' does not contain commit " +
                    $"{manifest.Commit}, so it cannot witness the vendored bytes. Fetch " +
                    "that commit to run this comparison.");
                return;
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    pinned.Keys.OrderBy(n => n, StringComparer.Ordinal),
                    Is.EqualTo(manifest.Files.Select(f => f.Name)).AsCollection,
                    $"The examples published at commit {manifest.Commit} are not the " +
                    "vendored set. " + RegenerationHint);

                foreach (WotSpecFixtureEntry entry in manifest.Files)
                {
                    if (!pinned.TryGetValue(entry.Name, out byte[]? source))
                    {
                        continue;
                    }
                    Assert.That(
                        Sha256Hex(source),
                        Is.EqualTo(entry.Sha256),
                        $"'{entry.Name}' differs from the specification source. " +
                        RegenerationHint);
                }
            });
        }

        /// <summary>
        /// Reads the examples of the pinned commit out of the object database
        /// of a checkout, leaving the working tree untouched.
        /// </summary>
        private static bool TryReadPinnedExamples(
            string examples,
            WotSpecFixtureManifest manifest,
            out Dictionary<string, byte[]> pinned)
        {
            pinned = [];
            string root;
            try
            {
                root = FindSpecificationRoot(examples);
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            string? listing = TryGit(
                root, $"ls-tree --name-only {manifest.Commit} {manifest.SourcePath}/");
            if (listing is null)
            {
                return false;
            }

            foreach (string path in listing.Split(
                ['\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = path.Trim();
                if (!entry.EndsWith(".jsonld", StringComparison.Ordinal))
                {
                    continue;
                }
                byte[]? bytes = TryGitBlob(root, $"{manifest.Commit}:{entry}");
                if (bytes is null)
                {
                    return false;
                }
                pinned[entry.Substring(entry.LastIndexOf('/') + 1)] = bytes;
            }
            return pinned.Count > 0;
        }

        /// <summary>
        /// Copies the published examples over the vendored ones and rewrites
        /// the manifest from the source checkout. Explicit, because it is an
        /// authoring step, not a check: run it, then review the diff.
        /// </summary>
        [Test]
        [Explicit(
            "Developer step: re-vendors the WoT Binding examples from a sibling " +
            "spec-drafts checkout and rewrites spec-examples.manifest.json.")]
        public void RegenerateFromSpecCheckout()
        {
            string examples = TryFindSpecificationExamples()
                ?? throw new InvalidOperationException(
                    "No specification checkout found. Set " + SpecCheckoutVariable +
                    " to the root of a 'spec-drafts' checkout.");
            string repositoryRoot = FindRepositoryRoot();
            string assets = Path.Combine(
                repositoryRoot, "tests", "Opc.Ua.Types.Tests", "Wot", "Assets");
            Assert.That(Directory.Exists(assets), Is.True, $"'{assets}' should exist.");

            string specificationRoot = FindSpecificationRoot(examples);
            var entries = new List<WotSpecFixtureEntry>();
            foreach (string source in Directory
                .EnumerateFiles(examples, "*.jsonld")
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                string name = Path.GetFileName(source);
                byte[] bytes = File.ReadAllBytes(source);
                File.WriteAllBytes(Path.Combine(assets, name), bytes);
                entries.Add(new WotSpecFixtureEntry(name, bytes.Length, Sha256Hex(bytes)));
            }

            Assert.That(entries, Is.Not.Empty, "The checkout published no examples.");

            var manifest = new WotSpecFixtureManifest(
                Git(specificationRoot, "config --get remote.origin.url"),
                Git(specificationRoot, "rev-parse --abbrev-ref HEAD"),
                Git(specificationRoot, "rev-parse HEAD"),
                RelativeExamplePath(specificationRoot, examples),
                WotBindingConformance.CurrentRevision,
                "lf",
                entries.Count,
                entries);

            File.WriteAllText(
                Path.Combine(assets, ManifestFileName),
                manifest.Write(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            TestContext.Out.WriteLine(
                $"Re-vendored {entries.Count} examples from {manifest.Commit} " +
                $"({manifest.Branch}).");
        }

        private static string DescribeDifference(byte[] actual, WotSpecFixtureEntry entry)
        {
            if (Array.IndexOf(actual, (byte)'\r') >= 0 &&
                Sha256Hex(WithoutCarriageReturns(actual)) == entry.Sha256)
            {
                return "The content matches but the line endings are CRLF. The fixtures are " +
                    "vendored byte-for-byte and .gitattributes marks them 'eol=lf'; run " +
                    "'git add --renormalize tests/Opc.Ua.Types.Tests/Wot/Assets' and check the " +
                    "files out again.";
            }
            return RegenerationHint;
        }

        internal static byte[] WithoutCarriageReturns(byte[] bytes)
        {
            var normalized = new List<byte>(bytes.Length);
            for (int ii = 0; ii < bytes.Length; ii++)
            {
                if (bytes[ii] == (byte)'\r' &&
                    ii + 1 < bytes.Length &&
                    bytes[ii + 1] == (byte)'\n')
                {
                    continue;
                }
                normalized.Add(bytes[ii]);
            }
            return [.. normalized];
        }

        private static string RelativeExamplePath(string root, string examples)
        {
            string relative = examples
                .Substring(root.Length)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            return relative.TrimStart('/');
        }

        private static string Git(string workingDirectory, string arguments)
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("git could not be started.");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'git {arguments}' failed with exit code {process.ExitCode}.");
            }
            return output.Trim();
        }

        /// <summary>
        /// Runs git for a question whose answer may legitimately be "no": a
        /// checkout that does not hold the pinned commit is a local state, not
        /// a defect in what is checked in.
        /// </summary>
        private static string? TryGit(string workingDirectory, string arguments)
        {
            try
            {
                return Git(workingDirectory, arguments);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads one blob of a checkout's object database verbatim.
        /// </summary>
        private static byte[]? TryGitBlob(string workingDirectory, string specifier)
        {
            var start = new ProcessStartInfo("git", "cat-file blob " + specifier)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using Process? process = Process.Start(start);
                if (process is null)
                {
                    return null;
                }
                using var buffer = new MemoryStream();
                process.StandardOutput.BaseStream.CopyTo(buffer);
                process.WaitForExit();
                return process.ExitCode == 0 ? buffer.ToArray() : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Locates the examples of a sibling specification checkout, without
        /// requiring one: the offline checks are the ones CI runs.
        /// </summary>
        /// <remarks>
        /// The specification repository has moved its sources under a
        /// <c>source/</c> directory, and a contributor may point the variable
        /// at the repository root, at that source directory, or straight at the
        /// examples. Probing every shape keeps the comparison from silently
        /// degrading into an ignored test the moment the upstream layout
        /// changes - which is the one failure this check exists to catch.
        /// </remarks>
        private static string? TryFindSpecificationExamples()
        {
            string? configured = Environment.GetEnvironmentVariable(SpecCheckoutVariable);
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(configured))
            {
                roots.Add(configured!);
            }
            string? repositoryRoot = TryFindRepositoryRoot();
            if (repositoryRoot is not null)
            {
                roots.Add(Path.Combine(repositoryRoot, "..", "spec-drafts"));
            }

            foreach (string root in roots)
            {
                foreach (string relative in s_specExampleLayouts)
                {
                    string examples = Path.GetFullPath(
                        Path.Combine(
                            root,
                            relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (Directory.Exists(examples))
                    {
                        return examples;
                    }
                }
                // Tolerate being pointed straight at the examples directory.
                string direct = Path.GetFullPath(root);
                if (Directory.Exists(direct) &&
                    string.Equals(
                        Path.GetFileName(direct), "examples", StringComparison.Ordinal))
                {
                    return direct;
                }
            }
            return null;
        }

        /// <summary>
        /// The layouts the specification repository has published its WoT
        /// Binding examples under, newest first.
        /// </summary>
        private static readonly string[] s_specExampleLayouts =
        [
            "source/wot-specs/WoT-Binding/examples",
            "wot-specs/WoT-Binding/examples"
        ];

        /// <summary>
        /// Walks up from the examples to the working tree that holds them, so
        /// the recorded commit describes the repository rather than whichever
        /// directory happens to sit three levels up.
        /// </summary>
        private static string FindSpecificationRoot(string examples)
        {
            string? directory = Path.GetFullPath(examples);
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")) ||
                    File.Exists(Path.Combine(directory, ".git")))
                {
                    return directory;
                }
                directory = Path.GetDirectoryName(directory);
            }
            throw new InvalidOperationException(
                $"'{examples}' is not inside a git working tree, so the source commit " +
                "cannot be recorded.");
        }

        private static string FindRepositoryRoot()
        {
            return TryFindRepositoryRoot()
                ?? throw new InvalidOperationException("Repository root was not found.");
        }

        private static string? TryFindRepositoryRoot()
        {
            string? directory = Path.GetDirectoryName(
                typeof(WotSpecFixtureManifestTests).Assembly.Location);
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "UA.slnx")))
                {
                    return directory;
                }
                directory = Path.GetDirectoryName(directory);
            }
            return null;
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
                .Where(n => n.Contains(ResourcePrefix, StringComparison.Ordinal) &&
                    n.EndsWith(".jsonld", StringComparison.Ordinal))
                .Select(n => n.Substring(
                    n.IndexOf(ResourcePrefix, StringComparison.Ordinal) + ResourcePrefix.Length))
                .OrderBy(n => n, StringComparer.Ordinal)];
        }

        internal static byte[] ReadEmbeddedExample(string name)
        {
            string resource = typeof(WotSpecFixtureManifestTests).Assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith(ResourcePrefix + name, StringComparison.Ordinal));
            using Stream stream = typeof(WotSpecFixtureManifestTests).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing fixture '{name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        internal const string ManifestFileName = "spec-examples.manifest.json";
        private const string ResourcePrefix = "Wot.Assets.";
        private const string SpecCheckoutVariable = "OPCUA_WOT_SPEC_DRAFTS";

        private const string RegenerationHint =
            "Run the explicit test " +
            "'Opc.Ua.Types.Tests.Wot.WotSpecFixtureManifestTests.RegenerateFromSpecCheckout' " +
            "against a spec-drafts checkout to re-vendor the examples and rewrite " +
            ManifestFileName + ", then review the diff.";
    }

    /// <summary>
    /// One vendored example and the bytes it is pinned to.
    /// </summary>
    internal sealed record WotSpecFixtureEntry(string Name, int Bytes, string Sha256);

    /// <summary>
    /// The checked-in record of where the vendored WoT Binding examples came
    /// from and what they contained.
    /// </summary>
    internal sealed record WotSpecFixtureManifest(
        string Repository,
        string Branch,
        string Commit,
        string SourcePath,
        string BindingRevision,
        string LineEnding,
        int ExampleCount,
        IReadOnlyList<WotSpecFixtureEntry> Files)
    {
        /// <summary>
        /// Reads the manifest embedded beside the examples.
        /// </summary>
        public static WotSpecFixtureManifest Load()
        {
            byte[] bytes = ReadManifest();
            using var document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            JsonElement source = root.GetProperty("source");

            var files = new List<WotSpecFixtureEntry>();
            foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
            {
                files.Add(new WotSpecFixtureEntry(
                    file.GetProperty("name").GetString()!,
                    file.GetProperty("bytes").GetInt32(),
                    file.GetProperty("sha256").GetString()!));
            }

            JsonElement encoding = root.GetProperty("encoding");
            return new WotSpecFixtureManifest(
                source.GetProperty("repository").GetString()!,
                source.GetProperty("branch").GetString()!,
                source.GetProperty("commit").GetString()!,
                source.GetProperty("path").GetString()!,
                root.GetProperty("bindingRevision").GetString()!,
                encoding.GetProperty("lineEnding").GetString()!,
                root.GetProperty("exampleCount").GetInt32(),
                files);
        }

        /// <summary>
        /// Renders the manifest exactly as it is checked in. The newlines are
        /// forced to LF so a regeneration on Windows produces no diff of its
        /// own.
        /// </summary>
        public string Write()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(
                buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("$comment", Comment);
                writer.WriteStartObject("source");
                writer.WriteString("repository", Repository);
                writer.WriteString("branch", Branch);
                writer.WriteString("commit", Commit);
                writer.WriteString("path", SourcePath);
                writer.WriteEndObject();
                writer.WriteString("bindingRevision", BindingRevision);
                writer.WriteStartObject("encoding");
                writer.WriteString("charset", "utf-8");
                writer.WriteString("lineEnding", LineEnding);
                writer.WriteString("transformation", "none");
                writer.WriteEndObject();
                writer.WriteNumber("exampleCount", ExampleCount);
                writer.WriteStartArray("files");
                foreach (WotSpecFixtureEntry entry in Files)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", entry.Name);
                    writer.WriteNumber("bytes", entry.Bytes);
                    writer.WriteString("sha256", entry.Sha256);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(
                WotSpecFixtureManifestTests.WithoutCarriageReturns(buffer.ToArray())) + "\n";
        }

        private static byte[] ReadManifest()
        {
            string resource = typeof(WotSpecFixtureManifest).Assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith(
                    "Wot.Assets." + WotSpecFixtureManifestTests.ManifestFileName,
                    StringComparison.Ordinal));
            using Stream stream = typeof(WotSpecFixtureManifest).Assembly
                .GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The fixture manifest is not embedded.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        private const string Comment =
            "Vendored copies of the WoT Binding specification examples. Generated by the " +
            "explicit test Opc.Ua.Types.Tests.Wot.WotSpecFixtureManifestTests." +
            "RegenerateFromSpecCheckout; do not edit by hand.";
    }
}
