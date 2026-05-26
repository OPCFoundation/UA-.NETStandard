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
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    /// <summary>
    /// Tests for <see cref="IssuerVerificationKey"/> signature verification
    /// across the JWS algorithms required by Part 6 §6.5.2 (RS256, ES256).
    /// </summary>
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class IssuerVerificationKeyTests
    {
        [Test]
        public void CtorRejectsNullKey()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new IssuerVerificationKey("kid", null!, "RS256"))!;
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        [Test]
        public void CtorRejectsEmptyAlgorithm()
        {
            using RSA rsa = RSA.Create(2048);
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new IssuerVerificationKey("kid", rsa, ""))!;
            Assert.That(ex.ParamName, Is.EqualTo("algorithm"));
        }

        [Test]
        public void CtorRejectsUnsupportedKeyType()
        {
            // ECDiffieHellman is an AsymmetricAlgorithm but is neither RSA
            // nor ECDsa, so it should be rejected.
            using ECDiffieHellman ecdh = ECDiffieHellman.Create();
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new IssuerVerificationKey("kid", ecdh, "RS256"))!;
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        [Test]
        public void VerifyRs256SignatureAcceptsValidSignature()
        {
            using RSA rsa = RSA.Create(2048);
            byte[] data = Encoding.UTF8.GetBytes("eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJhbGljZSJ9");
            byte[] sig = rsa.SignData(
                data,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");

            Assert.That(key.VerifySignature(data, sig), Is.True);
        }

        [Test]
        public void VerifyRs256SignatureRejectsTamperedData()
        {
            using RSA rsa = RSA.Create(2048);
            byte[] data = Encoding.UTF8.GetBytes("original payload");
            byte[] sig = rsa.SignData(
                data,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            byte[] tampered = Encoding.UTF8.GetBytes("tampered payload");

            using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");

            Assert.That(key.VerifySignature(tampered, sig), Is.False);
        }

        [Test]
        public void VerifyPs256SignatureAcceptsValidSignature()
        {
            using RSA rsa = RSA.Create(2048);
            byte[] data = Encoding.UTF8.GetBytes("payload");
            byte[] sig = rsa.SignData(
                data,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

            using var key = new IssuerVerificationKey("kid-1", rsa, "PS256");

            Assert.That(key.VerifySignature(data, sig), Is.True);
        }

        [Test]
        public void VerifyEs256SignatureAcceptsValidSignature()
        {
            using ECDsa ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] data = Encoding.UTF8.GetBytes("payload");
            byte[] sig = ec.SignData(data, HashAlgorithmName.SHA256);

            using var key = new IssuerVerificationKey("kid-1", ec, "ES256");

            Assert.That(key.VerifySignature(data, sig), Is.True);
        }

        [Test]
        public void VerifyAfterDisposeThrows()
        {
            using RSA rsa = RSA.Create(2048);
            var key = new IssuerVerificationKey("kid-1", rsa, "RS256");
            key.Dispose();

            Assert.That(
                () => key.VerifySignature(new byte[] { 1 }, new byte[] { 1 }),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void VerifyRejectsNullSigningInput()
        {
            using RSA rsa = RSA.Create(2048);
            using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => key.VerifySignature(null!, new byte[] { 0 }))!;
            Assert.That(ex.ParamName, Is.EqualTo("signingInput"));
        }

        [Test]
        public void VerifyRejectsNullSignature()
        {
            using RSA rsa = RSA.Create(2048);
            using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => key.VerifySignature(new byte[] { 0 }, null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("signature"));
        }

        [Test]
        public void VerifyUnsupportedAlgorithmThrows()
        {
            using RSA rsa = RSA.Create(2048);
            using var key = new IssuerVerificationKey("kid-1", rsa, "HS256");

            Assert.That(
                () => key.VerifySignature(new byte[] { 0 }, new byte[] { 0 }),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
