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
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.AuthorizationService
{
    [TestFixture]
    [Category("AuthorizationService")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class EcdsaJwtIssuerTests
    {
        private const string Issuer = "urn:opcua:test:gds";
        private const string Audience = "urn:opcua:test:server";
        private const string Subject = "subject-1";
        private static readonly string[] s_requestedScopes = ["read", "write"];
        private static readonly string[] s_roles = ["operator"];
        private static readonly string[] s_groups = ["engineering"];

        [Test]
        public async Task IssueAsyncCreatesEs256JwtAndAuthenticatorAcceptsIt()
        {
            using Certificate certificate = CertificateBuilder
                .Create("CN=GDS JWT Signing, O=OPC Foundation")
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            var issuer = new EcdsaJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());

            using AccessToken token = await issuer.IssueAsync(
                new TokenIssuanceRequest(
                    Subject,
                    Audience,
                    s_requestedScopes,
                    new Dictionary<string, object?>
                    {
                        ["roles"] = s_roles,
                        ["groups"] = s_groups
                    },
                    TimeSpan.FromMinutes(30)))
                .ConfigureAwait(false);

            string jwt = Encoding.UTF8.GetString(token.TokenData.ToArray());
            string[] parts = jwt.Split('.');
            Assert.That(parts, Has.Length.EqualTo(3));

            using JsonDocument headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            JsonElement header = headerDocument.RootElement;
            Assert.That(header.GetProperty("alg").GetString(), Is.EqualTo("ES256"));
            Assert.That(header.GetProperty("typ").GetString(), Is.EqualTo("JWT"));
            Assert.That(header.GetProperty("kid").GetString(), Is.EqualTo(certificate.Thumbprint));

            using JsonDocument payloadDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            JsonElement payload = payloadDocument.RootElement;
            Assert.That(payload.GetProperty("iss").GetString(), Is.EqualTo(Issuer));
            Assert.That(payload.GetProperty("aud").GetString(), Is.EqualTo(Audience));
            Assert.That(payload.GetProperty("sub").GetString(), Is.EqualTo(Subject));
            Assert.That(payload.GetProperty("scope").GetString(), Is.EqualTo("read write"));
            Assert.That(payload.TryGetProperty("exp", out _), Is.True);
            Assert.That(payload.TryGetProperty("iat", out _), Is.True);

            using ECDsa publicKey = certificate.GetECDsaPublicKey();
            byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            byte[] signature = Base64UrlDecode(parts[2]);
            Assert.That(VerifyEcdsaSignature(publicKey, signingInput, signature), Is.True);

            ECDsa verifier = certificate.GetECDsaPublicKey();
            using var resolver = new StaticIssuerKeyResolver(
                Issuer,
                new[] { new IssuerVerificationKey(certificate.Thumbprint, verifier, "ES256") });
            AuthenticationResult result = await new JwtAuthenticator(resolver, Audience, TimeSpan.Zero)
                .AuthenticateAsync(CreateContext(jwt))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims!.Subject, Is.EqualTo(Subject));
            Assert.That(claims.Roles, Does.Contain("operator"));
            Assert.That(claims.Groups, Does.Contain("engineering"));
        }

        private static bool VerifyEcdsaSignature(ECDsa publicKey, byte[] signingInput, byte[] signature)
        {
#if NET5_0_OR_GREATER
            return publicKey.VerifyData(
                signingInput,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
#else
            return publicKey.VerifyData(signingInput, signature, HashAlgorithmName.SHA256);
#endif
        }

        private static AuthenticationContext CreateContext(string jwt)
        {
            return new AuthenticationContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt)),
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    PolicyId = "jwt",
                    IssuedTokenType = Profiles.JwtUserToken
                },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
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
