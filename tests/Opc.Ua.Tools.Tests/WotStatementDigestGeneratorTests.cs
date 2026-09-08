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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// The statement-digest generator reads the specification's own requirement
    /// ledgers at an exact commit and verifies them, rather than re-deriving
    /// them from the prose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Re-deriving is what a previous version of the generator did, and it was
    /// wrong in the way a second implementation of a published algorithm always
    /// eventually is: a normative statement is a sentence, a table cell or a
    /// list item, not a line, so a line scanner found a different set with
    /// different ordinals and therefore different digests. Copying what the
    /// specification published removes the second implementation.
    /// </para>
    /// <para>
    /// These run against a synthetic specification repository built here, so
    /// they need no access to the members-only draft. The mutations are the
    /// point: each one is a way the pin could silently rot, and each has to
    /// stop the generator.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class WotStatementDigestGeneratorTests
    {
        /// <summary>
        /// A specification whose ledgers the stack ledger agrees with produces
        /// an inventory of exactly the requirements marked as left to an
        /// implementation, and reproduces byte for byte.
        /// </summary>
        [Test]
        public async Task TheGeneratorCopiesAndVerifiesThePinnedLedgersAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            using var inventory = JsonDocument.Parse(File.ReadAllBytes(fixture.OutputPath));
            JsonElement root = inventory.RootElement;
            JsonElement pinned = root.GetProperty("pinnedTo");

            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
                Assert.That(root.GetProperty("statementCount").GetInt32(), Is.EqualTo(2));
                Assert.That(
                    pinned.GetProperty("commit").GetString(), Is.EqualTo(fixture.Commit));
                Assert.That(
                    pinned.GetProperty("tree").GetString(),
                    Does.Match("^[0-9a-f]{40}$"),
                    "The tree is pinned as well as the commit.");
                Assert.That(
                    root.GetProperty("statements").EnumerateArray()
                        .Select(s => s.GetProperty("specId").GetString()),
                    Is.EqualTo(s_expectedSpecIds).AsCollection,
                    "Only the requirements marked pendingStackTests are selected, in order.");
                Assert.That(
                    root.GetProperty("statements").EnumerateArray()
                        .Select(s => s.GetProperty("statementHash").GetString()),
                    Is.EqualTo(new[] { fixture.AlphaHash, fixture.BetaHash }).AsCollection,
                    "The digests are the specification's, copied rather than recomputed " +
                    "from a second reading of the prose.");
                Assert.That(
                    root.GetProperty("statements").EnumerateArray()
                        .Select(s => s.GetProperty("applicability").GetString()),
                    Is.EqualTo(s_expectedApplicability).AsCollection);
                Assert.That(
                    inventory.RootElement.GetRawText(),
                    Does.Not.Contain("Alpha statement"),
                    "The normative prose of a members-only draft is not republished.");
            });
        }

        /// <summary>
        /// Regenerating from the same commit produces the same bytes, which is
        /// what makes the pinned digest worth anything.
        /// </summary>
        [Test]
        public async Task TheGeneratorIsByteReproducibleAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.RunAsync().ConfigureAwait(false);
            byte[] first = File.ReadAllBytes(fixture.OutputPath);

            await fixture.RunAsync().ConfigureAwait(false);
            byte[] second = File.ReadAllBytes(fixture.OutputPath);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo(first).AsCollection);
                Assert.That(
                    Encoding.UTF8.GetString(first),
                    Does.Not.Contain("\r"),
                    "The bytes are the same on every platform, so the newline is LF.");
                Assert.That(first[0], Is.EqualTo((byte)'{'), "No byte-order mark.");
            });
        }

        [Test]
        public void PowerShellErrorPresentationIsNormalized()
        {
            const string rendered =
                "\u001b[31;1mException:\u001b[0m script.ps1:316\n" +
                "\u001b[36;1mLine |\u001b[0m\n" +
                " 316 | throw ...\n" +
                "     | The stack ledger records 'sec-alpha#009', which the specification does |\n" +
                "     | not state.";

            string normalized = NormalizePowerShellOutput(rendered);

            Assert.Multiple(() =>
            {
                Assert.That(normalized, Does.Not.Contain('\u001b'));
                Assert.That(
                    normalized,
                    Does.Contain("which the specification does not state"));
            });
        }

        /// <summary>
        /// -Verify reproduces the inventory and compares it, so a maintainer
        /// holding the draft can prove the vendored file is what the pinned
        /// commit states.
        /// </summary>
        [Test]
        public async Task VerifyAcceptsTheInventoryItProducesAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.RunAsync().ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync("-Verify").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("Verified 2 statement digest(s)"));
            });
        }

        /// <summary>
        /// The reads go through the pinned commit, so a working tree edited
        /// afterwards - or a later commit on the same branch - cannot change
        /// what the inventory says. That is what makes the pin a pin.
        /// </summary>
        [Test]
        public async Task AChangeAfterThePinnedCommitCannotMoveTheAnswerAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.RunAsync().ConfigureAwait(false);
            byte[] pinned = File.ReadAllBytes(fixture.OutputPath);

            // A later commit restates a requirement, and the working tree is
            // left holding it. Neither is what the stack ledger pins.
            await fixture.MutateAndCommitAsync(
                SpecFixture.BindingLedgerPath,
                json => json.Replace(
                    "Alpha statement", "Alpha restatement", StringComparison.Ordinal))
                .ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, result.Output);
                Assert.That(
                    File.ReadAllBytes(fixture.OutputPath),
                    Is.EqualTo(pinned).AsCollection,
                    "The pinned commit still states what it stated.");
            });
        }

        /// <summary>
        /// A restatement upstream that was not re-hashed is the fault the whole
        /// mechanism exists for: the text and the digest beside it disagree.
        /// </summary>
        [Test]
        public async Task ARestatedRequirementIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.MutateUpstreamAndRepinAsync(
                SpecFixture.BindingLedgerPath,
                json => json.Replace(
                    "Alpha statement", "Alpha restatement", StringComparison.Ordinal))
                .ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("does not hash to the digest"));
                Assert.That(result.Output, Does.Contain("sec-alpha#001"));
            });
        }

        /// <summary>
        /// A stack ledger carrying a digest the specification does not state is
        /// a half-update: one file was edited and the other was not.
        /// </summary>
        [Test]
        public async Task AHashTheSpecificationDoesNotStateIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            fixture.MutateStackLedger(json => json.Replace(
                fixture.AlphaHash,
                "sha256:" + new string('0', 64),
                StringComparison.Ordinal));

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("the specification states"));
                Assert.That(result.Output, Does.Contain("sec-alpha#001"));
            });
        }

        /// <summary>
        /// A clause or applicability the two files disagree about is the same
        /// half-update wearing a different field.
        /// </summary>
        [TestCase("\"clause\": \"sec-alpha\"", "\"clause\": \"sec-gamma\"", "clause")]
        [TestCase("\"applicability\": \"converter\"", "\"applicability\": \"runtime\"",
            "applicability")]
        public async Task AFieldTheTwoFilesDisagreeAboutIsRefusedAsync(
            string from, string to, string expected)
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            fixture.MutateStackLedger(json => ReplaceFirst(json, from, to));

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain(expected));
            });
        }

        /// <summary>
        /// An identifier the specification no longer states cannot be answered
        /// for, however good the evidence beside it looks.
        /// </summary>
        [Test]
        public async Task AnIdentifierTheSpecificationDroppedIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            fixture.MutateStackLedger(json => json.Replace(
                "sec-alpha#001", "sec-alpha#009", StringComparison.Ordinal));

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("which the specification does not state"));
            });
        }

        /// <summary>
        /// A requirement the specification newly leaves to an implementation
        /// has to be answered for or written down as a gap; it may not simply
        /// go unrecorded.
        /// </summary>
        [Test]
        public async Task ARequirementTheSpecificationAddedIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            fixture.MutateStackLedger(json => RemoveBetaRequirement(json));

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("the ledger does not record"));
                Assert.That(result.Output, Does.Contain("sec-beta#002"));
            });
        }

        /// <summary>
        /// A requirement the specification stopped leaving to an implementation
        /// is evidence for a rule nobody is held to.
        /// </summary>
        [Test]
        public async Task ARequirementNoLongerLeftToTheStackIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.MutateUpstreamAndRepinAsync(
                SpecFixture.ConnectivityLedgerPath,
                json => json.Replace(
                    "\"pendingStackTests\": true", "\"pendingStackTests\": false",
                    StringComparison.Ordinal))
                .ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Output,
                    Does.Contain("no longer leaves to this"));
            });
        }

        /// <summary>
        /// An identifier names one statement or it names nothing, so a ledger
        /// that states it twice is refused on either side.
        /// </summary>
        [Test]
        public async Task ADuplicateIdentifierIsRefusedUpstreamAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.MutateUpstreamAndRepinAsync(
                SpecFixture.ConnectivityLedgerPath,
                json => json.Replace(
                    "\"id\": \"sec-gamma#001\"", "\"id\": \"sec-alpha#001\"",
                    StringComparison.Ordinal))
                .ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("is stated twice upstream"));
            });
        }

        [Test]
        public async Task ADuplicateIdentifierIsRefusedInTheStackLedgerAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            fixture.MutateStackLedger(json => json.Replace(
                "\"specId\": \"sec-beta#002\"", "\"specId\": \"sec-alpha#001\"",
                StringComparison.Ordinal));

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("twice"));
            });
        }

        /// <summary>
        /// Reading a different commit than the one pinned is the silent rot the
        /// pin exists to stop, and it is a different failure from a commit that
        /// is not there at all: one says re-pin, the other says fetch.
        /// </summary>
        [Test]
        public async Task ADifferentCommitIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            string second = await fixture.MutateAndCommitAsync(
                SpecFixture.BindingLedgerPath,
                json => json.Replace(
                    "\"positiveGate\"", "\"positiveGateway\"", StringComparison.Ordinal))
                .ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync("-Commit", second).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("but the stack ledger pins"));
            });
        }

        [Test]
        public async Task AnInaccessibleCommitIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            fixture.MutateStackLedger(json => json.Replace(
                fixture.Commit, new string('a', 40), StringComparison.Ordinal));

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("is not present in"));
            });
        }

        /// <summary>
        /// A checkout that is not a git repository holds no commit, so it
        /// cannot be read at a pinned one.
        /// </summary>
        [Test]
        public async Task ACheckoutThatIsNotARepositoryIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);

            ScriptResult result = await fixture
                .RunWithSpecRootAsync(Path.GetTempPath()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("is not a git repository"));
            });
        }

        /// <summary>
        /// A ledger written to a schema this script does not read is not one it
        /// may guess at.
        /// </summary>
        [Test]
        public async Task AnUnknownUpstreamSchemaIsRefusedAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.MutateUpstreamAndRepinAsync(
                SpecFixture.BindingLedgerPath,
                json => json.Replace(
                    "\"schemaVersion\": 1", "\"schemaVersion\": 7", StringComparison.Ordinal))
                .ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("this script reads"));
            });
        }

        /// <summary>
        /// The whole point of pinning by digest: a source that changed after
        /// the inventory was vendored fails -Verify, naming both digests.
        /// </summary>
        [Test]
        public async Task VerifyRefusesAnInventoryTheSourcesNoLongerProduceAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);
            await fixture.RunAsync().ConfigureAwait(false);

            // Re-pin the stack ledger onto a commit whose ledger states one of
            // the two requirements differently: the same 2 identifiers, a
            // different digest, so only the source digest moved.
            string second = await fixture.MutateAndCommitAsync(
                SpecFixture.ConnectivityLedgerPath,
                json => json
                    .Replace(fixture.BetaHash, fixture.BetaPrimeHash, StringComparison.Ordinal)
                    .Replace("Beta statement", "Beta statement prime", StringComparison.Ordinal))
                .ConfigureAwait(false);
            fixture.MutateStackLedger(json => json
                .Replace(fixture.Commit, second, StringComparison.Ordinal)
                .Replace(fixture.BetaHash, fixture.BetaPrimeHash, StringComparison.Ordinal));

            ScriptResult result = await fixture.RunAsync("-Verify").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("is not what commit"));
                Assert.That(result.Output, Does.Contain("Regenerate it"));
            });
        }

        [Test]
        public async Task VerifyRefusesAMissingInventoryAsync()
        {
            using SpecFixture fixture = await SpecFixture.CreateAsync().ConfigureAwait(false);

            ScriptResult result = await fixture.RunAsync("-Verify").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(result.Output, Does.Contain("to verify"));
            });
        }

        /// <summary>
        /// A maintainer holding the members-only draft can prove the vendored
        /// inventory is exactly what the pinned commit states. Everyone else
        /// gets the offline half, which is the pinned digest.
        /// </summary>
        [Test]
        public async Task TheVendoredInventoryMatchesThePinnedSourcesAsync()
        {
            string? specRoot = Environment.GetEnvironmentVariable("WOT_SPEC_ROOT");
            if (string.IsNullOrWhiteSpace(specRoot) || !Directory.Exists(specRoot))
            {
                Assert.Ignore(
                    "Set WOT_SPEC_ROOT to a spec-drafts checkout holding the pinned commit " +
                    "to verify the vendored inventory against it.");
            }

            string repositoryRoot = FindRepositoryRoot();
            ScriptResult result = await RunGeneratorAsync(
                repositoryRoot,
                [
                    "-SpecRoot", specRoot!,
                    "-LedgerPath",
                    Path.Combine(
                        repositoryRoot,
                        "tests", "Opc.Ua.Types.Tests", "Wot", "Assets",
                        "wot-spec-requirements.json"),
                    "-OutputPath",
                    Path.Combine(
                        repositoryRoot,
                        "tests", "Opc.Ua.Types.Tests", "Wot", "Assets",
                        "wot-spec-statements.json"),
                    "-Verify"
                ]).ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
        }

        private static string ReplaceFirst(string value, string from, string to)
        {
            int index = value.IndexOf(from, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"'{from}' is not in the fixture.");
            return string.Concat(value.AsSpan(0, index), to, value.AsSpan(index + from.Length));
        }

        private static string RemoveBetaRequirement(string json)
        {
            using var document = JsonDocument.Parse(json);
            var kept = new List<JsonElement>();
            foreach (JsonElement requirement in
                document.RootElement.GetProperty("requirements").EnumerateArray())
            {
                if (!string.Equals(
                    requirement.GetProperty("specId").GetString(),
                    "sec-beta#002",
                    StringComparison.Ordinal))
                {
                    kept.Add(requirement.Clone());
                }
            }

            var builder = new StringBuilder();
            builder.Append("{\"schemaVersion\":1,\"pinnedTo\":")
                .Append(document.RootElement.GetProperty("pinnedTo").GetRawText())
                .Append(",\"pendingStackTestCount\":").Append(kept.Count)
                .Append(",\"requirements\":[");
            for (int ii = 0; ii < kept.Count; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(',');
                }
                builder.Append(kept[ii].GetRawText());
            }
            return builder.Append("]}").ToString();
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(
                    current, "tools", "wot-spec", "Get-WotStatementDigests.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private static async Task<ScriptResult> RunGeneratorAsync(
            string repositoryRoot, IReadOnlyList<string> arguments)
        {
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = repositoryRoot;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.Environment["NO_COLOR"] = "1";
            process.StartInfo.Environment["TERM"] = "dumb";
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(
                repositoryRoot, "tools", "wot-spec", "Get-WotStatementDigests.ps1"));
            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            return new ScriptResult(
                process.ExitCode,
                NormalizePowerShellOutput(output + error));
        }

        private static string NormalizePowerShellOutput(string value)
        {
            var normalized = new StringBuilder(value.Length);
            bool previousWasWhitespace = false;
            for (int ii = 0; ii < value.Length; ii++)
            {
                char character = value[ii];
                if (character == '\u001b' &&
                    ii + 1 < value.Length &&
                    value[ii + 1] == '[')
                {
                    ii += 2;
                    while (ii < value.Length &&
                        (value[ii] < '@' || value[ii] > '~'))
                    {
                        ii++;
                    }
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    if (normalized.Length > 0 && !previousWasWhitespace)
                    {
                        normalized.Append(' ');
                    }
                    previousWasWhitespace = true;
                    continue;
                }

                normalized.Append(character);
                previousWasWhitespace = false;
            }

            if (normalized.Length > 0 && normalized[^1] == ' ')
            {
                normalized.Length--;
            }
            string result = normalized.ToString();
            while (result.Contains(" | ", StringComparison.Ordinal))
            {
                result = result.Replace(" | ", " ", StringComparison.Ordinal);
            }
            return result;
        }

        private sealed record ScriptResult(int ExitCode, string Output);

        /// <summary>
        /// The two requirements the fixture specification leaves to an
        /// implementation, in the order the inventory sorts them.
        /// </summary>
        private static readonly string[] s_expectedSpecIds = ["sec-alpha#001", "sec-beta#002"];

        /// <summary>
        /// The applicability the fixture specification states for each of them.
        /// </summary>
        private static readonly string[] s_expectedApplicability = ["converter", "runtime"];

        /// <summary>
        /// A synthetic specification repository laid out like spec-drafts: two
        /// requirement ledgers under source/wot-specs, committed, plus a stack
        /// evidence ledger pinned to that commit.
        /// </summary>
        private sealed class SpecFixture : IDisposable
        {
            public const string BindingLedgerPath =
                "source/wot-specs/WoT-Binding/tools/requirements.json";

            public const string ConnectivityLedgerPath =
                "source/wot-specs/WoT-Connectivity/tools/requirements.json";

            private SpecFixture(string rootPath, string repositoryRoot)
            {
                m_rootPath = rootPath;
                m_repositoryRoot = repositoryRoot;
                SpecRoot = Path.Combine(rootPath, "spec");
                StackLedgerPath = Path.Combine(rootPath, "wot-spec-requirements.json");
                OutputPath = Path.Combine(rootPath, "wot-spec-statements.json");
            }

            public string SpecRoot { get; }

            public string StackLedgerPath { get; }

            public string OutputPath { get; }

            public string Commit { get; private set; } = string.Empty;

            public string AlphaHash { get; } = Digest("Alpha statement");

            public string BetaHash { get; } = Digest("Beta statement");

            public string BetaPrimeHash { get; } = Digest("Beta statement prime");

            public static async Task<SpecFixture> CreateAsync()
            {
                string rootPath = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "wot-statement-fixtures",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(rootPath);
                var fixture = new SpecFixture(rootPath, FindRepositoryRoot());

                Directory.CreateDirectory(fixture.SpecRoot);
                await fixture.RunGitAsync("init", "-b", "main").ConfigureAwait(false);
                await fixture.RunGitAsync(
                    "config", "user.email", "wot-spec@example.invalid").ConfigureAwait(false);
                await fixture.RunGitAsync("config", "user.name", "WoT Spec").ConfigureAwait(false);

                fixture.Write(BindingLedgerPath, BindingLedger(fixture.AlphaHash));
                fixture.Write(ConnectivityLedgerPath, ConnectivityLedger(fixture.BetaHash));
                await fixture.RunGitAsync("add", ".").ConfigureAwait(false);
                await fixture.RunGitAsync("commit", "-m", "ledgers").ConfigureAwait(false);
                fixture.Commit = (await fixture
                    .RunGitAsync("rev-parse", "HEAD").ConfigureAwait(false)).Trim();

                File.WriteAllText(fixture.StackLedgerPath, StackLedger(fixture));
                return fixture;
            }

            public Task<ScriptResult> RunAsync(params string[] extra)
            {
                return RunWithSpecRootAsync(SpecRoot, extra);
            }

            public Task<ScriptResult> RunWithSpecRootAsync(
                string specRoot, params string[] extra)
            {
                var arguments = new List<string>
                {
                    "-SpecRoot", specRoot,
                    "-LedgerPath", StackLedgerPath,
                    "-OutputPath", OutputPath
                };
                arguments.AddRange(extra);
                return RunGeneratorAsync(m_repositoryRoot, arguments);
            }

            public void MutateStackLedger(Func<string, string> mutate)
            {
                File.WriteAllText(
                    StackLedgerPath, mutate(File.ReadAllText(StackLedgerPath)));
            }

            /// <summary>
            /// Rewrites one upstream ledger and commits it, returning the new
            /// commit so a test can ask for it explicitly.
            /// </summary>
            public async Task<string> MutateAndCommitAsync(
                string path, Func<string, string> mutate)
            {
                string full = Path.Combine(SpecRoot, path.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(full, mutate(File.ReadAllText(full)));
                await RunGitAsync("add", ".").ConfigureAwait(false);
                await RunGitAsync("commit", "-m", "mutate").ConfigureAwait(false);
                return (await RunGitAsync("rev-parse", "HEAD").ConfigureAwait(false)).Trim();
            }

            /// <summary>
            /// Rewrites one upstream ledger, commits it, and moves the stack
            /// ledger's pin onto the new commit - which is what a maintainer
            /// does when the specification lands a revision, and therefore what
            /// a mutation has to be for the generator to read it at all.
            /// </summary>
            public async Task MutateUpstreamAndRepinAsync(
                string path, Func<string, string> mutate)
            {
                string previous = Commit;
                string next = await MutateAndCommitAsync(path, mutate).ConfigureAwait(false);
                Commit = next;
                MutateStackLedger(json => json.Replace(
                    previous, next, StringComparison.Ordinal));
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(m_rootPath))
                    {
                        foreach (string file in Directory.EnumerateFiles(
                            m_rootPath, "*", SearchOption.AllDirectories))
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }
                        Directory.Delete(m_rootPath, true);
                    }
                }
                catch (IOException)
                {
                    TestContext.Out.WriteLine("Could not delete '{0}'.", m_rootPath);
                }
                catch (UnauthorizedAccessException)
                {
                    TestContext.Out.WriteLine("Could not delete '{0}'.", m_rootPath);
                }
            }

            private static string Digest(string statement)
            {
                return "sha256:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(statement))).ToLowerInvariant();
            }

            private static string BindingLedger(string alphaHash)
            {
                return $$"""
                {
                  "schemaVersion": 1,
                  "specification": "WoT Binding",
                  "source": "source/wot-specs/WoT-Binding/spec.md",
                  "positiveGate": "extras/wot-specs/validate_all.py",
                  "requirements": [
                    {
                      "id": "sec-alpha#001",
                      "clause": "sec-alpha",
                      "keywords": [ "shall" ],
                      "statement": "Alpha statement",
                      "statementHash": "{{alphaHash}}",
                      "applicability": "converter",
                      "evidence": [ "stack" ],
                      "pythonTests": [],
                      "dotnetTests": [ "Opc.Ua.Types.Tests::AlphaTests" ],
                      "vectors": [],
                      "pendingStackTests": true
                    },
                    {
                      "id": "sec-alpha#002",
                      "clause": "sec-alpha",
                      "keywords": [ "may" ],
                      "statement": "A statement the specification proves itself",
                      "statementHash": "{{Digest("A statement the specification proves itself")}}",
                      "applicability": "validator",
                      "evidence": [ "positive" ],
                      "pythonTests": [ "tools/test_alpha.py::test_alpha" ],
                      "dotnetTests": [],
                      "vectors": []
                    }
                  ]
                }
                """;
            }

            private static string ConnectivityLedger(string betaHash)
            {
                return $$"""
                {
                  "schemaVersion": 1,
                  "specification": "WoT Connectivity",
                  "source": "source/wot-specs/WoT-Connectivity/spec.md",
                  "positiveGate": "extras/wot-specs/validate_all.py",
                  "requirements": [
                    {
                      "id": "sec-beta#002",
                      "clause": "sec-beta",
                      "keywords": [ "shall", "shall not" ],
                      "statement": "Beta statement",
                      "statementHash": "{{betaHash}}",
                      "applicability": "runtime",
                      "evidence": [ "stack", "boundary" ],
                      "pythonTests": [],
                      "dotnetTests": [ "Opc.Ua.WotCon.Tests::BetaTests" ],
                      "vectors": [],
                      "pendingStackTests": true
                    },
                    {
                      "id": "sec-gamma#001",
                      "clause": "sec-gamma",
                      "keywords": [ "should" ],
                      "statement": "Gamma statement",
                      "statementHash": "{{Digest("Gamma statement")}}",
                      "applicability": "validator",
                      "evidence": [ "positive" ],
                      "pythonTests": [ "tools/test_gamma.py::test_gamma" ],
                      "dotnetTests": [],
                      "vectors": []
                    }
                  ]
                }
                """;
            }

            private static string StackLedger(SpecFixture fixture)
            {
                return $$"""
                {
                  "schemaVersion": 1,
                  "pinnedTo": {
                    "repository": "https://example.invalid/spec-drafts.git",
                    "branch": "main",
                    "commit": "{{fixture.Commit}}",
                    "bindingRevision": "1.1",
                    "ledgers": [
                      "source/wot-specs/WoT-Binding/tools/requirements.json",
                      "source/wot-specs/WoT-Connectivity/tools/requirements.json"
                    ],
                    "statementInventory": {
                      "path": "wot-spec-statements.json",
                      "sha256": "{{new string('0', 64)}}"
                    }
                  },
                  "pendingStackTestCount": 2,
                  "requirements": [
                    {
                      "specId": "sec-alpha#001",
                      "specification": "WoT Binding",
                      "clause": "sec-alpha",
                      "applicability": "converter",
                      "statementHash": "{{fixture.AlphaHash}}",
                      "assembly": "Opc.Ua.Types.Tests",
                      "tests": [ "Opc.Ua.Types.Tests.AlphaTests" ]
                    },
                    {
                      "specId": "sec-beta#002",
                      "specification": "WoT Connectivity",
                      "clause": "sec-beta",
                      "applicability": "runtime",
                      "statementHash": "{{fixture.BetaHash}}",
                      "assembly": "Opc.Ua.WotCon.Tests",
                      "tests": [ "Opc.Ua.WotCon.Tests.BetaTests" ]
                    }
                  ]
                }
                """;
            }

            private void Write(string relativePath, string content)
            {
                string full = Path.Combine(
                    SpecRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }

            private async Task<string> RunGitAsync(params string[] arguments)
            {
                using var process = new Process();
                process.StartInfo.FileName = "git";
                process.StartInfo.WorkingDirectory = SpecRoot;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                foreach (string argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                Assert.That(process.Start(), Is.True);
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"git {string.Join(' ', arguments)}: {output}{error}"));
                return output;
            }

            private readonly string m_rootPath;
            private readonly string m_repositoryRoot;
        }
    }
}
#endif
