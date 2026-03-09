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
    /// <summary>
    /// Coverage-driven tests for <see cref="QualifiedName"/> and
    /// <see cref="SerializableQualifiedName"/>.
    /// </summary>
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
            // Covers lines 129-132: CompareTo(object) dispatches to CompareTo(string)
            var qn = new QualifiedName("Alpha");
            int result = qn.CompareTo((object)"Beta");
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void CompareToObjectWithQualifiedName()
        {
            // Covers lines 129-130, 133: CompareTo(object) dispatches to CompareTo(QualifiedName)
            var qn1 = new QualifiedName("Alpha", 1);
            var qn2 = new QualifiedName("Beta", 2);
            int result = qn1.CompareTo((object)qn2);
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void CompareToObjectWithUnrelatedType()
        {
            // Covers lines 129-130, 134-135: default arm returns -1
            var qn = new QualifiedName("Test");
            int result = qn.CompareTo((object)42);
            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void CompareToQualifiedNameDifferentNamespaceIndex()
        {
            // Covers lines 140-143: branch where namespace indices differ
            var qn1 = new QualifiedName("Same", 1);
            var qn2 = new QualifiedName("Same", 5);
            int result = qn1.CompareTo(qn2);
            Assert.That(result, Is.LessThan(0));
        }

        [Test]
        public void CompareToQualifiedNameSameNamespaceIndex()
        {
            // Covers lines 140-141, 146-147: same namespace, compares names
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
            Assert.That(qn1.CompareTo(qn2), Is.EqualTo(0));
        }

        [Test]
        public void OperatorGreaterThanQualifiedName()
        {
            // Covers lines 151-153
            var qn1 = new QualifiedName("Beta", 1);
            var qn2 = new QualifiedName("Alpha", 1);
            Assert.That(qn1 > qn2, Is.True);
            Assert.That(qn2 > qn1, Is.False);
        }

        [Test]
        public void OperatorLessThanQualifiedName()
        {
            // Covers lines 157-159
            var qn1 = new QualifiedName("Alpha", 1);
            var qn2 = new QualifiedName("Beta", 1);
            Assert.That(qn1 < qn2, Is.True);
            Assert.That(qn2 < qn1, Is.False);
        }

        [Test]
        public void OperatorLessThanOrEqualQualifiedName()
        {
            // Covers lines 163-165
            var qn1 = new QualifiedName("Alpha", 1);
            var qn2 = new QualifiedName("Beta", 1);
            var qn3 = new QualifiedName("Alpha", 1);
            Assert.That(qn1 <= qn2, Is.True);
            Assert.That(qn1 <= qn3, Is.True);
            Assert.That(qn2 <= qn1, Is.False);
        }

        [Test]
        public void OperatorGreaterThanOrEqualQualifiedName()
        {
            // Covers lines 169-171
            var qn1 = new QualifiedName("Beta", 1);
            var qn2 = new QualifiedName("Alpha", 1);
            var qn3 = new QualifiedName("Beta", 1);
            Assert.That(qn1 >= qn2, Is.True);
            Assert.That(qn1 >= qn3, Is.True);
            Assert.That(qn2 >= qn1, Is.False);
        }

        [Test]
        public void CompareToStringWithNonZeroNamespaceIndex()
        {
            // Covers lines 175-178: NamespaceIndex != 0 returns -1
            var qn = new QualifiedName("Test", 5);
            Assert.That(qn.CompareTo("Test"), Is.EqualTo(-1));
        }

        [Test]
        public void CompareToStringWithZeroNamespaceIndex()
        {
            // Covers lines 175-176, 181-182: ordinal comparison of Name
            var qn = new QualifiedName("Alpha");
            Assert.That(qn.CompareTo("Beta"), Is.LessThan(0));
            Assert.That(qn.CompareTo("Alpha"), Is.EqualTo(0));
            Assert.That(qn.CompareTo("AAAAAA"), Is.GreaterThan(0));
        }

        [Test]
        public void OperatorGreaterThanString()
        {
            // Covers lines 186-188
            var qn = new QualifiedName("Beta");
            Assert.That(qn > "Alpha", Is.True);
            Assert.That(qn > "Gamma", Is.False);
        }

        [Test]
        public void OperatorLessThanString()
        {
            // Covers lines 192-194
            var qn = new QualifiedName("Alpha");
            Assert.That(qn < "Beta", Is.True);
            Assert.That(qn < "AAAAAA", Is.False);
        }

        [Test]
        public void OperatorLessThanOrEqualString()
        {
            // Covers lines 198-200
            var qn = new QualifiedName("Alpha");
            Assert.That(qn <= "Beta", Is.True);
            Assert.That(qn <= "Alpha", Is.True);
            Assert.That(qn <= "AAAAAA", Is.False);
        }

        [Test]
        public void OperatorGreaterThanOrEqualString()
        {
            // Covers lines 204-206
            var qn = new QualifiedName("Beta");
            Assert.That(qn >= "Alpha", Is.True);
            Assert.That(qn >= "Beta", Is.True);
            Assert.That(qn >= "Gamma", Is.False);
        }

        [Test]
        public void EqualsObjectWithString()
        {
            // Covers lines 210-213: dispatches to Equals(string)
            var qn = new QualifiedName("Hello");
            Assert.That(qn.Equals((object)"Hello"), Is.True);
            Assert.That(qn.Equals((object)"World"), Is.False);
        }

        [Test]
        public void EqualsObjectWithQualifiedName()
        {
            // Covers lines 210-211, 214: dispatches to Equals(QualifiedName)
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn.Equals((object)new QualifiedName("Hello", 1)), Is.True);
            Assert.That(qn.Equals((object)new QualifiedName("Hello", 2)), Is.False);
        }

        [Test]
        public void EqualsObjectWithUnrelatedType()
        {
            // Covers lines 210-211, 215-216: default arm calls base.Equals
            var qn = new QualifiedName("Test");
            Assert.That(qn.Equals((object)42), Is.False);
        }

        [Test]
        public void EqualsObjectWithNull()
        {
            // Covers lines 210-211, 215-216: null falls to default arm
            var qn = new QualifiedName("Test");
            Assert.That(qn.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsStringWithNonZeroNamespaceIndex()
        {
            // Covers lines 243-246: NamespaceIndex != 0 returns false
            var qn = new QualifiedName("Hello", 5);
            Assert.That(qn.Equals("Hello"), Is.False);
        }

        [Test]
        public void EqualsStringWithZeroNamespaceIndex()
        {
            // Covers lines 243-244, 248-249: compares Name
            var qn = new QualifiedName("Hello");
            Assert.That(qn.Equals("Hello"), Is.True);
            Assert.That(qn.Equals("World"), Is.False);
        }

        [Test]
        public void OperatorEqualityQualifiedNameString()
        {
            // Covers lines 253-255
            var qn = new QualifiedName("Hello");
            Assert.That(qn == "Hello", Is.True);
            Assert.That(qn == "World", Is.False);
        }

        [Test]
        public void OperatorInequalityQualifiedNameString()
        {
            // Covers lines 259-261
            var qn = new QualifiedName("Hello");
            Assert.That(qn != "World", Is.True);
            Assert.That(qn != "Hello", Is.False);
        }

        [Test]
        public void ToStringWithInvalidFormatThrows()
        {
            // Covers lines 312-313: non-null format throws FormatException
            var qn = new QualifiedName("Test", 1);
            Assert.That(
                () => qn.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void CreateWithNullNameReturnsNull()
        {
            // Covers lines 326-328: empty name returns Null
            var table = new NamespaceTable();
            var result = QualifiedName.Create(null, "http://test.org/", table);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void CreateWithEmptyNameReturnsNull()
        {
            // Covers lines 326-328: empty name returns Null
            var table = new NamespaceTable();
            var result = QualifiedName.Create("", "http://test.org/", table);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void CreateWithNullNamespaceUriReturnsDefaultNamespace()
        {
            // Covers lines 332-334: empty namespace URI returns ns 0
            var table = new NamespaceTable();
            var result = QualifiedName.Create("MyName", null, table);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void CreateWithValidNamespaceUriReturnsCorrectIndex()
        {
            // Covers lines 338-356: normal path with known namespace
            var table = new NamespaceTable();
            table.Append("http://test.org/");
            var result = QualifiedName.Create("MyName", "http://test.org/", table);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void CreateWithUnknownNamespaceUriThrows()
        {
            // Covers lines 346-351: namespace not in table throws
            var table = new NamespaceTable();
            Assert.That(
                () => QualifiedName.Create("MyName", "http://unknown.org/", table),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void CreateWithNullTableAndNonEmptyNamespaceThrows()
        {
            // Covers lines 340-351: null table means index stays -1, so it throws
            Assert.That(
                () => QualifiedName.Create("MyName", "http://test.org/", null),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void IsValidWithEmptyNameReturnsFalse()
        {
            // Covers lines 366-369: empty name is invalid
            Assert.That(QualifiedName.IsValid(new QualifiedName(null), null), Is.False);
            Assert.That(QualifiedName.IsValid(new QualifiedName(""), null), Is.False);
        }

        [Test]
        public void IsValidWithValidNameAndNullTableReturnsTrue()
        {
            // Covers lines 366-367, 372, 378-379: valid name, no table check
            Assert.That(QualifiedName.IsValid(new QualifiedName("Test"), null), Is.True);
        }

        [Test]
        public void IsValidWithNamespaceNotInTableReturnsFalse()
        {
            // Covers lines 372-375: namespace index not in table
            var table = new NamespaceTable();
            var qn = new QualifiedName("Test", 999);
            Assert.That(QualifiedName.IsValid(qn, table), Is.False);
        }

        [Test]
        public void IsValidWithValidNameAndValidNamespaceReturnsTrue()
        {
            // Covers lines 366-367, 372-373, 378-379
            var table = new NamespaceTable();
            table.Append("http://test.org/");
            var qn = new QualifiedName("Test", 1);
            Assert.That(QualifiedName.IsValid(qn, table), Is.True);
        }

        [Test]
        public void ParseEmptyStringReturnsNull()
        {
            // Covers lines 390-392: empty string returns Null
            Assert.That(QualifiedName.Parse(null).IsNull, Is.True);
            Assert.That(QualifiedName.Parse("").IsNull, Is.True);
        }

        [Test]
        public void ParseContextEmptyStringReturnsNull()
        {
            // Covers lines 421-423: empty text returns Null
            ServiceMessageContext context = CreateContext();
            Assert.That(QualifiedName.Parse(context, null, false).IsNull, Is.True);
            Assert.That(QualifiedName.Parse(context, "", false).IsNull, Is.True);
        }

        [Test]
        public void ParseContextNsuWithUpdateTables()
        {
            // Covers lines 426-452: nsu= prefix with updateTables=true
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "nsu=http://new.org/;MyName", true);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.GreaterThan(0));
        }

        [Test]
        public void ParseContextNsuWithoutUpdateTablesKnownUri()
        {
            // Covers lines 440-443: nsu= prefix, updateTables=false, known URI
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("http://known.org/");
            var result = QualifiedName.Parse(context, "nsu=http://known.org/;MyName", false);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseContextNsuWithoutUpdateTablesUnknownUriThrows()
        {
            // Covers lines 445-449: nsu= prefix, updateTables=false, unknown URI throws
            ServiceMessageContext context = CreateContext();
            Assert.That(
                () => QualifiedName.Parse(context, "nsu=http://unknown.org/;MyName", false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseContextNsuMissingSemicolonThrows()
        {
            // Covers lines 430-437: nsu= prefix without semicolon throws
            ServiceMessageContext context = CreateContext();
            Assert.That(
                () => QualifiedName.Parse(context, "nsu=http://test.org/NoSemicolon", false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseContextNumericNamespaceIndex()
        {
            // Covers lines 455-462, 472, 475-476: numeric index prefix
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "3:MyName", false);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void ParseContextInvalidNamespaceIndexThrows()
        {
            // Covers lines 455-468: non-numeric prefix that is not nsu= throws
            ServiceMessageContext context = CreateContext();
            Assert.That(
                () => QualifiedName.Parse(context, "abc:MyName", false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseContextNoColonUsesDefaultNamespace()
        {
            // Covers lines 455-456, 458, 470, 472, 475-476: no colon, index stays 0
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "SimpleName", false);
            Assert.That(result.Name, Is.EqualTo("SimpleName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void FormatWithEmptyNameReturnsEmpty()
        {
            // Covers lines 487-489: empty name returns empty string
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName(null, 5);
            Assert.That(qn.Format(context), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithNamespaceUriResolvable()
        {
            // Covers lines 496-504: useNamespaceUri with resolvable URI
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
            // Covers lines 496, 499, 506-508: useNamespaceUri but URI not found, falls back to index
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName("MyName", 5);
            string result = qn.Format(context, useNamespaceUri: true);
            Assert.That(result, Is.EqualTo("5:MyName"));
        }

        [Test]
        public void FormatWithoutNamespaceUri()
        {
            // Covers lines 511-513: useNamespaceUri=false, NamespaceIndex > 0
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName("MyName", 3);
            string result = qn.Format(context, useNamespaceUri: false);
            Assert.That(result, Is.EqualTo("3:MyName"));
        }

        [Test]
        public void FormatNamespaceIndexZero()
        {
            // Covers the NamespaceIndex == 0 path, just name
            ServiceMessageContext context = CreateContext();
            var qn = new QualifiedName("SimpleName");
            string result = qn.Format(context);
            Assert.That(result, Is.EqualTo("SimpleName"));
        }

        [Test]
        public void FromNullReturnsNull()
        {
            // Covers lines 526-528: null string returns Null
            Assert.That(QualifiedName.From(null).IsNull, Is.True);
        }

        [Test]
        public void FromEmptyReturnsNull()
        {
            // Covers lines 526-528: empty string returns Null
            Assert.That(QualifiedName.From("").IsNull, Is.True);
        }

        [Test]
        public void FromValidStringReturnsQualifiedName()
        {
            // Covers lines 530-531
            var result = QualifiedName.From("Test");
            Assert.That(result.Name, Is.EqualTo("Test"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ExplicitOperatorFromString()
        {
            // Covers lines 538-540
            QualifiedName qn = (QualifiedName)"Hello";
            Assert.That(qn.Name, Is.EqualTo("Hello"));
            Assert.That(qn.NamespaceIndex, Is.EqualTo(0));
        }

        [Test]
        public void ExplicitOperatorFromNull()
        {
            QualifiedName qn = (QualifiedName)(string)null;
            Assert.That(qn.IsNull, Is.True);
        }

        [Test]
        public void SerializableQualifiedNameDefaultConstructor()
        {
            // Covers lines 558-561: parameterless constructor
            var sqn = new SerializableQualifiedName();
            Assert.That(sqn.Value.IsNull, Is.True);
        }

        [Test]
        public void CompareToQualifiedNameByNamespaceIndexReversed()
        {
            // Covers the greater-than branch of NamespaceIndex comparison
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
            Assert.That(qn.Equals("Hello"), Is.False);
        }

        [Test]
        public void OperatorEqualityStringNonZeroNamespace()
        {
            // Tests == operator with non-zero namespace
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn == "Hello", Is.False);
        }

        [Test]
        public void OperatorInequalityStringNonZeroNamespace()
        {
            // Tests != operator with non-zero namespace
            var qn = new QualifiedName("Hello", 1);
            Assert.That(qn != "Hello", Is.True);
        }

        [Test]
        public void ParseContextNsuWithEscapedUri()
        {
            // Covers the nsu= parsing with special characters in URI
            ServiceMessageContext context = CreateContext();
            context.NamespaceUris.Append("http://test.org/;special");
            var result = QualifiedName.Parse(context, "nsu=http%3A//test.org/%3Bspecial;MyName", true);
            Assert.That(result.Name, Is.EqualTo("MyName"));
        }

        [Test]
        public void ParseContextZeroColonPrefix()
        {
            // Covers lines 458-462: index > 0 check with "0:Name" (index = 0)
            ServiceMessageContext context = CreateContext();
            var result = QualifiedName.Parse(context, "0:MyName", false);
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(0));
        }

        private static ServiceMessageContext CreateContext()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new ServiceMessageContext();
#pragma warning restore CS0618
        }
    }
}
