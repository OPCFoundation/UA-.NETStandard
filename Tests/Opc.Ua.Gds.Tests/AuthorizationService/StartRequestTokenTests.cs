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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server;
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
    public sealed class StartRequestTokenTests
    {
        private const string Issuer = "urn:opcua:test:gds";
        private const string Audience = "urn:opcua:test:server";
        private static readonly string[] s_requestedRoles = ["operator"];
        private static readonly string[] s_securityAdminRole = ["SecurityAdmin"];
        private static readonly string[] s_operatorAndAdminRoles = ["operator", "SecurityAdmin"];

        [Test]
        public async Task StartThenFinishIssuesJwtAcceptedByJwtAuthenticator()
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
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");
            // The operator explicitly authorizes the requested roles for the
            // authenticated caller; without this callback no roles are granted.
            options.AuthorizeRoles = (identity, audience, requestedRoles) => requestedRoles;

            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);
            var manager = new AuthorizationServiceManager(provider, issuer, options);

            (ByteString serviceData, Guid requestId) = await manager
                .StartRequestTokenAsync(
                    Audience,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes("read write")),
                    new UserIdentity("sysadmin", []))
                .ConfigureAwait(false);

            AccessTokenResult tokenResult = await manager
                .FinishRequestTokenAsync(
                    requestId,
                    s_requestedRoles.ToArrayOf(),
                    new UserNameIdentityToken { UserName = "sysadmin" },
                    new SignatureData())
                .ConfigureAwait(false);

            Assert.That(serviceData, Is.EqualTo(ByteString.Empty));
            Assert.That(tokenResult.TokenType, Is.EqualTo("JWT"));
            Assert.That(tokenResult.PolicyId, Is.EqualTo("jwt"));
            Assert.That(tokenResult.AccessToken, Does.Contain("."));
            Assert.That(tokenResult.RefreshToken, Is.Not.Null.And.Not.Empty);
            Assert.That(tokenResult.RefreshTokenExpiryTime, Is.GreaterThan(DateTime.UtcNow));

            ECDsa verifier = certificate.GetECDsaPublicKey();
            using var resolver = new StaticIssuerKeyResolver(
                Issuer,
                [new IssuerVerificationKey(certificate.Thumbprint, verifier, "ES256")]);
            AuthenticationResult result = await new JwtAuthenticator(resolver, Audience, TimeSpan.Zero)
                .AuthenticateAsync(CreateContext(tokenResult.AccessToken))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity!.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims!.Subject, Is.EqualTo("sysadmin"));
            Assert.That(claims.Roles, Does.Contain("operator"));
        }

        [Test]
        public async Task FinishDoesNotGrantRequestedRolesWithoutAuthorization()
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
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");
            // No AuthorizeRoles callback is configured: the provider must be
            // fail-closed and grant no roles at all.

            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);
            var manager = new AuthorizationServiceManager(provider, issuer, options);

            (_, Guid requestId) = await manager
                .StartRequestTokenAsync(
                    Audience,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes("read")),
                    new UserIdentity("operator", []))
                .ConfigureAwait(false);

            AccessTokenResult tokenResult = await manager
                .FinishRequestTokenAsync(
                    requestId,
                    s_securityAdminRole.ToArrayOf(),
                    new UserNameIdentityToken { UserName = "operator" },
                    new SignatureData())
                .ConfigureAwait(false);

            IIdentityClaims claims = await AuthenticateAsync(certificate, tokenResult.AccessToken)
                .ConfigureAwait(false);

            Assert.That(claims.Subject, Is.EqualTo("operator"));
            Assert.That(claims.Roles, Is.Empty,
                "A requested role must never be granted unless the operator explicitly authorizes it.");
        }

        [Test]
        public async Task FinishBindsSubjectToAuthenticatedIdentityNotRequestToken()
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
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");

            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);
            var manager = new AuthorizationServiceManager(provider, issuer, options);

            (_, Guid requestId) = await manager
                .StartRequestTokenAsync(
                    Audience,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes("read")),
                    new UserIdentity("authenticated-user", []))
                .ConfigureAwait(false);

            // The client attempts to spoof a privileged subject through the
            // request token; the issued token must ignore it.
            AccessTokenResult tokenResult = await manager
                .FinishRequestTokenAsync(
                    requestId,
                    Array.Empty<string>().ToArrayOf(),
                    new UserNameIdentityToken { UserName = "admin" },
                    new SignatureData())
                .ConfigureAwait(false);

            IIdentityClaims claims = await AuthenticateAsync(certificate, tokenResult.AccessToken)
                .ConfigureAwait(false);

            Assert.That(claims.Subject, Is.EqualTo("authenticated-user"),
                "The subject must be bound to the identity authenticated on the session, "
                + "not to the client-supplied request token.");
        }

        [Test]
        public async Task LegacyRequestAccessTokenIssuesAnonymousUnprivilegedToken()
        {
            using Certificate certificate = CreateSigningCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");

            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);

#pragma warning disable CS0618 // exercising the obsolete single-call wire method on purpose
            string jwt = await provider
                .RequestAccessTokenAsync(new UserNameIdentityToken { UserName = "admin" }, Audience)
                .ConfigureAwait(false);
#pragma warning restore CS0618

            IIdentityClaims claims = await AuthenticateAsync(certificate, jwt).ConfigureAwait(false);
            Assert.That(claims.Subject, Is.EqualTo("anonymous"),
                "The obsolete single-call RequestAccessToken must not trust the client-supplied "
                + "token for the subject.");
            Assert.That(claims.Roles, Is.Empty,
                "The obsolete single-call RequestAccessToken must not grant roles.");
        }

        [Test]
        public async Task FinishGrantsOnlyTheIntersectionOfAuthorizedAndRequestedRoles()
        {
            using Certificate certificate = CreateSigningCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");
            // The callback tries to grant an extra, unrequested role; it must be
            // dropped because the granted set is intersected with the request.
            options.AuthorizeRoles = (identity, audience, requestedRoles) => s_operatorAndAdminRoles;

            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);
            var manager = new AuthorizationServiceManager(provider, issuer, options);

            (_, Guid requestId) = await manager
                .StartRequestTokenAsync(
                    Audience,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes("read")),
                    new UserIdentity("operator", []))
                .ConfigureAwait(false);

            AccessTokenResult tokenResult = await manager
                .FinishRequestTokenAsync(
                    requestId,
                    s_requestedRoles.ToArrayOf(),
                    new UserNameIdentityToken { UserName = "operator" },
                    new SignatureData())
                .ConfigureAwait(false);

            IIdentityClaims claims = await AuthenticateAsync(certificate, tokenResult.AccessToken)
                .ConfigureAwait(false);
            Assert.That(claims.Roles, Does.Contain("operator"));
            Assert.That(claims.Roles, Does.Not.Contain("SecurityAdmin"),
                "A role the caller did not request must never be granted, even if the "
                + "authorization callback returns it.");
        }

        [Test]
        public async Task FinishGrantsNoRolesWhenAuthorizationReturnsEmpty()
        {
            using Certificate certificate = CreateSigningCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint }
            };
            options.AllowedAudiences.Add(Audience);
            options.DefaultScopes.Add("read");
            options.AuthorizeRoles = (identity, audience, requestedRoles) => [];

            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            var provider = new InMemoryAccessTokenProvider(issuer, options);
            var manager = new AuthorizationServiceManager(provider, issuer, options);

            (_, Guid requestId) = await manager
                .StartRequestTokenAsync(
                    Audience,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes("read")),
                    new UserIdentity("operator", []))
                .ConfigureAwait(false);

            AccessTokenResult tokenResult = await manager
                .FinishRequestTokenAsync(
                    requestId,
                    s_requestedRoles.ToArrayOf(),
                    new UserNameIdentityToken { UserName = "operator" },
                    new SignatureData())
                .ConfigureAwait(false);

            IIdentityClaims claims = await AuthenticateAsync(certificate, tokenResult.AccessToken)
                .ConfigureAwait(false);
            Assert.That(claims.Roles, Is.Empty,
                "An authorization callback that returns no roles must yield a token with no roles.");
        }

        private static Certificate CreateSigningCertificate()
        {
            return CertificateBuilder
                .Create("CN=GDS JWT Signing, O=OPC Foundation")
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
        }

        private static async Task<IIdentityClaims> AuthenticateAsync(Certificate certificate, string jwt)
        {
            ECDsa verifier = certificate.GetECDsaPublicKey();
            using var resolver = new StaticIssuerKeyResolver(
                Issuer,
                [new IssuerVerificationKey(certificate.Thumbprint, verifier, "ES256")]);
            AuthenticationResult result = await new JwtAuthenticator(resolver, Audience, TimeSpan.Zero)
                .AuthenticateAsync(CreateContext(jwt))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            var claims = result.Identity as IIdentityClaims;
            Assert.That(claims, Is.Not.Null);
            return claims!;
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
    }
}
