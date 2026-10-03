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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Opc.Ua.XRegistry.Tests.Conformance
{
    [TestFixture]
    public sealed class XRegistryConformanceLedgerTests
    {
        [Test]
        public void SourcePinsAndDraftDiscrepancyRemainImmutable()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement root = document.RootElement;
            JsonElement sources = root.GetProperty("sources");
            (string Id, string File, string Hash)[] publicSources =
            [
                ("core", "core/spec.md", "053684b607fbdbaf0062a1c37ca37bf6c44b785ff57a61069d4ef667b9f808b6"),
                ("http", "core/http.md", "97bf93e996dead5256e5c507601f022c8d71bae926719915509033f9f845492a"),
                ("model", "core/model.md", "dbefa739cc7d176b0477dc3bf413e8effe03812071733fd22226a22f923e3338"),
                ("events", "core/events.md", "b6d69a9bfbb83bdbaba7f8a54dbe3125bb4507145ec7c842620fb1bd21cab2d7"),
                ("pagination", "pagination/spec.md",
                    "d0a69ed47aa3408b334c2f93aaafaa322dad955e61dd2a7f3424931a7f046483"),
                ("opcua", "workingdrafts/bindings/opcua.md",
                    "8f9ee3ea89dc7e1f9cedbc7cd170f2cdf40ebe6c68a1756d2a73b945d26f5d3b")
            ];
            foreach ((string id, string file, string hash) in publicSources)
            {
                JsonElement source = sources.GetProperty(id);
                Assert.Multiple(() =>
                {
                    Assert.That(Text(source, "kind"), Is.EqualTo("public-spec"), id);
                    Assert.That(Text(source, "repository"), Is.EqualTo("xregistry/spec"), id);
                    Assert.That(Text(source, "file"), Is.EqualTo(file), id);
                    Assert.That(Text(source, "revision"), Is.EqualTo(k_publicRevision), id);
                    Assert.That(Text(source, "sha256"), Is.EqualTo(hash), id);
                    Assert.That(Text(source, "verification"), Is.EqualTo("pinned-public-bytes"), id);
                    Assert.That(source.GetProperty("redistributed").GetBoolean(), Is.False, id);
                    Assert.That(source.EnumerateObject().Select(value => value.Name),
                        Is.EquivalentTo(s_publicSourceProperties), id);
                });
            }

            JsonElement companion = sources.GetProperty("companion");
            JsonElement drift = root.GetProperty("draftDiscrepancy");
            Assert.Multiple(() =>
            {
                Assert.That(sources.EnumerateObject().Select(value => value.Name),
                    Is.EquivalentTo(publicSources.Select(value => value.Id).Concat(
                        [
                            "companion", "original-plan", "remaining-plan", "archived-qualified-plan",
                            "approved-expansion-plan", "reviewed-synthesis", "first-delivery"
                        ])));
                Assert.That(Text(companion, "kind"), Is.EqualTo("members-spec-reference"));
                Assert.That(Text(companion, "repository"), Is.EqualTo("OPCF-Members/spec-drafts"));
                Assert.That(Text(companion, "file"), Is.EqualTo("source/core-specs/xregistry/spec.md"));
                Assert.That(Text(companion, "revision"), Is.EqualTo(k_companionRevision));
                Assert.That(Text(companion, "blob"), Is.EqualTo("34aa4d4bfe060cc5c417613e7c9ee5c5c6b06b85"));
                Assert.That(Text(companion, "verification"),
                    Is.EqualTo("authorized-file-metadata-and-reviewed-synthesis"));
                Assert.That(companion.GetProperty("redistributed").GetBoolean(), Is.False);
                Assert.That(companion.EnumerateObject().Select(value => value.Name),
                    Is.EquivalentTo(s_companionSourceProperties));
                Assert.That(Text(drift, "source"), Is.EqualTo("opcua"));
                Assert.That(Text(drift, "section"), Is.EqualTo("1. Scope; 2. Normative references"));
                Assert.That(Text(drift, "referencedRepository"), Is.EqualTo("marcschier/opcua-drafts"));
                Assert.That(Text(drift, "referencedRevision"),
                    Is.EqualTo("ff22f224400fc8be813bf0abcbfc3cde52bc7ed3"));
                Assert.That(Text(drift, "referencedFile"), Is.EqualTo("core-specs/xregistry/OPC-UA-xRegistry.md"));
                Assert.That(Text(drift, "selectedSource"), Is.EqualTo("companion"));
                Assert.That(drift.GetProperty("precedenceEstablishedBySpecifications").GetBoolean(), Is.False);
                Assert.That(Text(root.GetProperty("versions"), "xregistry"), Is.EqualTo("1.0-rc4"));
                Assert.That(Text(root.GetProperty("versions"), "pagination"), Is.EqualTo("0.1-wip"));
                Assert.That(Text(root.GetProperty("versions"), "companionModel"), Is.EqualTo("0.6.0"));
                Assert.That(Text(root.GetProperty("versions"), "companionNamespace"),
                    Is.EqualTo("http://opcfoundation.org/UA/xRegistry/"));
                Assert.That(Text(root.GetProperty("versions"), "nativeExtension"),
                    Is.EqualTo("experimental-transactional-v1"));
                Assert.That(root.GetProperty("versions").GetProperty("entityVersionIdIsProtocolVersion").GetBoolean(),
                    Is.False);
            });

            (string Id, string File, string Hash)[] artifacts =
            [
                ("original-plan", @"files\xregistry-original-approved-plan.md",
                    "34d137743575872b0e4db809c71a5d01feca9ff24f7d0f57d708854e7e4271e1"),
                ("remaining-plan", "plan.md",
                    "f029cb9884e489ae90ead52189df04b220ebf028a065c6623c30f82f069f5b99"),
                ("archived-qualified-plan", @"files\xregistry-completed-qualified-profile-plan.md",
                    "9266d4e269af449fda87f9f9e3cb5f2429da3f5935af8f5b45918c591de1a44a"),
                ("approved-expansion-plan", @"files\xregistry-approved-expansion-plan.md",
                    "c489f47c255953e53deb522af8fa92be641a889177ff9ff320a1f55a38e6ff40"),
                ("reviewed-synthesis", @"files\xregistry-binding-research.md",
                    "99c072d5ab617b82e4cbf595b9353b8d6de6bd3dcb28be82970ca83e84f091f7"),
                ("first-delivery", @"files\xregistry-final-verification.md",
                    "5be07ee34871c6ee0cab9850ab8d1b35750dc7ecac73d02611392d7d85ca5ed7")
            ];
            foreach ((string id, string file, string hash) in artifacts)
            {
                JsonElement source = sources.GetProperty(id);
                Assert.Multiple(() =>
                {
                    Assert.That(Text(source, "file"), Is.EqualTo(file), id);
                    Assert.That(Text(source, "revision"), Is.EqualTo(hash), id);
                    Assert.That(Text(source, "kind"),
                        Is.EqualTo(id is "reviewed-synthesis" or "first-delivery"
                            ? "reviewed-evidence" : "acceptance-policy"), id);
                    Assert.That(Text(source, "verification"),
                        Is.EqualTo(id == "remaining-plan"
                            ? "historical-digest-original-unavailable" : "session-artifact-sha256"), id);
                    Assert.That(source.GetProperty("redistributed").GetBoolean(), Is.False, id);
                    Assert.That(source.EnumerateObject().Select(value => value.Name),
                        Is.EquivalentTo(s_artifactSourceProperties), id);
                });
            }
        }

        [Test]
        public void MissingHistoricalPlanBytesAreNotReplacedByNewerVerifiedRevisions()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement provenance = document.RootElement.GetProperty("planProvenance");
            JsonElement sources = document.RootElement.GetProperty("sources");
            Assert.Multiple(() =>
            {
                Assert.That(Text(provenance, "historicalSource"), Is.EqualTo("remaining-plan"));
                Assert.That(provenance.GetProperty("originalSnapshotAvailable").GetBoolean(), Is.False);
                Assert.That(Text(provenance, "archivedPredecessorSource"), Is.EqualTo("archived-qualified-plan"));
                Assert.That(Text(provenance, "approvedExpansionSource"), Is.EqualTo("approved-expansion-plan"));
                Assert.That(provenance.GetProperty("archivesIdentifyHistoricalBytes").GetBoolean(), Is.False);
                Assert.That(Text(sources.GetProperty("remaining-plan"), "verification"),
                    Is.EqualTo("historical-digest-original-unavailable"));
                Assert.That(new[]
                {
                    Text(sources.GetProperty("remaining-plan"), "revision"),
                    Text(sources.GetProperty("archived-qualified-plan"), "revision"),
                    Text(sources.GetProperty("approved-expansion-plan"), "revision")
                }, Is.Unique);
            });
        }

        [Test]
        public void OriginalAcceptanceAndRemainingGapsRetainTheirRequirementIds()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement root = document.RootElement;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["CORE"] = 13,
                ["HTTP"] = 25,
                ["QUERY"] = 15,
                ["MODEL"] = 23,
                ["NATIVE"] = 20,
                ["SYNC"] = 14,
                ["STATE"] = 8,
                ["IDENTITY"] = 6,
                ["HOST"] = 6,
                ["ASSURANCE"] = 8,
                ["SCOPE"] = 6,
                ["CONTRACT"] = 2
            };
            string[] expectedIds = [.. counts.SelectMany(pair => Enumerable.Range(1, pair.Value)
                .Select(index => $"XREG-{pair.Key}-{index.ToString("D3", CultureInfo.InvariantCulture)}"))];
            string[] actualIds =
                [.. root.GetProperty("requirements").EnumerateArray().Select(value => Text(value, "id"))];
            Assert.Multiple(() =>
            {
                Assert.That(actualIds, Is.Unique);
                Assert.That(actualIds, Is.EquivalentTo(expectedIds));
            });

            var inventory = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["original-protocol"] = "CORE HTTP QUERY",
                ["original-model-identity"] = "MODEL NATIVE IDENTITY",
                ["original-atomicity"] = "CORE NATIVE STATE",
                ["original-content"] = "CORE NATIVE STATE",
                ["original-gateways"] = "HTTP NATIVE IDENTITY HOST",
                ["original-sync"] = "SYNC",
                ["original-durability"] = "STATE",
                ["original-compatibility"] = "ASSURANCE NATIVE",
                ["original-runtime-delivery"] = "HOST ASSURANCE",
                ["original-decisions-exclusions"] = "SCOPE CONTRACT IDENTITY HOST",
                ["gap-conformance"] = "CONTRACT ASSURANCE",
                ["gap-core-model"] = "CORE MODEL",
                ["gap-query"] = "HTTP QUERY",
                ["gap-references-versions"] = "MODEL",
                ["gap-native"] = "NATIVE",
                ["gap-events"] = "NATIVE SYNC",
                ["gap-sync"] = "SYNC",
                ["gap-long-running-state"] = "STATE",
                ["gap-identity-hosting"] = "IDENTITY HOST",
                ["gap-operations"] = "HOST",
                ["gap-transfer"] = "STATE NATIVE",
                ["gap-merge"] = "ASSURANCE"
            };
            JsonElement buckets = root.GetProperty("inventory");
            Assert.That(buckets.EnumerateArray().Select(value => Text(value, "id")), Is.EquivalentTo(inventory.Keys));
            foreach (JsonElement bucket in buckets.EnumerateArray())
            {
                string id = Text(bucket, "id");
                Assert.Multiple(() =>
                {
                    Assert.That(Strings(bucket, "areas"), Is.EquivalentTo(inventory[id].Split(' ')), id);
                    Assert.That(Text(bucket, "source"),
                        Is.EqualTo(id.StartsWith("original-", StringComparison.Ordinal)
                            ? "original-plan" : "remaining-plan"), id);
                    Assert.That(Text(bucket, "section"), Is.Not.Empty, id);
                });
            }
        }

        [Test]
        public void RequirementsHavePinnedSourcesModesCapabilitiesAndHonestStatuses()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement root = document.RootElement;
            JsonElement evidence = root.GetProperty("evidence");
            foreach (JsonElement requirement in root.GetProperty("requirements").EnumerateArray())
            {
                string id = Text(requirement, "id");
                JsonElement source = root.GetProperty("sources").GetProperty(Text(requirement, "source"));
                string[] references = Strings(requirement, "evidence");
                Assert.Multiple(() =>
                {
                    Assert.That(requirement.EnumerateObject().Select(value => value.Name),
                        Is.EquivalentTo(s_requirementProperties), id);
                    Assert.That(id, Does.Match("^XREG-[A-Z]+-[0-9]{3}$"));
                    Assert.That(Text(requirement, "requirement"), Is.Not.Empty, id);
                    Assert.That(Text(requirement, "section"), Is.Not.Empty, id);
                    Assert.That(Text(requirement, "qualification"), Is.Not.Empty, id);
                    Assert.That(Text(requirement, "level"), Is.AnyOf("MUST", "SHOULD", "optional", "policy"), id);
                    Assert.That(Strings(requirement, "expected"), Is.Not.Empty.And.All.Not.Empty, id);
                    Assert.That(Strings(requirement, "modes"), Is.Not.Empty.And.Unique
                        .And.SubsetOf(s_modes), id);
                    Assert.That(root.GetProperty("capabilities").TryGetProperty(
                        Text(requirement, "capability"), out _), Is.True, id);
                    Assert.That(references, Is.Unique, id);
                    if (Text(source, "kind") is "acceptance-policy" or "reviewed-evidence")
                    {
                        Assert.That(Text(requirement, "level"), Is.EqualTo("policy"), id);
                    }
                    Assert.That(StatusViolation(
                        Text(requirement, "status"),
                        Text(requirement, "level"),
                        Text(requirement, "remaining"),
                        references.Select(reference => Text(evidence.GetProperty(reference), "kind"))),
                        Is.Null, id);
                });
            }
        }

        [Test]
        public void EvidenceNamesExactTestsAndSeparatesDenialsFromStructuralChecks()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement root = document.RootElement;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.GetProperty("evidence").EnumerateObject())
            {
                JsonElement evidence = property.Value;
                string file = Text(evidence, "file");
                string fixture = Text(evidence, "fixture");
                string test = Text(evidence, "test");
                string project = root.GetProperty("projects").GetProperty(Text(evidence, "project")).GetString()!;
                Assert.Multiple(() =>
                {
                    Assert.That(evidence.EnumerateObject().Select(value => value.Name),
                        Is.EquivalentTo(s_evidenceProperties), property.Name);
                    Assert.That(property.Name, Does.Match("^[a-z][a-z0-9-]+$"));
                    Assert.That(project, Does.StartWith(@"tests\").And.EndWith(".csproj"));
                    Assert.That(Path.IsPathRooted(file), Is.False, property.Name);
                    Assert.That(file, Does.EndWith(".cs").And.Not.Contain("..").And.Not.Contain("/"));
                    Assert.That(fixture, Does.Match("^Opc\\.Ua\\.[A-Za-z0-9.]+Tests$"));
                    Assert.That(test, Does.Match("^[A-Z][A-Za-z0-9]+$"));
                    Assert.That(names.Add($"{fixture}.{test}"), Is.True, property.Name);
                    Assert.That(Text(evidence, "origin"), Is.AnyOf("existing", "new"));
                    Assert.That(Text(evidence, "kind"), Is.AnyOf("positive", "safety", "denial", "structural"));
                    if (fixture.EndsWith(".XRegistryConformanceLedgerTests", StringComparison.Ordinal))
                    {
                        Assert.That(Text(evidence, "kind"), Is.EqualTo("structural"), property.Name);
                    }
                    if (property.Name.EndsWith("-denial", StringComparison.Ordinal) ||
                        property.Name == "provider-denied-action")
                    {
                        Assert.That(Text(evidence, "kind"), Is.EqualTo("denial"), property.Name);
                    }
                });
            }
        }

        [Test]
        public void RouteActionInventoryCannotSilentlyOmitOrAddOperations()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement root = document.RootElement;
            var requirements = new HashSet<string>(root.GetProperty("requirements").EnumerateArray()
                .Select(value => Text(value, "id")), StringComparer.Ordinal);
            var signatures = new List<string>();
            foreach (JsonElement operation in root.GetProperty("operations").EnumerateArray())
            {
                string id = Text(operation, "requirement");
                string path = Text(operation, "path");
                string[] actions = Strings(operation, "actions");
                string disposition = operation.TryGetProperty("prohibited", out JsonElement prohibited) &&
                    prohibited.GetBoolean() ? "prohibited" : "allowed";
                Assert.Multiple(() =>
                {
                    Assert.That(requirements, Does.Contain(id));
                    Assert.That(path, Is.Not.Empty);
                    Assert.That(actions, Is.Not.Empty.And.Unique.And.SubsetOf(s_actions));
                });
                signatures.AddRange(actions.Select(action => $"{action} {path} {id} {disposition}"));
            }
            byte[] contents = Encoding.UTF8.GetBytes(
                string.Join("\n", signatures.OrderBy(value => value, StringComparer.Ordinal)));
#if NET5_0_OR_GREATER
            byte[] bytes = SHA256.HashData(contents);
#else
            using var hash = SHA256.Create();
            byte[] bytes = hash.ComputeHash(contents);
#endif
            string digest = string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            Assert.Multiple(() =>
            {
                Assert.That(signatures, Has.Count.EqualTo(45).And.Unique);
                Assert.That(digest, Is.EqualTo("49b3f667a9ba49106bab35f70af0ae1e87651a2ff4ae44f72cee85463614521e"));
            });
        }

        [Test]
        public void LiteralOracleCasesLinkToExactExecutableTestsAndRequirements()
        {
            using JsonDocument ledger = XRegistryConformanceAssets.Read(k_ledger);
            using JsonDocument fixture = XRegistryConformanceAssets.Read("core-provider-oracles.json");
            JsonElement root = fixture.RootElement;
            var cases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["epoch-presence"] = nameof(XRegistryCoreOracleTests.ZeroEpochDiffersFromAbsentAndNullEpochAsync),
                ["create-epoch"] = nameof(XRegistryCoreOracleTests.CreateIgnoresASuppliedEpochAsync),
                ["identical-touch"] =
                    nameof(XRegistryCoreOracleTests.IdenticalPutAndEmptyPatchAdvanceEpochAndModifiedAtAsync),
                ["parent-membership"] =
                    nameof(XRegistryCoreOracleTests.ParentEpochsTrackMembershipNotDescendantUpdatesAsync),
                ["compound-rollback"] =
                    nameof(XRegistryCoreOracleTests.FailedCompoundWritePreservesMetadataDocumentsAndMembershipAsync),
                ["omitted-empty-document"] =
                    nameof(XRegistryCoreOracleTests.OmittedDocumentPreservesBytesButExplicitEmptyReplacesThemAsync),
                ["documentless-empty"] =
                    nameof(XRegistryCoreOracleTests.DocumentlessAndEmptyDocumentResourcesHaveDifferentBodiesAsync),
                ["missing-not-empty"] =
                    nameof(XRegistryCoreOracleTests.MissingResourceIsNotASuccessfulEmptyDocumentAsync)
            };
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("formatVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(Strings(root, "sourceIds"), Is.EquivalentTo(s_oracleSources));
                Assert.That(root.GetProperty("cases").EnumerateArray().Select(value => Text(value, "id")),
                    Is.EquivalentTo(cases.Keys));
            });
            foreach (JsonElement scenario in root.GetProperty("cases").EnumerateArray())
            {
                string name = cases[Text(scenario, "id")];
                Assert.That(Text(scenario, "testName"), Is.EqualTo(name));
                Assert.That(Strings(scenario, "requirements"), Is.Not.Empty.And.Unique);
                foreach (string id in Strings(scenario, "requirements"))
                {
                    JsonElement requirement = ledger.RootElement.GetProperty("requirements").EnumerateArray()
                        .Single(value => Text(value, "id") == id);
                    string[] tests = [.. Strings(requirement, "evidence")
                        .Select(reference => ledger.RootElement.GetProperty("evidence").GetProperty(reference))
                        .Where(value => Text(value, "origin") == "new" &&
                            Text(value, "kind") is "positive" or "safety")
                        .Select(value => Text(value, "test"))];
                    Assert.That(tests, Does.Contain(name), id);
                }
                Assert.That(scenario.GetProperty("steps").GetArrayLength(), Is.GreaterThan(1), name);
                foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
                {
                    JsonElement expected = step.GetProperty("expect");
                    int status = expected.GetProperty("status").GetInt32();
                    Assert.That(status, Is.AnyOf(200, 201, 204, 400, 404), name);
                    Assert.That(expected.TryGetProperty("error", out _), Is.True, name);
                    if (expected.TryGetProperty("metadata", out JsonElement metadata))
                    {
                        Assert.That(metadata.EnumerateObject().Count(), Is.GreaterThan(0), name);
                    }
                    if (status >= 400 && Text(step, "action") != "GET")
                    {
                        Assert.That(step.TryGetProperty("probes", out JsonElement probes), Is.True, name);
                        Assert.That(probes.GetArrayLength(), Is.GreaterThan(0), name);
                        Assert.That(Text(expected, "error"), Is.Not.Empty, name);
                    }
                }
            }
        }

        [Test]
        public void CurrentQualifiedScopeAndExternalGatesAreNotFullConformance()
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(k_ledger);
            JsonElement root = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("formatVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(Text(root, "objective"), Is.EqualTo("xreg-next-contract"));
                Assert.That(Text(root, "conformanceClaim"), Is.EqualTo("experimental-qualified-profile"));
                Assert.That(Text(root, "fullAcceptance"), Is.EqualTo("incomplete"));
                Assert.That(Text(root, "executionEvidence"), Is.EqualTo("test-results"));
                Assert.That(Text(root, "designSignoff"), Is.EqualTo("blocked-external"));
                Assert.That(root.GetProperty("statusDefinitions").EnumerateObject().Select(value => value.Name),
                    Is.EquivalentTo(s_statuses));
                foreach (JsonProperty capability in root.GetProperty("capabilities").EnumerateObject())
                {
                    Assert.That(capability.Value.GetString(), Is.Not.Null.And.Not.Empty, capability.Name);
                }
                foreach (string id in s_remainingRequirements)
                {
                    JsonElement requirement = root.GetProperty("requirements").EnumerateArray()
                        .Single(value => Text(value, "id") == id);
                    Assert.That(Text(requirement, "status"), Is.EqualTo("missing"), id);
                    Assert.That(Text(requirement, "remaining"), Is.Not.Empty, id);
                }
                foreach (string id in s_completedRequirements)
                {
                    JsonElement requirement = root.GetProperty("requirements").EnumerateArray()
                        .Single(value => Text(value, "id") == id);
                    Assert.That(Text(requirement, "status"), Is.EqualTo("implemented"), id);
                    Assert.That(Text(requirement, "remaining"), Is.Empty, id);
                }
            });
        }

        [TestCase("denial")]
        [TestCase("structural")]
        public void StatusRulesRejectDenialOrStructuralOnlyCompletion(string kind)
        {
            Assert.That(StatusViolation("implemented", "MUST", string.Empty, [kind]),
                Is.EqualTo("implemented-needs-behavior"));
        }

        [TestCase("implemented", "positive", "pending", "MUST", "implemented-with-gap")]
        [TestCase("missing", "denial", "", "MUST", "incomplete-without-gap")]
        [TestCase("provider-required", "denial", "", "optional", "incomplete-without-gap")]
        [TestCase("upstream-impossible", "positive", "base limitation", "policy", "impossible-without-denial")]
        [TestCase("excluded", "denial", "outside scope", "MUST", "mandatory-exclusion")]
        [TestCase("unknown", "positive", "pending", "policy", "unknown-status")]
        public void StatusRulesRejectUnexplainedGapsAndMandatoryExclusions(
            string status, string kind, string remaining, string level, string expected)
        {
            Assert.That(StatusViolation(status, level, remaining, [kind]), Is.EqualTo(expected));
        }

        [TestCase("implemented", "positive", "")]
        [TestCase("implemented", "safety", "")]
        [TestCase("missing", "denial", "positive behavior absent")]
        [TestCase("partial", "positive", "remaining interactions")]
        [TestCase("provider-required", "denial", "no registered resolver")]
        [TestCase("upstream-impossible", "denial", "no remote primitive")]
        [TestCase("excluded", "structural", "approved policy non-goal")]
        public void StatusRulesPermitQualifiedBehaviorAndExplicitIncompleteStates(
            string status, string kind, string remaining)
        {
            Assert.That(StatusViolation(status, "policy", remaining, [kind]), Is.Null);
        }

        [TestCase(k_ledger)]
        [TestCase("core-provider-oracles.json")]
        public void AssetsHaveNoDuplicateJsonKeys(string asset)
        {
            using JsonDocument document = XRegistryConformanceAssets.Read(asset);
            var duplicates = new List<string>();
            FindDuplicateProperties(document.RootElement, "$", duplicates);
            Assert.That(duplicates, Is.Empty, asset);
        }

        private static string? StatusViolation(
            string status, string level, string remaining, IEnumerable<string> evidenceKinds)
        {
            string[] kinds = [.. evidenceKinds];
            if (status == "implemented")
            {
                if (!string.IsNullOrWhiteSpace(remaining))
                {
                    return "implemented-with-gap";
                }
                return kinds.Any(kind => kind is "positive" or "safety") ? null : "implemented-needs-behavior";
            }
            if (status is not ("partial" or "missing" or "provider-required" or "upstream-impossible" or "excluded"))
            {
                return "unknown-status";
            }
            if (string.IsNullOrWhiteSpace(remaining))
            {
                return "incomplete-without-gap";
            }
            if (status == "upstream-impossible" && !kinds.Contains("denial", StringComparer.Ordinal))
            {
                return "impossible-without-denial";
            }
            return status == "excluded" && level is "MUST" or "SHOULD" ? "mandatory-exclusion" : null;
        }

        private static void FindDuplicateProperties(JsonElement element, string path, List<string> duplicates)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string child = $"{path}.{property.Name}";
                    if (!names.Add(property.Name))
                    {
                        duplicates.Add(child);
                    }
                    FindDuplicateProperties(property.Value, child, duplicates);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    FindDuplicateProperties(item, $"{path}[{index++}]", duplicates);
                }
            }
        }

        private static string[] Strings(JsonElement element, string property)
        {
            return [.. element.GetProperty(property).EnumerateArray().Select(value =>
                value.GetString() ?? throw new InvalidOperationException($"Invalid string in {property}."))];
        }

        private static string Text(JsonElement element, string property)
        {
            return XRegistryConformanceAssets.Text(element, property);
        }

        private const string k_ledger = "xregistry-acceptance-ledger.json";
        private const string k_publicRevision = "a1544396d63b74cdf1de5da6a269d02b88696802";
        private const string k_companionRevision = "9d3fdeb77259dedd257ee2f3f522cf7cc16f676e";

        private static readonly string[] s_publicSourceProperties =
            ["kind", "repository", "file", "revision", "sha256", "verification", "redistributed"];

        private static readonly string[] s_companionSourceProperties =
            ["kind", "repository", "file", "revision", "blob", "verification", "redistributed"];

        private static readonly string[] s_artifactSourceProperties =
            ["kind", "file", "revision", "verification", "redistributed"];

        private static readonly string[] s_requirementProperties =
        [
            "id", "requirement", "source", "section", "level", "modes", "capability",
            "status", "evidence", "expected", "qualification", "remaining"
        ];

        private static readonly string[] s_modes = ["http-gateway", "opcua-gateway", "sync"];

        private static readonly string[] s_evidenceProperties =
            ["project", "file", "fixture", "test", "kind", "origin"];

        private static readonly string[] s_actions = ["GET", "PUT", "PATCH", "POST", "DELETE", "OPTIONS"];
        private static readonly string[] s_oracleSources = ["core", "http", "model"];

        private static readonly string[] s_statuses =
            ["implemented", "partial", "missing", "provider-required", "upstream-impossible", "excluded"];

        private static readonly string[] s_remainingRequirements =
        [
            "XREG-ASSURANCE-007"
        ];

        private static readonly string[] s_completedRequirements =
        [
            "XREG-HTTP-002", "XREG-HTTP-024", "XREG-MODEL-004", "XREG-MODEL-015", "XREG-QUERY-002",
            "XREG-QUERY-013", "XREG-NATIVE-007", "XREG-NATIVE-017", "XREG-SYNC-007", "XREG-STATE-005", "XREG-STATE-006",
            "XREG-IDENTITY-003", "XREG-HOST-004"
        ];
    }
}
