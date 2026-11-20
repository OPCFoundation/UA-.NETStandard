// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Types.Tests.Wot;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Resolves the MCP WoT Binding traceability entries held in this assembly.
    /// </summary>
    [TestFixture]
    public sealed class WotBindingTraceabilityTests
    {
        /// <summary>
        /// Verifies that every mapped test in this assembly exists and runs normally.
        /// </summary>
        [Test]
        public void EveryClauseHeldHereResolvesAndWillRun()
        {
            Assembly assembly = typeof(WotBindingTraceabilityTests).Assembly;
            string name = assembly.GetName().Name!;
            List<WotTraceabilityClause> clauses = WotTraceabilityLedger.Load(assembly);

            Assert.Multiple(() =>
            {
                Assert.That(
                    clauses.Any(c => string.Equals(c.Assembly, name, StringComparison.Ordinal)),
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
#endif
