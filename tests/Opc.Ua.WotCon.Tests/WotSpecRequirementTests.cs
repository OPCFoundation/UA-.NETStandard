/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Holds this assembly's half of the WoT specification evidence ledger.
    /// </summary>
    /// <remarks>
    /// The ledger spans multiple test assemblies and none of them can see the
    /// others, so each embeds the same file and resolves only the mappings that
    /// name it. Most of the WoT Connectivity requirements land here, because
    /// most of what that specification states is about a running registry.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotSpecRequirementTests
    {
        private const string ThisAssembly = "Opc.Ua.WotCon.Tests";

        /// <summary>
        /// Verifies that every mapping naming this assembly resolves and will run.
        /// </summary>
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
                        IReadOnlyList<string> targets = WotSpecRequirementLedger.ReadTargetFrameworks(assembly, name);
                        Assert.That(targets, Is.Empty.Or.EquivalentTo(s_modernFrameworks));
#if !NET8_0_OR_GREATER
                        if (targets.Count != 0)
                        {
                            Assert.That(name, Is.EqualTo(
                                "Opc.Ua.WotCon.Tests.Materialization.WotEventModesLiveTests"));
                            Assert.That(assembly.GetType(name, throwOnError: false), Is.Null,
                                "This fixture is intentionally compiled only with modern protocol executors.");
                            continue;
                        }
#endif
                        Assert.That(
                            WotSpecRequirementLedger.DescribeResolution(assembly, name),
                            Is.EqualTo("runs"),
                            $"{requirement.SpecId} maps onto '{name}'.");
                    }
                }
            });
        }

        /// <summary>
        /// The mappings that name this assembly are not an empty set: if they
        /// were, this fixture would pass by checking nothing.
        /// </summary>
        [Test]
        public void ThisAssemblyCarriesEvidenceForSomeRequirement()
        {
            List<WotSpecRequirement> requirements = WotSpecRequirementLedger.Load(
                typeof(WotSpecRequirementTests).Assembly);

            Assert.That(
                requirements.Count(r => r.Assembly == ThisAssembly && r.Tests.Count > 0),
                Is.GreaterThan(0));
        }

        private static readonly string[] s_modernFrameworks = ["net10.0", "net11.0"];
    }
}
