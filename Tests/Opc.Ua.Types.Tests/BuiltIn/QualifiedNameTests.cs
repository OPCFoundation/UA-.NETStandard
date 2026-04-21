/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class QualifiedNameTests
    {
        [Test]
        public void CompareToObjectWithString()
        {
            var qn = new QualifiedName("Alpha");
            int result = qn.CompareTo((object)"Beta");
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void CompareToObjectWithQualifiedName()
        {
            var qn1 = new QualifiedName("Alpha", 1);
            var qn2 = new QualifiedName("Beta", 2);
            int result = qn1.CompareTo((object)qn2);
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void CompareToObjectWithUnrelatedType()
        {
            var qn = new QualifiedName("Test");
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            int result = qn.CompareTo((object)42);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void CompareToQualifiedNameDifferentNamespaceIndex()
        {
            var qn1 = new QualifiedName("Same", 1);
            var qn2 = new QualifiedName("Same", 5);
            int result = qn1.CompareTo(qn2);
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void CompareToQualifiedNameSameNamespaceIndex()
        {
            var qn1 = new QualifiedName("Alpha", 3);
            var qn2 = new QualifiedName("Beta", 3);
            Assert.That(qn1.CompareTo(qn2), Is.LessThan(0));
            Assert.That(qn2.CompareTo(qn1), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToQualifiedNameEqual()
        {
            var qn1 = new QualifiedName("Same", 3);
            var qn2 = new QualifiedName("Same", 3);
            Assert.That(qn1.CompareTo(qn2), Is.Zero);
        }

        [Test]
        public void OperatorGreaterThanQualifiedName()
        {
            var qn1 = new QualifiedName("Beta", 1);
            var qn2 = new QualifiedName("Alpha", 1);
            Assert.That(qn1, Is.GreaterThan(qn2));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn2 > qn1, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void OperatorLessThanQualifiedName()
        {
            var qn1 = new QualifiedName("Alpha", 1);
            var qn2 = new QualifiedName("Beta", 1);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn1 < qn2, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn2 < qn1, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void OperatorLessThanOrEqualQualifiedName()
        {
            var qn1 = new QualifiedName("Alpha", 1);
            var qn2 = new QualifiedName("Beta", 1);
            var qn3 = new QualifiedName("Alpha", 1);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn1 <= qn2, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn1 <= qn3, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn2, Is.GreaterThan(qn1));
        }

        [Test]
        public void OperatorGreaterThanOrEqualQualifiedName()
        {
            var qn1 = new QualifiedName("Beta", 1);
            var qn2 = new QualifiedName("Alpha", 1);
            var qn3 = new QualifiedName("Beta", 1);
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn1 >= qn2, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn1 >= qn3, Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn2 >= qn1, Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void CompareToStringWithNonZeroNamespaceIndex()
        {
            var qn = new QualifiedName("Test", 5);
            Assert.That(qn.CompareTo("Test"), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToStringWithZeroNamespaceIndex()
        {
            var qn = new QualifiedName("Alpha");
            Assert.That(qn.CompareTo("Beta"), Is.LessThan(0));
            Assert.That(qn.CompareTo("Alpha"), Is.Zero);
            Assert.That(qn.CompareTo("AAAAAA"), Is.GreaterThan(0));
        }

        [Test]
        public void OperatorGreaterThanString()
        {
            var qn = new QualifiedName("Beta");
            Assert.That(qn, Is.GreaterThan("Alpha"));
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn > "Gamma", Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void OperatorLessThanString()
        {
            var qn = new QualifiedName("Alpha");
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn < "Beta", Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn < "AAAAAA", Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void OperatorLessThanOrEqualString()
        {
            var qn = new QualifiedName("Alpha");
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn <= "Beta", Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn <= "Alpha", Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn, Is.GreaterThan("AAAAAA"));
        }

        [Test]
        public void OperatorGreaterThanOrEqualString()
        {
            var qn = new QualifiedName("Beta");
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn >= "Alpha", Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn >= "Beta", Is.True);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
#pragma warning disable NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
            Assert.That(qn >= "Gamma", Is.False);
#pragma warning restore NUnit2043 // Use ComparisonConstraint for better assertion messages in case of failure
        }

        [Test]
        public void EqualsObjectWithString()
        {
            var qn = new QualifiedName("Hello");
            Assert.That(qn, Is.EqualTo((object)"Hello"));
            Assert.That(qn, Is.Not.EqualTo((object)"World"));
        }

        [Test]
        public void EqualsObjectWithQualifiedName()
        {
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn, Is.EqualTo((object)new QualifiedName("Hello", 1)));
            Assert.That(qn, Is.Not.EqualTo((object)new QualifiedName("Hello", 2)));
        }

        [Test]
        public void EqualsObjectWithUnrelatedType()
        {
            var qn = new QualifiedName("Test");
            Assert.That(qn, Is.Not.EqualTo((object)42));
        }

        [Test]
        public void EqualsObjectWithNull()
        {
            var qn = new QualifiedName("Test");
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(qn, Is.Not.EqualTo((object)null));
#pragma warning restore NUnit4002 // Use Specific constraint
        }

        [Test]
        public void EqualsStringWithNonZeroNamespaceIndex()
        {
            var qn = new QualifiedName("Hello", 5);
            Assert.That(qn, Is.Not.EqualTo("Hello"));
        }

        [Test]
        public void EqualsStringWithZeroNamespaceIndex()
        {
            var qn = new QualifiedName("Hello");
            Assert.That(qn, Is.EqualTo("Hello"));
            Assert.That(qn, Is.Not.EqualTo("World"));
        }

        [Test]
        public void OperatorEqualityQualifiedNameString()
        {
            var qn = new QualifiedName("Hello");
            Assert.That(qn, Is.EqualTo("Hello"));
            Assert.That(qn, Is.Not.EqualTo("World"));
        }

        [Test]
        public void OperatorInequalityQualifiedNameString()
        {
            var qn = new QualifiedName("Hello");
            Assert.That(qn, Is.Not.EqualTo("World"));
            Assert.That(qn, Is.EqualTo("Hello"));
        }

        [Test]
        public void ToStringWithInvalidFormatThrows()
        {
            var qn = new QualifiedName("Test", 1);
            Assert.That(
                () => qn.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void CreateWithNullNameReturnsNull()
        {
            var table = new NamespaceTable();
            var result = QualifiedName.Create(null, "http://test.org/", table);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void CreateWithEmptyNameReturnsNull()
        {
            var table = new NamespaceTable();
            var result = QualifiedName.Create(string.Empty, "http://test.org/", table);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void CreateWithNullNamespaceUriReturnsDefaultNamespace()
        {
            var table = new NamespaceTable();
            var result = QualifiedName.Create("MyName", null, table);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void CreateWithValidNamespaceUriReturnsCorrectIndex()
        {
            var table = new NamespaceTable();
            table.Append("http://test.org/");
            var result = QualifiedName.Create("MyName", "http://test.org/", table);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void CreateWithUnknownNamespaceUriThrows()
        {
            var table = new NamespaceTable();
            Assert.That(
                () => QualifiedName.Create("MyName", "http://unknown.org/", table),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void CreateWithNullTableAndNonEmptyNamespaceThrows()
        {
            Assert.That(
    () => QualifiedName.Create("MyName", "http://test.org/", null),
    Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void IsValidWithEmptyNameReturnsFalse()
        {
            Assert.That(QualifiedName.IsValid(new QualifiedName(null), null), Is.False);
            Assert.That(QualifiedName.IsValid(new QualifiedName(string.Empty), null), Is.False);
        }

        [Test]
        public void IsValidWithValidNameAndNullTableReturnsTrue()
        {
            Assert.That(QualifiedName.IsValid(new QualifiedName("Test"), null), Is.True);
        }

        [Test]
        public void IsValidWithNamespaceNotInTableReturnsFalse()
        {
            var table = new NamespaceTable();
            var qn = new QualifiedName("Test", 999);
            Assert.That(QualifiedName.IsValid(qn, table), Is.False);
        }

        [Test]
        public void IsValidWithValidNameAndValidNamespaceReturnsTrue()
        {
            var table = new NamespaceTable();
            table.Append("http://test.org/");
            var qn = new QualifiedName("Test", 1);
            Assert.That(QualifiedName.IsValid(qn, table), Is.True);
        }

        [Test]
        public void ParseEmptyStringReturnsNull()
        {
            Assert.That(QualifiedName.Parse(null).IsNull, Is.True);
            Assert.That(QualifiedName.Parse(string.Empty).IsNull, Is.True);
        }

        [Test]
        public void ParseContextEmptyStringReturnsNull()
        {
            ServiceMessageContext context = CreateContext();
            Assert.That(QualifiedName.Parse(context, null, false).IsNull, Is.True);
            Assert.That(QualifiedName.Parse(context, string.Empty, false).IsNull, Is.True);
        }

        [Test]
        public void ParseContextNsuWithUpdateTables()
        {
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "nsu=http://new.org/;MyName", true);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.GreaterThan(0));
        }

        [Test]
        public void ParseContextNsuWithoutUpdateTablesKnownUri()
        {
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("http://known.org/");
            var result = QualifiedName.Parse(context, "nsu=http://known.org/;MyName", false);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseContextNsuWithoutUpdateTablesUnknownUriThrows()
        {
            ServiceMessageContext context = CreateContext();
            Assert.That(
                () => QualifiedName.Parse(context, "nsu=http://unknown.org/;MyName", false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseContextNsuMissingSemicolonThrows()
        {
            ServiceMessageContext context = CreateContext();
            Assert.That(
                () => QualifiedName.Parse(context, "nsu=http://test.org/NoSemicolon", false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseContextNumericNamespaceIndex()
        {
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "3:MyName", false);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ParseContextInvalidNamespaceIndexThrows()
        {
            ServiceMessageContext context = CreateContext();
            Assert.That(
                () => QualifiedName.Parse(context, "abc:MyName", false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseContextNoColonUsesDefaultNamespace()
        {
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "SimpleName", false);
            Assert.That(result.Name, Is.EqualTo("SimpleName"));
            Assert.That(result.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void FormatWithEmptyNameReturnsEmpty()
        {
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName(null, 5);
            Assert.That(qn.Format(context), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithNamespaceUriResolvable()
        {
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("http://test.org/");
            var qn = new QualifiedName("MyName", 1);
            string result = qn.Format(context, useNamespaceUri: true);
            Assert.That(result, Does.StartWith("nsu="));
            Assert.That(result, Does.Contain("http://test.org/"));
            Assert.That(result, Does.EndWith("MyName"));
        }

        [Test]
        public void FormatWithNamespaceUriNotResolvable()
        {
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName("MyName", 5);
            string result = qn.Format(context, useNamespaceUri: true);
            Assert.That(result, Is.EqualTo("5:MyName"));
        }

        [Test]
        public void FormatWithoutNamespaceUri()
        {
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName("MyName", 3);
            string result = qn.Format(context, useNamespaceUri: false);
            Assert.That(result, Is.EqualTo("3:MyName"));
        }

        [Test]
        public void FormatNamespaceIndexZero()
        {
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName("SimpleName");
            string result = qn.Format(context);
            Assert.That(result, Is.EqualTo("SimpleName"));
        }

        [Test]
        public void FromNullReturnsNull()
        {
            Assert.That(QualifiedName.From(null).IsNull, Is.True);
        }

        [Test]
        public void FromEmptyReturnsNull()
        {
            Assert.That(QualifiedName.From(string.Empty).IsNull, Is.True);
        }

        [Test]
        public void FromValidStringReturnsQualifiedName()
        {
            var result = QualifiedName.From("Test");
            Assert.That(result.Name, Is.EqualTo("Test"));
            Assert.That(result.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ExplicitOperatorFromString()
        {
            var qn = (QualifiedName)"Hello";
            Assert.That(qn.Name, Is.EqualTo("Hello"));
            Assert.That(qn.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ExplicitOperatorFromNull()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            var qn = (QualifiedName)(string)null;
#pragma warning restore IDE0004 // Remove Unnecessary Cast
            Assert.That(qn.IsNull, Is.True);
        }

        [Test]
        public void SerializableQualifiedNameDefaultConstructor()
        {
            var sqn = new SerializableQualifiedName();
            Assert.That(sqn.Value.IsNull, Is.True);
        }

        [Test]
        public void CompareToQualifiedNameByNamespaceIndexReversed()
        {
            var qn1 = new QualifiedName("Same", 10);
            var qn2 = new QualifiedName("Same", 1);
            Assert.That(qn1.CompareTo(qn2), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToStringNonZeroNamespaceAlwaysReturnsMinusOne()
        {
            // Ensures NamespaceIndex != 0 always returns -1 regardless of name
            var qn = new QualifiedName("ZZZZ", 1);
            Assert.That(qn.CompareTo("AAAA"), Is.EqualTo(-1));
        }

        [Test]
        public void EqualsStringNonZeroNamespaceAlwaysReturnsFalse()
        {
            // Ensures NamespaceIndex != 0 always returns false
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn, Is.Not.EqualTo("Hello"));
        }

        [Test]
        public void OperatorEqualityStringNonZeroNamespace()
        {
            // Tests == operator with non-zero namespace
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn, Is.Not.EqualTo("Hello"));
        }

        [Test]
        public void OperatorInequalityStringNonZeroNamespace()
        {
            // Tests != operator with non-zero namespace
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn, Is.Not.EqualTo("Hello"));
        }

        [Test]
        public void ParseContextNsuWithEscapedUri()
        {
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("http://test.org/;special");
            var result = QualifiedName.Parse(context, "nsu=http%3A//test.org/%3Bspecial;MyName", true);
            Assert.That(result.Name, Is.EqualTo("MyName"));
        }

        [Test]
        public void ParseContextZeroColonPrefix()
        {
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "0:MyName", false);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.Zero);
        }

        private static ServiceMessageContext CreateContext()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new ServiceMessageContext();
#pragma warning restore CS0618
        }

        private const string ParseLongFormKnownNamespace= "http://opcfoundation.org/UA/Test/";
        private const string ParseLongFormUnknownNamespace = "http://opcfoundation.org/UA/Unknown/";

        private static NamespaceTable BuildParseLongFormNamespaces()
        {
            var table = new NamespaceTable();
            table.Append(ParseLongFormKnownNamespace);
            return table;
        }

        [Test]
        public void ParseLongFormThrowsWhenTableIsNull()
        {
            Assert.That(
                () => QualifiedName.ParseLongForm("Foo", null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ParseLongFormReturnsNullForNullText()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            QualifiedName result = QualifiedName.ParseLongForm(null, table);
            Assert.That(result, Is.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void ParseLongFormReturnsNullForEmptyText()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            QualifiedName result = QualifiedName.ParseLongForm(string.Empty, table);
            Assert.That(result, Is.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void ParseLongFormBareName()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            QualifiedName result = QualifiedName.ParseLongForm("Foo", table);
            Assert.That(result.Name, Is.EqualTo("Foo"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ParseLongFormNumericPrefixShortcut()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            QualifiedName result = QualifiedName.ParseLongForm("1:Foo", table);
            Assert.That(result.Name, Is.EqualTo("Foo"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseLongFormResolvesKnownNamespaceUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            QualifiedName result = QualifiedName.ParseLongForm(
                $"nsu={ParseLongFormKnownNamespace};Bar", table);
            Assert.That(result.Name, Is.EqualTo("Bar"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseLongFormThrowsForUnresolvedNamespaceUri()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            Assert.That(
                () => QualifiedName.ParseLongForm(
                    $"nsu={ParseLongFormUnknownNamespace};Bar", table),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseLongFormThrowsForMissingSemicolon()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            Assert.That(
                () => QualifiedName.ParseLongForm(
                    $"nsu={ParseLongFormKnownNamespace}Bar", table),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void ParseLongFormDoesNotMutateNamespaceTable()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            int initialCount = table.Count;
            try
            {
                QualifiedName.ParseLongForm($"nsu={ParseLongFormUnknownNamespace};Bar", table);
            }
            catch (ServiceResultException)
            {
            }
            Assert.That(table.Count, Is.EqualTo(initialCount));
        }

        [Test]
        public void ParseLongFormRoundTrip()
        {
            NamespaceTable table = BuildParseLongFormNamespaces();
            ServiceMessageContext context = ServiceMessageContext.CreateEmpty(null);
            context.NamespaceUris = table;

            var original = new QualifiedName("Bar", 1);
            string formatted = original.Format(context, useNamespaceUri: true);
            QualifiedName parsed = QualifiedName.ParseLongForm(formatted, table);

            Assert.That(parsed.Name, Is.EqualTo("Bar"));
            Assert.That(parsed.NamespaceIndex, Is.EqualTo(1));
        }
    }
}
