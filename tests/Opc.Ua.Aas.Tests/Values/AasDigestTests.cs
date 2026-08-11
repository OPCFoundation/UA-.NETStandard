/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Values
{
    /// <summary>
    /// Tests the digest rule of clause 6.5.4. A Server publishes a digest and
    /// a Client verifies it, so the two only agree if the algorithm name is
    /// compared case-sensitively and the value is lower-case hexadecimal.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasDigestTests
    {
        [Test]
        public void ComputeHexAgreesWithTheFrameworkHash()
        {
            byte[] content = Encoding.UTF8.GetBytes("asset administration shell");

            Assert.Multiple(() =>
            {
                Assert.That(AasDigest.ComputeHex(ByteString.From(content), AasDigest.Sha256Name),
                    Is.EqualTo(Hex(Sha256(content))));
                Assert.That(AasDigest.ComputeHex(ByteString.From(content), AasDigest.Sha384Name),
                    Is.EqualTo(Hex(Sha384(content))));
                Assert.That(AasDigest.ComputeHex(ByteString.From(content), AasDigest.Sha512Name),
                    Is.EqualTo(Hex(Sha512(content))));
            });
        }

        private static byte[] Sha256(byte[] content)
        {
#if NET5_0_OR_GREATER
            return System.Security.Cryptography.SHA256.HashData(content);
#else
            using var sha = System.Security.Cryptography.SHA256.Create();
            return sha.ComputeHash(content);
#endif
        }

        private static byte[] Sha384(byte[] content)
        {
#if NET5_0_OR_GREATER
            return System.Security.Cryptography.SHA384.HashData(content);
#else
            using var sha = System.Security.Cryptography.SHA384.Create();
            return sha.ComputeHash(content);
#endif
        }

        private static byte[] Sha512(byte[] content)
        {
#if NET5_0_OR_GREATER
            return System.Security.Cryptography.SHA512.HashData(content);
#else
            using var sha = System.Security.Cryptography.SHA512.Create();
            return sha.ComputeHash(content);
#endif
        }

        [Test]
        public void ComputeHexIsLowerCaseHexadecimalWithoutAPrefix()
        {
            string digest = AasDigest.ComputeHex(ByteString.From([1, 2, 3]), AasDigest.Sha256Name);

            Assert.Multiple(() =>
            {
                Assert.That(digest, Has.Length.EqualTo(64));
                Assert.That(AasDigest.IsHex(digest), Is.True);
                Assert.That(digest, Does.Not.Contain(":"));
                Assert.That(digest, Is.EqualTo(digest.ToLowerInvariant()));
            });
        }

        [Test]
        public void AlgorithmNamesAreComparedCaseSensitively()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AasDigest.IsSupportedAlgorithm(AasDigest.Sha256Name), Is.True);
                Assert.That(AasDigest.IsSupportedAlgorithm("sha256"), Is.False);
                Assert.That(AasDigest.IsSupportedAlgorithm("SHA256"), Is.False);
                Assert.That(AasDigest.IsSupportedAlgorithm("Sha1"), Is.False);
                Assert.That(AasDigest.IsSupportedAlgorithm(null), Is.False);
                Assert.Throws<ArgumentException>(() => AasDigest.ValidateAlgorithm("sha256"));
                Assert.Throws<ArgumentNullException>(() => AasDigest.ValidateAlgorithm(null!));
            });
        }

        [Test]
        public void MatchesAcceptsTheContentItWasComputedFrom()
        {
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes("nameplate"));
            string digest = AasDigest.ComputeHex(content, AasDigest.Sha256Name);

            Assert.Multiple(() =>
            {
                Assert.That(AasDigest.Matches(content, AasDigest.Sha256Name, digest), Is.True);
                Assert.That(AasDigest.Matches(
                    ByteString.From(Encoding.UTF8.GetBytes("nameplate ")),
                    AasDigest.Sha256Name,
                    digest), Is.False);
                Assert.That(AasDigest.Matches(content, AasDigest.Sha384Name, digest), Is.False);
            });
        }

        [Test]
        public void MatchesRejectsADigestThatIsNotLowerCaseHexadecimal()
        {
            ByteString content = ByteString.From(Encoding.UTF8.GetBytes("nameplate"));
            string digest = AasDigest.ComputeHex(content, AasDigest.Sha256Name);

            Assert.Multiple(() =>
            {
                Assert.That(AasDigest.Matches(content, AasDigest.Sha256Name, digest.ToUpperInvariant()),
                    Is.False);
                Assert.That(AasDigest.Matches(content, AasDigest.Sha256Name, "sha256:" + digest),
                    Is.False);
                Assert.That(AasDigest.Matches(content, AasDigest.Sha256Name, null), Is.False);
                Assert.That(AasDigest.Matches(content, "sha256", digest), Is.False);
            });
        }

        [Test]
        public void IsHexRejectsOddLengthAndNonHexadecimal()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AasDigest.IsHex("00ff"), Is.True);
                Assert.That(AasDigest.IsHex("0"), Is.False);
                Assert.That(AasDigest.IsHex("00F"), Is.False);
                Assert.That(AasDigest.IsHex("00FF"), Is.False);
                Assert.That(AasDigest.IsHex("00g0"), Is.False);
                Assert.That(AasDigest.IsHex(string.Empty), Is.False);
                Assert.That(AasDigest.IsHex(null), Is.False);
            });
        }

        [Test]
        public void EmptyContentStillHasADigest()
        {
            string digest = AasDigest.ComputeHex(ByteString.Empty, AasDigest.Sha256Name);

            Assert.Multiple(() =>
            {
                Assert.That(digest, Has.Length.EqualTo(64));
                Assert.That(AasDigest.ToHex(ByteString.Empty), Is.Empty);
                Assert.That(AasDigest.Matches(ByteString.Empty, AasDigest.Sha256Name, digest), Is.True);
            });
        }

        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
            {
                builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
