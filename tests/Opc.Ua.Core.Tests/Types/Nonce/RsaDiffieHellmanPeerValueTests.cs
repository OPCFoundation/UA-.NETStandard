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
using System.Globalization;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Nonce
{
    /// <summary>
    /// Regression tests for the peer public value validation in
    /// <see cref="RSADiffieHellman"/>.
    /// </summary>
    /// <remarks>
    /// A finite field Diffie-Hellman peer value has to lie in
    /// <c>2 &lt;= y &lt;= p-2</c> (RFC 7919 5.1). Without that check a peer can
    /// send 0, 1 or p-1 and pin the shared secret to a value it already knows,
    /// which pins every key derived from it for the channel.
    /// </remarks>
    [TestFixture]
    [Category("NonceTests")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class RsaDiffieHellmanPeerValueTests
    {
        /// <summary>
        /// The ffdhe2048 prime from RFC 7919 Appendix A.1, which is the group a
        /// 256 byte RSA-DH nonce belongs to.
        /// </summary>
        private const string kFfdhe2048Hex =
            "FFFFFFFFFFFFFFFFADF85458A2BB4A9AAFDC5620273D3CF1D8B9C583CE2D3695A9E13641146433FBCC939DCE249B3EF9" +
            "7D2FE363630C75D8F681B202AEC4617AD3DF1ED5D5FD65612433F51F5F066ED0856365553DED1AF3B557135E7F57C935" +
            "984F0C70E0E68B77E2A689DAF3EFE8721DF158A136ADE73530ACCA4F483A797ABC0AB182B324FB61D108A94BB2C8E3FB" +
            "B96ADAB760D7F4681D4F42A3DE394DF4AE56EDE76372BB190B07A7C8EE0A6D709E02FCE1CDF7E2ECC03404CD28342F61" +
            "9172FE9CE98583FF8E4F1232EEF28183C3FE3B1B4C6FAD733BB5FCBC2EC22005C58EF1837D1683B2C6F34A26C1B2EFFA" +
            "886B423861285C97FFFFFFFFFFFFFFFF";

        private const int kFfdhe2048NonceSize = 256;

        /// <summary>
        /// Zero and one are the degenerate peer values: the shared secret comes
        /// out as zero or one no matter what private key this side holds.
        /// </summary>
        [TestCase(0UL)]
        [TestCase(1UL)]
        public void CreateRejectsPeerValueBelowTwo(ulong value)
        {
            byte[] nonce = new byte[kFfdhe2048NonceSize];
            nonce[^1] = (byte)value;

            Assert.That(
                () => RSADiffieHellman.Create(nonce),
                Throws.ArgumentException);
        }

        /// <summary>
        /// p-1 has order two, so the shared secret is either one or p-1 and an
        /// observer guesses it with one bit. p itself is congruent to zero.
        /// </summary>
        [TestCase(1)]
        [TestCase(0)]
        public void CreateRejectsPeerValueAtOrAboveModulusMinusOne(int subtract)
        {
            byte[] nonce = GetModulus();
            nonce[^1] -= (byte)subtract;

            Assert.That(
                () => RSADiffieHellman.Create(nonce),
                Throws.ArgumentException);
        }

        /// <summary>
        /// A value above the modulus is outside the group altogether.
        /// </summary>
        [Test]
        public void CreateRejectsPeerValueAboveTheModulus()
        {
            byte[] nonce = new byte[kFfdhe2048NonceSize];

            for (int ii = 0; ii < nonce.Length; ii++)
            {
                nonce[ii] = 0xFF;
            }

            Assert.That(
                () => RSADiffieHellman.Create(nonce),
                Throws.ArgumentException);
        }

        /// <summary>
        /// Both ends of the valid range are accepted, so the check rejects the
        /// degenerate values and nothing else.
        /// </summary>
        [Test]
        public void CreateAcceptsPeerValuesInsideTheGroup()
        {
            byte[] lowest = new byte[kFfdhe2048NonceSize];
            lowest[^1] = 2;

            byte[] highest = GetModulus();
            highest[^1] -= 2;

            Assert.That(RSADiffieHellman.Create(lowest), Is.Not.Null);
            Assert.That(RSADiffieHellman.Create(highest), Is.Not.Null);
        }

        /// <summary>
        /// A nonce whose length names no supported group is data the peer sent,
        /// so it is rejected rather than resolved to some default group.
        /// </summary>
        [TestCase(0)]
        [TestCase(32)]
        [TestCase(255)]
        [TestCase(257)]
        public void CreateRejectsUnsupportedNonceLength(int length)
        {
            byte[] nonce = new byte[length];

            if (length > 0)
            {
                nonce[^1] = 2;
            }

            Assert.That(
                () => RSADiffieHellman.Create(nonce),
                Throws.ArgumentException);
        }

        [Test]
        public void CreateRejectsNullNonce()
        {
            Assert.That(
                () => RSADiffieHellman.Create(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// The nonce a locally generated key publishes is accepted by the peer
        /// and both sides arrive at the same secret, so the range check does not
        /// reject anything the stack itself produces.
        /// </summary>
        [Test]
        public void PeersDeriveTheSameSecretAgreement()
        {
            RSADiffieHellman local = RSADiffieHellman.Create(RSADiffieHellmanGroup.FFDHE2048);
            RSADiffieHellman remote = RSADiffieHellman.Create(RSADiffieHellmanGroup.FFDHE2048);

            byte[] fromLocal = local.DeriveRawSecretAgreement(
                RSADiffieHellman.Create(remote.GetNonce()));
            byte[] fromRemote = remote.DeriveRawSecretAgreement(
                RSADiffieHellman.Create(local.GetNonce()));

            Assert.That(fromLocal, Is.EqualTo(fromRemote));
            Assert.That(fromLocal, Is.Not.All.Zero);
        }

        /// <summary>
        /// A remote key belonging to a different group is rejected. The check is
        /// repeated here because a key can be built without going through
        /// <c>Create(byte[])</c>.
        /// </summary>
        [Test]
        public void DeriveRawSecretAgreementRejectsRemoteKeyFromAnotherGroup()
        {
            RSADiffieHellman local = RSADiffieHellman.Create(RSADiffieHellmanGroup.FFDHE2048);
            RSADiffieHellman remote = RSADiffieHellman.Create(RSADiffieHellmanGroup.FFDHE3072);

            Assert.That(
                () => local.DeriveRawSecretAgreement(
                    RSADiffieHellman.Create(remote.GetNonce())),
                Throws.ArgumentException);
        }

        [Test]
        public void DeriveRawSecretAgreementRejectsNullRemoteKey()
        {
            RSADiffieHellman local = RSADiffieHellman.Create(RSADiffieHellmanGroup.FFDHE2048);

            Assert.That(
                () => local.DeriveRawSecretAgreement(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// The ffdhe2048 prime as a big endian nonce of the group's size.
        /// </summary>
        private static byte[] GetModulus()
        {
            byte[] modulus = new byte[kFfdhe2048NonceSize];

            for (int ii = 0; ii < modulus.Length; ii++)
            {
                modulus[ii] = byte.Parse(
                    kFfdhe2048Hex.AsSpan(ii * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }

            return modulus;
        }
    }
}
