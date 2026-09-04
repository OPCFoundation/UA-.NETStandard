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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

#nullable enable

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
    /// The ledger is temporary. The specification publishes no requirement
    /// identifiers yet, so the clause numbers are read from a pinned commit and
    /// the pin is checked against the fixture manifest beside it; when the
    /// specification lands its published revision, re-pin both.
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

        /// <summary>
        /// The clause numbering was read from one revision of the
        /// specification, and so were the vendored examples. If the two pins
        /// disagree, one of them was updated and the other was forgotten.
        /// </summary>
        [Test]
        public void TheLedgerAndTheFixturesArePinnedToTheSameRevision()
        {
            using JsonDocument document = JsonDocument.Parse(WotTraceabilityLedger.Read(Assembly));
            JsonElement pinned = document.RootElement.GetProperty("pinnedTo");
            WotSpecFixtureManifest manifest = WotSpecFixtureManifest.Load();

            Assert.Multiple(() =>
            {
                Assert.That(
                    pinned.GetProperty("commit").GetString(),
                    Is.EqualTo(manifest.Commit));
                Assert.That(
                    pinned.GetProperty("repository").GetString(),
                    Is.EqualTo(manifest.Repository));
                Assert.That(
                    pinned.GetProperty("bindingRevision").GetString(),
                    Is.EqualTo(Opc.Ua.Wot.WotBindingConformance.CurrentRevision));
            });
        }
    }
}
