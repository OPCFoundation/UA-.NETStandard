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
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for <see cref="UaPath"/>.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPathTests
    {
        [Test]
        public void ParseEmptyReturnsEmptyArray()
        {
            Assert.That(UaPath.Parse(string.Empty), Is.Empty);
        }

        [Test]
        public void ParseRootReturnsEmptyArray()
        {
            Assert.That(UaPath.Parse("/"), Is.Empty);
        }

        [Test]
        public void ParseSingleSegmentReturnsOneEntry()
        {
            QualifiedName[] segments = UaPath.Parse("foo");
            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(segments[0].Name, Is.EqualTo("foo"));
            Assert.That(segments[0].NamespaceIndex, Is.Zero);
        }

        [Test]
        public void ParseLeadingAndTrailingSlashesAreTolerated()
        {
            QualifiedName[] segments = UaPath.Parse("/foo/bar/");
            Assert.That(segments, Has.Length.EqualTo(2));
            Assert.That(segments[0].Name, Is.EqualTo("foo"));
            Assert.That(segments[1].Name, Is.EqualTo("bar"));
        }

        [Test]
        public void ParseQualifiedSegmentRetainsNamespaceIndex()
        {
            QualifiedName[] segments = UaPath.Parse("1:Reports/2:2024/data.csv");
            Assert.That(segments, Has.Length.EqualTo(3));
            Assert.That(segments[0].NamespaceIndex, Is.EqualTo(1));
            Assert.That(segments[0].Name, Is.EqualTo("Reports"));
            Assert.That(segments[1].NamespaceIndex, Is.EqualTo(2));
            Assert.That(segments[1].Name, Is.EqualTo("2024"));
            Assert.That(segments[2].NamespaceIndex, Is.Zero);
            Assert.That(segments[2].Name, Is.EqualTo("data.csv"));
        }

        [Test]
        public void ParseEmptyMiddleSegmentThrows()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => UaPath.Parse("foo//bar"));
            Assert.That(ex.Message, Does.Contain("empty segment"));
        }

        [Test]
        public void ParseInvalidNamespacePrefixThrows()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => UaPath.Parse("abc:foo"));
            Assert.That(ex.Message, Does.Contain("non-numeric"));
        }

        [Test]
        public void ParseEmptyNamespacePrefixThrows()
        {
            Assert.Throws<ArgumentException>(() => UaPath.Parse(":foo"));
        }

        [Test]
        public void ParseEmptyNameAfterNamespacePrefixThrows()
        {
            Assert.Throws<ArgumentException>(() => UaPath.Parse("1:"));
        }

        [Test]
        public void ParseNullThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => UaPath.Parse(null!));
        }

        [Test]
        public void FormatEmptyReturnsRoot()
        {
            Assert.That(UaPath.Format([]), Is.EqualTo("/"));
        }

        [Test]
        public void FormatPreservesNamespaceIndex()
        {
            string formatted = UaPath.Format(
            [
                new QualifiedName("Reports", 1),
                new QualifiedName("2024", 2),
                new QualifiedName("data.csv")
            ]);
            Assert.That(formatted, Is.EqualTo("/1:Reports/2:2024/data.csv"));
        }

        [Test]
        public void FormatRoundTripsThroughParse()
        {
            const string original = "/1:Reports/2:2024/data.csv";
            string roundTripped = UaPath.Format(UaPath.Parse(original));
            Assert.That(roundTripped, Is.EqualTo(original));
        }

        [Test]
        public void FormatSegmentNs0OmitsPrefix()
        {
            Assert.That(
                UaPath.FormatSegment(new QualifiedName("foo")),
                Is.EqualTo("foo"));
        }

        [Test]
        public void FormatSegmentNs1IncludesPrefix()
        {
            Assert.That(
                UaPath.FormatSegment(new QualifiedName("foo", 1)),
                Is.EqualTo("1:foo"));
        }

        [Test]
        public void FormatSegmentEmptyNameThrows()
        {
            Assert.Throws<ArgumentException>(
                () => UaPath.FormatSegment(QualifiedName.Null));
        }

        [Test]
        public void CombineWithRelativeRightAppends()
        {
            Assert.That(
                UaPath.Combine("/foo", "bar"),
                Is.EqualTo("/foo/bar"));
        }

        [Test]
        public void CombineWithAbsoluteRightReplacesLeft()
        {
            Assert.That(
                UaPath.Combine("/foo", "/baz/qux"),
                Is.EqualTo("/baz/qux"));
        }

        [Test]
        public void CombineNullLeftIsRoot()
        {
            Assert.That(
                UaPath.Combine(null!, "bar"),
                Is.EqualTo("/bar"));
        }

        [Test]
        public void CombineEmptyRightReturnsLeft()
        {
            Assert.That(
                UaPath.Combine("/foo", string.Empty),
                Is.EqualTo("/foo"));
        }

        [Test]
        public void CombineNullRightThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => UaPath.Combine("/foo", null!));
        }

        [Test]
        public void GetDirectoryNameOfRootReturnsNull()
        {
            Assert.That(UaPath.GetDirectoryName("/"), Is.Null);
        }

        [Test]
        public void GetDirectoryNameOfSingleSegmentReturnsRoot()
        {
            Assert.That(UaPath.GetDirectoryName("foo"), Is.EqualTo("/"));
        }

        [Test]
        public void GetDirectoryNameOfMultipleSegmentsReturnsParent()
        {
            Assert.That(
                UaPath.GetDirectoryName("/1:Reports/2024/data.csv"),
                Is.EqualTo("/1:Reports/2024"));
        }

        [Test]
        public void GetFileNameOfRootReturnsNullQualifiedName()
        {
            Assert.That(UaPath.GetFileName("/").IsNull, Is.True);
        }

        [Test]
        public void GetFileNameReturnsLeafSegment()
        {
            QualifiedName name = UaPath.GetFileName("/1:Reports/2024/data.csv");
            Assert.That(name.NamespaceIndex, Is.Zero);
            Assert.That(name.Name, Is.EqualTo("data.csv"));
        }

        [Test]
        public void NormalizeAddsLeadingSlashAndStripsTrailing()
        {
            Assert.That(UaPath.Normalize("foo/bar/"), Is.EqualTo("/foo/bar"));
        }

        [Test]
        public void NamespacedSiblingsProduceDistinctCanonicalPaths()
        {
            string a = UaPath.Format([new QualifiedName("foo", 1)]);
            string b = UaPath.Format([new QualifiedName("foo", 2)]);
            Assert.That(a, Is.Not.EqualTo(b));
        }
    }
}
