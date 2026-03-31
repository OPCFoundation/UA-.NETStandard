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
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.CoreUtilsTests
{
    /// <summary>
    /// Tests for <see cref="CoreUtils"/> utility methods that were not previously covered.
    /// </summary>
    [TestFixture]
    [Category("CoreUtils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CoreUtilsTests
    {
        #region FlattenArray

        /// <summary>
        /// FlattenArray on a 1-D array returns an equivalent flat array.
        /// </summary>
        [Test]
        public void FlattenArray1DReturnsEquivalentArray()
        {
            int[] source = [1, 2, 3, 4];
            Array flat = CoreUtils.FlattenArray(source);
            Assert.That(flat.Length, Is.EqualTo(source.Length));
            for (int i = 0; i < source.Length; i++)
            {
                Assert.That(flat.GetValue(i), Is.EqualTo(source[i]));
            }
        }

        /// <summary>
        /// FlattenArray on a 2-D array flattens all elements.
        /// </summary>
        [Test]
        public void FlattenArray2DReturnsAllElements()
        {
            var source = new int[2, 3] { { 1, 2, 3 }, { 4, 5, 6 } };
            Array flat = CoreUtils.FlattenArray(source);
            Assert.That(flat.Length, Is.EqualTo(6));
        }

        #endregion

        #region Match

        /// <summary>
        /// Match with an empty pattern always returns true.
        /// </summary>
        [Test]
        public void MatchEmptyPatternAlwaysReturnsTrue()
        {
            Assert.That(CoreUtils.Match("anything", string.Empty, true), Is.True);
            Assert.That(CoreUtils.Match("anything", null, true), Is.True);
        }

        /// <summary>
        /// Match with an empty target and non-empty pattern returns false.
        /// </summary>
        [Test]
        public void MatchEmptyTargetReturnsFalse()
        {
            Assert.That(CoreUtils.Match(string.Empty, "pattern", true), Is.False);
            Assert.That(CoreUtils.Match(null, "pattern", true), Is.False);
        }

        /// <summary>
        /// Match returns true for an exact match (case-sensitive).
        /// </summary>
        [Test]
        public void MatchExactMatchCaseSensitiveReturnsTrue()
        {
            Assert.That(CoreUtils.Match("Hello", "Hello", true), Is.True);
        }

        /// <summary>
        /// Match returns false when case differs and case-sensitive mode is on.
        /// </summary>
        [Test]
        public void MatchCaseSensitiveDifferentCaseReturnsFalse()
        {
            Assert.That(CoreUtils.Match("hello", "Hello", true), Is.False);
        }

        /// <summary>
        /// Match returns true for case-insensitive comparison.
        /// </summary>
        [Test]
        public void MatchCaseInsensitiveMatchReturnsTrue()
        {
            Assert.That(CoreUtils.Match("hello", "Hello", false), Is.True);
        }

        /// <summary>
        /// Match with a wildcard '*' matches any sequence of characters.
        /// </summary>
        [Test]
        public void MatchWildcardStarMatchesAnySequence()
        {
            Assert.That(CoreUtils.Match("Hello World", "Hello*", true), Is.True);
            Assert.That(CoreUtils.Match("Hello World", "*World", true), Is.True);
            Assert.That(CoreUtils.Match("Hello World", "*", true), Is.True);
            Assert.That(CoreUtils.Match("Hello World", "H*d", true), Is.True);
        }

        /// <summary>
        /// Match with a wildcard '?' matches exactly one character.
        /// </summary>
        [Test]
        public void MatchWildcardQuestionMarkMatchesSingleChar()
        {
            Assert.That(CoreUtils.Match("abc", "a?c", true), Is.True);
            Assert.That(CoreUtils.Match("aXc", "a?c", true), Is.True);
        }

        /// <summary>
        /// Match with a character set [] matches a character in the set.
        /// </summary>
        [Test]
        public void MatchCharacterSetMatchesCharInSet()
        {
            Assert.That(CoreUtils.Match("abc", "a[bc]c", true), Is.True);
            Assert.That(CoreUtils.Match("adc", "a[bc]c", true), Is.False);
        }

        /// <summary>
        /// Match with negated character set [!] matches a character NOT in the set.
        /// </summary>
        [Test]
        public void MatchNegatedCharacterSetMatchesCharNotInSet()
        {
            Assert.That(CoreUtils.Match("adc", "a[!bc]c", true), Is.True);
            Assert.That(CoreUtils.Match("abc", "a[!bc]c", true), Is.False);
        }

        #endregion

        #region FixupAsSemanticVersion

        /// <summary>
        /// FixupAsSemanticVersion with a valid three-part version returns it unchanged.
        /// </summary>
        [Test]
        public void FixupAsSemanticVersionThreePartVersionReturnsSame()
        {
            string result = CoreUtils.FixupAsSemanticVersion("1.2.3");
            Assert.That(result, Is.EqualTo("1.2.3"));
        }

        /// <summary>
        /// FixupAsSemanticVersion with a one-part version expands to three parts.
        /// </summary>
        [Test]
        public void FixupAsSemanticVersionOnePartExpandsToThreeParts()
        {
            string result = CoreUtils.FixupAsSemanticVersion("1");
            Assert.That(result, Is.EqualTo("1.0.0"));
        }

        /// <summary>
        /// FixupAsSemanticVersion with a two-part version expands to three parts.
        /// </summary>
        [Test]
        public void FixupAsSemanticVersionTwoPartsExpandsToThree()
        {
            string result = CoreUtils.FixupAsSemanticVersion("1.2");
            Assert.That(result, Is.EqualTo("1.2.0"));
        }

        /// <summary>
        /// FixupAsSemanticVersion handles a version with a pre-release suffix.
        /// </summary>
        [Test]
        public void FixupAsSemanticVersionPreReleaseSuffixPreserved()
        {
            string result = CoreUtils.FixupAsSemanticVersion("1.2.3-beta");
            Assert.That(result, Is.EqualTo("1.2.3-beta"));
        }

        /// <summary>
        /// FixupAsSemanticVersion with a '+' build metadata suffix preserves it.
        /// </summary>
        [Test]
        public void FixupAsSemanticVersionBuildMetadataSuffixPreserved()
        {
            string result = CoreUtils.FixupAsSemanticVersion("1.2.3+build");
            Assert.That(result, Is.EqualTo("1.2.3+build"));
        }

        /// <summary>
        /// FixupAsSemanticVersion with null or whitespace input returns null.
        /// </summary>
        [Test]
        public void FixupAsSemanticVersionNullOrWhitespaceReturnsNull()
        {
            Assert.That(CoreUtils.FixupAsSemanticVersion(null), Is.Null);
            Assert.That(CoreUtils.FixupAsSemanticVersion("   "), Is.Null);
        }

        #endregion

        #region GetAssemblySoftwareVersion / GetAssemblyBuildNumber

        /// <summary>
        /// GetAssemblySoftwareVersion returns a non-null, non-empty string.
        /// </summary>
        [Test]
        public void GetAssemblySoftwareVersionReturnsNonEmptyString()
        {
            string version = CoreUtils.GetAssemblySoftwareVersion();
            Assert.That(version, Is.Not.Null);
            Assert.That(version, Is.Not.Empty);
        }

        /// <summary>
        /// GetAssemblyBuildNumber returns a non-null, non-empty string.
        /// </summary>
        [Test]
        public void GetAssemblyBuildNumberReturnsNonEmptyString()
        {
            string buildNumber = CoreUtils.GetAssemblyBuildNumber();
            Assert.That(buildNumber, Is.Not.Null);
            Assert.That(buildNumber, Is.Not.Empty);
        }

        #endregion

        #region Clone variants

        /// <summary>
        /// Clone for a struct value type returns the same value.
        /// </summary>
        [Test]
        public void CloneStructReturnsEqualValue()
        {
            int original = 42;
            int clone = CoreUtils.Clone(in original);
            Assert.That(clone, Is.EqualTo(original));
        }

        /// <summary>
        /// Clone for a string returns the same reference (strings are immutable).
        /// </summary>
        [Test]
        public void CloneStringReturnsSameReference()
        {
            const string original = "hello";
            string clone = CoreUtils.Clone(original);
            Assert.That(clone, Is.SameAs(original));
        }

        /// <summary>
        /// Clone for an ArrayOf T returns an equivalent array with the same elements.
        /// </summary>
        [Test]
        public void CloneArrayOfReturnsEquivalentArray()
        {
            ArrayOf<int> original = [1, 2, 3];
            ArrayOf<int> clone = CoreUtils.Clone(original);
            Assert.That(clone.Count, Is.EqualTo(original.Count));
            for (int i = 0; i < original.Count; i++)
            {
                Assert.That(clone[i], Is.EqualTo(original[i]));
            }
        }

        /// <summary>
        /// Clone for ExtensionObject with no encodeable body returns the original.
        /// </summary>
        [Test]
        public void CloneExtensionObjectWithNoEncodeableReturnsOriginal()
        {
            var original = ExtensionObject.Null;
            ExtensionObject clone = CoreUtils.Clone(original);
            Assert.That(clone, Is.EqualTo(original));
        }

        #endregion

        #region IsEqual variants

        /// <summary>
        /// IsEqual for two equal DateTime values with the same kind returns true.
        /// </summary>
        [Test]
        public void IsEqualDateTimeEqualValuesReturnsTrue()
        {
            var dt = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(CoreUtils.IsEqual(dt, dt), Is.True);
        }

        /// <summary>
        /// IsEqual for two DateTime values below the OPC UA time base both map to the
        /// minimum and are considered equal.
        /// </summary>
        [Test]
        public void IsEqualDateTimeBothBelowTimeBaseReturnsTrue()
        {
            DateTime dt1 = CoreUtils.TimeBase.AddDays(-1);
            DateTime dt2 = CoreUtils.TimeBase.AddDays(-2);
            Assert.That(CoreUtils.IsEqual(dt1, dt2), Is.True);
        }

        /// <summary>
        /// IsEqual for two different DateTime values in the normal range returns false.
        /// </summary>
        [Test]
        public void IsEqualDateTimeDifferentValuesReturnsFalse()
        {
            var dt1 = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var dt2 = new DateTime(2024, 1, 15, 13, 0, 0, DateTimeKind.Utc);
            Assert.That(CoreUtils.IsEqual(dt1, dt2), Is.False);
        }

        /// <summary>
        /// IsEqual for two equal struct values using the in T overload returns true.
        /// </summary>
        [Test]
        public void IsEqualInStructEqualValuesReturnsTrue()
        {
            int a = 42;
            int b = 42;
            Assert.That(CoreUtils.IsEqual(in a, in b), Is.True);
        }

        /// <summary>
        /// IsEqual for two different struct values using the in T overload returns false.
        /// </summary>
        [Test]
        public void IsEqualInStructDifferentValuesReturnsFalse()
        {
            int a = 42;
            int b = 99;
            Assert.That(CoreUtils.IsEqual(in a, in b), Is.False);
        }

        /// <summary>
        /// IsEqual for two equal IEnumerable sequences returns true.
        /// </summary>
        [Test]
        public void IsEqualIEnumerableEqualSequencesReturnsTrue()
        {
            IEnumerable<int> a = [1, 2, 3];
            IEnumerable<int> b = [1, 2, 3];
            Assert.That(CoreUtils.IsEqual(a, b), Is.True);
        }

        /// <summary>
        /// IsEqual for two different IEnumerable sequences returns false.
        /// </summary>
        [Test]
        public void IsEqualIEnumerableDifferentSequencesReturnsFalse()
        {
            IEnumerable<int> a = [1, 2, 3];
            IEnumerable<int> b = [1, 2, 4];
            Assert.That(CoreUtils.IsEqual(a, b), Is.False);
        }

        /// <summary>
        /// IsEqual for two null IEnumerable sequences returns true because they
        /// are the same reference (both null).
        /// </summary>
        [Test]
        public void IsEqualIEnumerableBothNullReturnsTrue()
        {
            IEnumerable<int> a = null;
            IEnumerable<int> b = null;
            Assert.That(CoreUtils.IsEqual(a, b), Is.True);
        }

        /// <summary>
        /// IsEqual for same reference IEnumerable returns true.
        /// </summary>
        [Test]
        public void IsEqualIEnumerableSameReferenceReturnsTrue()
        {
            IEnumerable<int> a = [1, 2, 3];
            Assert.That(CoreUtils.IsEqual(a, a), Is.True);
        }

        /// <summary>
        /// IsEqual for two equal T[] arrays (unmanaged) returns true.
        /// </summary>
        [Test]
        public void IsEqualTArrayEqualArraysReturnsTrue()
        {
            int[] a = [1, 2, 3];
            int[] b = [1, 2, 3];
            Assert.That(CoreUtils.IsEqual(a, b), Is.True);
        }

        /// <summary>
        /// IsEqual for two different T[] arrays (unmanaged) returns false.
        /// </summary>
        [Test]
        public void IsEqualTArrayDifferentArraysReturnsFalse()
        {
            int[] a = [1, 2, 3];
            int[] b = [1, 2, 4];
            Assert.That(CoreUtils.IsEqual(a, b), Is.False);
        }

        /// <summary>
        /// IsEqual for two null T[] arrays returns true because they
        /// are the same reference (both null).
        /// </summary>
        [Test]
        public void IsEqualTArrayBothNullReturnsTrue()
        {
            int[] a = null;
            int[] b = null;
            Assert.That(CoreUtils.IsEqual(a, b), Is.True);
        }

        /// <summary>
        /// IsEqual for same reference T[] array returns true.
        /// </summary>
        [Test]
        public void IsEqualTArraySameReferenceReturnsTrue()
        {
            int[] a = [1, 2, 3];
            Assert.That(CoreUtils.IsEqual(a, a), Is.True);
        }

        #endregion

        #region LoadInnerXml

        /// <summary>
        /// LoadInnerXml loads valid XML into an XmlDocument.
        /// </summary>
        [Test]
        public void LoadInnerXmlValidXmlLoadsSuccessfully()
        {
            var doc = new XmlDocument();
            doc.LoadInnerXml("<root><child>text</child></root>");
            Assert.That(doc.DocumentElement, Is.Not.Null);
            Assert.That(doc.DocumentElement.Name, Is.EqualTo("root"));
        }

        /// <summary>
        /// LoadInnerXml with XML containing a child node populates the document correctly.
        /// </summary>
        [Test]
        public void LoadInnerXmlChildNodeIsAccessible()
        {
            var doc = new XmlDocument();
            doc.LoadInnerXml("<root><child>42</child></root>");
            XmlNode child = doc.DocumentElement?.SelectSingleNode("child");
            Assert.That(child, Is.Not.Null);
            Assert.That(child.InnerText, Is.EqualTo("42"));
        }

        /// <summary>
        /// LoadInnerXml with a DTD declaration throws an XmlException because
        /// DTD processing is prohibited.
        /// </summary>
        [Test]
        public void LoadInnerXmlWithDtdThrows()
        {
            var doc = new XmlDocument();
            const string xmlWithDtd =
                "<?xml version=\"1.0\"?><!DOCTYPE root [<!ELEMENT root (#PCDATA)>]><root/>";
            Assert.Throws<XmlException>(() => doc.LoadInnerXml(xmlWithDtd));
        }

        #endregion

        #region GetOpcUaAssembly

        /// <summary>
        /// GetOpcUaAssembly returns an assembly (or null) without throwing.
        /// </summary>
        [Test]
        public void GetOpcUaAssemblyDoesNotThrow()
        {
            Assert.DoesNotThrow(() => _ = CoreUtils.GetOpcUaAssembly());
        }

        #endregion

        #region SilentDispose

        /// <summary>
        /// SilentDispose on a null object does not throw.
        /// </summary>
        [Test]
        public void SilentDisposeNullObjectDoesNotThrow()
        {
            Assert.DoesNotThrow(() => CoreUtils.SilentDispose((object)null));
        }

        /// <summary>
        /// SilentDispose on a null IDisposable does not throw.
        /// </summary>
        [Test]
        public void SilentDisposeNullIDisposableDoesNotThrow()
        {
            Assert.DoesNotThrow(() => CoreUtils.SilentDispose((IDisposable)null));
        }

        /// <summary>
        /// SilentDispose on a valid IDisposable calls Dispose.
        /// </summary>
        [Test]
        public void SilentDisposeCallsDispose()
        {
            bool disposed = false;
            var disposable = new DisposableAction(() => disposed = true);
            CoreUtils.SilentDispose(disposable);
            Assert.That(disposed, Is.True);
        }

        /// <summary>
        /// SilentDispose on an IDisposable that throws does not propagate the exception.
        /// </summary>
        [Test]
        public void SilentDisposeThrowingDisposableDoesNotThrow()
        {
            var disposable = new DisposableAction(() => throw new InvalidOperationException("Dispose error"));
            Assert.DoesNotThrow(() => CoreUtils.SilentDispose(disposable));
        }

        private sealed class DisposableAction : IDisposable
        {
            private readonly Action m_action;

            public DisposableAction(Action action)
            {
                m_action = action;
            }

            public void Dispose()
            {
                m_action();
            }
        }

        #endregion
    }
}
