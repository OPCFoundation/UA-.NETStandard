// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.SpecTraceability;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Contains tests for WoT spec requirement.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotSpecRequirementTests
    {
        /// <summary>
        /// Verifies that core lifecycle evidence mappings resolve to runnable tests.
        /// </summary>
        [Test]
        public void CoreLifecycleEvidenceMappingsResolveToRunnableTests()
        {
            Assembly assembly = typeof(WotSpecRequirementTests).Assembly;
            WotSpecRequirement[] requirements = WotSpecRequirementLedger.Load(assembly)
                .Where(requirement => requirement.Assembly == assembly.GetName().Name)
                .ToArray();

            Assert.That(requirements, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (WotSpecRequirement requirement in requirements)
                {
                    foreach (string test in requirement.Tests)
                    {
                        Assert.That(WotSpecRequirementLedger.DescribeResolution(assembly, test),
                            Is.EqualTo("runs"), $"{requirement.SpecId} maps onto '{test}'.");
                    }
                }
            });
        }
    }
}
