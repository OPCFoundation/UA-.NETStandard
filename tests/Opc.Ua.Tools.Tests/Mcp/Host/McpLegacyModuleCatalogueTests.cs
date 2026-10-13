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
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins independent module profile selection, expanded PubSub ownership and connection deduplication.
    /// Only registered metadata is observed; none of the tools is invoked.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpLegacyModuleCatalogueTests
    {
        [TestCaseSource(nameof(AppendedEnumCases))]
        public void AppendedEnumProfilesPublishExactOwnedCatalogues(
            string module,
            McpToolProfile profile,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = [];
            Assert.That(
                () => actual = Observe(builder => RegisterEnum(builder, module, profile, diagnosticsToolsEnabled)),
                Throws.Nothing);

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(UnselectedLegacyCases))]
        public void UnselectedLegacyEnumProfilesLeaveExactEmptyCatalogues(
            string module,
            McpToolProfile profile,
            bool diagnosticsToolsEnabled)
        {
            string[] actual = Observe(builder => RegisterEnum(builder, module, profile, diagnosticsToolsEnabled));

            AssertCatalogue(actual, [], 0, 0);
        }

        [TestCaseSource(nameof(UnselectedLegacyCases))]
        public void UnownedLegacySetProfilesRespectIndependentConnectionRules(
            string module,
            McpToolProfile profile,
            bool diagnosticsToolsEnabled)
        {
            var profiles = new McpToolProfileSet(profile);
            string[] actual = Observe(builder => RegisterSet(builder, module, profiles, diagnosticsToolsEnabled));
            bool ownsConnections = module == "Core" && s_coreUnownedSessionProfiles.Contains(profile);

            AssertCatalogue(actual, ownsConnections ? s_connectionNames : [], ownsConnections ? 4 : 0,
                ownsConnections ? 1 : 0);
        }

        [TestCaseSource(nameof(EmptySetCases))]
        public void EmptyProfileSetsLeaveEveryLegacyCatalogueEmpty(
            string module,
            McpToolProfileSet profiles,
            bool diagnosticsToolsEnabled)
        {
            string[] actual = Observe(builder => RegisterSet(builder, module, profiles, diagnosticsToolsEnabled));

            AssertCatalogue(actual, [], 0, 0);
        }

        [TestCaseSource(nameof(AppendedOnlySetCases))]
        public void AppendedOnlySetsPublishExactOwnedCatalogues(
            string module,
            McpToolProfileSet profiles,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = Observe(builder => RegisterSet(builder, module, profiles, diagnosticsToolsEnabled));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(SelectedMixedSetCases))]
        public void AppendedProfilesComposeExactSelectedCatalogues(
            string module,
            McpToolProfileSet profiles,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = Observe(builder => RegisterSet(builder, module, profiles, diagnosticsToolsEnabled));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(UnownedMixedSetCases))]
        public void AppendedProfilesComposeWithUnownedLegacySelections(
            string module,
            McpToolProfileSet profiles,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = Observe(builder => RegisterSet(builder, module, profiles, diagnosticsToolsEnabled));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(FullSetCases))]
        public void AllThirtySixProfilesProduceExactIndependentFullSetCatalogues(
            string module,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            McpToolProfileSet profiles = AllProfiles();
            Assert.That(profiles.Count, Is.EqualTo(36));
            string[] actual = Observe(builder => RegisterSet(builder, module, profiles, diagnosticsToolsEnabled));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(OmittedProfileCases))]
        public void OmittedEnumProfilesUseExactLegacyFullCatalogues(
            string module,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = Observe(builder => RegisterWithoutProfile(builder, module));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(OmittedDiagnosticsGateCases))]
        public void OmittedDiagnosticsGatesRemainDisabled(
            string module,
            McpToolProfile profile,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = Observe(builder => RegisterWithoutDiagnosticsGate(builder, module, profile));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(CoreSelectionCases))]
        public void CoreSelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Core", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(DiagnosticsSelectionCases))]
        public void DiagnosticsSelectionsRespectGateAndConnectionOwnership(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Diagnostics", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(PubSubSelectionCases))]
        public void PubSubSelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("PubSub", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(PubSubDiagnosticsSelectionCases))]
        public void PubSubDiagnosticsSelectionsRespectProfileAndGate(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("PubSub.Diagnostics", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(DiSelectionCases))]
        public void DiSelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Di", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(AmbSelectionCases))]
        public void AmbSelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Amb", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(Isa95SelectionCases))]
        public void Isa95SelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Isa95", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(MachinerySelectionCases))]
        public void MachinerySelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Machinery", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(PositioningSelectionCases))]
        public void PositioningSelectionsPreserveConnectionOwnership(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Positioning", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(PumpsSelectionCases))]
        public void PumpsSelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Pumps", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(RoboticsSelectionCases))]
        public void RoboticsSelectionsPreserveConnectionOwnership(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Robotics", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(ScalesSelectionCases))]
        public void ScalesSelectionsPreserveConnectionOwnership(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Scales", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(VisionSelectionCases))]
        public void VisionSelectionsPreserveConnectionOwnership(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("Vision", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(WotSelectionCases))]
        public void WotSelectionsSeparateManagementAndDocumentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("WotCon", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(XRegistrySelectionCases))]
        public void XRegistrySelectionsPublishExactIndependentCatalogues(
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            AssertIndependentSelection("XRegistry", profile, useSet, diagnosticsToolsEnabled, catalogue,
                expectedCount, expectedConnectionCount);
        }

        [TestCaseSource(nameof(CoreCombinedCases))]
        public void CoreCombinedSelectionsDeduplicateGroupsAndReserveSetConfigurationForFull(
            McpToolProfileSet profiles,
            string catalogue,
            int expectedCount,
            bool includesFull)
        {
            string[] actual = Observe(builder => builder.WithOpcUaCoreTools(profiles));

            using (Assert.EnterMultipleScope())
            {
                AssertCatalogue(actual, ExpectedNames(catalogue, 1), expectedCount, 1);
                Assert.That(actual.Count(name => name == "GetConfiguration"), Is.EqualTo(1));
                Assert.That(actual.Count(name => name == "SetConfiguration"), Is.EqualTo(includesFull ? 1 : 0));
            }
        }

        [TestCaseSource(nameof(WotCombinedCases))]
        public void WotCombinedSelectionsPublishEachLiteralToolOnce(McpToolProfileSet profiles)
        {
            string[] actual = Observe(builder => builder.WithOpcUaWotConTools(profiles));

            AssertCatalogue(actual, ExpectedNames("WotFull", 1), 25, 1);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ConnectionHelperIsIdempotentWithinOneServiceCollection(int repetitions)
        {
            string[] actual = Observe(builder => RegisterConnections(builder, repetitions));

            AssertCatalogue(actual, s_connectionNames, 4, 1);
        }

        [TestCaseSource(nameof(CompositionCases))]
        public void ComposedLegacyModulesDeduplicateConnectionsInBothOrders(
            bool reverseOrder,
            string registration,
            bool diagnosticsToolsEnabled,
            bool pubSubDiagnosticsToolsEnabled,
            int expectedCount)
        {
            string[] actual = Observe(builder => RegisterComposition(builder, registration, reverseOrder,
                diagnosticsToolsEnabled, pubSubDiagnosticsToolsEnabled, "None"));

            AssertCatalogue(actual, CombinedExpectedNames(diagnosticsToolsEnabled, pubSubDiagnosticsToolsEnabled),
                expectedCount, 1);
        }

        [TestCaseSource(nameof(ExplicitConnectionCompositionCases))]
        public void ExplicitConnectionHelpersDoNotDuplicateComposedRegistrations(
            bool reverseOrder,
            string registration,
            string helperPlacement)
        {
            string[] actual = Observe(builder => RegisterComposition(builder, registration, reverseOrder,
                true, true, helperPlacement));

            AssertCatalogue(actual, CombinedExpectedNames(true, true), 366, 1);
        }

        [Test]
        public void ConnectionDeduplicationIsSharedByBuildersOfOneServiceCollection()
        {
            var services = new ServiceCollection();
            IMcpServerBuilder first = services.AddMcpServer();
            IMcpServerBuilder second = services.AddMcpServer();

            first.WithOpcUaConnectionTools();
            second.WithOpcUaConnectionTools();
            first.WithOpcUaConnectionTools();
            second.WithOpcUaConnectionTools();

            AssertCatalogue(ReadNames(services), s_connectionNames, 4, 1);
        }

        [TestCase("HelperOnly", "Composed")]
        [TestCase("Composed", "HelperOnly")]
        [TestCase("Composed", "ReverseComposed")]
        public void ConnectionDeduplicationIsLocalToEachServiceCollection(
            string firstConfiguration,
            string secondConfiguration)
        {
            var firstServices = new ServiceCollection();
            ConfigureCollection(firstServices, firstConfiguration);
            var secondServices = new ServiceCollection();
            ConfigureCollection(secondServices, secondConfiguration);

            // Observe both only after both collections have registered their connections.
            string[] first = ReadNames(firstServices);
            string[] second = ReadNames(secondServices);

            using (Assert.EnterMultipleScope())
            {
                AssertCatalogue(first,
                    firstConfiguration == "HelperOnly" ? s_connectionNames : CombinedExpectedNames(true, true),
                    firstConfiguration == "HelperOnly" ? 4 : 366, 1);
                AssertCatalogue(second,
                    secondConfiguration == "HelperOnly" ? s_connectionNames : CombinedExpectedNames(true, true),
                    secondConfiguration == "HelperOnly" ? 4 : 366, 1);
            }
        }

        [TestCaseSource(nameof(UndefinedEnumCases))]
        public void UndefinedEnumProfilesAreRejectedByEveryLegacyModule(
            string module,
            McpToolProfile invalid,
            bool diagnosticsToolsEnabled)
        {
            var services = new ServiceCollection();
            IMcpServerBuilder builder = services.AddMcpServer();

            Assert.That(
                () => RegisterEnum(builder, module, invalid, diagnosticsToolsEnabled),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ActualValue)).EqualTo(invalid));

            AssertCatalogue(ReadNames(services), [], 0, 0);
        }

        [TestCaseSource(nameof(ModuleGateCases))]
        public void NullBuildersAreRejectedByEveryLegacyModule(string module, bool diagnosticsToolsEnabled)
        {
            Assert.That(
                () => RegisterEnum(null!, module, McpToolProfile.Full, diagnosticsToolsEnabled),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => RegisterEnum(null!, module, McpToolProfile.WotRuntime, diagnosticsToolsEnabled),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => RegisterSet(null!, module, McpToolProfileSet.Empty, diagnosticsToolsEnabled),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => RegisterSet(null!, module, AllProfiles(), diagnosticsToolsEnabled),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConnectionHelperRejectsNullBuilder()
        {
            IMcpServerBuilder builder = null!;

            Assert.That(() => builder.WithOpcUaConnectionTools(), Throws.TypeOf<ArgumentNullException>());
        }

        private static IEnumerable<TestCaseData> AppendedEnumCases()
        {
            // Keep all 255 rows: four PubSub-owned selections and 251 unrelated, empty catalogues.
            foreach (string module in s_modules)
            {
                foreach (bool gate in GateValues(module))
                {
                    foreach (McpToolProfile profile in s_appendedProfiles)
                    {
                        Selection? selection = FindSelection(module, profile, gate);
                        yield return new TestCaseData(module, profile, gate,
                            selection?.Catalogue ?? "None",
                            selection?.EnumCount ?? 0,
                            selection?.EnumConnections ?? 0);
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> UndefinedEnumCases()
        {
            McpToolProfile[] invalidProfiles =
            [
                (McpToolProfile)(-1),
                (McpToolProfile)36,
                (McpToolProfile)63,
                (McpToolProfile)64,
                (McpToolProfile)int.MinValue,
                (McpToolProfile)int.MaxValue
            ];
            foreach (string module in s_modules)
            {
                foreach (bool gate in GateValues(module))
                {
                    foreach (McpToolProfile invalid in invalidProfiles)
                    {
                        yield return new TestCaseData(module, invalid, gate);
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> ModuleGateCases()
        {
            foreach (string module in s_modules)
            {
                foreach (bool gate in GateValues(module))
                {
                    yield return new TestCaseData(module, gate);
                }
            }
        }

        private static IEnumerable<TestCaseData> UnselectedLegacyCases()
        {
            foreach (string module in s_modules)
            {
                foreach (bool gate in GateValues(module))
                {
                    foreach (McpToolProfile profile in s_boundedProfiles)
                    {
                        // Ownership comes only from the independent literal selection table below.
                        if (!s_selections.Any(selection => selection.Module == module && selection.Profile == profile))
                        {
                            yield return new TestCaseData(module, profile, gate);
                        }
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> EmptySetCases()
        {
            foreach (string module in s_modules)
            {
                foreach (bool gate in GateValues(module))
                {
                    yield return EmptyCatalogueCase(module, McpToolProfileSet.Empty, gate, "Empty");
                    yield return EmptyCatalogueCase(module, default, gate, "Default");
                }
            }
        }

        private static IEnumerable<TestCaseData> AppendedOnlySetCases()
        {
            foreach (string module in s_modules)
            {
                foreach (bool gate in GateValues(module))
                {
                    foreach (McpToolProfile profile in s_appendedProfiles)
                    {
                        Selection? selection = FindSelection(module, profile, gate);
                        yield return new TestCaseData(module, new McpToolProfileSet(profile), gate,
                            selection?.Catalogue ?? "None",
                            selection?.SetCount ?? 0,
                            selection?.SetConnections ?? 0)
                            .SetName($"{{m}}({module},Singleton{profile},Gate{gate})");
                    }

                    bool pubSub = module == "PubSub";
                    yield return new TestCaseData(module, new McpToolProfileSet(s_appendedProfiles), gate,
                        pubSub ? "PubSubExpanded" : "None", pubSub ? 56 : 0, pubSub ? 1 : 0)
                        .SetName($"{{m}}({module},AllAppended,Gate{gate})");
                }
            }
        }

        private static Selection? FindSelection(string module, McpToolProfile profile, bool gate)
        {
            return s_selections.SingleOrDefault(selection =>
                selection.Module == module && selection.Profile == profile && selection.Gate == gate);
        }

        private static TestCaseData EmptyCatalogueCase(
            string module,
            McpToolProfileSet profiles,
            bool gate,
            string scenario)
        {
            return new TestCaseData(module, profiles, gate)
                .SetName($"{{m}}({module},{scenario},Gate{gate})");
        }

        private static IEnumerable<TestCaseData> SelectedMixedSetCases()
        {
            foreach (Selection selection in s_selections)
            {
                var profiles = new McpToolProfileSet([selection.Profile, .. s_appendedProfiles]);
                string catalogue = selection.Catalogue;
                int expectedCount = selection.SetCount;
                int connections = selection.SetConnections;
                if (selection.Module == "PubSub")
                {
                    bool legacy = selection.Profile is McpToolProfile.PubSub or McpToolProfile.Full;
                    catalogue = legacy ? "PubSubFull" : "PubSubExpanded";
                    expectedCount = legacy ? 70 : 56;
                    connections = 1;
                }
                yield return new TestCaseData(selection.Module, profiles, selection.Gate, catalogue,
                    expectedCount, connections)
                    .SetName($"{{m}}({selection.Module},{selection.Profile}WithAllAppended,Gate{selection.Gate})");
            }

            yield return new TestCaseData("WotCon",
                new McpToolProfileSet([McpToolProfile.WotDocuments, McpToolProfile.WotRuntime]),
                false, "WotDocuments", 5, 0)
                .SetName("{m}(WotDocumentsWithWotRuntime)");
            yield return new TestCaseData("PubSub",
                new McpToolProfileSet(
                [
                    McpToolProfile.PubSub,
                    McpToolProfile.PubSubRuntime,
                    McpToolProfile.PubSubConfiguration,
                    McpToolProfile.PubSubBridge,
                    McpToolProfile.PubSubSecurity
                ]),
                false, "PubSubFull", 70, 1)
                .SetName("{m}(PubSubWithAllFourNewPubSubProfiles)");
            yield return new TestCaseData("Core",
                new McpToolProfileSet(
                [
                    McpToolProfile.Core,
                    McpToolProfile.PubSubSecurity,
                    McpToolProfile.WotRuntime
                ]),
                false, "Core", 15, 1)
                .SetName("{m}(CoreWithBothHighProfiles)");
        }

        private static IEnumerable<TestCaseData> UnownedMixedSetCases()
        {
            foreach (string module in s_modules)
            {
                McpToolProfile[] unownedProfiles = module switch
                {
                    "Core" =>
                    [
                        McpToolProfile.PubSub,
                        McpToolProfile.WotDocuments,
                        McpToolProfile.Diagnostics
                    ],
                    "PubSub.Diagnostics" => [McpToolProfile.Diagnostics],
                    _ => [McpToolProfile.Core]
                };

                foreach (bool gate in GateValues(module))
                {
                    foreach (McpToolProfile profile in unownedProfiles)
                    {
                        var profiles = new McpToolProfileSet([profile, .. s_appendedProfiles]);
                        int connections = module == "Core" && profile == McpToolProfile.Diagnostics ? 1 : 0;
                        string catalogue = module == "PubSub" ? "PubSubExpanded" : "None";
                        int expectedCount = module == "PubSub" ? 56 : connections * 4;
                        if (module == "PubSub")
                        {
                            connections = 1;
                        }
                        yield return new TestCaseData(module, profiles, gate, catalogue, expectedCount, connections)
                            .SetName($"{{m}}({module},{profile}WithAllAppended,Gate{gate})");
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> FullSetCases()
        {
            foreach (Selection selection in s_selections.Where(selection => selection.Profile == McpToolProfile.Full))
            {
                yield return new TestCaseData(selection.Module, selection.Gate, selection.Catalogue,
                    selection.SetCount, selection.SetConnections);
            }
        }

        private static IEnumerable<TestCaseData> OmittedProfileCases()
        {
            foreach (Selection selection in s_selections)
            {
                if (selection.Profile == McpToolProfile.Full && !selection.Gate)
                {
                    yield return new TestCaseData(selection.Module, selection.Catalogue,
                        selection.EnumCount, selection.EnumConnections);
                }
            }
        }

        private static IEnumerable<TestCaseData> OmittedDiagnosticsGateCases()
        {
            foreach (Selection selection in s_selections)
            {
                if (selection.Module is "Diagnostics" or "PubSub.Diagnostics" && !selection.Gate)
                {
                    yield return new TestCaseData(selection.Module, selection.Profile, selection.Catalogue,
                        selection.EnumCount, selection.EnumConnections);
                }
            }
        }

        private static IEnumerable<TestCaseData> CoreSelectionCases => SelectionCases("Core");
        private static IEnumerable<TestCaseData> DiagnosticsSelectionCases => SelectionCases("Diagnostics");
        private static IEnumerable<TestCaseData> PubSubSelectionCases => SelectionCases("PubSub");
        private static IEnumerable<TestCaseData> DiSelectionCases => SelectionCases("Di");
        private static IEnumerable<TestCaseData> AmbSelectionCases => SelectionCases("Amb");
        private static IEnumerable<TestCaseData> Isa95SelectionCases => SelectionCases("Isa95");
        private static IEnumerable<TestCaseData> MachinerySelectionCases => SelectionCases("Machinery");
        private static IEnumerable<TestCaseData> PositioningSelectionCases => SelectionCases("Positioning");
        private static IEnumerable<TestCaseData> PumpsSelectionCases => SelectionCases("Pumps");
        private static IEnumerable<TestCaseData> RoboticsSelectionCases => SelectionCases("Robotics");
        private static IEnumerable<TestCaseData> ScalesSelectionCases => SelectionCases("Scales");
        private static IEnumerable<TestCaseData> VisionSelectionCases => SelectionCases("Vision");
        private static IEnumerable<TestCaseData> WotSelectionCases => SelectionCases("WotCon");
        private static IEnumerable<TestCaseData> XRegistrySelectionCases => SelectionCases("XRegistry");

        private static IEnumerable<TestCaseData> PubSubDiagnosticsSelectionCases()
        {
            foreach (TestCaseData selected in SelectionCases("PubSub.Diagnostics"))
            {
                yield return selected;
            }

            foreach (bool gate in s_booleanValues)
            {
                foreach (bool useSet in s_booleanValues)
                {
                    yield return new TestCaseData(McpToolProfile.Diagnostics, useSet, gate, "None", 0, 0)
                        .SetName($"{{m}}(UnselectedDiagnostics,{(useSet ? "Set" : "Enum")},Gate{gate})");
                }
            }
        }

        private static IEnumerable<TestCaseData> SelectionCases(string module)
        {
            foreach (Selection selection in s_selections.Where(selection => selection.Module == module))
            {
                yield return new TestCaseData(selection.Profile, false, selection.Gate, selection.Catalogue,
                    selection.EnumCount, selection.EnumConnections)
                    .SetName($"{{m}}({selection.Profile},Enum,Gate{selection.Gate})");
                yield return new TestCaseData(selection.Profile, true, selection.Gate, selection.Catalogue,
                    selection.SetCount, selection.SetConnections)
                    .SetName($"{{m}}({selection.Profile},Set,Gate{selection.Gate})");
            }
        }

        private static IEnumerable<TestCaseData> CoreCombinedCases()
        {
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Core, McpToolProfile.Services]), "Services", 47, false)
                .SetName("{m}(CoreAndServices)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Core, McpToolProfile.Administration]),
                "CoreAdministration", 21, false)
                .SetName("{m}(CoreAndAdministration)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Services, McpToolProfile.Administration]),
                "CoreBounded", 53, false)
                .SetName("{m}(ServicesAndAdministration)");
            yield return new TestCaseData(
                new McpToolProfileSet(
                [
                    McpToolProfile.Core,
                    McpToolProfile.Services,
                    McpToolProfile.Administration
                ]),
                "CoreBounded", 53, false)
                .SetName("{m}(AllThreeBoundedSelections)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Core, McpToolProfile.Full]), "CoreFull", 54, true)
                .SetName("{m}(CoreAndFull)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Services, McpToolProfile.Full]), "CoreFull", 54, true)
                .SetName("{m}(ServicesAndFull)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Administration, McpToolProfile.Full]), "CoreFull", 54, true)
                .SetName("{m}(AdministrationAndFull)");
            yield return new TestCaseData(
                new McpToolProfileSet(
                [
                    McpToolProfile.Core,
                    McpToolProfile.Services,
                    McpToolProfile.Administration,
                    McpToolProfile.Full
                ]),
                "CoreFull", 54, true)
                .SetName("{m}(AllThreeBoundedSelectionsAndFull)");
            yield return new TestCaseData(
                new McpToolProfileSet(
                [
                    McpToolProfile.Core,
                    McpToolProfile.Services,
                    McpToolProfile.Administration,
                    .. s_appendedProfiles
                ]),
                "CoreBounded", 53, false)
                .SetName("{m}(AllThreeBoundedSelectionsAndAllAppendedWithoutFull)");
        }

        private static IEnumerable<TestCaseData> WotCombinedCases()
        {
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Wot, McpToolProfile.WotDocuments]))
                .SetName("{m}(ManagementAndDocuments)");
            yield return new TestCaseData(
                new McpToolProfileSet(
                [
                    McpToolProfile.WotDocuments,
                    McpToolProfile.Wot,
                    McpToolProfile.WotDocuments,
                    McpToolProfile.Wot
                ]))
                .SetName("{m}(ReversedAndRepeatedManagementAndDocuments)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Wot, McpToolProfile.WotDocuments, McpToolProfile.Full]))
                .SetName("{m}(ManagementAndDocumentsAndFull)");
            yield return new TestCaseData(
                new McpToolProfileSet([McpToolProfile.Wot, McpToolProfile.WotDocuments, .. s_appendedProfiles]))
                .SetName("{m}(ManagementAndDocumentsAndAllAppended)");
        }

        private static IEnumerable<TestCaseData> CompositionCases()
        {
            foreach (bool reverseOrder in s_booleanValues)
            {
                foreach (string registration in s_compositionRegistrations)
                {
                    foreach (bool diagnosticsGate in s_booleanValues)
                    {
                        foreach (bool pubSubGate in s_booleanValues)
                        {
                            int count = (diagnosticsGate, pubSubGate) switch
                            {
                                (false, false) => 356,
                                (true, false) => 363,
                                (false, true) => 359,
                                (true, true) => 366
                            };
                            yield return new TestCaseData(reverseOrder, registration,
                                diagnosticsGate, pubSubGate, count);
                        }
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> ExplicitConnectionCompositionCases()
        {
            foreach (bool reverseOrder in s_booleanValues)
            {
                foreach (string registration in s_compositionRegistrations)
                {
                    foreach (string placement in s_helperPlacements)
                    {
                        yield return new TestCaseData(reverseOrder, registration, placement);
                    }
                }
            }
        }

        private static bool[] GateValues(string module)
        {
            return module is "Diagnostics" or "PubSub.Diagnostics" ? s_booleanValues : s_disabledGate;
        }

        private static McpToolProfileSet AllProfiles()
        {
            // These two explicit, disjoint tables contain all 36 profiles, not just Full.
            return new McpToolProfileSet([.. s_boundedProfiles, .. s_appendedProfiles]);
        }

        private static void AssertIndependentSelection(
            string module,
            McpToolProfile profile,
            bool useSet,
            bool diagnosticsToolsEnabled,
            string catalogue,
            int expectedCount,
            int expectedConnectionCount)
        {
            string[] actual = useSet
                ? Observe(builder => RegisterSet(builder, module, new McpToolProfileSet(profile),
                    diagnosticsToolsEnabled))
                : Observe(builder => RegisterEnum(builder, module, profile, diagnosticsToolsEnabled));

            AssertCatalogue(actual, ExpectedNames(catalogue, expectedConnectionCount),
                expectedCount, expectedConnectionCount);
        }

        private static string[] Observe(Action<IMcpServerBuilder> configure)
        {
            var services = new ServiceCollection();
            configure(services.AddMcpServer());
            return ReadNames(services);
        }

        private static string[] ReadNames(IServiceCollection services)
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            return [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
        }

        private static void AssertCatalogue(
            string[] actual,
            string[] expected,
            int expectedCount,
            int expectedConnectionCount)
        {
            // Never deduplicate actual: all assertions inspect the raw registration array.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual, Is.EquivalentTo(expected), "Complete independent literal catalogue.");
                Assert.That(actual, Has.Length.EqualTo(expectedCount), "Raw tool registration count.");
                Assert.That(actual, Is.Unique, "Duplicate registrations must remain observable.");
                foreach (string connection in s_connectionNames)
                {
                    Assert.That(
                        actual.Count(name => string.Equals(name, connection, StringComparison.Ordinal)),
                        Is.EqualTo(expectedConnectionCount),
                        $"Raw registration count for {connection}.");
                }
            }
        }

        private static IMcpServerBuilder RegisterEnum(
            IMcpServerBuilder builder,
            string module,
            McpToolProfile profile,
            bool diagnosticsToolsEnabled)
        {
            // Every argument here is explicitly McpToolProfile: no implicit set conversion.
            IMcpServerBuilder registered = module switch
            {
                "Core" => builder.WithOpcUaCoreTools(profile),
                "Diagnostics" => builder.WithOpcUaDiagnosticsTools(profile, diagnosticsToolsEnabled),
                "PubSub" => builder.WithOpcUaPubSubTools(profile),
                "PubSub.Diagnostics" => builder.WithOpcUaPubSubDiagnosticsTools(profile, diagnosticsToolsEnabled),
                "Di" => builder.WithOpcUaDiTools(profile),
                "Amb" => builder.WithOpcUaAmbTools(profile),
                "Isa95" => builder.WithOpcUaIsa95Tools(profile),
                "Machinery" => builder.WithOpcUaMachineryTools(profile),
                "Positioning" => builder.WithOpcUaPositioningTools(profile),
                "Pumps" => builder.WithOpcUaPumpsTools(profile),
                "Robotics" => builder.WithOpcUaRoboticsTools(profile),
                "Scales" => builder.WithOpcUaScalesTools(profile),
                "Vision" => builder.WithOpcUaVisionTools(profile),
                "WotCon" => builder.WithOpcUaWotConTools(profile),
                "XRegistry" => builder.WithOpcUaXRegistryTools(profile),
                _ => throw new ArgumentOutOfRangeException(nameof(module), module, "Unknown test module.")
            };
            Assert.That(registered, Is.SameAs(builder), "Enum registration preserves the fluent builder.");
            return registered;
        }

        private static IMcpServerBuilder RegisterSet(
            IMcpServerBuilder builder,
            string module,
            McpToolProfileSet profiles,
            bool diagnosticsToolsEnabled)
        {
            IMcpServerBuilder registered = module switch
            {
                "Core" => builder.WithOpcUaCoreTools(profiles),
                "Diagnostics" => builder.WithOpcUaDiagnosticsTools(profiles, diagnosticsToolsEnabled),
                "PubSub" => builder.WithOpcUaPubSubTools(profiles),
                "PubSub.Diagnostics" => builder.WithOpcUaPubSubDiagnosticsTools(profiles, diagnosticsToolsEnabled),
                "Di" => builder.WithOpcUaDiTools(profiles),
                "Amb" => builder.WithOpcUaAmbTools(profiles),
                "Isa95" => builder.WithOpcUaIsa95Tools(profiles),
                "Machinery" => builder.WithOpcUaMachineryTools(profiles),
                "Positioning" => builder.WithOpcUaPositioningTools(profiles),
                "Pumps" => builder.WithOpcUaPumpsTools(profiles),
                "Robotics" => builder.WithOpcUaRoboticsTools(profiles),
                "Scales" => builder.WithOpcUaScalesTools(profiles),
                "Vision" => builder.WithOpcUaVisionTools(profiles),
                "WotCon" => builder.WithOpcUaWotConTools(profiles),
                "XRegistry" => builder.WithOpcUaXRegistryTools(profiles),
                _ => throw new ArgumentOutOfRangeException(nameof(module), module, "Unknown test module.")
            };
            Assert.That(registered, Is.SameAs(builder), "Set registration preserves the fluent builder.");
            return registered;
        }

        private static IMcpServerBuilder RegisterWithoutProfile(IMcpServerBuilder builder, string module)
        {
            // Omit the optional profile and, for diagnostics modules, the optional gate.
            IMcpServerBuilder registered = module switch
            {
                "Core" => builder.WithOpcUaCoreTools(),
                "Diagnostics" => builder.WithOpcUaDiagnosticsTools(),
                "PubSub" => builder.WithOpcUaPubSubTools(),
                "PubSub.Diagnostics" => builder.WithOpcUaPubSubDiagnosticsTools(),
                "Di" => builder.WithOpcUaDiTools(),
                "Amb" => builder.WithOpcUaAmbTools(),
                "Isa95" => builder.WithOpcUaIsa95Tools(),
                "Machinery" => builder.WithOpcUaMachineryTools(),
                "Positioning" => builder.WithOpcUaPositioningTools(),
                "Pumps" => builder.WithOpcUaPumpsTools(),
                "Robotics" => builder.WithOpcUaRoboticsTools(),
                "Scales" => builder.WithOpcUaScalesTools(),
                "Vision" => builder.WithOpcUaVisionTools(),
                "WotCon" => builder.WithOpcUaWotConTools(),
                "XRegistry" => builder.WithOpcUaXRegistryTools(),
                _ => throw new ArgumentOutOfRangeException(nameof(module), module, "Unknown test module.")
            };
            Assert.That(registered, Is.SameAs(builder), "Default registration preserves the fluent builder.");
            return registered;
        }

        private static IMcpServerBuilder RegisterWithoutDiagnosticsGate(
            IMcpServerBuilder builder,
            string module,
            McpToolProfile profile)
        {
            IMcpServerBuilder registered = module switch
            {
                "Diagnostics" => builder.WithOpcUaDiagnosticsTools(profile),
                "PubSub.Diagnostics" => builder.WithOpcUaPubSubDiagnosticsTools(profile),
                _ => throw new ArgumentOutOfRangeException(nameof(module), module, "Unknown diagnostics module.")
            };
            Assert.That(registered, Is.SameAs(builder), "Default-gate registration preserves the fluent builder.");
            return registered;
        }

        private static void RegisterConnections(IMcpServerBuilder builder, int repetitions)
        {
            for (int index = 0; index < repetitions; index++)
            {
                IMcpServerBuilder registered = builder.WithOpcUaConnectionTools();
                Assert.That(registered, Is.SameAs(builder), "Connection registration preserves the fluent builder.");
            }
        }

        private static void RegisterComposition(
            IMcpServerBuilder builder,
            string registration,
            bool reverseOrder,
            bool diagnosticsToolsEnabled,
            bool pubSubDiagnosticsToolsEnabled,
            string helperPlacement)
        {
            if (helperPlacement == "Before")
            {
                RegisterConnections(builder, 2);
            }

            string[] modules = reverseOrder ? [.. s_modules.Reverse()] : s_modules;
            for (int index = 0; index < modules.Length; index++)
            {
                string module = modules[index];
                bool gate = module switch
                {
                    "Diagnostics" => diagnosticsToolsEnabled,
                    "PubSub.Diagnostics" => pubSubDiagnosticsToolsEnabled,
                    _ => false
                };

                // Each domain module is called once, regardless of connection-helper placement.
                switch (registration)
                {
                    case "EnumFull":
                        RegisterEnum(builder, module, McpToolProfile.Full, gate);
                        break;
                    case "SetFull":
                        RegisterSet(builder, module, new McpToolProfileSet(McpToolProfile.Full), gate);
                        break;
                    case "AllProfiles":
                        RegisterSet(builder, module, AllProfiles(), gate);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(registration), registration, "Unknown test registration path.");
                }

                if (helperPlacement == "Between" && index == modules.Length / 2)
                {
                    RegisterConnections(builder, 2);
                }
            }

            if (helperPlacement == "After")
            {
                RegisterConnections(builder, 2);
            }
        }

        private static void ConfigureCollection(IServiceCollection services, string configuration)
        {
            IMcpServerBuilder builder = services.AddMcpServer();
            switch (configuration)
            {
                case "HelperOnly":
                    RegisterConnections(builder, 2);
                    break;
                case "Composed":
                    RegisterComposition(builder, "AllProfiles", false, true, true, "None");
                    break;
                case "ReverseComposed":
                    RegisterComposition(builder, "AllProfiles", true, true, true, "None");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(configuration), configuration, "Unknown test collection configuration.");
            }
        }

        private static string[] ExpectedNames(string catalogue, int connections)
        {
            string[] domain = DomainNames(catalogue);
            return connections == 1 ? LiteralUnion(s_connectionNames, domain) : domain;
        }

        private static string[] DomainNames(string catalogue)
        {
            return catalogue switch
            {
                "None" => [],
                "Core" => LiteralUnion(s_coreConfigurationNames, s_coreConvenienceNames),
                "Services" => LiteralUnion(s_coreConfigurationNames, s_coreConvenienceNames, s_coreServiceNames),
                "Administration" => LiteralUnion(s_coreConfigurationNames, s_coreAdministrationNames),
                "CoreAdministration" => LiteralUnion(
                    s_coreConfigurationNames, s_coreConvenienceNames, s_coreAdministrationNames),
                "CoreBounded" => LiteralUnion(
                    s_coreConfigurationNames, s_coreConvenienceNames, s_coreServiceNames, s_coreAdministrationNames),
                "CoreFull" => LiteralUnion(s_coreConfigurationNames, s_coreConvenienceNames, s_coreServiceNames,
                    s_coreAdministrationNames, s_coreFullOnlyNames),
                "DiagnosticsCapture" => s_diagnosticsCaptureNames,
                "DiagnosticsEnabled" => LiteralUnion(
                    s_diagnosticsCaptureNames, s_diagnosticsDecodeNames, s_diagnosticsReplayNames),
                "PubSub" => s_pubSubNames,
                "PubSubRuntime" => s_pubSubApplicationNames,
                "PubSubConfiguration" => s_pubSubConfigurationNames,
                "PubSubBridge" => s_pubSubBridgeNames,
                "PubSubSecurity" => s_pubSubSecurityNames,
                "PubSubExpanded" => LiteralUnion(s_pubSubApplicationNames, s_pubSubConfigurationNames,
                    s_pubSubBridgeNames, s_pubSubSecurityNames),
                "PubSubFull" => LiteralUnion(s_pubSubNames, s_pubSubApplicationNames, s_pubSubConfigurationNames,
                    s_pubSubBridgeNames, s_pubSubSecurityNames),
                "PubSubCapture" => s_pubSubCaptureNames,
                "PubSubEnabled" => LiteralUnion(s_pubSubCaptureNames, s_pubSubDecodeNames),
                "Di" => s_diNames,
                "Amb" => s_ambNames,
                "Isa95" => s_isa95Names,
                "Machinery" => s_machineryNames,
                "Positioning" => s_positioningNames,
                "Pumps" => s_pumpsNames,
                "Robotics" => s_roboticsNames,
                "Scales" => s_scalesNames,
                "Vision" => s_visionNames,
                "WotManagement" => s_wotManagementNames,
                "WotDocuments" => s_wotDocumentNames,
                "WotFull" => LiteralUnion(s_wotManagementNames, s_wotDocumentNames),
                "XRegistry" => s_xRegistryNames,
                _ => throw new ArgumentOutOfRangeException(nameof(catalogue), catalogue, "Unknown literal catalogue.")
            };
        }

        private static string[] CombinedExpectedNames(bool diagnosticsEnabled, bool pubSubDiagnosticsEnabled)
        {
            return LiteralUnion(
                s_connectionNames,
                DomainNames("CoreFull"),
                s_diagnosticsCaptureNames,
                diagnosticsEnabled ? s_diagnosticsDecodeNames : [],
                diagnosticsEnabled ? s_diagnosticsReplayNames : [],
                DomainNames("PubSubFull"),
                s_pubSubCaptureNames,
                pubSubDiagnosticsEnabled ? s_pubSubDecodeNames : [],
                s_diNames,
                s_ambNames,
                s_isa95Names,
                s_machineryNames,
                s_positioningNames,
                s_pumpsNames,
                s_roboticsNames,
                s_scalesNames,
                s_visionNames,
                s_wotManagementNames,
                s_wotDocumentNames,
                s_xRegistryNames);
        }

        private static string[] LiteralUnion(params string[][] groups)
        {
            // Deduplication is allowed only for expected literal groups, never for observed tool names.
            return [.. groups.SelectMany(group => group).Distinct(StringComparer.Ordinal)];
        }

        private sealed record Selection(
            string Module,
            McpToolProfile Profile,
            bool Gate,
            string Catalogue,
            int EnumCount,
            int EnumConnections,
            int SetCount,
            int SetConnections);

        private static readonly bool[] s_booleanValues = [false, true];
        private static readonly bool[] s_disabledGate = [false];
        private static readonly string[] s_compositionRegistrations = ["EnumFull", "SetFull", "AllProfiles"];
        private static readonly string[] s_helperPlacements = ["Before", "Between", "After"];

        /// <summary>
        /// This is the module order required by the independent legacy catalogue contract.
        /// </summary>
        private static readonly string[] s_modules =
        [
            "Core", "Diagnostics", "PubSub", "PubSub.Diagnostics", "Di",
            "Amb", "Isa95", "Machinery", "Positioning", "Pumps",
            "Robotics", "Scales", "Vision", "WotCon", "XRegistry"
        ];

        private static readonly McpToolProfile[] s_boundedProfiles =
        [
            McpToolProfile.Core,
            McpToolProfile.Services,
            McpToolProfile.Administration,
            McpToolProfile.PubSub,
            McpToolProfile.Diagnostics,
            McpToolProfile.Robotics,
            McpToolProfile.Vision,
            McpToolProfile.Full,
            McpToolProfile.Amb,
            McpToolProfile.Machinery,
            McpToolProfile.Scales,
            McpToolProfile.Pumps,
            McpToolProfile.Di,
            McpToolProfile.Isa95,
            McpToolProfile.Positioning,
            McpToolProfile.Wot,
            McpToolProfile.XRegistry,
            McpToolProfile.WotDocuments,
            McpToolProfile.MachineTool,
            McpToolProfile.Gms,
            McpToolProfile.CranesHoists
        ];

        /// <summary>
        /// The fifteen appended values (18-32) are deliberate literals, not Enum.GetValues output.
        /// </summary>
        private static readonly McpToolProfile[] s_appendedProfiles =
        [
            McpToolProfile.Gds,
            McpToolProfile.Certificates,
            McpToolProfile.Credentials,
            McpToolProfile.Onboarding,
            McpToolProfile.Roles,
            McpToolProfile.Users,
            McpToolProfile.Files,
            McpToolProfile.Alarms,
            McpToolProfile.History,
            McpToolProfile.Schemas,
            McpToolProfile.PubSubRuntime,
            McpToolProfile.PubSubConfiguration,
            McpToolProfile.PubSubBridge,
            McpToolProfile.PubSubSecurity,
            McpToolProfile.WotRuntime
        ];

        private static readonly McpToolProfile[] s_coreUnownedSessionProfiles =
        [
            McpToolProfile.Diagnostics,
            McpToolProfile.Robotics,
            McpToolProfile.Vision,
            McpToolProfile.Amb,
            McpToolProfile.Machinery,
            McpToolProfile.Scales,
            McpToolProfile.Pumps,
            McpToolProfile.Di,
            McpToolProfile.Isa95,
            McpToolProfile.Positioning,
            McpToolProfile.Wot,
            McpToolProfile.XRegistry,
            McpToolProfile.MachineTool,
            McpToolProfile.Gms,
            McpToolProfile.CranesHoists
        ];

        /// <summary>
        /// Explicit independent oracles: module, profile, gate, domain, E count/C, S count/C.
        /// There are 41 selected rows per overload, including both gates of each diagnostics module.
        /// </summary>
        private static readonly Selection[] s_selections =
        [
            new("Core", McpToolProfile.Core, false, "Core", 15, 1, 15, 1),
            new("Core", McpToolProfile.Services, false, "Services", 47, 1, 47, 1),
            new("Core", McpToolProfile.Administration, false, "Administration", 14, 1, 14, 1),
            new("Core", McpToolProfile.Full, false, "CoreFull", 54, 1, 54, 1),
            new("Diagnostics", McpToolProfile.Diagnostics, false, "DiagnosticsCapture", 10, 1, 6, 0),
            new("Diagnostics", McpToolProfile.Diagnostics, true, "DiagnosticsEnabled", 17, 1, 13, 0),
            new("Diagnostics", McpToolProfile.Full, false, "DiagnosticsCapture", 6, 0, 6, 0),
            new("Diagnostics", McpToolProfile.Full, true, "DiagnosticsEnabled", 13, 0, 13, 0),
            // Approved PubSub ownership: legacy 14; runtime 10; configuration 20; bridge/adapter 10; security 12.
            // Only configuration, bridge, security and their Full union also own the four connection tools.
            new("PubSub", McpToolProfile.PubSub, false, "PubSub", 14, 0, 14, 0),
            new("PubSub", McpToolProfile.PubSubRuntime, false, "PubSubRuntime", 10, 0, 10, 0),
            new("PubSub", McpToolProfile.PubSubConfiguration, false, "PubSubConfiguration", 24, 1, 24, 1),
            new("PubSub", McpToolProfile.PubSubBridge, false, "PubSubBridge", 14, 1, 14, 1),
            new("PubSub", McpToolProfile.PubSubSecurity, false, "PubSubSecurity", 16, 1, 16, 1),
            new("PubSub", McpToolProfile.Full, false, "PubSubFull", 70, 1, 70, 1),
            new("PubSub.Diagnostics", McpToolProfile.PubSub, false, "PubSubCapture", 4, 0, 4, 0),
            new("PubSub.Diagnostics", McpToolProfile.PubSub, true, "PubSubEnabled", 7, 0, 7, 0),
            new("PubSub.Diagnostics", McpToolProfile.Full, false, "PubSubCapture", 4, 0, 4, 0),
            new("PubSub.Diagnostics", McpToolProfile.Full, true, "PubSubEnabled", 7, 0, 7, 0),
            new("Di", McpToolProfile.Di, false, "Di", 28, 1, 28, 1),
            new("Di", McpToolProfile.Full, false, "Di", 28, 1, 28, 1),
            new("Amb", McpToolProfile.Amb, false, "Amb", 16, 1, 16, 1),
            new("Amb", McpToolProfile.Full, false, "Amb", 16, 1, 16, 1),
            new("Isa95", McpToolProfile.Isa95, false, "Isa95", 24, 1, 24, 1),
            new("Isa95", McpToolProfile.Full, false, "Isa95", 24, 1, 24, 1),
            new("Machinery", McpToolProfile.Machinery, false, "Machinery", 23, 1, 23, 1),
            new("Machinery", McpToolProfile.Full, false, "Machinery", 23, 1, 23, 1),
            new("Positioning", McpToolProfile.Positioning, false, "Positioning", 11, 1, 11, 1),
            new("Positioning", McpToolProfile.Full, false, "Positioning", 7, 0, 11, 1),
            new("Pumps", McpToolProfile.Pumps, false, "Pumps", 12, 1, 12, 1),
            new("Pumps", McpToolProfile.Full, false, "Pumps", 12, 1, 12, 1),
            new("Robotics", McpToolProfile.Robotics, false, "Robotics", 42, 1, 38, 0),
            new("Robotics", McpToolProfile.Full, false, "Robotics", 38, 0, 38, 0),
            new("Scales", McpToolProfile.Scales, false, "Scales", 28, 1, 28, 1),
            new("Scales", McpToolProfile.Full, false, "Scales", 24, 0, 28, 1),
            new("Vision", McpToolProfile.Vision, false, "Vision", 43, 1, 39, 0),
            new("Vision", McpToolProfile.Full, false, "Vision", 39, 0, 39, 0),
            new("WotCon", McpToolProfile.Wot, false, "WotManagement", 20, 1, 20, 1),
            new("WotCon", McpToolProfile.WotDocuments, false, "WotDocuments", 5, 0, 5, 0),
            new("WotCon", McpToolProfile.Full, false, "WotFull", 25, 1, 25, 1),
            new("XRegistry", McpToolProfile.XRegistry, false, "XRegistry", 18, 1, 18, 1),
            new("XRegistry", McpToolProfile.Full, false, "XRegistry", 18, 1, 18, 1)
        ];

        /// <summary>
        /// All 366 distinct names below are literal catalogue oracles, independent of registration.
        /// </summary>
        private static readonly string[] s_connectionNames =
        [
            "Connect",
            "Disconnect",
            "GetEndpoints",
            "GetConnectionStatus"
        ];

        private static readonly string[] s_coreConfigurationNames =
        [
            "GetConfiguration",
            "SetTransportConfiguration",
            "SetClientConfiguration",
            "SetSecurityConfiguration"
        ];

        private static readonly string[] s_coreConvenienceNames =
        [
            "ReadValue",
            "ReadValues",
            "WriteValue",
            "BrowseAll",
            "CallMethod",
            "ReadNode",
            "Cancel"
        ];

        private static readonly string[] s_coreServiceNames =
        [
            // Attribute services.
            "Read",
            "Write",
            "HistoryRead",
            "HistoryUpdate",
            // Discovery services.
            "FindServers",
            "FindServersOnNetwork",
            "RegisterServer",
            "RegisterServer2",
            // Method services.
            "Call",
            // Monitored item services.
            "CreateMonitoredItems",
            "ModifyMonitoredItems",
            "SetMonitoringMode",
            "SetTriggering",
            "DeleteMonitoredItems",
            // Node management services.
            "AddNodes",
            "AddReferences",
            "DeleteNodes",
            "DeleteReferences",
            // Subscription services.
            "CreateSubscription",
            "ModifySubscription",
            "SetPublishingMode",
            "Publish",
            "Republish",
            "DeleteSubscriptions",
            "TransferSubscriptions",
            // View services.
            "Browse",
            "BrowseNext",
            "TranslateBrowsePaths",
            "RegisterNodes",
            "UnregisterNodes",
            "QueryFirst",
            "QueryNext"
        ];

        private static readonly string[] s_coreAdministrationNames =
        [
            "ExportNodeSet",
            "ExportNodeSetPerNamespace",
            "ListCertificates",
            "TrustCertificate",
            "RemoveCertificate",
            "GetPkiStorePaths"
        ];

        private static readonly string[] s_coreFullOnlyNames = ["SetConfiguration"];

        private static readonly string[] s_diagnosticsCaptureNames =
        [
            "list_interfaces",
            "start_capture",
            "stop_capture",
            "list_captures",
            "get_capture",
            "capture_now"
        ];

        private static readonly string[] s_diagnosticsDecodeNames =
        [
            "list_active_channels",
            "dump_keys",
            "decode_pcap_with_keys",
            "summarize_service_calls"
        ];

        private static readonly string[] s_diagnosticsReplayNames =
        [
            "replay_pcap",
            "stop_replay",
            "list_replays"
        ];

        private static readonly string[] s_pubSubNames =
        [
            // Action.
            "pubsub_invoke_action",
            "pubsub_register_action_responder",
            "pubsub_bind_action_method",
            "pubsub_list_action_targets",
            "pubsub_list_action_responders",
            // Discovery.
            "pubsub_discover_metadata",
            "pubsub_discover_writer_config",
            "pubsub_discover_publisher_endpoints",
            // Runtime metadata only.
            "pubsub_runtime_start_publisher",
            "pubsub_runtime_start_subscriber",
            "pubsub_runtime_publish",
            "pubsub_runtime_read_received",
            "pubsub_runtime_status",
            "pubsub_runtime_stop"
        ];

        /// <summary>
        /// These names belong to the approved PubSub expansion, not to the legacy pubsub profile.
        /// </summary>
        private static readonly string[] s_pubSubApplicationNames =
        [
            "pubsub_capabilities",
            "pubsub_application_validate",
            "pubsub_application_create",
            "pubsub_application_list",
            "pubsub_application_start",
            "pubsub_application_stop",
            "pubsub_application_remove",
            "pubsub_application_status",
            "pubsub_application_publish",
            "pubsub_application_read"
        ];

        private static readonly string[] s_pubSubConfigurationNames =
        [
            "pubsub_configuration_inspect",
            "pubsub_configuration_add_connection",
            "pubsub_configuration_remove_connection",
            "pubsub_configuration_add_writer_group",
            "pubsub_configuration_add_reader_group",
            "pubsub_configuration_remove_group",
            "pubsub_configuration_add_writer",
            "pubsub_configuration_remove_writer",
            "pubsub_configuration_add_reader",
            "pubsub_configuration_remove_reader",
            "pubsub_configuration_add_dataset",
            "pubsub_configuration_remove_dataset",
            "pubsub_configuration_add_folder",
            "pubsub_configuration_remove_folder",
            "pubsub_configuration_add_fields",
            "pubsub_configuration_remove_fields",
            "pubsub_configuration_enable",
            "pubsub_configuration_disable",
            // The same tool class also owns the file-transfer partial.
            "pubsub_configuration_export",
            "pubsub_configuration_import"
        ];

        private static readonly string[] s_pubSubBridgeNames =
        [
            "pubsub_bridge_validate",
            "pubsub_bridge_create",
            "pubsub_bridge_list",
            "pubsub_bridge_start",
            "pubsub_bridge_stop",
            "pubsub_bridge_status",
            "pubsub_bridge_remove",
            // The bridge profile explicitly owns all three adapter bindings too.
            "pubsub_adapter_bind_source",
            "pubsub_adapter_bind_sink",
            "pubsub_adapter_bind_action"
        ];

        private static readonly string[] s_pubSubSecurityNames =
        [
            "pubsub_security_inspect",
            "pubsub_security_add_group",
            "pubsub_security_remove_group",
            "pubsub_security_rotate",
            "pubsub_security_invalidate",
            "pubsub_security_pull_keys",
            "pubsub_security_push_keys",
            "pubsub_security_add_target",
            "pubsub_security_remove_target",
            "pubsub_security_connect_groups",
            "pubsub_security_disconnect_groups",
            "pubsub_security_trigger_update"
        ];

        private static readonly string[] s_pubSubCaptureNames =
        [
            "pubsub_start_capture",
            "pubsub_stop_capture",
            "pubsub_capture_status",
            "pubsub_write_pcap"
        ];

        private static readonly string[] s_pubSubDecodeNames =
        [
            "pubsub_dissect_capture",
            "pubsub_decode_pcap",
            "pubsub_load_keylog"
        ];

        private static readonly string[] s_diNames =
        [
            // Read.
            "di_discover_devices",
            "di_browse_topology",
            "di_read_device",
            "di_read_property",
            "di_read_functional_group",
            "di_read_lock",
            // Command.
            "di_write_property",
            "di_acquire_lock",
            "di_renew_lock",
            "di_release_lock",
            "di_break_lock",
            "di_transfer_to_device",
            "di_transfer_from_device",
            "di_fetch_transfer_results",
            // Software update.
            "di_read_software_update",
            "di_observe_software_update",
            "di_prepare_software_update",
            "di_abort_prepare",
            "di_upload_software_package",
            "di_install_software_package",
            "di_install_files",
            "di_uninstall_software",
            "di_resume_installation",
            "di_confirm_software_update"
        ];

        private static readonly string[] s_ambNames =
        [
            "amb_discover_assets",
            "amb_find_assets",
            "amb_list_aliases",
            "amb_read_category",
            "amb_read_asset",
            "amb_browse_locations",
            "amb_observe",
            "amb_write_asset_id",
            "amb_acknowledge",
            "amb_add_documentation_link",
            "amb_remove_documentation_link",
            "amb_write_documentation_link"
        ];

        private static readonly string[] s_isa95Names =
        [
            "isa95_list_common_objects",
            "isa95_read_common_object",
            "isa95_discover_job_endpoints",
            "isa95_v1_receive_order",
            "isa95_v1_query_responses",
            "isa95_v1_receive_response",
            "isa95_v2_store",
            "isa95_v2_store_and_start",
            "isa95_v2_update",
            "isa95_v2_start",
            "isa95_v2_stop",
            "isa95_v2_pause",
            "isa95_v2_resume",
            "isa95_v2_abort",
            "isa95_v2_revoke_start",
            "isa95_v2_cancel",
            "isa95_v2_clear",
            "isa95_v2_query_responses",
            "isa95_v2_receive_response",
            "isa95_v2_observe_status"
        ];

        private static readonly string[] s_machineryNames =
        [
            "machinery_list_machines",
            "machinery_list_components",
            "machinery_list_building_blocks",
            "machinery_read",
            "machinery_list_lifetime_counters",
            "machinery_list_equipment",
            "machinery_list_process_values",
            "machinery_read_process_value",
            "machinery_zero_point_adjustment",
            "machinery_list_energy",
            "machinery_read_meter",
            "machinery_list_jobs",
            "machinery_read_job_parameters",
            "machinery_get_job_endpoints",
            "machinery_list_results",
            "machinery_read_result",
            "machinery_list_published_results",
            "machinery_download_result",
            "machinery_observe"
        ];

        private static readonly string[] s_positioningNames =
        [
            "positioning_rsl_list",
            "positioning_rsl_read",
            "positioning_rsl_observe",
            "positioning_gpos_list",
            "positioning_gpos_read",
            "positioning_gpos_transform",
            "positioning_gpos_observe"
        ];

        private static readonly string[] s_pumpsNames =
        [
            "pumps_list_pumps",
            "pumps_read_nameplate",
            "pumps_list_markings",
            "pumps_read_group",
            "pumps_list_ports",
            "pumps_read_value_set",
            "pumps_read_snapshot",
            "pumps_classify_type"
        ];

        private static readonly string[] s_roboticsNames =
        [
            // Discovery.
            "robotics_list_controllers",
            "robotics_read_controller",
            // Monitoring.
            "robotics_read_state",
            "robotics_list_operations",
            "robotics_list_missions",
            "robotics_wait_operation",
            "robotics_wait_mission",
            // Control.
            "robotics_request_control",
            "robotics_release_control",
            "robotics_cancel_intent",
            "robotics_cancel_all",
            "robotics_pause",
            "robotics_resume",
            "robotics_retry",
            "robotics_submit_joint_move",
            "robotics_submit_linear_move",
            "robotics_submit_circular_move",
            "robotics_submit_trajectory",
            "robotics_submit_cartesian_path",
            "robotics_submit_force",
            "robotics_submit_arc_weld",
            "robotics_submit_spot_weld",
            "robotics_submit_dispense",
            "robotics_submit_fasten",
            "robotics_submit_palletise",
            "robotics_submit_surface_finish",
            "robotics_submit_grasp",
            "robotics_submit_release",
            "robotics_submit_pick",
            "robotics_submit_place",
            "robotics_submit_tool_change",
            "robotics_submit_set_output",
            "robotics_submit_call_program",
            "robotics_submit_wait",
            // Mission.
            "robotics_submit_mission",
            "robotics_update_mission",
            "robotics_cancel_mission",
            // Vision.
            "robotics_vision_pick"
        ];

        private static readonly string[] s_scalesNames =
        [
            "scales_list",
            "scales_read",
            "scales_observe",
            "scales_set_zero",
            "scales_set_tare",
            "scales_clear_tare",
            "scales_set_preset_tare",
            "scales_register_weight",
            "scales_add_product",
            "scales_remove_product",
            "scales_select_product",
            "scales_deselect_product",
            "scales_switch_product",
            "scales_packml_command",
            "scales_standard_command",
            "scales_set_reference_piece_weight",
            "scales_set_number_of_reference_pieces",
            "scales_start_reference",
            "scales_set_draft_shields",
            "scales_weigh_vehicle",
            "scales_set_feeder_speed",
            "scales_add_recipe",
            "scales_add_recipe_element",
            "scales_process_recipe"
        ];

        private static readonly string[] s_visionNames =
        [
            // Discovery.
            "vision_list_sensors",
            "vision_list_pipelines",
            "vision_list_frames",
            "vision_list_calibrations",
            // Monitoring.
            "vision_read_sensor",
            "vision_read_extrinsic_calibration",
            "vision_read_pipeline",
            "vision_read_detection_result",
            "vision_read_inspection_result",
            "vision_read_segmentation_result",
            // Seeing.
            "vision_get_frame",
            "vision_get_frame_metadata",
            // Inference.
            "vision_run_inference",
            "vision_start_continuous_inference",
            "vision_stop_inference",
            // Feedback.
            "vision_submit_detections",
            "vision_submit_inspection_result",
            "vision_submit_correction",
            "vision_submit_image_reference",
            // Geometry.
            "vision_read_frame",
            "vision_compose_pose",
            "vision_compose_transform",
            // Machine base.
            "vision_machine_list",
            "vision_machine_read_state",
            "vision_machine_command",
            "vision_machine_read_asset",
            "vision_machine_recipes",
            "vision_machine_configurations",
            "vision_machine_results",
            "vision_machine_report_safety",
            // Machine files.
            "vision_machine_file",
            "vision_machine_recipe_object",
            "vision_machine_condition",
            // Machine workflows.
            "vision_machine_capabilities",
            "vision_machine_relationships",
            "vision_machine_observe",
            "vision_machine_upload_recipe",
            "vision_machine_upload_configuration",
            "vision_machine_download_result"
        ];

        private static readonly string[] s_wotManagementNames =
        [
            "wot_inspect_server",
            "wot_list_assets",
            "wot_inspect_asset",
            "wot_discover_endpoints",
            "wot_test_connection",
            "wot_create_asset",
            "wot_create_asset_for_endpoint",
            "wot_delete_asset",
            "wot_upload_thing_description",
            "wot_download_thing_description",
            "wot_read_resource_state",
            "wot_validate_resource",
            "wot_set_enabled",
            "wot_set_default_version",
            "wot_refresh",
            "wot_observe_events"
        ];

        private static readonly string[] s_wotDocumentNames =
        [
            "wot_inspect_document",
            "wot_validate_document",
            "wot_plan_bindings",
            "wot_to_nodeset",
            "wot_from_nodeset"
        ];

        private static readonly string[] s_xRegistryNames =
        [
            "xregistry_discover",
            "xregistry_list",
            "xregistry_read",
            "xregistry_create_group",
            "xregistry_create_resource",
            "xregistry_upload_document",
            "xregistry_download_document",
            "xregistry_resolve_content",
            "xregistry_set_label",
            "xregistry_remove_label",
            "xregistry_delete_group",
            "xregistry_delete_resource",
            "xregistry_delete_version",
            "xregistry_observe_events"
        ];
    }
}
#endif
