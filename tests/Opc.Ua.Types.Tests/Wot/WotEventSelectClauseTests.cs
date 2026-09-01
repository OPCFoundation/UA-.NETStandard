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

using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The materialization rule of WoT Binding Section 6.1: which <c>data</c>
    /// member a select clause fills, and the uniqueness rule stated over that
    /// member rather than over the browse path it was derived from.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotEventSelectClauseTests
    {
        [TestCase("", "ConditionId")]
        [TestCase("Severity", "Severity")]
        [TestCase("pump:Severity", "Severity")]
        [TestCase("nsu=urn:example:pump;Severity", "Severity")]
        [TestCase("{urn:example:pump}Severity", "Severity")]
        [TestCase("EnabledState", "EnabledState.Name")]
        [TestCase("EnabledState/Id", "EnabledState.Id")]
        [TestCase("EnabledState/Name", "EnabledState.Name")]
        [TestCase("pump:Detail/pump:Inner/Value", "Detail.Inner.Value")]
        public void AClauseMaterializesIntoOneDataMember(string browsePath, string expected)
        {
            var clause = new WotEventSelectClause("i=2041", browsePath);

            Assert.That(
                WotEventSelectClauses.FormatMemberPath(clause.MemberPath),
                Is.EqualTo(expected),
                "The namespace qualification says where a field is declared and never what " +
                "the member is called, and a state Variable's own clause fills that " +
                "object's Name member.");
        }

        [Test]
        public void ACompanionStateIsRecognizedFromTheListRatherThanFromOneClause()
        {
            ArrayOf<WotEventSelectClause> clauses =
            [
                new WotEventSelectClause("i=2041", "pump:LatchState"),
                new WotEventSelectClause("i=2041", "pump:LatchState/Id"),
                new WotEventSelectClause("i=2041", "pump:Detail/Value")
            ];

            ArrayOf<ArrayOf<string>> members =
                WotEventSelectClauses.GetMaterializedMemberPaths(clauses);

            Assert.Multiple(() =>
            {
                Assert.That(
                    WotEventSelectClauses.FormatMemberPath(members[0]),
                    Is.EqualTo("LatchState.Name"),
                    "A field another clause of the same list reaches through is an object, " +
                    "so the clause naming the field supplies that object's Name member.");
                Assert.That(
                    WotEventSelectClauses.FormatMemberPath(members[1]),
                    Is.EqualTo("LatchState.Id"));
                Assert.That(
                    WotEventSelectClauses.FormatMemberPath(members[2]),
                    Is.EqualTo("Detail.Value"),
                    "A field no clause selects on its own stays a plain nested member.");
                Assert.That(
                    WotEventSelectClauses.FormatMemberPath(clauses[0].MemberPath),
                    Is.EqualTo("LatchState"),
                    "One clause on its own cannot know the list, so the per-clause member " +
                    "path names only what this Binding declares.");
            });
        }

        [Test]
        public void ANestedListWhoseClausesReachDistinctMembersParses()
        {
            Assert.That(
                TryParse(
                    "[{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"\"}," +
                    "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"EnabledState\"}," +
                    "{\"uav:typeDefinitionId\":\"i=2782\"," +
                    "\"uav:browsePath\":\"EnabledState/Id\"}," +
                    "{\"uav:typeDefinitionId\":\"i=2041\"," +
                    "\"uav:browsePath\":\"pump:Detail/pump:Inner/Value\"}]",
                    out ArrayOf<WotEventSelectClause> clauses,
                    out string error),
                Is.True,
                error);
            Assert.That(clauses.Count, Is.EqualTo(4));
        }

        [TestCase(
            "[{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"EnabledState\"}," +
            "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"EnabledState/Name\"}]",
            "EnabledState.Name",
            TestName = "AStateVariableAndItsNameMemberCollide")]
        [TestCase(
            "[{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}," +
            "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"pump:Severity\"}]",
            "Severity",
            TestName = "ABareAndAQualifiedNameCollide")]
        [TestCase(
            "[{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"pump:LatchState\"}," +
            "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"pump:LatchState/Id\"}," +
            "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"pump:LatchState/Name\"}]",
            "LatchState.Name",
            TestName = "ACompanionStateAndItsNameMemberCollide")]
        [TestCase(
            "[{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"\"}," +
            "{\"uav:typeDefinitionId\":\"i=2881\",\"uav:browsePath\":\"\"}]",
            "ConditionId",
            TestName = "TwoEmptyPathsCollideOnConditionId")]
        [TestCase(
            "[{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}," +
            "{\"uav:typeDefinitionId\":\"i=2782\",\"uav:browsePath\":\"Severity\"}]",
            "Severity",
            TestName = "TwoEventTypesCollideOnOneMember")]
        public void TwoClausesThatMaterializeOneMemberAreRejected(string json, string member)
        {
            Assert.That(
                TryParse(json, out _, out string error),
                Is.False,
                "Section 6.1 gives a data member exactly one clause.");
            Assert.That(error, Does.Contain(member));
            Assert.That(error, Does.Contain("materialized member path"));
        }

        [Test]
        public void TheSameClauseTwiceIsReportedAsARepeatRatherThanAsACollision()
        {
            Assert.That(
                TryParse(
                    "[{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}," +
                    "{\"uav:typeDefinitionId\":\"i=2041\",\"uav:browsePath\":\"Severity\"}]",
                    out _,
                    out string error),
                Is.False);
            Assert.That(error, Does.Contain("appears twice"));
        }

        /// <summary>
        /// The documented default list is a process-wide shared value that
        /// every planner and channel reads. A member computed on first use
        /// would be written by whichever thread reached it first, which is a
        /// data race on a multiword <see cref="ArrayOf{T}"/> field.
        /// </summary>
        [Test]
        public void TheDefaultClausesCarryNoLazilyWrittenState()
        {
            FieldInfo[] fields = typeof(WotEventSelectClause)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(fields, Is.Not.Empty);
            foreach (FieldInfo field in fields)
            {
                Assert.That(
                    field.IsInitOnly,
                    Is.True,
                    $"'{field.Name}' can be written after construction. A clause is shared " +
                    "across threads, so every value it exposes is computed while it is " +
                    "being constructed and never afterwards.");
            }
        }

        [Test]
        public void TheDefaultClausesReadTheSameMemberPathFromEveryThread()
        {
            ArrayOf<WotEventSelectClause> clauses = WotEventSelectClauses.Default;
            var observed = new string[64][];

            Parallel.For(0, observed.Length, ii =>
            {
                var seen = new List<string>(clauses.Count);
                for (int jj = 0; jj < clauses.Count; jj++)
                {
                    ArrayOf<string> memberPath = clauses[jj].MemberPath;
                    Assert.That(memberPath.IsNull, Is.False);
                    seen.Add(WotEventSelectClauses.FormatMemberPath(memberPath));
                }
                observed[ii] = seen.ToArray();
            });

            for (int ii = 1; ii < observed.Length; ii++)
            {
                Assert.That(observed[ii], Is.EqualTo(observed[0]).AsCollection);
            }
            Assert.That(observed[0], Is.EqualTo(FieldNames()).AsCollection);
        }

        private static string[] FieldNames()
        {
            var names = new string[WotEventSelectClauses.DefaultFieldNames.Count];
            for (int ii = 0; ii < names.Length; ii++)
            {
                names[ii] = WotEventSelectClauses.DefaultFieldNames[ii];
            }
            return names;
        }

        private static bool TryParse(
            string json, out ArrayOf<WotEventSelectClause> clauses, out string error)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return WotEventSelectClauses.TryParse(
                document.RootElement, out clauses, out error, out _);
        }
    }
}
