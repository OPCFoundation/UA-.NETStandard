#if NET10_0
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthentication
{
    [TestFixture]
    public sealed class McpJwtEndpointTests
    {
        [Test]
        public async Task DefaultSelectionUsesStrongestCompatibleJwtNotAnonymousOrSaml()
        {
            EndpointDescription anonymous = JwtTestData.Endpoint(level: 255);
            anonymous.UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous)];
            EndpointDescription saml = JwtTestData.Endpoint(level: 254);
            saml.UserIdentityTokens[0].IssuedTokenType = "urn:example:saml";
            EndpointDescription weak = JwtTestData.Endpoint(level: 10);
            EndpointDescription strong = JwtTestData.Endpoint(level: 200);
            EndpointDescription selected = await SelectAsync([weak, anonymous, strong, saml])
                .ConfigureAwait(false);
            Assert.That(selected, Is.SameAs(strong));
            Assert.That(selected.UserIdentityTokens[0].IssuedTokenType, Is.EqualTo(Profiles.JwtUserToken));
        }

        [TestCase(UserTokenType.Anonymous, null)]
        [TestCase(UserTokenType.IssuedToken, "urn:example:saml")]
        public async Task NoCompatibleJwtEndpointFailsWithoutFallback(UserTokenType type, string? profile)
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            endpoint.UserIdentityTokens[0].TokenType = type;
            endpoint.UserIdentityTokens[0].IssuedTokenType = profile;
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await SelectAsync([endpoint]).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task EmptyDiscoveryFailsExplicitly()
        {
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await SelectAsync([]).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task ExplicitModePolicyAndTransportFiltersAreAppliedBeforeRanking()
        {
            EndpointDescription wrongTransport = JwtTestData.Endpoint(level: 255);
            wrongTransport.EndpointUrl = "https://jwt-test.invalid:4840";
            EndpointDescription wrongMode = JwtTestData.Endpoint(level: 254);
            EndpointDescription wrongPolicy = JwtTestData.Endpoint(MessageSecurityMode.Sign, 253);
            wrongPolicy.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
            EndpointDescription match = JwtTestData.Endpoint(MessageSecurityMode.Sign, 10);
            match.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            Assert.That(await SelectAsync([wrongTransport, wrongMode, wrongPolicy, match],
                "Sign", "Basic256Sha256").ConfigureAwait(false), Is.SameAs(match));
        }

        [Test]
        public async Task DisabledTokenEncryptionPolicyIsNotSelected()
        {
            EndpointDescription endpoint = JwtTestData.Endpoint(MessageSecurityMode.None);
            IdentitySelectionContext context = JwtTestData.Context(endpoint) with
            {
                EnabledSecurityPolicyUris = [SecurityPolicies.None]
            };
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await JwtTestData.Provider().SelectUserTokenPolicyAsync(context).ConfigureAwait(false))
                .ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task EndpointSelectorHonorsConfiguredEnabledPolicySnapshot()
        {
            ApplicationConfiguration configuration = JwtTestData.Configuration();
            configuration.SecurityConfiguration.ApplicationCertificates =
                [new CertificateIdentifier
                {
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }];
            EndpointDescription disabled = JwtTestData.Endpoint(level: 200);
            disabled.UserIdentityTokens[0].SecurityPolicyUri = SecurityPolicies.ECC_nistP256;
            EndpointDescription allowed = JwtTestData.Endpoint(level: 10);
            allowed.UserIdentityTokens[0].SecurityPolicyUri = SecurityPolicies.None;
            EndpointDescription selected = await OpcUaSessionManager.SelectJwtEndpointAsync(
                [disabled, allowed], JwtTestData.Url, null, null, JwtTestData.Provider(configuration),
                configuration, CancellationToken.None).ConfigureAwait(false);
            Assert.That(selected, Is.SameAs(allowed));
            Assert.That(selected.UserIdentityTokens[0].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Test]
        public async Task EndpointSelectorSkipsUnsupportedSecureChannelPolicies()
        {
            ApplicationConfiguration configuration = JwtTestData.Configuration();
            configuration.SecurityConfiguration.ApplicationCertificates =
                [new CertificateIdentifier
                {
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }];
            EndpointDescription unsupported = JwtTestData.Endpoint(level: 200);
            unsupported.SecurityPolicyUri = SecurityPolicies.ECC_nistP256;
            EndpointDescription supported = JwtTestData.Endpoint(level: 10);

            EndpointDescription selected = await OpcUaSessionManager.SelectJwtEndpointAsync(
                [unsupported, supported], JwtTestData.Url, null, null,
                JwtTestData.Provider(configuration), configuration, CancellationToken.None).ConfigureAwait(false);

            Assert.That(selected, Is.SameAs(supported));
        }

        [Test]
        public async Task EndpointSelectorUsesRequestedHostAndPortButPreservesAdvertisedPath()
        {
            EndpointDescription endpoint = JwtTestData.Endpoint();
            endpoint.EndpointUrl = "opc.tcp://internal.invalid:4840/ua/primary";
            ApplicationConfiguration configuration = JwtTestData.Configuration();

            EndpointDescription selected = await OpcUaSessionManager.SelectJwtEndpointAsync(
                [endpoint], "opc.tcp://forwarded.invalid:50000/gateway", null, null,
                JwtTestData.Provider(configuration), configuration, CancellationToken.None).ConfigureAwait(false);

            Assert.That(selected.EndpointUrl, Is.EqualTo("opc.tcp://forwarded.invalid:50000/ua/primary"));
        }

        [Test]
        public async Task EndpointSelectionPropagatesCancellation()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<System.OperationCanceledException>(async () =>
                await OpcUaSessionManager.SelectJwtEndpointAsync([JwtTestData.Endpoint()], JwtTestData.Url,
                    null, null, JwtTestData.Provider(), JwtTestData.Configuration(), cancellation.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static Task<EndpointDescription> SelectAsync(
            ArrayOf<EndpointDescription> endpoints, string? mode = null, string? policy = null)
        {
            return OpcUaSessionManager.SelectJwtEndpointAsync(endpoints, JwtTestData.Url, mode, policy,
                JwtTestData.Provider(), JwtTestData.Configuration(), CancellationToken.None);
        }
    }
}
#endif
