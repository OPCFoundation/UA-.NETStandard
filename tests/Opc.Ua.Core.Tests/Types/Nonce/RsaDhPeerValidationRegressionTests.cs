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
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Nonce
{
    [TestFixture]
    [Category("NonceTests")]
    public sealed class RsaDhPeerValidationRegressionTests
    {
        [Test]
        public void RsaDhRejectsInvalidPeerValuesBeforeAgreement(
            [Values(RSADiffieHellmanGroup.FFDHE2048, RSADiffieHellmanGroup.FFDHE3072, RSADiffieHellmanGroup.FFDHE4096)]
            RSADiffieHellmanGroup group,
            [Values("zero", "one", "pMinusOne", "p", "pPlusOne", "empty", "short", "long", "wrongGroup")]
            string invalid)
        {
            RSADiffieHellman local = RSADiffieHellman.Create(group);
            int length = local.GetNonce().Length;
            BigInteger prime = GetPrime(length);
            BigInteger value = invalid switch
            {
                "zero" => BigInteger.Zero,
                "one" => BigInteger.One,
                "pMinusOne" => prime - 1,
                "p" => prime,
                "pPlusOne" => prime + 1,
                _ => new BigInteger(2)
            };
            int peerLength = invalid switch
            {
                "empty" => 0,
                "short" => length - 1,
                "long" => length + 1,
                "wrongGroup" => length == 256 ? 384 : 256,
                _ => length
            };
            byte[] encoded = Encode(value, peerLength);
            Assert.Throws<CryptographicException>(() =>
            {
                RSADiffieHellman remote = RSADiffieHellman.Create(encoded);
                local.DeriveRawSecretAgreement(remote);
            });
        }

        [TestCase(RSADiffieHellmanGroup.FFDHE2048)]
        [TestCase(RSADiffieHellmanGroup.FFDHE3072)]
        [TestCase(RSADiffieHellmanGroup.FFDHE4096)]
        public void ValidPeerAgreementAndZeroPaddedBoundsRemainSupported(RSADiffieHellmanGroup group)
        {
            RSADiffieHellman first = RSADiffieHellman.Create(group);
            RSADiffieHellman second = RSADiffieHellman.Create(group);
            int length = first.GetNonce().Length;
            byte[] left = first.DeriveRawSecretAgreement(RSADiffieHellman.Create(second.GetNonce()));
            byte[] right = second.DeriveRawSecretAgreement(RSADiffieHellman.Create(first.GetNonce()));
            try
            {
                Assert.That(left, Has.Length.EqualTo(length));
                Assert.That(left, Is.EqualTo(right));
                foreach (BigInteger bound in new[] { new BigInteger(2), GetPrime(length) - 2 })
                {
                    byte[] secret = first.DeriveRawSecretAgreement(RSADiffieHellman.Create(Encode(bound, length)));
                    Assert.That(secret, Has.Length.EqualTo(length));
                    CryptoUtils.ZeroMemory(secret);
                }
            }
            finally
            {
                CryptoUtils.ZeroMemory(left);
                CryptoUtils.ZeroMemory(right);
            }
        }

        private static BigInteger GetPrime(int length)
        {
            FieldInfo field = typeof(RSADiffieHellman).GetField(
                "s_P" + (length * 8), BindingFlags.NonPublic | BindingFlags.Static)!;
            return ((Lazy<BigInteger>)field.GetValue(null)!).Value;
        }

        private static byte[] Encode(BigInteger value, int length)
        {
            byte[] encoded = new byte[length];
            byte[] littleEndian = value.ToByteArray();
            for (int index = 0; index < Math.Min(length, littleEndian.Length); index++)
            {
                encoded[length - index - 1] = littleEndian[index];
            }
            return encoded;
        }
    }
}
