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

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Registry
{
    /// <summary>
    /// Exercises the <see cref="WotContentDigest"/>, <see cref="WotResourceVersion"/>,
    /// and <see cref="WotLabels"/> value objects from <c>WotRegistryModel.cs</c>.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotRegistryModelTests
    {
        [Test]
        public void ComputeReturnsSha256DigestOfContent()
        {
            byte[] content = Encoding.UTF8.GetBytes("hello");
            ByteString digest = WotContentDigest.Compute(content);

            Assert.That(digest.Span.Length, Is.EqualTo(32),
                "SHA-256 always produces 32 bytes.");
            Assert.That(digest.Span.ToArray(), Is.Not.All.EqualTo((byte)0));
        }

        [Test]
        public void ComputeProducesDifferentDigestsForDifferentContent()
        {
            ByteString digest1 = WotContentDigest.Compute(Encoding.UTF8.GetBytes("aaa"));
            ByteString digest2 = WotContentDigest.Compute(Encoding.UTF8.GetBytes("bbb"));

            Assert.That(digest1.Span.ToArray(), Is.Not.EqualTo(digest2.Span.ToArray()));
        }

        [Test]
        public void ComputeProducesSameDigestForSameContent()
        {
            byte[] content = Encoding.UTF8.GetBytes("deterministic");
            ByteString d1 = WotContentDigest.Compute(content);
            ByteString d2 = WotContentDigest.Compute(content);

            Assert.That(d1.Span.ToArray(), Is.EqualTo(d2.Span.ToArray()));
        }

        [Test]
        public void ToHexWithNullReturnsEmptyString()
        {
            string result = WotContentDigest.ToHex(default(ByteString));
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToHexWithEmptyArrayReturnsEmptyString()
        {
            string result = WotContentDigest.ToHex(Array.Empty<byte>());
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ToHexProducesLowercaseHexString()
        {
            byte[] digest = new byte[] { 0xAB, 0xCD, 0xEF, 0x01 };
            string hex = WotContentDigest.ToHex(digest);

            Assert.That(hex, Is.EqualTo("abcdef01"));
        }

        [Test]
        public void ToHexRoundTripsAllNibbleValues()
        {
            var bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                bytes[i] = (byte)((i << 4) | i);
            }
            string hex = WotContentDigest.ToHex(bytes);

            Assert.That(hex, Has.Length.EqualTo(32));
            Assert.That(hex, Does.Match("^[0-9a-f]+$"));
        }

        [Test]
        public void EqualReturnsTrueForSameReference()
        {
            byte[] digest = new byte[] { 1, 2, 3 };
            Assert.That(WotContentDigest.Equal(digest, digest), Is.True);
        }

        [Test]
        public void EqualReturnsFalseWhenLeftIsNull()
        {
            Assert.That(WotContentDigest.Equal(null, new byte[] { 1 }), Is.False);
        }

        [Test]
        public void EqualReturnsFalseWhenRightIsNull()
        {
            Assert.That(WotContentDigest.Equal(new byte[] { 1 }, null), Is.False);
        }

        [Test]
        public void EqualReturnsTrueForEqualContent()
        {
            byte[] left = new byte[] { 10, 20, 30 };
            byte[] right = new byte[] { 10, 20, 30 };

            Assert.That(WotContentDigest.Equal(left, right), Is.True);
        }

        [Test]
        public void EqualReturnsFalseForDifferentContent()
        {
            byte[] left = new byte[] { 10, 20, 30 };
            byte[] right = new byte[] { 10, 20, 31 };

            Assert.That(WotContentDigest.Equal(left, right), Is.False);
        }

        [Test]
        public void WotResourceVersionConstructorSetsAllProperties()
        {
            byte[] content = Encoding.UTF8.GetBytes("{\"title\":\"test\"}");
            var now = DateTime.UtcNow;
            ByteString digest = WotContentDigest.Compute(content);
            var version = new WotResourceVersion(
                versionId: "v1",
                digest: digest,
                contentLength: content.Length,
                contentType: "application/td+json",
                format: "WoT-TD/1.1",
                createdAt: now,
                modifiedAt: now);

            Assert.That(version.VersionId, Is.EqualTo("v1"));
            Assert.That(version.ContentLength, Is.EqualTo(content.Length));
            Assert.That(version.ContentType, Is.EqualTo("application/td+json"));
            Assert.That(version.Format, Is.EqualTo("WoT-TD/1.1"));
            Assert.That(version.CreatedAt, Is.EqualTo(now));
            Assert.That(version.ModifiedAt, Is.EqualTo(now));
            Assert.That(version.Digest.IsNull, Is.False);
            Assert.That(version.Digest, Has.Length.EqualTo(32));
        }

        [Test]
        public void WotResourceVersionWithExplicitDigestUsesSuppliedDigest()
        {
            byte[] content = Encoding.UTF8.GetBytes("doc");
            var digest = new byte[32];
            digest[0] = 42;
            var version = new WotResourceVersion(
                versionId: "v1", digest: ByteString.From(digest), contentLength: content.Length,
                contentType: "ct", format: "f", createdAt: default, modifiedAt: default);

            Assert.That(version.Digest.Span[0], Is.EqualTo(42));
        }

        [Test]
        public void WotResourceVersionWithRetainsOrReplacesDigest()
        {
            ByteString originalDigest = WotContentDigest.Compute(
                Encoding.UTF8.GetBytes("original"));
            ByteString replacementDigest = WotContentDigest.Compute(
                Encoding.UTF8.GetBytes("replacement"));
            var version = new WotResourceVersion(
                versionId: "v1",
                digest: originalDigest,
                contentLength: 8,
                contentType: "ct",
                format: "f",
                createdAt: default,
                modifiedAt: default);

            WotResourceVersion retained = version.With(contentType: "updated");
            WotResourceVersion replaced = version.With(
                digest: replacementDigest,
                contentLength: 11);

            Assert.Multiple(() =>
            {
                Assert.That(WotContentDigest.Equal(retained.Digest, originalDigest), Is.True);
                Assert.That(retained.ContentType, Is.EqualTo("updated"));
                Assert.That(WotContentDigest.Equal(replaced.Digest, replacementDigest), Is.True);
                Assert.That(replaced.ContentLength, Is.EqualTo(11));
            });
        }

        [Test]
        public void WotResourceVersionWithCannotCommitPlaceholderWithoutDigest()
        {
            WotResourceVersion placeholder = WotResourceVersion.CreatePlaceholder(
                "v1",
                DateTime.UtcNow);

            Assert.That(
                () => placeholder.With(hasContent: true),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void WotResourceVersionPublicMethodsDoNotExposeNullableByteString()
        {
            MethodInfo[] publicMethods = typeof(WotResourceVersion).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

            Assert.Multiple(() =>
            {
                Assert.That(
                    publicMethods.Any(method =>
                        Nullable.GetUnderlyingType(method.ReturnType) == typeof(ByteString)),
                    Is.False);
                Assert.That(
                    publicMethods.SelectMany(method => method.GetParameters()).Any(parameter =>
                        Nullable.GetUnderlyingType(parameter.ParameterType) == typeof(ByteString)),
                    Is.False);
                Assert.That(
                    typeof(WotResourceVersion).GetMethod(
                        "With",
                        BindingFlags.Public | BindingFlags.Instance),
                    Is.Null);
            });
        }

        [Test]
        public void WotResourceVersionDigestHexIsHexString()
        {
            byte[] content = Encoding.UTF8.GetBytes("doc");
            var version = new WotResourceVersion(
                versionId: "v1", digest: WotContentDigest.Compute(content), contentLength: content.Length, contentType: "ct",
                format: "f", createdAt: default, modifiedAt: default);

            Assert.That(version.DigestHex, Has.Length.EqualTo(64));
            Assert.That(version.DigestHex, Does.Match("^[0-9a-f]+$"));
        }

        [Test]
        public void WotLabelsEmptyIsAnEmptyImmutableSortedDictionary()
        {
            Assert.That(WotLabels.Empty, Is.Empty);
            Assert.That(WotLabels.Empty, Is.Not.Null);
        }

        [Test]
        public void WotLabelsEmptyHasOrdinalOrdering()
        {
            var labels = WotLabels.Empty
                .Add("beta", "2")
                .Add("alpha", "1");

            string[] keys = [.. labels.Keys];
            Assert.That(keys[0], Is.EqualTo("alpha"),
                "Labels must enumerate in ordinal key order.");
            Assert.That(keys[1], Is.EqualTo("beta"));
        }

        [Test]
        public void ValidateRejectsNonPositiveMaxOpenFileHandles()
        {
            var bounds = new WotRegistryPersistenceBounds
            {
                MaxOpenFileHandles = 0
            };

            Assert.That(
                () => bounds.Validate(),
                Throws.TypeOf<ArgumentOutOfRangeException>()
                    .With.Property(nameof(ArgumentOutOfRangeException.ParamName))
                    .EqualTo(nameof(WotRegistryPersistenceBounds.MaxOpenFileHandles)));
        }
    }
}
