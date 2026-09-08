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

using System.Linq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// The nested event <c>data</c> object of WoT Binding Sections 6.1 and
    /// 13.3: what a select clause materializes into, and how that differs from
    /// the flat transport-side index a runtime keys by joined browse path.
    /// </summary>
    [TestFixture]
    [Category("Wot")]
    [Parallelizable]
    public sealed class WotEventDataTests
    {
        [Test]
        public void AOneElementPathNamesAMemberOfData()
        {
            var clause = new WotResolvedEventSelectClause("i=2041", "Severity");

            Assert.Multiple(() =>
            {
                Assert.That(clause.MemberPath.ToArray(), Is.EqualTo(s_stringValues1));
                Assert.That(clause.FieldName, Is.EqualTo("Severity"));
            });
        }

        [Test]
        public void ALongerPathNestsOneObjectMemberPerPrecedingElement()
        {
            var clause = new WotResolvedEventSelectClause("i=2782", "EnabledState/Id");

            Assert.Multiple(() =>
            {
                Assert.That(
                    clause.MemberPath.ToArray(),
                    Is.EqualTo(s_stringValues2),
                    "EnabledState/Id materializes data.EnabledState.Id and never a member " +
                    "literally called 'EnabledState/Id'.");
                Assert.That(
                    clause.MemberPath.ToArray()!.Any(
                        member => member.Contains('/', System.StringComparison.Ordinal)),
                    Is.False,
                    "A data member name never contains the path separator.");
            });
        }

        [Test]
        public void AStateVariableSuppliesTheNameMemberOfItsObject()
        {
            var clause = new WotResolvedEventSelectClause("i=2782", "EnabledState");

            Assert.That(
                clause.MemberPath.ToArray(),
                Is.EqualTo(s_stringValues3),
                "A state Variable's own value is the state's localized display text, so " +
                "selecting the field supplies that object's Name member.");
        }

        [Test]
        public void TheEmptyPathMaterializesConditionId()
        {
            var clause = new WotResolvedEventSelectClause("i=2782", string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(clause.IsConditionIdSelection, Is.True);
                Assert.That(clause.MemberPath.ToArray(), Is.EqualTo(s_stringValues4));
                Assert.That(
                    clause.GetNormalizedBrowsePath(),
                    Is.Empty,
                    "The empty path normalizes to itself, and no other clause may take it.");
            });
        }

        [Test]
        public void AMemberNameDropsTheNamespaceQualification()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    new WotResolvedEventSelectClause("i=2041", "pump:Trace").FieldName,
                    Is.EqualTo("Trace"),
                    "A prefix says where the field is declared, not what the member is " +
                    "called.");
                Assert.That(
                    new WotResolvedEventSelectClause(
                        "i=2041", "nsu=urn:example:pump;Trace").FieldName,
                    Is.EqualTo("Trace"));
                Assert.That(
                    new WotResolvedEventSelectClause("i=2041", "{urn:example:pump}Trace").FieldName,
                    Is.EqualTo("Trace"));
            });
        }

        [Test]
        public void TwoPrefixesForOneNamespaceNormalizeToOnePath()
        {
            static string? Resolve(string prefix)
            {
                return prefix is "pump" or "p2" ? "urn:example:pump" : null;
            }

            Assert.That(
                new WotResolvedEventSelectClause("i=2041", "pump:Trace").GetNormalizedBrowsePath(Resolve),
                Is.EqualTo(
                    new WotResolvedEventSelectClause("i=2041", "p2:Trace")
                        .GetNormalizedBrowsePath(Resolve)),
                "Normalization is what makes two paths that name the same elements the same " +
                "path even when their prefixes differ.");
        }

        [Test]
        public void TheDataObjectCarriesTheExactShapeTheBindingDescribes()
        {
            var builder = new WotEventDataBuilder();
            builder.Add(
                new WotResolvedEventSelectClause("i=2041", "Severity").MemberPath, Value(500));
            builder.Add(
                new WotResolvedEventSelectClause("i=2782", "EnabledState").MemberPath, Value("Enabled"));
            builder.Add(
                new WotResolvedEventSelectClause("i=2782", "EnabledState/Id").MemberPath, Value(true));
            builder.Add(
                new WotResolvedEventSelectClause("i=2782", string.Empty).MemberPath, Value("cond-1"));

            WotEventData data = builder.Build();

            Assert.Multiple(() =>
            {
                Assert.That(
                    data.Members.Keys.OrderBy(k => k, System.StringComparer.Ordinal),
                    Is.EqualTo(s_stringValues5));
                Assert.That(data["Severity"]!.HasValue, Is.True);
                Assert.That(data["EnabledState"]!.HasValue, Is.False);
                Assert.That(
                    data["EnabledState"]!.Members.Keys.OrderBy(
                        k => k, System.StringComparer.Ordinal),
                    Is.EqualTo(s_stringValues6));
                Assert.That(
                    data["EnabledState"]!["Name"]!.Value.WrappedValue.TryGetValue(
                        out string? name),
                    Is.True);
                Assert.That(name, Is.EqualTo("Enabled"));
                Assert.That(
                    data["EnabledState"]!["Id"]!.Value.WrappedValue.TryGetValue(out bool id),
                    Is.True);
                Assert.That(id, Is.True);
            });
        }

        [Test]
        public void ACompanionStateSelectedAsAValueBecomesAnObjectWhenAClauseNestsThroughIt()
        {
            var builder = new WotEventDataBuilder();
            builder.Add(
                new WotResolvedEventSelectClause("i=2041", "pump:LatchState").MemberPath,
                Value("Latched"));
            builder.Add(
                new WotResolvedEventSelectClause("i=2041", "pump:LatchState/Id").MemberPath,
                Value(true));

            WotEventData data = builder.Build();

            Assert.Multiple(() =>
            {
                Assert.That(data["LatchState"]!.HasValue, Is.False);
                Assert.That(
                    data["LatchState"]!.Members.Keys.OrderBy(
                        k => k, System.StringComparer.Ordinal),
                    Is.EqualTo(s_stringValues6),
                    "A state this Binding does not name is recognized from the selection " +
                    "itself, whichever order the two clauses appear in.");
            });
        }

        [Test]
        public void ACompanionStateNestedFirstStillTakesTheValueIntoItsNameMember()
        {
            var builder = new WotEventDataBuilder();
            builder.Add(
                new WotResolvedEventSelectClause("i=2041", "pump:LatchState/Id").MemberPath,
                Value(true));
            builder.Add(
                new WotResolvedEventSelectClause("i=2041", "pump:LatchState").MemberPath,
                Value("Latched"));

            WotEventData data = builder.Build();

            Assert.That(
                data["LatchState"]!.Members.Keys.OrderBy(k => k, System.StringComparer.Ordinal),
                Is.EqualTo(s_stringValues6));
        }

        [Test]
        public void ACollisionKeepsTheFirstClauseAndReportsTheSecond()
        {
            var builder = new WotEventDataBuilder();
            Assert.That(
                builder.Add(
                    new WotResolvedEventSelectClause("i=2041", "Severity").MemberPath, Value(100)),
                Is.True);
            Assert.That(
                builder.Add(
                    new WotResolvedEventSelectClause("i=2782", "Severity").MemberPath, Value(900)),
                Is.False,
                "Two clauses that materialize one member compete for it, so the first " +
                "stated clause wins rather than the last quietly replacing it.");

            WotEventData data = builder.Build();

            Assert.That(
                data["Severity"]!.Value.WrappedValue.TryGetValue(out int severity), Is.True);
            Assert.That(severity, Is.EqualTo(100));
        }

        [Test]
        public void ATypeConflictBetweenTwoClausesForOneMemberKeepsTheFirst()
        {
            var builder = new WotEventDataBuilder();
            builder.Add(
                new WotResolvedEventSelectClause("i=2782", "EnabledState/Name").MemberPath,
                Value("Enabled"));

            Assert.That(
                builder.Add(
                    new WotResolvedEventSelectClause("i=2782", "EnabledState").MemberPath,
                    Value("Disabled")),
                Is.False,
                "The state Variable's own value belongs in the Name member, which another " +
                "clause already filled, so the second clause is reported rather than " +
                "silently overwriting the first.");

            WotEventData data = builder.Build();
            Assert.That(
                data["EnabledState"]!["Name"]!.Value.WrappedValue.TryGetValue(out string? name),
                Is.True);
            Assert.That(name, Is.EqualTo("Enabled"));
        }

        [Test]
        public void AValueMemberANestedClauseReachesThroughBecomesThatObjectsName()
        {
            var builder = new WotEventDataBuilder();
            builder.Add(
                new WotResolvedEventSelectClause("i=2782", "EnabledState/Id").MemberPath, Value(true));

            Assert.That(
                builder.Add(
                    new WotResolvedEventSelectClause("i=2782", "EnabledState/Id/Extra").MemberPath,
                    Value("x")),
                Is.True,
                "The rule applies at every level: a field another clause nests through is a " +
                "state Variable, and its own value becomes that object's Name member.");

            WotEventData data = builder.Build();
            Assert.That(
                data["EnabledState"]!["Id"]!.Members.Keys.OrderBy(
                    k => k, System.StringComparer.Ordinal),
                Is.EqualTo(s_stringValues7));
        }

        [Test]
        public void TheDataObjectResolvesAMemberPath()
        {
            var builder = new WotEventDataBuilder();
            var clause = new WotResolvedEventSelectClause("i=2782", "EnabledState/Id");
            builder.Add(clause.MemberPath, Value(true));

            WotEventData data = builder.Build();

            Assert.Multiple(() =>
            {
                Assert.That(data.TryGetValue(clause.MemberPath, out DataValue value), Is.True);
                Assert.That(value.WrappedValue.TryGetValue(out bool flag), Is.True);
                Assert.That(flag, Is.True);
                Assert.That(
                    data.TryGetValue(
                        new WotResolvedEventSelectClause("i=2782", "AckedState").MemberPath, out _),
                    Is.False);
            });
        }

        [Test]
        public void APropertyObserveNotificationCarriesAnEmptyDataObject()
        {
            var notification = new WotNotification(new DataValue(new Variant(1)));

            Assert.Multiple(() =>
            {
                Assert.That(notification.Data, Is.Not.Null);
                Assert.That(notification.Data.Members, Is.Empty);
                Assert.That(notification.EventFields, Is.Empty);
            });
        }

        private static DataValue Value(int value)
        {
            return new DataValue(new Variant(value));
        }

        private static DataValue Value(bool value)
        {
            return new DataValue(new Variant(value));
        }

        private static DataValue Value(string value)
        {
            return new DataValue(new Variant(value));
        }

        private static readonly string[] s_stringValues1 = ["Severity"];
        private static readonly string[] s_stringValues2 = ["EnabledState", "Id"];
        private static readonly string[] s_stringValues3 = ["EnabledState", "Name"];
        private static readonly string[] s_stringValues4 = ["ConditionId"];
        private static readonly string[] s_stringValues5 = ["ConditionId", "EnabledState", "Severity"];
        private static readonly string[] s_stringValues6 = ["Id", "Name"];
        private static readonly string[] s_stringValues7 = ["Extra", "Name"];
    }
}
