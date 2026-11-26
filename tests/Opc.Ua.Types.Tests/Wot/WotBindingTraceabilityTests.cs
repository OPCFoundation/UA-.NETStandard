// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Holds the temporary WoT Binding traceability ledger to something a test
    /// run can check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A clause-to-test map is only worth having if it cannot rot. Written as
    /// prose it decays the first time a test is renamed and nobody notices;
    /// written as names a test run resolves, a rename is a failure with the old
    /// name in the message.
    /// </para>
    /// <para>
    /// So every mapping is resolved by reflection: the type has to exist, the
    /// method has to exist on it, and both have to be things NUnit will
    /// actually run. A mapping onto an <c>[Explicit]</c> or <c>[Ignore]</c>d
    /// test is rejected, because a clause held only by a test that never runs
    /// is a clause held by nothing.
    /// </para>
    /// <para>
    /// This clause-level view complements the statement-level requirement
    /// inventory. The local fixture manifest is independent and does not
    /// establish conformance to the specification baseline.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Category("WotSpecExamples")]
    [Parallelizable]
    public sealed class WotBindingTraceabilityTests
    {
        [Test]
        public void EveryMappedTestExistsAndWillRun()
        {
            List<WotTraceabilityClause> clauses = WotTraceabilityLedger.Load(Assembly);
            string thisAssembly =
                Assembly.GetName().Name!;

            Assert.Multiple(() =>
            {
                foreach (WotTraceabilityClause clause in clauses
                    .Where(c => string.Equals(
                        c.Assembly, thisAssembly, StringComparison.Ordinal)))
                {
                    foreach (string name in clause.Tests)
                    {
                        Assert.That(
                            WotTraceabilityLedger.DescribeResolution(Assembly, name),
                            Is.EqualTo("runs"),
                            $"Clause {clause.Clause} maps onto '{name}'.");
                    }
                }
            });
        }

        /// <summary>
        /// A clause held in another assembly still has to name one this
        /// solution builds, and its tests have to live in it, or the mapping
        /// would be verified nowhere.
        /// </summary>
        [Test]
        public void EveryClauseNamesAKnownAssemblyAndStaysInIt()
        {
            List<WotTraceabilityClause> clauses = WotTraceabilityLedger.Load(Assembly);

            Assert.Multiple(() =>
            {
                foreach (WotTraceabilityClause clause in clauses)
                {
                    Assert.That(
                        WotTraceabilityLedger.Assemblies,
                        Does.Contain(clause.Assembly),
                        $"Clause {clause.Clause} names an assembly no fixture runs in.");
                    foreach (string name in clause.Tests)
                    {
                        Assert.That(
                            name,
                            Does.StartWith(clause.Assembly + "."),
                            $"Clause {clause.Clause} is held in '{clause.Assembly}' but " +
                            $"names '{name}', which no fixture there can resolve.");
                    }
                }
            });
        }

        private static Assembly Assembly =>
            typeof(WotBindingTraceabilityTests).Assembly;

        [Test]
        public void EveryClauseMapsOntoAtLeastOneTest()
        {
            List<WotTraceabilityClause> clauses = WotTraceabilityLedger.Load(Assembly);

            Assert.Multiple(() =>
            {
                Assert.That(clauses, Is.Not.Empty);
                foreach (WotTraceabilityClause clause in clauses)
                {
                    Assert.That(
                        clause.Tests,
                        Is.Not.Empty,
                        $"Clause {clause.Clause} holds nothing.");
                    Assert.That(
                        clause.Title,
                        Is.Not.Empty,
                        $"Clause {clause.Clause} states no title, so the map cannot be read.");
                }
                Assert.That(
                    clauses.Select(c => c.Clause),
                    Is.Unique,
                    "A clause listed twice hides one of the two lists.");
            });
        }

    }
}
