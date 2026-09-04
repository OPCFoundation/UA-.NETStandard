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
