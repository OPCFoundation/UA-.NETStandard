// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET5_0_OR_GREATER
using System;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that certificate key generation clears temporary elliptic-curve private key material.
    /// </summary>
    [TestFixture]
    [Category("CertificateManager")]
    public sealed class EccScalarLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies that the imported private scalar is cleared after success, import failure, or cancellation.
        /// </summary>
        [TestCase("success")]
        [TestCase("failure")]
        [TestCase("cancellation")]
        public void ImportedEccScalarIsClearedOnEveryExit(string outcome)
        {
            if (!AdditionalEntropyCertificateKeyGenerator.IsEccKeyRegenerationSupported)
            {
                Assert.Ignore(
                    "ECC key regeneration with additional entropy requires .NET 5 or later; " +
                    "the Opc.Ua.Server assembly under test was compiled without it.");
            }

            using var cancellation = new CancellationTokenSource();
            byte[]? captured = null;
            ECDsa Import(ECParameters parameters)
            {
                captured = parameters.D;
                Assert.That(captured, Is.Not.Null.And.Length.EqualTo(32));
                Assert.That(captured, Is.Not.All.EqualTo(0));
                if (outcome == "failure")
                {
                    throw new CryptographicException("import failed");
                }
                if (outcome == "cancellation")
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                return ECDsa.Create(parameters);
            }
            var generator = new AdditionalEntropyCertificateKeyGenerator(
                DefaultCertificateFactory.Instance, count => new byte[count], Import);
            var request = new PushCertificateKeyGenerationRequest
            {
                CertificateTypeId = ObjectTypeIds.EccNistP256ApplicationCertificateType,
                ApplicationUri = "urn:regression:ecc-scalar",
                ApplicationName = "Scalar lifetime",
                SubjectName = "CN=Scalar Lifetime",
                DomainNames = ["localhost"],
                NotBefore = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                NotAfter = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AdditionalEntropy = ByteString.From(new byte[32])
            };
            if (outcome == "success")
            {
                using Certificate certificate = generator.CreateApplicationCertificate(request, cancellation.Token);
                using ECDsa key = certificate.GetECDsaPrivateKey()!;
                using ECDsa publicKey = certificate.GetECDsaPublicKey()!;
                byte[] data = [1, 2, 3];
                byte[] signature = key.SignData(data, HashAlgorithmName.SHA256);
                Assert.That(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256), Is.True);
            }
            else if (outcome == "failure")
            {
                CryptographicException error = Assert.Throws<CryptographicException>(() =>
                    generator.CreateApplicationCertificate(request, cancellation.Token));
                Assert.That(error.Message, Is.EqualTo("import failed"));
            }
            else
            {
                Assert.Throws<OperationCanceledException>(() =>
                    generator.CreateApplicationCertificate(request, cancellation.Token));
            }
            Assert.That(captured, Is.Not.Null);
            Assert.That(Array.TrueForAll(captured, value => value == 0), Is.True,
                "The temporary private scalar must be cleared after import.");
        }
    }
}
#endif
