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
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Holds candidate stack test references and explicit gaps for selected WoT
    /// specification requirements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stable specification ledger tags implementation requirements with
    /// <c>stack</c> - it cannot name a test in a repository it does not build.
    /// This fixture resolves references in this assembly by reflection and
    /// checks they are runnable. It does not infer assertion coverage from a
    /// fixture name; requirements without a reviewed mapping remain gaps.
    /// </para>
    /// <para>
    /// The whole-ledger invariants are checked here because this is the
    /// assembly the ledger lives in: the entry count, the uniqueness of the
    /// requirement identifiers, and the stable specification commit. The
    /// example manifest and clause-level traceability file remain independently
    /// pinned to their own source revision.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Category("WotSpecExamples")]
    [Parallelizable]
    public sealed class WotSpecRequirementTests
    {
        private const string ThisAssembly = "Opc.Ua.Types.Tests";
        private static readonly string[] s_projectionProfiles =
        [
            "WoT-ArchivalConverter",
            "WoT-Converter",
            "WoT-Modeller"
        ];
        private static readonly string[] s_semanticChangeRequirementIds =
        [
            "sec-semantic-change-events-optional#001",
            "sec-semantic-change-events-optional#002"
        ];

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
        /// A requirement without a candidate test reference states its gap.
        /// Partial gaps may coexist with references that cover only part of a requirement.
        /// </summary>
        [Test]
        public void EveryRequirementWithoutTestReferencesStatesItsGap()
        {
            List<WotSpecRequirement> requirements = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly);

            Assert.Multiple(() =>
            {
                foreach (WotSpecRequirement requirement in requirements)
                {
                    if (requirement.Tests.Count > 0)
                    {
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
        /// Verifies that any target-specific test references name declared runnable frameworks.
        /// </summary>
        [Test]
        public void TargetSpecificEvidenceNamesOnlyDeclaredRunnableFrameworks()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            string[] scoped = WotSpecRequirementLedger.Load(assembly)
                .SelectMany(requirement => requirement.Tests).Distinct()
                .Where(test => WotSpecRequirementLedger.ReadTargetFrameworks(assembly, test).Count != 0)
                .ToArray();
            Assert.That(scoped, Is.EqualTo(s_targetSpecificTests));
            foreach (string test in scoped)
            {
                Assert.That(WotSpecRequirementLedger.ReadTargetFrameworks(assembly, test),
                    Is.EqualTo(s_modernFrameworks));
            }
        }
        /// <summary>
        /// The ledger tracks every stable stack-applicable requirement and all
        /// Projections validator requirements without treating the latter as
        /// stack runtime obligations.
        /// </summary>
        [Test]
        public void TheLedgerCoversEveryStackRequirementAndTracksValidatorRequirements()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);
            (_, _, _, int stackApplicable, int trackedValidator, _) =
                WotSpecRequirementLedger.ReadHeader(assembly);
            int actualStackApplicable = requirements.Count(
                requirement => requirement.SourceEvidence.Contains("stack"));
            int actualTrackedValidator = requirements.Count(
                requirement => requirement.Specification == "WoT Projections" &&
                    requirement.Applicability == "validator");
            int validatorRowsWithoutRuntimeClaims = requirements.Count(
                requirement => requirement.Specification == "WoT Projections" &&
                    requirement.Applicability == "validator" &&
                    !requirement.SourceEvidence.Contains("stack") &&
                    requirement.Tests.Count == 0 &&
                    requirement.Gap?.StartsWith(
                        "Validator-applicability requirement",
                        StringComparison.Ordinal) == true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    actualStackApplicable,
                    Is.EqualTo(stackApplicable),
                    "Every requirement tagged for stack evidence by the stable source is tracked.");
                Assert.That(
                    actualTrackedValidator,
                    Is.EqualTo(trackedValidator),
                    "Projections validator requirements are tracked separately from runtime claims.");
                Assert.That(
                    validatorRowsWithoutRuntimeClaims,
                    Is.EqualTo(trackedValidator),
                    "Validator requirements are neither counted as stack gaps nor assigned " +
                    "stack runtime tests.");
                Assert.That(requirements, Has.Count.EqualTo(stackApplicable + trackedValidator));
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
                    "Every assigned assembly is one of the test assemblies that embeds " +
                    "and checks this ledger.");
            });
        }

        /// <summary>
        /// Keeps profile-closure, unclaimed full-profile, and draft CTT
        /// metadata distinct from executable stack test evidence.
        /// </summary>
        [Test]
        public void TheConformanceUnitAndCttCatalogueClaimsAreDistinctFromStackEvidence()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            WotSpecAuditMetadata metadata =
                WotSpecRequirementLedger.ReadAuditMetadata(assembly);
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);
            bool referencesCttId = requirements
                .SelectMany(requirement => requirement.Tests)
                .Any(test => test.Contains("ctt-", StringComparison.OrdinalIgnoreCase));

            Assert.Multiple(() =>
            {
                Assert.That(metadata.CttCatalogueStatus, Is.EqualTo("proposal-only"));
                Assert.That(metadata.CttGeneratedRequirementCaseCount, Is.EqualTo(333));
                Assert.That(metadata.CttSupplementalDefaultingCaseCount, Is.EqualTo(7));
                Assert.That(metadata.CttCaseHeadingCount, Is.EqualTo(340));
                Assert.That(metadata.CttHeadingAnchorCount, Is.EqualTo(342));
                Assert.That(metadata.StackTestsReferenceCttIds, Is.False);
                Assert.That(referencesCttId, Is.EqualTo(metadata.StackTestsReferenceCttIds));
                Assert.That(metadata.ProjectionUnit, Is.EqualTo("WoT-Projection"));
                Assert.That(metadata.PartQualifiedProjectionUnit, Is.EqualTo("2:WoT-Projection"));
                Assert.That(
                    metadata.ProjectionProfiles,
                    Is.EquivalentTo(s_projectionProfiles));
                Assert.That(
                    WotSpecRequirementLedger.DescribeResolution(
                        assembly,
                        metadata.ProjectionClosureTest),
                    Is.EqualTo("runs"));
                Assert.That(metadata.ProjectionEvidenceStatus, Is.EqualTo("profile-closure-only"));
                Assert.That(metadata.ConnectivityUnit, Is.EqualTo("WOTC-SemanticChange"));
                Assert.That(
                    metadata.ConnectivityUnitStatus,
                    Is.EqualTo("full-profile-only-unclaimed"));
                Assert.That(
                    metadata.ConnectivityUnitEvidenceStatus,
                    Is.EqualTo("no-conformance-claim"));
                Assert.That(
                    metadata.ConnectivityUnitRequirementIds,
                    Is.EquivalentTo(s_semanticChangeRequirementIds));
            });

            foreach (string requirementId in metadata.ConnectivityUnitRequirementIds)
            {
                WotSpecRequirement requirement =
                    requirements.Single(candidate => candidate.SpecId == requirementId);
                Assert.Multiple(() =>
                {
                    Assert.That(requirement.Tests, Is.Empty);
                    Assert.That(requirement.Gap, Does.Contain(metadata.ConnectivityUnit));
                });
            }
        }

        /// <summary>
        /// Keeps EventDriven refresh evidence separate from unimplemented mode claims.
        /// </summary>
        [Test]
        public void RefreshRequirementRecordsEventDrivenEvidenceAndRemainingModeGap()
        {
            WotSpecRequirement requirement = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly)
                .Single(candidate =>
                    candidate.SpecId == "sec-automatic-and-explicit-refresh-idempotence#002");

            Assert.Multiple(() =>
            {
                Assert.That(
                    requirement.Tests,
                    Is.EquivalentTo(new[]
                    {
                        "Opc.Ua.WotCon.Tests.WotBindingDescriptorTests." +
                            "RefreshModeIsEventDrivenAndRejectsUnsupportedWrites",
                        "Opc.Ua.WotCon.Tests.Materialization.WotAtomicPublicationNativeTests." +
                            "AutomaticRefreshPublishesTheSameRegistryCompletionState"
                    }));
                Assert.That(requirement.Gap, Does.Contain("Periodic"));
                Assert.That(requirement.Gap, Does.Contain("Scheduled"));
                Assert.That(requirement.Gap, Does.Contain("Full refresh-clause coverage is not claimed."));
            });
        }

        /// <summary>
        /// Keeps dependency authorization evidence scoped to the local node permissions exercised.
        /// </summary>
        [Test]
        public void DependencyAuthorizationRequirementRecordsUncoveredPermissionScopes()
        {
            WotSpecRequirement requirement = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly)
                .Single(candidate =>
                    candidate.SpecId == "sec-dependency-snapshots-and-target-pins#011");

            Assert.Multiple(() =>
            {
                Assert.That(requirement.Tests, Is.EquivalentTo(new[]
                {
                    "Opc.Ua.WotCon.Tests.WotSelectedDependenciesLiveTests." +
                        "DependencyObservationRequiresReadAccessToEveryCapturedTarget"
                }));
                Assert.That(requirement.Gap, Does.Contain("per-node role permissions"));
                Assert.That(requirement.Gap, Does.Contain("namespace-default permission behavior"));
                Assert.That(requirement.Gap, Does.Contain("federated/remote targets"));
                Assert.That(requirement.Gap, Does.Contain("No conformance claim"));
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
                    Assert.That(
                        found[0].Evidence,
                        Is.EquivalentTo(requirement.SourceEvidence),
                        $"{requirement.SpecId} has source evidence tags that disagree.");
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
                        Is.Not.Empty,
                        $"{statement.SpecId} has no source evidence tags.");
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
        /// The inventory and requirement ledger were read from one stable
        /// source revision. Example fixtures have a separate provenance pin.
        /// The inventory names each source by its path, blob id and byte
        /// digest, so a re-vendor from another revision cannot look identical.
        /// </summary>
        [Test]
        public void TheInventoryAndTheLedgerPinTheSameRevision()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            (string commit, string repository, _, int stackApplicable, int trackedValidator, _) =
                WotSpecRequirementLedger.ReadHeader(assembly);
            WotSpecInventoryHeader inventory =
                WotSpecRequirementLedger.ReadInventoryHeader(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(inventory.Commit, Is.EqualTo(commit));
                Assert.That(inventory.Repository, Is.EqualTo(repository));
                Assert.That(inventory.SchemaVersion, Is.EqualTo(3));
                Assert.That(
                    inventory.Tree,
                    Does.Match("^[0-9a-f]{40}$"),
                    "The commit's tree is pinned, not just the commit.");
                Assert.That(
                    inventory.StatementCount,
                    Is.EqualTo(stackApplicable + trackedValidator),
                    "The inventory states how many tracked requirements it holds, so dropping one " +
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
                        Is.EqualTo(s_sourceRequirementCounts[
                            Array.IndexOf(s_upstreamLedgers, source.Path)]),
                        $"{source.Path} is pinned to the expected stable ledger size.");
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
        /// The three stable upstream requirement ledgers tracked by the stack.
        /// </summary>
        private static readonly string[] s_upstreamLedgers =
        [
            "extras/WoT-Binding/tools/requirements.json",
            "extras/WoT-Connectivity/tools/requirements.json",
            "extras/WoT-Projections/tools/requirements.json"
        ];
        private static readonly int[] s_sourceRequirementCounts = [438, 333, 90];

        /// <summary>
        /// The requirement ledger follows the stable specification provenance.
        /// The example manifest has its own independent source pin.
        /// </summary>
        [Test]
        public void TheLedgerIsPinnedToTheStableSpecificationRevision()
        {
            (string commit, string repository, string revision, _, _, _) =
                WotSpecRequirementLedger.ReadHeader(
                    typeof(WotSpecRequirementTests).Assembly);

            Assert.Multiple(() =>
            {
                Assert.That(commit, Is.EqualTo("40125acd1b65cdf291a92a67a5c6565656358e6e"));
                Assert.That(
                    repository,
                    Is.EqualTo("https://github.com/OPCF-Members/OPC10100-WoT.git"));
                Assert.That(
                    revision,
                    Is.EqualTo(Opc.Ua.Wot.WotBindingConformance.CurrentRevision));
            });
        }

        /// <summary>
        /// Stack requirements without a candidate test reference remain countable.
        /// </summary>
        [Test]
        public void TheStackRequirementsWithoutTestReferencesMatchTheLedger()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            List<WotSpecRequirement> requirements =
                WotSpecRequirementLedger.Load(assembly);
            (_, _, _, _, _, int withoutTestReferenceCount) =
                WotSpecRequirementLedger.ReadHeader(assembly);
            string[] unmappedIds = [.. requirements
                .Where(requirement =>
                    requirement.SourceEvidence.Contains("stack") && requirement.Tests.Count == 0)
                .Select(requirement => requirement.SpecId)
                .OrderBy(id => id, StringComparer.Ordinal)];
            string unmappedDigest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join("\n", unmappedIds)))).ToLowerInvariant();

            Assert.Multiple(() =>
            {
                Assert.That(unmappedIds, Has.Length.EqualTo(withoutTestReferenceCount));
                Assert.That(unmappedDigest, Is.EqualTo(s_unmappedRequirementIdsSha256));
            });
        }

        /// <summary>
        /// Pins the exact set of stable stack requirements with no candidate test reference.
        /// </summary>
        private const string s_unmappedRequirementIdsSha256 =
            "c5ff75365ae2b3a5b4a98744f8daafc1ded0dd80d8150c89a1e395231ad357a2";

        private static readonly string[] s_assemblies =
        [
            "Opc.Ua.Types.Tests",
            "Opc.Ua.WotCon.Tests",
            "Opc.Ua.WotCon.Bindings.Tests",
            "Opc.Ua.Server.Tests"
        ];
        private static readonly string[] s_targetSpecificTests = [];
        private static readonly string[] s_modernFrameworks = ["net8.0", "net9.0", "net10.0"];
    }
}
