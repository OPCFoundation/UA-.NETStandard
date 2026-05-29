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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.AuthorizationService
{
    [TestFixture]
    [Category("AuthorizationService")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class EcdsaJwtIssuerEdgeCaseTests
    {
        private const string Issuer = "urn:opcua:test:gds-edge";
        private const string Audience = "urn:opcua:test:server-edge";
        private static readonly string[] s_duplicateAndWhitespaceScopes =
            ["read", "read", "", "  ", "write"];
        private static readonly string[] s_dedupedScopes = ["read", "write"];

        [Test]
        public void IssueAsyncWithoutSigningCertificateThrowsBadConfigurationError()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await issuer.IssueAsync(new TokenIssuanceRequest(
                    "subject",
                    Audience,
                    Array.Empty<string>(),
                    new Dictionary<string, object?>(),
                    TimeSpan.FromMinutes(5))));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void IssueAsyncWithoutSubjectThrowsBadIdentityTokenRejected()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await issuer.IssueAsync(new TokenIssuanceRequest(
                    string.Empty,
                    Audience,
                    Array.Empty<string>(),
                    new Dictionary<string, object?>(),
                    TimeSpan.FromMinutes(5))));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public void IssueAsyncWithoutAudienceThrowsBadInvalidArgument()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await issuer.IssueAsync(new TokenIssuanceRequest(
                    "subject",
                    string.Empty,
                    Array.Empty<string>(),
                    new Dictionary<string, object?>(),
                    TimeSpan.FromMinutes(5))));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task IssueAsyncWithNistP384CertificateProducesEs384HeaderAndIeeeP1363SignatureAsync()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP384);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            using AccessToken token = await issuer.IssueAsync(
                new TokenIssuanceRequest(
                    "subject",
                    Audience,
                    Array.Empty<string>(),
                    new Dictionary<string, object?>(),
                    TimeSpan.FromMinutes(5)))
                .ConfigureAwait(false);

            string[] parts = Encoding.UTF8.GetString(token.TokenData.ToArray()).Split('.');
            Assert.That(parts, Has.Length.EqualTo(3));

            using JsonDocument headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            JsonElement header = headerDocument.RootElement;
            Assert.That(header.GetProperty("alg").GetString(), Is.EqualTo("ES384"));
            Assert.That(header.GetProperty("kid").GetString(), Is.EqualTo(certificate.Thumbprint));

            byte[] signature = Base64UrlDecode(parts[2]);
            Assert.That(signature, Has.Length.EqualTo(96));

#if NET5_0_OR_GREATER
            using ECDsa publicKey = certificate.GetECDsaPublicKey();
            byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            Assert.That(
                publicKey.VerifyData(
                    signingInput,
                    signature,
                    HashAlgorithmName.SHA384,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
                Is.True);
#endif
        }

        [Test]
        public async Task IssueAsyncWithRsaCertificateProducesRs256HeaderAsync()
        {
            using Certificate certificate = CertificateBuilder.Create("CN=RSA JWT Signing").CreateForRSA();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            using AccessToken token = await issuer.IssueAsync(
                new TokenIssuanceRequest(
                    "subject",
                    Audience,
                    Array.Empty<string>(),
                    new Dictionary<string, object?>(),
                    TimeSpan.FromMinutes(5)))
                .ConfigureAwait(false);

            string[] parts = Encoding.UTF8.GetString(token.TokenData.ToArray()).Split('.');
            using JsonDocument headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            JsonElement header = headerDocument.RootElement;
            Assert.That(header.GetProperty("alg").GetString(), Is.EqualTo("RS256"));

            using RSA publicKey = certificate.GetRSAPublicKey();
            byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            Assert.That(
                publicKey.VerifyData(
                    signingInput,
                    Base64UrlDecode(parts[2]),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1),
                Is.True);
        }

        [Test]
        public async Task IssueAsyncSkipsReservedAdditionalClaimsAsync()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            var additional = new Dictionary<string, object?>
            {
                ["iss"] = "attacker",
                ["sub"] = "attacker",
                ["aud"] = "attacker",
                ["exp"] = 1L,
                ["nbf"] = 1L,
                ["iat"] = 1L,
                ["scope"] = "evil",
                ["email"] = "user@example.test"
            };

            using AccessToken token = await issuer.IssueAsync(
                new TokenIssuanceRequest(
                    "subject-1",
                    Audience,
                    Array.Empty<string>(),
                    additional,
                    TimeSpan.FromMinutes(5)))
                .ConfigureAwait(false);

            string[] parts = Encoding.UTF8.GetString(token.TokenData.ToArray()).Split('.');
            using JsonDocument payloadDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            JsonElement payload = payloadDocument.RootElement;
            Assert.That(payload.GetProperty("iss").GetString(), Is.EqualTo(Issuer));
            Assert.That(payload.GetProperty("sub").GetString(), Is.EqualTo("subject-1"));
            Assert.That(payload.GetProperty("aud").GetString(), Is.EqualTo(Audience));
            Assert.That(payload.TryGetProperty("scope", out _), Is.False);
            Assert.That(payload.GetProperty("email").GetString(), Is.EqualTo("user@example.test"));
        }

        [Test]
        public async Task IssueAsyncDeduplicatesAndFiltersWhitespaceScopesAsync()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            using AccessToken token = await issuer.IssueAsync(
                new TokenIssuanceRequest(
                    "subject-1",
                    Audience,
                    s_duplicateAndWhitespaceScopes,
                    new Dictionary<string, object?>(),
                    TimeSpan.FromMinutes(5)))
                .ConfigureAwait(false);

            string[] parts = Encoding.UTF8.GetString(token.TokenData.ToArray()).Split('.');
            using JsonDocument payloadDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            Assert.That(payloadDocument.RootElement.GetProperty("scope").GetString(), Is.EqualTo("read write"));
            Assert.That(token.GrantedScopes, Is.EqualTo(s_dedupedScopes));
        }

        [Test]
        public async Task IssueAsyncFallsBackToDefaultLifetimeWhenRequestedIsZeroAsync()
        {
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint },
                DefaultTokenLifetime = TimeSpan.FromMinutes(7)
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            DateTime issuedAt = DateTime.UtcNow;
            using AccessToken token = await issuer.IssueAsync(
                new TokenIssuanceRequest(
                    "subject-1",
                    Audience,
                    Array.Empty<string>(),
                    new Dictionary<string, object?>(),
                    TimeSpan.Zero))
                .ConfigureAwait(false);

            TimeSpan delta = token.ExpiresAt - issuedAt;
            Assert.That(delta, Is.GreaterThanOrEqualTo(TimeSpan.FromMinutes(6))
                .And.LessThanOrEqualTo(TimeSpan.FromMinutes(8)));
        }

        [Test]
        public void IssuerUriFallsBackToBuiltInWhenOptionsAndDefaultAreEmpty()
        {
            var options = new AuthorizationServiceOptions();
            using Certificate certificate = CreateEcCertificate(ECCurve.NamedCurves.nistP256);
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var issuer = new EcdsaJwtIssuer(
                options,
                certificateProvider,
                NUnitTelemetryContext.Create());

            Assert.That(issuer.IssuerUri, Is.EqualTo("urn:opcua:gds:authorization-service"));
            Assert.That(issuer.ProfileUri, Is.EqualTo(Profiles.JwtUserToken));
        }

        private static Certificate CreateEcCertificate(ECCurve curve)
        {
            return CertificateBuilder
                .Create("CN=EcdsaJwtIssuerEdgeCaseTests")
                .SetECCurve(curve)
                .CreateForECDsa();
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
