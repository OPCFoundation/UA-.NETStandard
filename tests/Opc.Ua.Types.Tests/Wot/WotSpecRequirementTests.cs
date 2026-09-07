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
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Holds the stack-side evidence for the WoT specification requirements
    /// whose proof is an OPC UA implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification's own ledgers mark 97 requirements
    /// <c>pendingStackTests</c> - it cannot name a test in a repository it does
    /// not build. The checked-in ledger names them, and this fixture is what
    /// makes the naming worth anything: every mapping that names this assembly
    /// is resolved by reflection and has to be something NUnit will run.
    /// </para>
    /// <para>
    /// The whole-ledger invariants are checked here because this is the
    /// assembly the ledger lives in: the entry count, the uniqueness of the
    /// requirement identifiers, and the pinned specification commit, which is
    /// checked against the fixture manifest so a revision bump cannot update
    /// one and forget the other.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Category("WotSpecExamples")]
    [Parallelizable]
    public sealed class WotSpecRequirementTests
    {
        private const string ThisAssembly = "Opc.Ua.Types.Tests";

        [Test]
        public void EveryMappingNamingThisAssemblyResolvesAndWillRun()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);

            Assert.Multiple(() =>
            {
                foreach (WotSpecRequirement requirement in requirements
                    .Where(r => r.Assembly == ThisAssembly))
                {
                    foreach (string name in requirement.Tests)
                    {
                        Assert.That(
                            WotSpecRequirementLedger.DescribeResolution(assembly, name),
                            Is.EqualTo("runs"),
                            $"{requirement.SpecId} maps onto '{name}'.");
                    }
                }
            });
        }

        /// <summary>
        /// A requirement either names evidence or states why it has none.
        /// Silence would be the one outcome that hides the gap the whole ledger
        /// exists to expose.
        /// </summary>
        [Test]
        public void EveryRequirementEitherHasEvidenceOrStatesItsGap()
        {
            List<WotSpecRequirement> requirements = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly);

            Assert.Multiple(() =>
            {
                foreach (WotSpecRequirement requirement in requirements)
                {
                    if (requirement.Tests.Count > 0)
                    {
                        Assert.That(
                            requirement.Gap,
                            Is.Null,
                            $"{requirement.SpecId} names evidence and a gap; it is one " +
                            "or the other.");
                        continue;
                    }
                    Assert.That(
                        requirement.Gap,
                        Is.Not.Null.And.Not.Empty,
                        $"{requirement.SpecId} names no test and states no reason.");
                }
            });
        }

        /// <summary>
        /// The ledger covers exactly the requirements the specification marked
        /// pending, so an entry cannot be quietly dropped and the count cannot
        /// drift from what the specification publishes.
        /// </summary>
        [Test]
        public void TheLedgerCoversEveryPendingRequirementExactlyOnce()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);
            (_, _, _, int pending) = WotSpecRequirementLedger.ReadHeader(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(
                    requirements,
                    Has.Count.EqualTo(pending),
                    "The ledger records how many requirements the specification left to " +
                    "this stack, so dropping one is a failure rather than a shorter file.");
                Assert.That(
                    requirements.Select(r => r.SpecId),
                    Is.Unique,
                    "A requirement listed twice hides one of the two mappings.");
                Assert.That(
                    requirements.Select(r => r.StatementHash),
                    Is.All.StartWith("sha256:"),
                    "Each mapping carries the hash of the statement it answers, so a " +
                    "restatement upstream invalidates it rather than keeping evidence " +
                    "for something the specification no longer says.");
                Assert.That(
                    requirements.Select(r => r.Assembly).Distinct(),
                    Is.EquivalentTo(s_assemblies),
                    "Every mapping names an assembly that checks it; a name nothing " +
                    "checks is a mapping nothing proves.");
            });
        }

        /// <summary>
        /// The statement digests are checked rather than merely carried. The
        /// inventory is pinned by the digest of its actual bytes, so a
        /// statementHash edited in the ledger and not re-vendored - or an
        /// inventory edited without re-pinning - fails here rather than being
        /// carried as evidence for a statement nobody verified.
        /// </summary>
        [Test]
        public void TheStatementInventoryIsTheOneTheLedgerPinned()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            (string path, string pinned) =
                WotSpecRequirementLedger.ReadInventoryPin(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(
                    path,
                    Is.EqualTo(WotSpecRequirementLedger.InventoryFileName));
                Assert.That(
                    pinned,
                    Has.Length.EqualTo(64),
                    "A pin is a whole SHA-256, not a prefix of one.");
                Assert.That(pinned, Does.Match("^[0-9a-f]{64}$"));
                Assert.That(
                    WotSpecRequirementLedger.ComputeInventoryDigest(assembly),
                    Is.EqualTo(pinned),
                    "The digest is over the inventory's embedded bytes, so this answers " +
                    "'is this the file the ledger was pinned to'.");
            });
        }

        /// <summary>
        /// Every requirement the ledger records resolves to exactly one
        /// inventory record, and the two agree on the digest in full and on
        /// every field the specification states about it. A half update - an
        /// identifier on one side only, or a hash or clause changed on one side
        /// only - is what this refuses.
        /// </summary>
        [Test]
        public void EveryRequirementResolvesToExactlyOneVerifiedStatement()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);
            List<WotSpecStatement> statements =
                WotSpecRequirementLedger.LoadStatements(assembly);

            var byId = new Dictionary<string, List<WotSpecStatement>>(StringComparer.Ordinal);
            foreach (WotSpecStatement statement in statements)
            {
                if (!byId.TryGetValue(statement.SpecId, out List<WotSpecStatement>? bucket))
                {
                    bucket = [];
                    byId[statement.SpecId] = bucket;
                }
                bucket.Add(statement);
            }

            Assert.Multiple(() =>
            {
                foreach (WotSpecRequirement requirement in requirements)
                {
                    Assert.That(
                        byId.TryGetValue(requirement.SpecId, out List<WotSpecStatement>? found),
                        Is.True,
                        $"{requirement.SpecId} names no statement in the pinned inventory.");
                    if (found is null)
                    {
                        continue;
                    }
                    Assert.That(
                        found,
                        Has.Count.EqualTo(1),
                        $"{requirement.SpecId} appears {found.Count} times in the inventory.");
                    Assert.That(
                        found[0].StatementHash,
                        Is.EqualTo(requirement.StatementHash),
                        $"{requirement.SpecId} carries a digest the inventory does not.");
                    Assert.That(found[0].Specification, Is.EqualTo(requirement.Specification));
                    Assert.That(found[0].Clause, Is.EqualTo(requirement.Clause));
                    Assert.That(
                        found[0].Applicability,
                        Is.EqualTo(requirement.Applicability),
                        $"{requirement.SpecId} is applicable to something the specification " +
                        "does not say it is.");
                }
                Assert.That(
                    statements.Select(s => s.SpecId),
                    Is.EquivalentTo(requirements.Select(r => r.SpecId)),
                    "The inventory holds exactly the requirements the ledger records; an " +
                    "extra one is a digest nothing maps onto, and a missing one is a " +
                    "mapping nothing verifies.");
            });
        }

        /// <summary>
        /// Each record carries what the specification's own ledger states about
        /// the statement, so a reader can tell what kind of rule a digest stands
        /// for without holding the draft.
        /// </summary>
        [Test]
        public void EveryStatementCarriesTheMetadataTheSpecificationStates()
        {
            List<WotSpecStatement> statements = WotSpecRequirementLedger.LoadStatements(
                typeof(WotSpecRequirementTests).Assembly);

            Assert.Multiple(() =>
            {
                foreach (WotSpecStatement statement in statements)
                {
                    Assert.That(
                        statement.Evidence,
                        Does.Contain("stack"),
                        $"{statement.SpecId} is not a requirement the specification leaves " +
                        "to an implementation, so this repository has no business answering " +
                        "for it.");
                    Assert.That(
                        statement.Keywords,
                        Is.Not.Empty,
                        $"{statement.SpecId} states no RFC 2119 keyword, so it is not a " +
                        "normative statement.");
                    Assert.That(
                        statement.Keywords,
                        Is.All.Matches<string>(
                            k => k is "shall" or "shall not" or "should" or "should not"
                                or "may" or "may not"),
                        $"{statement.SpecId} names a keyword RFC 2119 does not.");
                    Assert.That(
                        statement.Applicability,
                        Is.Not.Empty,
                        $"{statement.SpecId} states no applicability.");
                    Assert.That(
                        statement.StatementLength,
                        Is.GreaterThan(0),
                        $"{statement.SpecId} records an empty statement.");
                }
            });
        }

        /// <summary>
        /// A digest is verified as bytes, not as a label. Every hash is the
        /// full 64 hexadecimal digits of a SHA-256, in the one spelling that
        /// compares equal.
        /// </summary>
        [Test]
        public void EveryStatementDigestIsAWholeSha256()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecStatement> statements =
                WotSpecRequirementLedger.LoadStatements(assembly);
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);

            Assert.Multiple(() =>
            {
                foreach (string hash in statements.Select(s => s.StatementHash)
                    .Concat(requirements.Select(r => r.StatementHash)))
                {
                    Assert.That(
                        hash,
                        Does.Match("^sha256:[0-9a-f]{64}$"),
                        $"'{hash}' is not a whole lower-case SHA-256.");
                }
            });
        }

        /// <summary>
        /// A requirement's identifier is its clause and its ordinal within that
        /// clause, so the inventory's decomposition and the ledger's identifier
        /// are two spellings of one fact and have to agree.
        /// </summary>
        [Test]
        public void EveryStatementIdentifierDecomposesIntoItsClauseAndOrdinal()
        {
            List<WotSpecStatement> statements = WotSpecRequirementLedger.LoadStatements(
                typeof(WotSpecRequirementTests).Assembly);

            Assert.Multiple(() =>
            {
                foreach (WotSpecStatement statement in statements)
                {
                    Assert.That(
                        statement.SpecId,
                        Is.EqualTo(
                            statement.Clause + "#" +
                            statement.Ordinal.ToString(
                                "D3", System.Globalization.CultureInfo.InvariantCulture)),
                        $"{statement.SpecId} and its decomposition disagree.");
                    Assert.That(statement.Ordinal, Is.GreaterThan(0));
                }
            });
        }

        /// <summary>
        /// The inventory, the ledger and the vendored examples were read from
        /// one revision of the specification. If any two disagree, one was
        /// updated and another was forgotten. The inventory also names the
        /// sources it was read out of, by both the identity git stores them
        /// under and the digest of their bytes, so a re-vendor from another
        /// revision cannot look like this one.
        /// </summary>
        [Test]
        public void TheInventoryAndTheLedgerPinTheSameRevision()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            (string commit, string repository, _, int pending) =
                WotSpecRequirementLedger.ReadHeader(assembly);
            WotSpecInventoryHeader inventory =
                WotSpecRequirementLedger.ReadInventoryHeader(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(inventory.Commit, Is.EqualTo(commit));
                Assert.That(inventory.Repository, Is.EqualTo(repository));
                Assert.That(inventory.SchemaVersion, Is.EqualTo(2));
                Assert.That(
                    inventory.Tree,
                    Does.Match("^[0-9a-f]{40}$"),
                    "The commit's tree is pinned, not just the commit.");
                Assert.That(
                    inventory.StatementCount,
                    Is.EqualTo(pending),
                    "The inventory states how many statements it holds, so dropping one " +
                    "is a failure rather than a shorter file.");
                Assert.That(
                    WotSpecRequirementLedger.LoadStatements(assembly),
                    Has.Count.EqualTo(inventory.StatementCount));
                Assert.That(
                    inventory.Ledgers.Select(l => l.Path),
                    Is.EquivalentTo(s_upstreamLedgers),
                    "The inventory was read out of the specification's own requirement " +
                    "ledgers, which are the files that publish the digests.");
                foreach (WotSpecInventorySource source in inventory.Ledgers)
                {
                    Assert.That(source.Blob, Does.Match("^[0-9a-f]{40}$"));
                    Assert.That(source.Sha256, Does.Match("^[0-9a-f]{64}$"));
                    Assert.That(
                        source.RequirementCount,
                        Is.GreaterThanOrEqualTo(inventory.StatementCount / 2),
                        "A ledger that suddenly states almost nothing was not the one this " +
                        "inventory was built from.");
                }
            });
        }

        /// <summary>
        /// The stack ledger and the inventory name the same upstream files, so
        /// the two cannot be pinned to different halves of the specification.
        /// </summary>
        [Test]
        public void TheLedgerNamesTheUpstreamFilesTheInventoryWasReadFrom()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            WotSpecInventoryHeader inventory =
                WotSpecRequirementLedger.ReadInventoryHeader(assembly);

            Assert.That(
                WotSpecRequirementLedger.ReadLedgerPaths(assembly),
                Is.EquivalentTo(inventory.Ledgers.Select(l => l.Path)));
        }

        /// <summary>
        /// The two upstream requirement ledgers the digests are published in.
        /// </summary>
        private static readonly string[] s_upstreamLedgers =
        [
            "source/wot-specs/WoT-Binding/tools/requirements.json",
            "source/wot-specs/WoT-Connectivity/tools/requirements.json"
        ];

        /// <summary>
        /// The ledger and the vendored examples were read from one revision of
        /// the specification. If the two pins disagree, one was updated and the
        /// other was forgotten.
        /// </summary>
        [Test]
        public void TheLedgerAndTheFixturesArePinnedToTheSameRevision()
        {
            (string commit, string repository, string revision, _) =
                WotSpecRequirementLedger.ReadHeader(
                    typeof(WotSpecRequirementTests).Assembly);
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                Assert.That(commit, Is.EqualTo(manifest.Commit));
                Assert.That(repository, Is.EqualTo(manifest.Repository));
                Assert.That(
                    revision,
                    Is.EqualTo(Opc.Ua.Wot.WotBindingConformance.CurrentRevision));
            });
        }

        /// <summary>
        /// What is not proved is countable. The number is asserted so that
        /// closing a gap - or opening one - is a deliberate edit rather than a
        /// silent drift.
        /// </summary>
        [Test]
        public void TheUnprovedRequirementsAreExactlyTheOnesRecorded()
        {
            List<WotSpecRequirement> requirements = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly);

            IReadOnlyList<string> unproved = [.. requirements
                .Where(r => r.Tests.Count == 0)
                .Select(r => r.SpecId)
                .OrderBy(id => id, StringComparer.Ordinal)];

            Assert.That(unproved, Is.EqualTo(s_unprovedRequirements).AsCollection);
        }

        private static readonly string[] s_assemblies =
        [
            "Opc.Ua.Types.Tests",
            "Opc.Ua.WotCon.Tests",
            "Opc.Ua.WotCon.Bindings.Tests"
        ];

        /// <summary>
        /// The requirements this stack does not yet prove, in ascending order.
        /// Each carries its reason in the ledger.
        /// </summary>
        /// <remarks>
        /// The list is empty, and asserting an empty list is the point: every
        /// requirement the specification left to this stack now names evidence,
        /// so re-opening a gap has to be written down here before it is
        /// accepted.
        /// </remarks>
        private static readonly string[] s_unprovedRequirements = [];
    }
}
