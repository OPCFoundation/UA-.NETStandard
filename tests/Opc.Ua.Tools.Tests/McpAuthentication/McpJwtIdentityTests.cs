#if NET10_0
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Mcp;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests.McpAuthentication
{
    [TestFixture]
    public sealed class McpJwtIdentityTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t\r\n")]
        [TestCase("Bearer header.payload.signature")]
        [TestCase("header.páyload.signature")]
        [TestCase("header.\u007fpayload.signature")]
        public void InvalidRawTokenIsRejectedWithoutEcho(string? jwt)
        {
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                McpJwtIdentityProvider.ValidateToken(jwt!, new TransportQuotas()))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            if (!string.IsNullOrWhiteSpace(jwt))
            {
                Assert.That(error.Message, Does.Not.Contain(jwt));
            }
        }

        [TestCase("string")]
        [TestCase("bytes")]
        [TestCase("message")]
        public void TokenQuotaAcceptsExactLimitAndRejectsOneOver(string quota)
        {
            var quotas = new TransportQuotas
            {
                MaxStringLength = quota == "string" ? 16 : 1024,
                MaxByteStringLength = quota == "bytes" ? 16 : 1024,
                MaxMessageSize = quota == "message" ? 16 : 1024
            };
            Assert.DoesNotThrow(() => McpJwtIdentityProvider.ValidateToken(new string('a', 16), quotas));
            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                McpJwtIdentityProvider.ValidateToken(new string('a', 17), quotas))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(error.Message, Does.Not.Contain(new string('a', 17)));
        }

        [Test]
        public void ZeroQuotasDoNotIntroduceAnUnrelatedTokenLimit()
        {
            Assert.DoesNotThrow(() => McpJwtIdentityProvider.ValidateToken(new string('a', 1024),
                new TransportQuotas { MaxStringLength = 0, MaxByteStringLength = 0, MaxMessageSize = 0 }));
            Assert.DoesNotThrow(() => McpJwtIdentityProvider.ValidateToken(new string('a', 1024), null));
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task InheritedEncryptedPolicyIsAccepted(MessageSecurityMode mode)
        {
            EndpointDescription endpoint = JwtTestData.Endpoint(mode);
            endpoint.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.UserIdentityTokens[0].SecurityPolicyUri = null;
            Assert.That((await JwtTestData.Provider().CanSatisfyAsync(endpoint.UserIdentityTokens[0],
                JwtTestData.Context(endpoint)).ConfigureAwait(false)).CanSatisfy, Is.True);
        }

        [TestCase(MessageSecurityMode.None, SecurityPolicies.Basic256Sha256, true)]
        [TestCase(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, true)]
        [TestCase(MessageSecurityMode.None, SecurityPolicies.None, false)]
        [TestCase(MessageSecurityMode.Sign, SecurityPolicies.None, false)]
        [TestCase(MessageSecurityMode.None, null, false)]
        [TestCase(MessageSecurityMode.Sign, null, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.None, true)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, null, true)]
        [TestCase(MessageSecurityMode.Invalid, SecurityPolicies.Basic256Sha256, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, "urn:unknown:crypto", false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15, false)]
        public async Task JwtConfidentialityMatrix(
            MessageSecurityMode mode, string? tokenPolicy, bool accepted)
        {
            EndpointDescription endpoint = JwtTestData.Endpoint(mode);
            UserTokenPolicy policy = endpoint.UserIdentityTokens[0];
            policy.SecurityPolicyUri = tokenPolicy;
            CanSatisfyResult result = await JwtTestData.Provider().CanSatisfyAsync(
                policy, JwtTestData.Context(endpoint)).ConfigureAwait(false);
            Assert.That(result.CanSatisfy, Is.EqualTo(accepted));
            if (!accepted)
            {
                Assert.That(result.RejectionReason, Is.Not.Empty);
            }
        }

        [TestCase(UserTokenType.Anonymous, null)]
        [TestCase(UserTokenType.UserName, null)]
        [TestCase(UserTokenType.Certificate, null)]
        [TestCase(UserTokenType.IssuedToken, "urn:example:saml")]
        [TestCase(UserTokenType.IssuedToken, null)]
        public async Task JwtNeverFallsBackToAnotherIdentity(UserTokenType type, string? profile)
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            UserTokenPolicy policy = endpoint.UserIdentityTokens[0];
            policy.TokenType = type;
            policy.IssuedTokenType = profile;
            var provider = JwtTestData.Provider();
            Assert.That(provider.SupportedTokenTypes, Is.EqualTo(new[] { UserTokenType.IssuedToken }));
            Assert.That(provider.SupportedIssuedTokenProfileUris, Is.EqualTo(new[] { Profiles.JwtUserToken }));
            Assert.That((await provider.CanSatisfyAsync(policy, JwtTestData.Context(endpoint))
                .ConfigureAwait(false)).CanSatisfy, Is.False);
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetIdentityAsync(policy, JwtTestData.Context(endpoint)).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task MissingCertificateCannotBeUsedEvenWithEncryptedChannel()
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            endpoint.ServerCertificate = default;
            var provider = JwtTestData.Provider();
            Assert.That((await provider.CanSatisfyAsync(endpoint.UserIdentityTokens[0],
                JwtTestData.Context(endpoint)).ConfigureAwait(false)).CanSatisfy, Is.False);
        }

        [Test]
        public async Task NullTokenPolicyIsRejected()
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            Assert.That((await JwtTestData.Provider().CanSatisfyAsync(null!, JwtTestData.Context(endpoint))
                .ConfigureAwait(false)).CanSatisfy, Is.False);
        }

        [Test]
        public async Task IdentityCannotBeReleasedWithoutInitializedCertificateManager()
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await JwtTestData.Provider().GetIdentityAsync(endpoint.UserIdentityTokens[0],
                    JwtTestData.Context(endpoint)).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(error.Message, Does.Not.Contain(JwtTestData.Token));
        }

        [Test]
        public async Task ProviderCancellationPrecedesPolicyEvaluation()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            EndpointDescription endpoint = JwtTestData.Endpoint();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await JwtTestData.Provider().CanSatisfyAsync(endpoint.UserIdentityTokens[0],
                    JwtTestData.Context(endpoint), cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public async Task ChangedProfileOrConfidentialityIsRecheckedBeforeIdentityRelease()
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            UserTokenPolicy policy = endpoint.UserIdentityTokens[0];
            var provider = JwtTestData.Provider();
            Assert.That((await provider.CanSatisfyAsync(policy, JwtTestData.Context(endpoint))
                .ConfigureAwait(false)).CanSatisfy, Is.True);
            policy.IssuedTokenType = "urn:example:saml";
            ServiceResultException profileError = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetIdentityAsync(policy, JwtTestData.Context(endpoint)).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(profileError.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            policy.IssuedTokenType = Profiles.JwtUserToken;
            endpoint.SecurityMode = MessageSecurityMode.Sign;
            policy.SecurityPolicyUri = SecurityPolicies.None;
            ServiceResultException downgradeError = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.GetIdentityAsync(policy, JwtTestData.Context(endpoint)).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(downgradeError.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(provider.ExpiresAt, Is.EqualTo(DateTime.MaxValue));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CertificateValidationPrecedesJwtIdentityRelease(bool trusted)
        {
            EndpointDescription endpoint = JwtTestData.Endpoint(MessageSecurityMode.None);
            ApplicationConfiguration configuration = JwtTestData.Configuration();
            var certificates = new Mock<ICertificateManager>(MockBehavior.Strict);
            certificates.Setup(manager => manager.ValidateAsync(It.IsAny<CertificateCollection>(),
                null, It.IsAny<global::Opc.Ua.Security.Certificates.CertificateValidationOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CertificateValidationResult(trusted,
                    trusted ? StatusCodes.Good : StatusCodes.BadCertificateUntrusted, [], false));
            configuration.CertificateManager = certificates.Object;
            var provider = JwtTestData.Provider(configuration);
            if (trusted)
            {
                IUserIdentity identity = await provider.GetIdentityAsync(
                    endpoint.UserIdentityTokens[0], JwtTestData.Context(endpoint)).ConfigureAwait(false);
                Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
                Assert.That(identity.PolicyId, Is.EqualTo("jwt"));
                Assert.That(((IssuedIdentityTokenHandler)identity.TokenHandler).DecryptedTokenData,
                    Is.EqualTo(Encoding.UTF8.GetBytes(JwtTestData.Token)));
                Assert.That(provider.ExpiresAt, Is.EqualTo(DateTime.MaxValue),
                    "Unverified token claims must not become a local expiry schedule.");
                (identity as IDisposable)?.Dispose();
            }
            else
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await provider.GetIdentityAsync(endpoint.UserIdentityTokens[0], JwtTestData.Context(endpoint))
                        .ConfigureAwait(false)).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
                Assert.That(error.Message, Does.Not.Contain(JwtTestData.Token));
            }
            certificates.Verify(manager => manager.ValidateAsync(It.IsAny<CertificateCollection>(),
                null, It.IsAny<global::Opc.Ua.Security.Certificates.CertificateValidationOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
            certificates.VerifyNoOtherCalls();
        }

        [Test]
        public async Task JwtTrustValidationOverridesGlobalAutoAcceptanceWithoutReplacingTheManager()
        {
            ApplicationConfiguration configuration = JwtTestData.Configuration();
            configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            var global = new Mock<ICertificateManager>();
            global.SetupProperty(value => value.AcceptError, (_, _) => true);
            configuration.CertificateManager = global.Object;
            global::Opc.Ua.Security.Certificates.CertificateValidationOptions? validationOptions = null;
            global.Setup(manager => manager.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                It.IsAny<TrustListIdentifier?>(),
                It.IsAny<global::Opc.Ua.Security.Certificates.CertificateValidationOptions>(),
                It.IsAny<CancellationToken>()))
                .Callback<CertificateCollection, TrustListIdentifier?,
                global::Opc.Ua.Security.Certificates.CertificateValidationOptions,
                CancellationToken>((_, _, options, _) => validationOptions = options)
                .ReturnsAsync(new CertificateValidationResult(
                false, StatusCodes.BadCertificateUntrusted, [], false));
            EndpointDescription endpoint = JwtTestData.Endpoint();
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await JwtTestData.Provider(configuration).GetIdentityAsync(
                    endpoint.UserIdentityTokens[0], JwtTestData.Context(endpoint)).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(configuration.CertificateManager, Is.SameAs(global.Object));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(error.Message, Does.Not.Contain(JwtTestData.Token));
            Assert.That(validationOptions, Is.Not.Null);
            Assert.That(validationOptions!.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(validationOptions.AcceptError, Is.Not.Null);
            using CertificateCollection chain = Utils.ParseCertificateChainBlob(
                endpoint.ServerCertificate.Memory, NUnitTelemetryContext.Create());
            Assert.That(validationOptions.AcceptError!(chain[0], new ServiceResult(
                StatusCodes.BadCertificateUntrusted)), Is.False);
            Assert.That(configuration.CertificateManager.AcceptError, Is.Not.Null,
                "The JWT check must not mutate the host's global acceptance callback.");
        }

        [Test]
        public async Task JwtRejectsCertificateHostnameMismatchDespiteGlobalAcceptCallback()
        {
            ApplicationConfiguration configuration = JwtTestData.Configuration();
            var certificates = new Mock<ICertificateManager>();
            certificates.SetupProperty(value => value.AcceptError, (_, _) => true);
            certificates.Setup(manager => manager.ValidateAsync(
                It.IsAny<CertificateCollection>(),
                It.IsAny<TrustListIdentifier?>(),
                It.IsAny<global::Opc.Ua.Security.Certificates.CertificateValidationOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CertificateValidationResult(true, StatusCodes.Good, [], false));
            configuration.CertificateManager = certificates.Object;
            EndpointDescription endpoint = JwtTestData.Endpoint();
            endpoint.EndpointUrl = "opc.tcp://wrong-host.invalid:4840";

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await JwtTestData.Provider(configuration).GetIdentityAsync(
                    endpoint.UserIdentityTokens[0], JwtTestData.Context(endpoint)).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(error.Message, Does.Not.Contain(JwtTestData.Token));
        }
    }

    internal static class JwtTestData
    {
        internal const string Token = "test-header.test-payload.test-signature";
        internal const string Url = "opc.tcp://jwt-test.invalid:4840";

        internal static ApplicationConfiguration Configuration()
        {
            var configuration = new ApplicationConfiguration
            {
                ApplicationName = "McpJwtTests",
                ApplicationType = ApplicationType.Client,
                TransportQuotas = new TransportQuotas(),
                SecurityConfiguration = new SecurityConfiguration(),
                ClientConfiguration = new ClientConfiguration()
            };
            configuration.SecurityConfiguration.ApplicationCertificates =
                [new CertificateIdentifier
                {
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }];
            return configuration;
        }

        internal static McpJwtIdentityProvider Provider(ApplicationConfiguration? configuration = null)
        {
            return new McpJwtIdentityProvider(Token, configuration ?? Configuration(),
                NUnitTelemetryContext.Create());
        }

        internal static EndpointDescription Endpoint(
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt, byte level = 100)
        {
            return new EndpointDescription
            {
                EndpointUrl = Url,
                SecurityMode = mode,
                SecurityPolicyUri = mode == MessageSecurityMode.SignAndEncrypt
                    ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None,
                SecurityLevel = level,
                ServerCertificate = s_certificate.ToByteString(),
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "jwt",
                        TokenType = UserTokenType.IssuedToken,
                        IssuedTokenType = Profiles.JwtUserToken,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
        }

        internal static IdentitySelectionContext Context(EndpointDescription endpoint)
        {
            return new IdentitySelectionContext(endpoint, endpoint.UserIdentityTokens,
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
        }

        private static byte[] CreateCertificate()
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=McpJwtTests", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.NonRepudiation, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddUri(new Uri("urn:jwt-test:server"));
            names.AddDnsName("jwt-test.invalid");
            request.CertificateExtensions.Add(names.Build());
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return certificate.RawData;
        }

        private static readonly byte[] s_certificate = CreateCertificate();
    }
}
#endif
