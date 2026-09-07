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

#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Net.Pkcs11Interop.Common;
using NUnit.Framework;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Covers the translations between .NET's algorithm names and PKCS#11's.
    /// </summary>
    /// <remarks>
    /// These are pure functions, so they need no token - which matters, because
    /// getting them wrong is not a failure that shows up as a failure. A wrong
    /// DigestInfo prefix produces a signature that is well formed but attests to
    /// a different algorithm than the one the caller asked for, and a mismatched
    /// MGF1 produces a PSS signature the peer silently rejects.
    /// </remarks>
    [TestFixture]
    [Category("Pkcs11")]
    [Parallelizable(ParallelScope.All)]
    public class Pkcs11DigestTests
    {
        [Test]
        [TestCase(32, 19)]
        [TestCase(48, 19)]
        [TestCase(64, 19)]
        public void DigestInfoIsThePrefixFollowedByTheHash(int hashLength, int prefixLength)
        {
            HashAlgorithmName algorithm = hashLength switch
            {
                32 => HashAlgorithmName.SHA256,
                48 => HashAlgorithmName.SHA384,
                _ => HashAlgorithmName.SHA512
            };

            byte[] hash = new byte[hashLength];
            for (int ii = 0; ii < hash.Length; ii++)
            {
                hash[ii] = (byte)(ii + 1);
            }

            byte[] digestInfo = Pkcs11Digest.WrapInDigestInfo(hash, algorithm);

            var carried = new byte[hashLength];
            Buffer.BlockCopy(digestInfo, prefixLength, carried, 0, hashLength);

            Assert.Multiple(() =>
            {
                Assert.That(digestInfo, Has.Length.EqualTo(prefixLength + hashLength));
                Assert.That(
                    carried,
                    Is.EqualTo(hash),
                    "the hash must be carried through unchanged");
                Assert.That(digestInfo[0], Is.EqualTo(0x30), "DigestInfo is a DER SEQUENCE");
                Assert.That(
                    digestInfo[1],
                    Is.EqualTo(prefixLength + hashLength - 2),
                    "the DER length must cover everything after the header");
            });
        }

        /// <summary>
        /// The three prefixes must differ, or a signature would attest to the
        /// wrong algorithm.
        /// </summary>
        [Test]
        public void EachAlgorithmHasItsOwnDigestInfoPrefix()
        {
            byte[] sha256 = Pkcs11Digest.WrapInDigestInfo(new byte[32], HashAlgorithmName.SHA256);
            byte[] sha384 = Pkcs11Digest.WrapInDigestInfo(new byte[48], HashAlgorithmName.SHA384);
            byte[] sha512 = Pkcs11Digest.WrapInDigestInfo(new byte[64], HashAlgorithmName.SHA512);

            // The OID's last byte distinguishes the three (RFC 8017 B.1).
            Assert.Multiple(() =>
            {
                Assert.That(sha256[14], Is.EqualTo(0x01));
                Assert.That(sha384[14], Is.EqualTo(0x02));
                Assert.That(sha512[14], Is.EqualTo(0x03));
            });
        }

        [Test]
        [TestCase(31)]
        [TestCase(33)]
        [TestCase(0)]
        public void DigestInfoRejectsAHashOfTheWrongLength(int hashLength)
        {
            Assert.Throws<CryptographicException>(
                () => Pkcs11Digest.WrapInDigestInfo(new byte[hashLength], HashAlgorithmName.SHA256));
        }

        [Test]
        public void DigestInfoRejectsAnUnsupportedAlgorithm()
        {
            Assert.Throws<CryptographicException>(
                () => Pkcs11Digest.WrapInDigestInfo(new byte[20], HashAlgorithmName.SHA1));
        }

        [Test]
        public void MechanismMapsEachSupportedAlgorithm()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    Pkcs11Digest.ToMechanism(HashAlgorithmName.SHA256), Is.EqualTo(CKM.CKM_SHA256));
                Assert.That(
                    Pkcs11Digest.ToMechanism(HashAlgorithmName.SHA384), Is.EqualTo(CKM.CKM_SHA384));
                Assert.That(
                    Pkcs11Digest.ToMechanism(HashAlgorithmName.SHA512), Is.EqualTo(CKM.CKM_SHA512));
            });
        }

        /// <summary>
        /// SHA-1 must not be reachable: the stack does not sign with it.
        /// </summary>
        [Test]
        public void MechanismRejectsSha1AndUnknownAlgorithms()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<CryptographicException>(
                    () => Pkcs11Digest.ToMechanism(HashAlgorithmName.SHA1));
                Assert.Throws<CryptographicException>(
                    () => Pkcs11Digest.ToMechanism(HashAlgorithmName.MD5));
            });
        }

        /// <summary>
        /// The mask generation function must follow the signature hash, or a PSS
        /// signature is rejected by a peer that follows the specification.
        /// </summary>
        [Test]
        public void MaskGenerationFunctionFollowsTheHash()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    Pkcs11Digest.ToMaskGenerationFunction(HashAlgorithmName.SHA256),
                    Is.EqualTo(CKG.CKG_MGF1_SHA256));
                Assert.That(
                    Pkcs11Digest.ToMaskGenerationFunction(HashAlgorithmName.SHA384),
                    Is.EqualTo(CKG.CKG_MGF1_SHA384));
                Assert.That(
                    Pkcs11Digest.ToMaskGenerationFunction(HashAlgorithmName.SHA512),
                    Is.EqualTo(CKG.CKG_MGF1_SHA512));
            });
        }

        [Test]
        public void MaskGenerationFunctionRejectsAnUnsupportedAlgorithm()
        {
            Assert.Throws<CryptographicException>(
                () => Pkcs11Digest.ToMaskGenerationFunction(HashAlgorithmName.SHA1));
        }

        [Test]
        public void ComputeMatchesThePlatformHashOverABuffer()
        {
            byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

            byte[] actual = Pkcs11Digest.Compute(data, 2, 5, HashAlgorithmName.SHA256);

#if NET6_0_OR_GREATER
            byte[] expected = SHA256.HashData(data.AsSpan(2, 5));
#else
            byte[] expected;
            using (SHA256 sha256 = SHA256.Create())
            {
                expected = sha256.ComputeHash(data, 2, 5);
            }
#endif

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ComputeMatchesThePlatformHashOverAStream()
        {
            byte[] data = [9, 8, 7, 6, 5];

            byte[] actual;
            using (var stream = new MemoryStream(data))
            {
                actual = Pkcs11Digest.Compute(stream, HashAlgorithmName.SHA384);
            }

#if NET6_0_OR_GREATER
            byte[] expected = SHA384.HashData(data);
#else
            byte[] expected;
            using (SHA384 sha384 = SHA384.Create())
            {
                expected = sha384.ComputeHash(data);
            }
#endif

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ComputeRejectsAnUnsupportedAlgorithm()
        {
            Assert.Throws<CryptographicException>(
                () => Pkcs11Digest.Compute([1, 2, 3], 0, 3, HashAlgorithmName.SHA1));
        }
    }
}
