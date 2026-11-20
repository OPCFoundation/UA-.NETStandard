/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Types.Tests.Wot;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Resolves the clauses of the WoT Binding traceability ledger that are
    /// held in this assembly.
    /// </summary>
    /// <remarks>
    /// No assembly can see another's types, so a clause held here can only be
    /// checked here. Without this fixture the binding-side half of the ledger
    /// would be prose again: names nothing resolves, which decay the first time
    /// a test is renamed.
    /// </remarks>
    [TestFixture]
    public sealed class WotBindingTraceabilityTests
    {
        [Test]
        public void EveryClauseHeldHereResolvesAndWillRun()
        {
            Assembly assembly = typeof(WotBindingTraceabilityTests).Assembly;
            string name = assembly.GetName().Name!;
            List<WotTraceabilityClause> clauses = WotTraceabilityLedger.Load(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.Any(c => string.Equals(
                        c.Assembly, name, StringComparison.Ordinal)),
                    Is.True,
                    "This assembly holds no clause, so the fixture proves nothing.");
                foreach (WotTraceabilityClause clause in clauses
                    .Where(c => string.Equals(c.Assembly, name, StringComparison.Ordinal)))
                {
                    foreach (string test in clause.Tests)
                    {
                        Assert.That(
                            WotTraceabilityLedger.DescribeResolution(assembly, test),
                            Is.EqualTo("runs"),
                            $"Clause {clause.Clause} maps onto '{test}'.");
                    }
                }
            });
        }
    }
}
