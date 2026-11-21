// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("X509IdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class X509IdentityTokenHandlerTests
    {
        /// <summary>
        /// Verifies the <see cref="X509IdentityTokenHandler(CertificateIdentifier,
        /// ICertificatePasswordProvider, ICertificateProvider)"/> ctor:
        /// the handler is a POCO (no live cert reference) and
        /// <see cref="X509IdentityTokenHandler.SignAsync"/> resolves
        /// the private-key cert via <see cref="ICertificateProvider"/>
        /// on each call.
        /// </summary>
        [Test]
        public async Task CertificateIdentifierCtorResolvesViaProviderForSignAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string storePath = Path.Combine(
                Path.GetTempPath(),
                "opcua-x509handler-id-" + System.Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(storePath);
            try
            {
                using Certificate cert = CertificateBuilder
                    .Create("CN=X509HandlerCertIdentifier, O=OPC Foundation")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                await cert.AddToStoreAsync(
                    CertificateStoreType.Directory,
                    storePath,
                    password: null,
                    telemetry).ConfigureAwait(false);

                var id = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = storePath,
                    Thumbprint = cert.Thumbprint
                };

                using var manager = new CertificateManager(telemetry);
                var passwordProvider = new CertificatePasswordProvider();
                var handler = new X509IdentityTokenHandler(
                    id,
                    passwordProvider,
                    manager.CertificateProvider);

                Assert.That(handler.Token, Is.Not.Null,
                    "Wire-format X509IdentityToken must be populated.");
                Assert.That(((X509IdentityToken)handler.Token).CertificateData.Length,
                    Is.GreaterThan(0));

                SignatureData signature = await handler.SignAsync(
                    [0x01, 0x02, 0x03, 0x04],
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

                Assert.That(signature, Is.Not.Null);
                Assert.That(signature.Signature.Length, Is.GreaterThan(0));
            }
            finally
            {
                if (Directory.Exists(storePath))
                {
                    Directory.Delete(storePath, true);
                }
            }
        }

        /// <summary>
        /// A user identity token signature that carries an unexpected /
        /// mismatched signature algorithm must be surfaced as
        /// <see cref="StatusCodes.BadUserSignatureInvalid"/>, not the
        /// channel-level <see cref="StatusCodes.BadSecurityChecksFailed"/> that
        /// the shared <see cref="SecurityPolicies.VerifySignatureData"/> raises
        /// internally (OPC UA Part 4 5.7.3.3). Regression for CTT Security User
        /// X509 018, which prefers BadUserSignatureInvalid on secure channels.
        /// </summary>
        [Test]
        public async Task VerifyWithMismatchedSignatureAlgorithmYieldsBadUserSignatureInvalid()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=X509HandlerWrongAlg, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            var token = new X509IdentityToken
            {
                CertificateData = cert.RawData.ToByteString()
            };
            var handler = new X509IdentityTokenHandler(token);

            // Basic256Sha256 expects RsaSha256; RsaSha1 is a valid-but-mismatched
            // algorithm that makes VerifySignatureData fall through to its
            // BadSecurityChecksFailed guard. The signature bytes are irrelevant
            // because the algorithm mismatch short-circuits before the crypto verify.
            var signature = new SignatureData
            {
                Algorithm = SecurityAlgorithms.RsaSha1,
                Signature = new byte[] { 0x00 }.ToByteString()
            };

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                () => handler.VerifyAsync(
                    [0x01, 0x02, 0x03, 0x04],
                    signature,
                    SecurityPolicies.Basic256Sha256).AsTask());

            Assert.That(
                ex.StatusCode,
                Is.EqualTo(StatusCodes.BadUserSignatureInvalid),
                "A user-token signature with a mismatched algorithm is an invalid user signature.");
        }
    }
}
