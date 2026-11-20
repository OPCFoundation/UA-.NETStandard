// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#pragma warning disable CA2000
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("IssuedIdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class IssuedIdentityTokenHandlerTests
    {
        private const string kSecurityPolicyUri = SecurityPolicies.Basic256Sha256;

        /// <summary>
        /// Part 4 §7.40.6: an encrypted IssuedIdentityToken uses the EncryptedSecret
        /// format, and the server ignores encryptionAlgorithm.
        /// </summary>
        [TestCase(null)]
        [TestCase(SecurityAlgorithms.RsaOaep)]
        public async Task DecryptSupportsRsaEncryptedSecretFormatAsync(string? encryptionAlgorithm)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(kSecurityPolicyUri)!;
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data!;
            byte[] expectedToken = Encoding.UTF8.GetBytes("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ0ZXN0In0.");

            using Certificate certificate = CertificateBuilder
                .Create("CN=Issued Identity Token Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] encryptedSecret = UserNameIdentityTokenHandlerTests.CreateRsaEncryptedSecret(
                context,
                certificate,
                kSecurityPolicyUri,
                expectedToken,
                receiverNonce);

            var token = new IssuedIdentityToken
            {
                TokenData = encryptedSecret.ToByteString(),
                EncryptionAlgorithm = encryptionAlgorithm
            };

            var tokenHandler = new IssuedIdentityTokenHandler(token);
            await tokenHandler.DecryptAsync(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context).ConfigureAwait(false);

            Assert.That(tokenHandler.DecryptedTokenData, Is.EqualTo(expectedToken));
        }

        /// <summary>
        /// The envelope type id may use the seven-byte Numeric NodeId encoding instead of
        /// the compact four-byte one; both identify an RsaEncryptedSecret.
        /// </summary>
        [Test]
        public async Task DecryptSupportsNonCompactRsaEncryptedSecretTypeIdAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(kSecurityPolicyUri)!;
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data!;
            byte[] expectedToken = Encoding.UTF8.GetBytes("non-compact-type-id");

            using Certificate certificate = CertificateBuilder
                .Create("CN=Issued Identity Token Numeric Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] nonCompact = UserNameIdentityTokenHandlerTests.CreateRsaEncryptedSecret(
                context,
                certificate,
                kSecurityPolicyUri,
                expectedToken,
                receiverNonce,
                numericTypeId: true);
            Assert.That(nonCompact[0], Is.EqualTo(0x02));

            var tokenHandler = new IssuedIdentityTokenHandler(
                new IssuedIdentityToken { TokenData = nonCompact.ToByteString() });
            await tokenHandler.DecryptAsync(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context).ConfigureAwait(false);

            Assert.That(tokenHandler.DecryptedTokenData, Is.EqualTo(expectedToken));
        }

        [Test]
        public async Task DecryptRejectsRsaEncryptedSecretLongerThanTheIssuedTokenLimitAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(kSecurityPolicyUri)!;
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data!;
            byte[] tooLongToken = new byte[IdentityTokenLimits.MaxIssuedTokenDataLength + 1];

            using Certificate certificate = CertificateBuilder
                .Create("CN=Issued Identity Token Limit Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] encryptedSecret = UserNameIdentityTokenHandlerTests.CreateRsaEncryptedSecret(
                context,
                certificate,
                kSecurityPolicyUri,
                tooLongToken,
                receiverNonce);

            var tokenHandler = new IssuedIdentityTokenHandler(
                new IssuedIdentityToken { TokenData = encryptedSecret.ToByteString() });

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await tokenHandler.DecryptAsync(
                    certificate,
                    Nonce.CreateNonce(securityPolicy, receiverNonce),
                    kSecurityPolicyUri,
                    context).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task DecryptKeepsLegacyRsaEncryptedTokenPathAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(kSecurityPolicyUri)!;
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data!;
            byte[] expectedToken = Encoding.UTF8.GetBytes("legacy-issued-token");

            using Certificate certificate = CertificateBuilder
                .Create("CN=Issued Identity Token Legacy Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            EncryptedData encryptedData = SecurityPolicies.Default.Encrypt(
                certificate,
                kSecurityPolicyUri,
                Utils.Append(expectedToken, receiverNonce));

            var token = new IssuedIdentityToken
            {
                TokenData = encryptedData.Data.ToByteString(),
                EncryptionAlgorithm = encryptedData.Algorithm
            };

            var tokenHandler = new IssuedIdentityTokenHandler(token);
            await tokenHandler.DecryptAsync(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context).ConfigureAwait(false);

            Assert.That(tokenHandler.DecryptedTokenData, Is.EqualTo(expectedToken));
        }
    }
}
