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

using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Tests.Protocol
{
    [TestFixture]
    [Category("XRegistryProtocol")]
    public sealed class XRegistryPathTests
    {
        [TestCase("caf%C3%A9", "caf\u00e9")]
        [TestCase("cafe%CC%81", "cafe\u0301")]
        [TestCase("%F0%9F%92%A1", "\U0001f4a1")]
        [TestCase("%e6%b0%b4", "\u6c34")]
        [TestCase("100%25", "100%")]
        [TestCase("%252F", "%2F")]
        [TestCase("%252e%252e", "%2e%2e")]
        [TestCase("a%20b", "a b")]
        [TestCase("%3F%23", "?#")]
        [TestCase("Mixed-._~Case", "Mixed-._~Case")]
        public void GetSegmentsDecodesExactIdentityOnlyOnce(string escaped, string identity)
        {
            ArrayOf<string> segments = XRegistryPath.GetSegments("/groups/" + escaped);

            Assert.That(segments.Count, Is.EqualTo(2));
            Assert.That(segments[0], Is.EqualTo("groups"));
            Assert.That(segments[1], Is.EqualTo(identity));
        }

        [TestCase("/groups/caf%c3%a9/", "/groups/caf%C3%A9")]
        [TestCase("/groups/cafe%cc%81", "/groups/cafe%CC%81")]
        [TestCase("/groups/caf\u00e9", "/groups/caf%C3%A9")]
        [TestCase("/groups/cafe\u0301", "/groups/cafe%CC%81")]
        [TestCase("/groups/\U0001f4a1", "/groups/%F0%9F%92%A1")]
        [TestCase("/groups/%252f", "/groups/%252f")]
        [TestCase("/groups/a b", "/groups/a%20b")]
        [TestCase("/groups/MixedCase", "/groups/MixedCase")]
        public void NormalizeCanonicalizesEscapesWithoutChangingIdentity(string path, string expected)
        {
            Assert.That(XRegistryPath.Normalize(path), Is.EqualTo(expected));
        }

        [TestCase("caf\u00e9", "/groups/caf%C3%A9")]
        [TestCase("cafe\u0301", "/groups/cafe%CC%81")]
        [TestCase("\U0001f4a1", "/groups/%F0%9F%92%A1")]
        [TestCase("100%", "/groups/100%25")]
        [TestCase("%2F", "/groups/%252F")]
        [TestCase("?#", "/groups/%3F%23")]
        [TestCase("a b", "/groups/a%20b")]
        public void FromSegmentsEscapesDecodedIdentitiesWithoutInterpretingPercent(string identity, string expected)
        {
            Assert.That(XRegistryPath.FromSegments(["groups", identity]), Is.EqualTo(expected));
        }

        [Test]
        public void EmptySegmentsRepresentRootAndOneTrailingSlashIsAllowed()
        {
            Assert.That(XRegistryPath.GetSegments("/").Count, Is.Zero);
            Assert.That(XRegistryPath.FromSegments([]), Is.EqualTo("/"));
            Assert.That(XRegistryPath.Normalize("/"), Is.EqualTo("/"));
            Assert.That(XRegistryPath.FromSegments(["groups"]), Is.EqualTo("/groups"));
            ArrayOf<string> collection = XRegistryPath.GetSegments("/groups/");
            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0], Is.EqualTo("groups"));
            ArrayOf<string> entity = XRegistryPath.GetSegments("/groups/g/");
            Assert.That(entity.Count, Is.EqualTo(2));
            Assert.That(entity[0], Is.EqualTo("groups"));
            Assert.That(entity[1], Is.EqualTo("g"));
        }

        [TestCase("")]
        [TestCase("groups/g")]
        [TestCase("//")]
        [TestCase("//groups")]
        [TestCase("/groups//g")]
        [TestCase("/groups/g//")]
        [TestCase("/.")]
        [TestCase("/..")]
        [TestCase("/groups/%2e")]
        [TestCase("/groups/.%2E")]
        [TestCase("/groups/%2e.")]
        [TestCase("/groups/%2e%2e")]
        [TestCase("/groups/%2f")]
        [TestCase("/groups/a%2Fb")]
        [TestCase("/groups/%5c")]
        [TestCase("/groups/a\\b")]
        [TestCase("/groups/g?")]
        [TestCase("/groups/g?filter=x")]
        [TestCase("/groups/g#")]
        [TestCase("/groups/g#part")]
        [TestCase("/groups/%")]
        [TestCase("/groups/%1")]
        [TestCase("/groups/%GG")]
        [TestCase("/groups/%0g")]
        [TestCase("/groups/%80")]
        [TestCase("/groups/%FF")]
        [TestCase("/groups/%C3")]
        [TestCase("/groups/%C3x%A9")]
        [TestCase("/groups/%C0%AF")]
        [TestCase("/groups/%E0%80%AF")]
        [TestCase("/groups/%ED%A0%80")]
        [TestCase("/groups/%F4%90%80%80")]
        [TestCase("/groups/%F0%9F%92")]
        [TestCase("/groups/%00")]
        [TestCase("/groups/%0A")]
        [TestCase("/groups/%7F")]
        [TestCase("/groups/a\tb")]
        public void EscapedPathsRejectAmbiguousOrMalformedIdentities(string path)
        {
            Assert.That(() => XRegistryPath.GetSegments(path), Throws.ArgumentException);
            Assert.That(() => XRegistryPath.Normalize(path), Throws.ArgumentException);
        }

        [Test]
        public void NullPathsAreRejectedExplicitly()
        {
            Assert.That(() => XRegistryPath.GetSegments(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("path"));
            Assert.That(() => XRegistryPath.Normalize(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("path"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("\0")]
        [TestCase("\n")]
        [TestCase("\u007f")]
        public void FromSegmentsRejectsInvalidDecodedIdentities(string? identity)
        {
            Assert.That(() => XRegistryPath.FromSegments(["groups", identity!]), Throws.ArgumentException);
        }

        [Test]
        public void InvalidUtf16SurrogatesAreRejectedWithoutReplacement()
        {
            string[] identities = ["\ud800", "\udc00", "\ud800x", "x\udc00", "\ud800\ud800"];
            foreach (string identity in identities)
            {
                Assert.That(() => XRegistryPath.FromSegments(["groups", identity]), Throws.ArgumentException);
                Assert.That(() => XRegistryPath.GetSegments("/groups/" + identity), Throws.ArgumentException);
            }
        }
    }
}
