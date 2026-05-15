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

#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages

using System;
using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// Tests for the <see cref="CoreUtils"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CoreUtilsTests
    {
        private sealed class CloneableObject : ICloneable
        {
            public int Value { get; set; }

            public object Clone()
            {
                return new CloneableObject { Value = Value };
            }
        }

        [Test]
        public void TimeBaseIs1601January1Utc()
        {
            var expected = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.That(CoreUtils.TimeBase, Is.EqualTo(expected));
            Assert.That(CoreUtils.TimeBase.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void ToOpcUaUniversalTimeMinValueReturnsMinValue()
        {
            DateTime result = CoreUtils.ToOpcUaUniversalTime(DateTime.MinValue);

            Assert.That(result, Is.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void ToOpcUaUniversalTimeMaxValueReturnsMaxValue()
        {
            DateTime result = CoreUtils.ToOpcUaUniversalTime(DateTime.MaxValue);

            Assert.That(result, Is.EqualTo(DateTime.MaxValue));
        }

        [Test]
        public void ToOpcUaUniversalTimeUtcPassesThrough()
        {
            var utcTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

            DateTime result = CoreUtils.ToOpcUaUniversalTime(utcTime);

            Assert.That(result, Is.EqualTo(utcTime));
            Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void ToOpcUaUniversalTimeNonUtcConvertsToUtc()
        {
            var localTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Local);

            DateTime result = CoreUtils.ToOpcUaUniversalTime(localTime);

            Assert.That(result.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(result, Is.EqualTo(localTime.ToUniversalTime()));
        }

        [Test]
        public void EscapeUriNormalStringUnchanged()
        {
            const string uri = "http://example.com/path";

            string result = CoreUtils.EscapeUri(uri);

            Assert.That(result, Is.EqualTo(uri));
        }

        [Test]
        public void EscapeUriEscapesSemicolons()
        {
            const string uri = "http://example.com/a;b;c";

            string result = CoreUtils.EscapeUri(uri);

            Assert.That(result, Is.EqualTo("http://example.com/a%3Bb%3Bc"));
        }

        [Test]
        public void EscapeUriEscapesPercent()
        {
            const string uri = "http://example.com/100%done";

            string result = CoreUtils.EscapeUri(uri);

            Assert.That(result, Is.EqualTo("http://example.com/100%25done"));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void EscapeUriNullOrEmptyOrWhitespaceReturnsEmpty(string uri)
        {
            string result = CoreUtils.EscapeUri(uri);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void UnescapeUriDecodesPercentEncoding()
        {
            const string encoded = "http://example.com/a%3Bb%3Bc";

            string result = CoreUtils.UnescapeUri(encoded);

            Assert.That(result, Is.EqualTo("http://example.com/a;b;c"));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void UnescapeUriNullOrEmptyOrWhitespaceReturnsEmpty(string uri)
        {
            string result = CoreUtils.UnescapeUri(uri);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void UnescapeUriReadOnlySpanOverloadWorks()
        {
            const string encoded = "http://example.com/a%3Bb";
            ReadOnlySpan<char> span = encoded.AsSpan();

            string result = CoreUtils.UnescapeUri(span);

            Assert.That(result, Is.EqualTo("http://example.com/a;b"));
        }

        [Test]
        public void UnescapeUriReadOnlySpanEmptyReturnsEmpty()
        {
            ReadOnlySpan<char> span = [];

            string result = CoreUtils.UnescapeUri(span);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToHexStringNullReturnsEmpty()
        {
            string result = CoreUtils.ToHexString(null);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToHexStringEmptyArrayReturnsEmpty()
        {
            string result = CoreUtils.ToHexString([]);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToHexStringNormalBytes()
        {
            byte[] buffer = [0xDE, 0xAD, 0xBE, 0xEF];

            string result = CoreUtils.ToHexString(buffer);

            Assert.That(result, Is.EqualTo("DEADBEEF"));
        }

        [Test]
        public void ToHexStringInvertEndian()
        {
            byte[] buffer = [0xDE, 0xAD, 0xBE, 0xEF];

            string result = CoreUtils.ToHexString(buffer, invertEndian: true);

            Assert.That(result, Is.EqualTo("EFBEADDE"));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        public void FromHexStringNullOrEmptyReturnsEmptyArray(string text)
        {
            byte[] result = CoreUtils.FromHexString(text);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FromHexStringValidHex()
        {
            byte[] result = CoreUtils.FromHexString("DEADBEEF");

            Assert.That(result, Is.EqualTo(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
        }

        [Test]
        public void FromHexStringWithWhitespace()
        {
            byte[] result = CoreUtils.FromHexString("DE AD BE EF");

            Assert.That(result, Is.EqualTo(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
        }

        [Test]
        public void FromHexStringInvalidCharThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => CoreUtils.FromHexString("ZZZZ"));
        }

        [Test]
        public void FormatSimpleFormatting()
        {
            string result = CoreUtils.Format("Hello {0}, you have {1} items.", "World", 42);

            Assert.That(result, Is.EqualTo("Hello World, you have 42 items."));
        }

        [Test]
        public void CloneStructReturnsCopy()
        {
#pragma warning disable RCS1118 // Mark local variable as const
            int original = 42;
#pragma warning restore RCS1118 // Mark local variable as const

            int cloned = CoreUtils.Clone(in original);

            Assert.That(cloned, Is.EqualTo(42));
        }

        [Test]
        public void CloneStringReturnsSameReference()
        {
            const string original = "test";

            string cloned = CoreUtils.Clone(original);

            Assert.That(cloned, Is.SameAs(original));
        }

        [Test]
        public void CloneStringNullReturnsNull()
        {
            const string original = null;

            string cloned = CoreUtils.Clone(original);

            Assert.That(cloned, Is.Null);
        }

        [Test]
        public void CloneICloneableNullReturnsDefault()
        {
            CloneableObject original = null;

            CloneableObject cloned = CoreUtils.Clone(original);

            Assert.That(cloned, Is.Null);
        }

        [Test]
        public void CloneICloneableNonNullClones()
        {
            var original = new CloneableObject { Value = 99 };

            CloneableObject cloned = CoreUtils.Clone(original);

            Assert.That(cloned, Is.Not.SameAs(original));
            Assert.That(cloned.Value, Is.EqualTo(99));
        }

        [Test]
        public void IsEqualStructEqualValuesReturnsTrue()
        {
            Assert.That(CoreUtils.IsEqual(42, 42), Is.True);
        }

        [Test]
        public void IsEqualStructDifferentValuesReturnsFalse()
        {
            Assert.That(CoreUtils.IsEqual(42, 99), Is.False);
        }

        [Test]
        public void IsEqualClassSameReferenceReturnsTrue()
        {
            const string text = "hello";

            Assert.That(CoreUtils.IsEqual(text, text), Is.True);
        }

        [Test]
        public void IsEqualClassBothNullReturnsTrue()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((string)null, (string)null), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualClassOneNullReturnsFalse()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual("hello", (string)null), Is.False);
            Assert.That(CoreUtils.IsEqual((string)null, "hello"), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualClassDifferentValuesReturnsFalse()
        {
            Assert.That(CoreUtils.IsEqual("hello", "world"), Is.False);
        }

        [Test]
        public void IsEqualClassEqualValuesReturnsTrue()
        {
            Assert.That(CoreUtils.IsEqual("hello", new string("hello".ToCharArray())), Is.True);
        }

        [Test]
        public void IsEqualEnumerableSameReferenceReturnsTrue()
        {
            IEnumerable<int> list = [1, 2, 3];

            Assert.That(CoreUtils.IsEqual(list, list), Is.True);
        }

        [Test]
        public void IsEqualEnumerableBothNullReturnsTrue()
        {
            // ReferenceEquals(null, null) is true, so this returns true
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((IEnumerable<int>)null, (IEnumerable<int>)null), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualEnumerableOneNullReturnsFalse()
        {
            IEnumerable<int> list = [1, 2, 3];

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual(list, (IEnumerable<int>)null), Is.False);
            Assert.That(CoreUtils.IsEqual((IEnumerable<int>)null, list), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualEnumerableEqualSequencesReturnsTrue()
        {
            IEnumerable<int> list1 = [1, 2, 3];
            IEnumerable<int> list2 = [1, 2, 3];

            Assert.That(CoreUtils.IsEqual(list1, list2), Is.True);
        }

        [Test]
        public void IsEqualEnumerableDifferentSequencesReturnsFalse()
        {
            IEnumerable<int> list1 = [1, 2, 3];
            IEnumerable<int> list2 = [4, 5, 6];

            Assert.That(CoreUtils.IsEqual(list1, list2), Is.False);
        }

        [Test]
        public void IsEqualArraySameReferenceReturnsTrue()
        {
            int[] array = [1, 2, 3];

            Assert.That(CoreUtils.IsEqual(array, array), Is.True);
        }

        [Test]
        public void IsEqualArrayBothNullReturnsTrue()
        {
            // ReferenceEquals(null, null) is true, so this returns true
#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((int[])null, (int[])null), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualArrayOneNullReturnsFalse()
        {
            int[] array = [1, 2, 3];

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual(array, (int[])null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((int[])null, array), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualArrayEqualArraysReturnsTrue()
        {
            int[] array1 = [1, 2, 3];
            int[] array2 = [1, 2, 3];

            Assert.That(CoreUtils.IsEqual(array1, array2), Is.True);
        }

        [Test]
        public void IsEqualArrayDifferentArraysReturnsFalse()
        {
            int[] array1 = [1, 2, 3];
            int[] array2 = [4, 5, 6];

            Assert.That(CoreUtils.IsEqual(array1, array2), Is.False);
        }

        [Test]
        public void IsEqualObjectSameReferenceReturnsTrue()
        {
            object obj = new NodeId(1);

            Assert.That(CoreUtils.IsEqual(obj, obj), Is.True);
        }

        [Test]
        public void IsEqualObjectBothNullReturnsTrue()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((object)null, (object)null), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualObjectOneNullReturnsFalse()
        {
            object obj = "hello";

            Assert.That(CoreUtils.IsEqual(obj, null), Is.False);
            Assert.That(CoreUtils.IsEqual(null, obj), Is.False);
        }

        [Test]
        public void IsEqualObjectNodeIdEqual()
        {
            object nodeId1 = new NodeId(42);
            object nodeId2 = new NodeId(42);

            Assert.That(CoreUtils.IsEqual(nodeId1, nodeId2), Is.True);
        }

        [Test]
        public void IsEqualObjectNodeIdNotEqual()
        {
            object nodeId1 = new NodeId(42);
            object nodeId2 = new NodeId(99);

            Assert.That(CoreUtils.IsEqual(nodeId1, nodeId2), Is.False);
        }

        [Test]
        public void IsEqualObjectIEncodeableEqual()
        {
            var field1 = new EnumField { Name = "Test", Value = 1 };
            var field2 = new EnumField { Name = "Test", Value = 1 };

#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((object)field1, (object)field2), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualObjectIEncodeableNotEqual()
        {
            var field1 = new EnumField { Name = "Test", Value = 1 };
            var field2 = new EnumField { Name = "Other", Value = 2 };

#pragma warning disable IDE0004 // Remove Unnecessary Cast
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(CoreUtils.IsEqual((object)field1, (object)field2), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void IsEqualObjectDateTimeEqual()
        {
            object dt1 = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            object dt2 = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

            Assert.That(CoreUtils.IsEqual(dt1, dt2), Is.True);
        }

        [Test]
        public void IsEqualObjectDateTimeNotEqual()
        {
            object dt1 = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            object dt2 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.That(CoreUtils.IsEqual(dt1, dt2), Is.False);
        }

        [Test]
        public void IsEqualObjectArrayEqual()
        {
            object array1 = new int[] { 1, 2, 3 };
            object array2 = new int[] { 1, 2, 3 };

            Assert.That(CoreUtils.IsEqual(array1, array2), Is.True);
        }

        [Test]
        public void IsEqualObjectArrayNotEqual()
        {
            object array1 = new int[] { 1, 2, 3 };
            object array2 = new int[] { 4, 5, 6 };

            Assert.That(CoreUtils.IsEqual(array1, array2), Is.False);
        }

        [Test]
        public void IsEqualObjectArrayDifferentLengthReturnsFalse()
        {
            object array1 = new int[] { 1, 2 };
            object array2 = new int[] { 1, 2, 3 };

            Assert.That(CoreUtils.IsEqual(array1, array2), Is.False);
        }

        [Test]
        public void IsEqualObjectXmlElementEqual()
        {
            var doc1 = new XmlDocument();
#pragma warning disable CA3075 // Insecure DTD processing in XML
            doc1.LoadXml("<root>value</root>");

            var doc2 = new XmlDocument();
            doc2.LoadXml("<root>value</root>");
#pragma warning restore CA3075 // Insecure DTD processing in XML

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(
                CoreUtils.IsEqual(
                    (object)doc1.DocumentElement,
                    (object)doc2.DocumentElement),
                Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }
    }
}
