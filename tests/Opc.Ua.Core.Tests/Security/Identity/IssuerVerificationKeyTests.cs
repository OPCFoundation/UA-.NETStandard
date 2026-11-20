/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        public void VerificationKeyViewIsNotDisposable()
        {
            // The consumer-facing view exposes verification only; the resolver
            // retains and disposes the concrete key, so the view must not be
            // IDisposable (consumers cannot dispose what they do not own).
            Assert.That(
                typeof(IDisposable).IsAssignableFrom(typeof(IIssuerVerificationKey)),
                Is.False);
            Assert.That(
                typeof(IIssuerVerificationKey).IsAssignableFrom(typeof(IssuerVerificationKey)),
                Is.True);
        }

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
            using var rsa = RSA.Create(2048);
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new IssuerVerificationKey("kid", rsa, string.Empty))!;
            Assert.That(ex.ParamName, Is.EqualTo("algorithm"));
        }

        [Test]
        public void CtorRejectsUnsupportedKeyType()
        {
            // ECDiffieHellman is an AsymmetricAlgorithm but is neither RSA
            // nor ECDsa, so it should be rejected.
            using var ecdh = ECDiffieHellman.Create();
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new IssuerVerificationKey("kid", ecdh, "RS256"))!;
            Assert.That(ex.ParamName, Is.EqualTo("key"));
        }

        [Test]
        public void VerifyRs256SignatureAcceptsValidSignature()
        {
            using var rsa = RSA.Create(2048);
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
            using var rsa = RSA.Create(2048);
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
            using var rsa = RSA.Create(2048);
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
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] data = Encoding.UTF8.GetBytes("payload");
            byte[] sig = ec.SignData(data, HashAlgorithmName.SHA256);

            using var key = new IssuerVerificationKey("kid-1", ec, "ES256");

            Assert.That(key.VerifySignature(data, sig), Is.True);
        }

        [Test]
        public void VerifyAfterDisposeThrows()
        {
            using var rsa = RSA.Create(2048);
            var key = new IssuerVerificationKey("kid-1", rsa, "RS256");
            key.Dispose();

            Assert.That(
                () => key.VerifySignature([1], [1]),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void VerifyRejectsNullSigningInput()
        {
            using var rsa = RSA.Create(2048);
            using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => key.VerifySignature(null!, [0]))!;
            Assert.That(ex.ParamName, Is.EqualTo("signingInput"));
        }

        [Test]
        public void VerifyRejectsNullSignature()
        {
            using var rsa = RSA.Create(2048);
            using var key = new IssuerVerificationKey("kid-1", rsa, "RS256");

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => key.VerifySignature([0], null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("signature"));
        }

        [Test]
        public void VerifyUnsupportedAlgorithmThrows()
        {
            using var rsa = RSA.Create(2048);
            using var key = new IssuerVerificationKey("kid-1", rsa, "HS256");

            Assert.That(
                () => key.VerifySignature([0], [0]),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
