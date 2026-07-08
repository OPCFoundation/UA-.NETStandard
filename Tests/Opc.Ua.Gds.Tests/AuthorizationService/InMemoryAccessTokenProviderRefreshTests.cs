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
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.AuthorizationService
{
    [TestFixture]
    [Category("AuthorizationService")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class InMemoryAccessTokenProviderRefreshTests
    {
        private const string Issuer = "urn:opcua:test:gds";
        private const string Audience = "urn:opcua:test:server";
        private static readonly string[] s_authenticatedUserRole = ["AuthenticatedUser"];

        [Test]
        public async Task RefreshTokenAsyncReturnsNewAccessAndRefreshToken()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(provider).ConfigureAwait(false);
            AccessTokenResult refreshed = await provider
                .RefreshTokenAsync(Audience, original.RefreshToken!)
                .ConfigureAwait(false);

            Assert.That(refreshed.AccessToken, Does.Contain("."));
            Assert.That(refreshed.AccessTokenExpiryTime, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(refreshed.RefreshToken, Is.Not.Null.And.Not.Empty);
            Assert.That(refreshed.RefreshToken, Is.Not.EqualTo(original.RefreshToken));
            Assert.That(refreshed.RefreshTokenExpiryTime, Is.GreaterThan(DateTime.UtcNow));
        }

        [Test]
        public async Task RefreshTokenAsyncRotatesRefreshTokenSoOldOneIsRevoked()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(provider).ConfigureAwait(false);
            await provider.RefreshTokenAsync(Audience, original.RefreshToken!).ConfigureAwait(false);

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync(Audience, original.RefreshToken!),
                StatusCodes.BadIdentityTokenRejected,
                "already been used").ConfigureAwait(false);
        }

        [Test]
        public async Task RefreshTokenAsyncRejectsUnknownToken()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync(Audience, new string('A', 64)),
                StatusCodes.BadIdentityTokenRejected,
                "Unknown refresh token").ConfigureAwait(false);
        }

        [Test]
        public async Task RefreshTokenAsyncRejectsExpiredToken()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            options.DefaultRefreshTokenLifetime = TimeSpan.FromMilliseconds(50);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(provider).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync(Audience, original.RefreshToken!),
                StatusCodes.BadTimeout,
                "expired").ConfigureAwait(false);
        }

        [Test]
        public async Task RefreshTokenAsyncRejectsResourceIdMismatch()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(provider, "urn:resource:A").ConfigureAwait(false);

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync("urn:resource:B", original.RefreshToken!),
                StatusCodes.BadInvalidArgument,
                "different resource").ConfigureAwait(false);
        }

        [Test]
        public async Task RefreshTokenAsyncRejectsEmptyInputs()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync(string.Empty, new string('A', 64)),
                StatusCodes.BadInvalidArgument,
                "Resource id is required").ConfigureAwait(false);

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync(Audience, string.Empty),
                StatusCodes.BadInvalidArgument,
                "Refresh token is required").ConfigureAwait(false);
        }

        [Test]
        public async Task RefreshTokenAsyncDisabledFlagThrowsBadNotSupported()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            options.EnableRefreshTokens = false;
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(provider).ConfigureAwait(false);

            Assert.That(original.RefreshToken, Is.Null);
            Assert.That(original.RefreshTokenExpiryTime, Is.EqualTo(DateTime.MinValue));

            await AssertServiceResultAsync(
                () => provider.RefreshTokenAsync(Audience, new string('A', 64)),
                StatusCodes.BadNotSupported,
                "disabled").ConfigureAwait(false);
        }

        [Test]
        public async Task RefreshTokenAsyncDoesNotWidenScopesOrRoles()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            options.DefaultScopes.Add("write");
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(
                provider,
                Audience,
                ["read"],
                ["AuthenticatedUser"]).ConfigureAwait(false);
            AccessTokenResult refreshed = await provider
                .RefreshTokenAsync(Audience, original.RefreshToken!)
                .ConfigureAwait(false);

            using var payload = JsonDocument.Parse(Base64UrlDecode(refreshed.AccessToken.Split('.')[1]));
            JsonElement root = payload.RootElement;
            Assert.That(root.GetProperty("scope").GetString(), Is.EqualTo("read"));
            string[] roles = [.. root.GetProperty("roles")
                .EnumerateArray()
                .Select(role => role.GetString() ?? string.Empty)];
            Assert.That(roles, Is.EqualTo(s_authenticatedUserRole));
        }

        [Test]
        public async Task RefreshTokenAsyncIsThreadSafe()
        {
            using Certificate certificate = CreateCertificate();
            using var certificateProvider = new InProcessCertificateProvider(certificate);
            AuthorizationServiceOptions options = CreateOptions(certificate);
            InMemoryAccessTokenProvider provider = CreateProvider(certificateProvider, options);

            AccessTokenResult original = await IssueTokenAsync(provider).ConfigureAwait(false);
            Task<AccessTokenResult>[] tasks = [.. Enumerable.Range(0, 8)
                .Select(_ => Task.Run(async () => await provider
                    .RefreshTokenAsync(Audience, original.RefreshToken!)
                    .ConfigureAwait(false)))];

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
            }

            Assert.That(tasks.Count(task => task.Status == TaskStatus.RanToCompletion), Is.EqualTo(1));
            Assert.That(tasks.Count(IsRejectedReplay), Is.EqualTo(7));
        }

        private static Certificate CreateCertificate()
        {
            return CertificateBuilder
                .Create("CN=GDS JWT Signing, O=OPC Foundation")
                .SetECCurve(ECCurve.NamedCurves.nistP256)
                .CreateForECDsa();
        }

        private static AuthorizationServiceOptions CreateOptions(Certificate certificate)
        {
            var options = new AuthorizationServiceOptions
            {
                IssuerUri = Issuer,
                SigningCertificate = new CertificateIdentifier { Thumbprint = certificate.Thumbprint },
                // These tests exercise refresh mechanics, so the operator authorizes
                // the requested roles; the fail-closed default (no roles) is covered
                // by StartRequestTokenTests.
                AuthorizeRoles = (identity, audience, requestedRoles) => requestedRoles
            };
            return options;
        }

        private static InMemoryAccessTokenProvider CreateProvider(
            InProcessCertificateProvider certificateProvider,
            AuthorizationServiceOptions options)
        {
            var issuer = new CertificateJwtIssuer(options, certificateProvider, NUnitTelemetryContext.Create());
            return new InMemoryAccessTokenProvider(issuer, options);
        }

        private static async Task<AccessTokenResult> IssueTokenAsync(
            InMemoryAccessTokenProvider provider,
            string resourceId = Audience,
            string[] scopes = null,
            string[] roles = null)
        {
            scopes ??= ["read", "write"];
            roles ??= ["operator"];
            (_, Guid requestId) = await provider
                .StartRequestTokenAsync(
                    resourceId,
                    "jwt",
                    ByteString.From(Encoding.UTF8.GetBytes(string.Join(" ", scopes))),
                    new UserIdentity("sysadmin", []))
                .ConfigureAwait(false);

            return await provider
                .FinishRequestTokenAsync(
                    requestId,
                    roles.ToArrayOf(),
                    new UserNameIdentityToken { UserName = "sysadmin" },
                    new SignatureData())
                .ConfigureAwait(false);
        }

        private static Task<ServiceResultException> AssertServiceResultAsync(
            Func<ValueTask<AccessTokenResult>> action,
            StatusCode statusCode,
            string message)
        {
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await action().ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.StatusCode, Is.EqualTo(statusCode));
            Assert.That(ex.Message, Does.Contain(message));
            return Task.FromResult(ex);
        }

        private static bool IsRejectedReplay(Task<AccessTokenResult> task)
        {
            if (!task.IsFaulted || task.Exception == null)
            {
                return false;
            }

            return task.Exception.Flatten().InnerExceptions.Any(
                ex => ex is ServiceResultException sre &&
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected);
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
