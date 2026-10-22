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
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;

namespace UaLens.Tests.Connection
{
    [TestFixture]
    public sealed class IdentityTokenInteractionTests
    {
        [Test]
        public async Task OptionalInteractionAndArgumentGuardsDoNotAcquireCredentials()
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var source = new ConfiguredAccessTokenSource("authority", "Configured authority", access.Object);

            Assert.That(source.Interaction, Is.Null);
            Assert.That(source.Provider, Is.SameAs(access.Object));
            Assert.That(source.AuthorityUri, Is.EqualTo(kAuthority));
            Assert.That(source.ProfileUri, Is.EqualTo(Profiles.JwtUserToken));
            Assert.That(() => new ConfiguredAccessTokenSource("authority", "Configured authority", null!),
                Throws.ArgumentNullException);
            Assert.That(() => new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, "urn:unsupported-profile"),
                Throws.ArgumentException);
            await Assert.ThatAsync(() => IdentityTokenInteraction.AuthorizeAsync(
                null!, Policy(), null, CancellationToken.None), Throws.ArgumentNullException).ConfigureAwait(false);
            await Assert.ThatAsync(() => IdentityTokenInteraction.AuthorizeAsync(
                source, null!, null, CancellationToken.None), Throws.ArgumentNullException).ConfigureAwait(false);
            await Assert.ThatAsync(() => IdentityTokenInteraction.AuthorizeAsync(
                source, Policy(), null, CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("external authorization"))
                .ConfigureAwait(false);

            VerifyNoAcquisition(access);
        }

        [Test]
        public async Task AsyncAuthorizationAndFreshAcquisitionUseTheSameSanitizedMetadata()
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
            var entered = new TaskCompletionSource<AuthorizationServerMetadata>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = new Mock<IProgress<IdentityInteractionStage>>(MockBehavior.Strict);
            progress.Setup(value => value.Report(IdentityInteractionStage.WaitingForUser));
            interaction.Setup(value => value.AuthorizeAsync(
                It.IsAny<AuthorizationServerMetadata>(), progress.Object, It.IsAny<CancellationToken>()))
                .Returns((AuthorizationServerMetadata metadata, IProgress<IdentityInteractionStage>? reporter,
                    CancellationToken token) =>
                {
                    Assert.That(token.CanBeCanceled, Is.True);
                    reporter!.Report(IdentityInteractionStage.WaitingForUser);
                    entered.TrySetResult(metadata);
                    return release.Task;
                });
            var source = new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, interaction: interaction.Object);
            UserTokenPolicy policy = Policy();
            var advertised = AuthorizationServerMetadata.Parse(policy.IssuerEndpointUrl);
            Task authorization = IdentityTokenInteraction.AuthorizeAsync(
                source, policy, progress.Object, CancellationToken.None);
            try
            {
                AuthorizationServerMetadata sent = await entered.Task.WaitAsync(kWait).ConfigureAwait(false);
                AssertSanitized(sent);
                Assert.That(authorization.IsCompleted, Is.False);
                VerifyNoAcquisition(access);
                progress.Verify(value => value.Report(IdentityInteractionStage.WaitingForUser), Times.Once);
            }
            finally
            {
                release.TrySetResult();
            }
            await authorization.WaitAsync(kWait).ConfigureAwait(false);
            VerifyNoAcquisition(access);

            byte[] tokenBytes = Guid.NewGuid().ToByteArray();
            byte[] expectedBytes = (byte[])tokenBytes.Clone();
            var clock = new Mock<TimeProvider>();
            clock.Setup(value => value.GetUtcNow()).Returns(sNow);
            access.Setup(value => value.AcquireAsync(
                It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()))
                .Returns((AuthorizationServerMetadata metadata, CancellationToken _) =>
                {
                    AssertSanitized(metadata);
                    return ValueTask.FromResult(new AccessToken(
                        Profiles.JwtUserToken, tokenBytes, sNow.UtcDateTime.AddMinutes(10), string.Empty));
                });
            var guarded = new GuardedAccessTokenProvider(source, clock.Object);
            using (AccessToken acquired = await guarded.AcquireAsync(advertised).ConfigureAwait(false))
            {
                Assert.That(acquired.ProfileUri, Is.EqualTo(Profiles.JwtUserToken));
                Assert.That(acquired.TokenData.ToArray(), Is.EqualTo(expectedBytes));
                Assert.That(acquired.ExpiresAt, Is.EqualTo(sNow.UtcDateTime.AddMinutes(10)));
            }

            Assert.That(tokenBytes, Is.All.Zero);
            Assert.That(advertised.TokenEndpoint, Is.EqualTo("https://redirect.example.test/token"));
            Assert.That(advertised.AuthorizationEndpoint, Is.EqualTo("https://redirect.example.test/authorize"));
            Assert.That(advertised.JwksUri, Is.EqualTo("https://redirect.example.test/keys"));
            Assert.That(advertised.AdditionalFields, Contains.Key("device_authorization_endpoint"));
            access.Verify(value => value.AcquireAsync(
                It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()), Times.Once);
            interaction.Verify(value => value.AuthorizeAsync(
                It.IsAny<AuthorizationServerMetadata>(), progress.Object, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task IncompatiblePolicyAndMetadataNeverReachTheConfiguredInteraction()
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
            var source = new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, interaction: interaction.Object);
            UserTokenPolicy wrongType = Policy();
            wrongType.TokenType = UserTokenType.Certificate;
            UserTokenPolicy wrongProfile = Policy();
            wrongProfile.IssuedTokenType = "urn:unsupported-profile";
            UserTokenPolicy wrongAuthority = Policy();
            wrongAuthority.IssuerEndpointUrl =
                "{\"authorityUri\":\"https://other.example.test\",\"ua:resourceUri\":\"urn:fixture:server\"}";
            UserTokenPolicy missingResource = Policy();
            missingResource.IssuerEndpointUrl = "{\"authorityUri\":\"" + kAuthority + "\"}";
            UserTokenPolicy blankResource = Policy();
            blankResource.IssuerEndpointUrl =
                "{\"authorityUri\":\"" + kAuthority + "\",\"ua:resourceUri\":\" \"}";
            UserTokenPolicy missingAuthority = Policy();
            missingAuthority.IssuerEndpointUrl = "{\"ua:resourceUri\":\"urn:fixture:server\"}";
            foreach (UserTokenPolicy policy in (UserTokenPolicy[])
                [wrongType, wrongProfile, wrongAuthority, missingResource, blankResource, missingAuthority])
            {
                await Assert.ThatAsync(() => IdentityTokenInteraction.AuthorizeAsync(
                    source, policy, null, CancellationToken.None),
                    Throws.TypeOf<ConnectionIdentityException>()
                        .With.Property(nameof(ConnectionIdentityException.Failure))
                        .EqualTo(ConnectionIdentityFailure.Incompatible)).ConfigureAwait(false);
            }
            UserTokenPolicy malformed = Policy();
            malformed.IssuerEndpointUrl = "{";
            await Assert.ThatAsync(() => IdentityTokenInteraction.AuthorizeAsync(
                source, malformed, null, CancellationToken.None),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadDecodingError))
                .ConfigureAwait(false);

            interaction.VerifyNoOtherCalls();
            VerifyNoAcquisition(access);
        }

        [Test]
        public async Task ProviderFailuresExposeOnlySanitizedClassification()
        {
            string sensitive = Guid.NewGuid().ToString("N");
            Exception[] failures =
            [
                new UnauthorizedAccessException(sensitive),
                new ServiceResultException(StatusCodes.BadUserAccessDenied, sensitive),
                new InvalidOperationException(sensitive),
                new IOException(sensitive),
                new HttpRequestException(sensitive),
                new TimeoutException(sensitive),
                new CryptographicException(sensitive),
                new AuthenticationException(sensitive),
                new ArgumentException(sensitive),
                new FormatException(sensitive),
                new NotSupportedException(sensitive)
            ];
            Mock<IAccessTokenProvider> access = AccessProvider();
            foreach (Exception failure in failures)
            {
                var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
                interaction.Setup(value => value.AuthorizeAsync(
                    It.IsAny<AuthorizationServerMetadata>(), null, It.IsAny<CancellationToken>()))
                    .Returns(() => Task.FromException(failure));
                var source = new ConfiguredAccessTokenSource(
                    "authority", "Configured authority", access.Object, interaction: interaction.Object);
                ConnectionIdentityException? observed = null;
                try
                {
                    await IdentityTokenInteraction.AuthorizeAsync(
                        source, Policy(), null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (ConnectionIdentityException error)
                {
                    observed = error;
                }

                Assert.That(observed, Is.Not.Null, failure.GetType().Name);
                Assert.That(observed!.Failure, Is.EqualTo(ConnectionIdentityFailure.Denied));
                Assert.That(observed.Message, Does.Contain("No other authority was contacted"));
                Assert.That(observed.ToString(), Does.Not.Contain(sensitive));
                Assert.That(observed.InnerException, Is.Null);
                interaction.Verify(value => value.AuthorizeAsync(
                    It.IsAny<AuthorizationServerMetadata>(), null, It.IsAny<CancellationToken>()), Times.Once);
            }
            VerifyNoAcquisition(access);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task CancellationBeforeOrAfterProviderCompletionNeverReturnsAuthorization(bool cancelBefore)
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<CancellationToken>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            interaction.Setup(value => value.AuthorizeAsync(
                It.IsAny<AuthorizationServerMetadata>(), null, It.IsAny<CancellationToken>()))
                .Returns((AuthorizationServerMetadata _, IProgress<IdentityInteractionStage>? _,
                    CancellationToken token) =>
                {
                    entered.TrySetResult(token);
                    return release.Task;
                });
            var source = new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, interaction: interaction.Object);
            if (cancelBefore)
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            Task pending = IdentityTokenInteraction.AuthorizeAsync(source, Policy(), null, cancellation.Token);
            try
            {
                if (!cancelBefore)
                {
                    CancellationToken providerToken = await entered.Task.WaitAsync(kWait).ConfigureAwait(false);
                    Assert.That(providerToken, Is.Not.EqualTo(cancellation.Token));
                    Assert.That(pending.IsCompleted, Is.False);
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    Assert.That(providerToken.IsCancellationRequested, Is.True);
                }
            }
            finally
            {
                release.TrySetResult();
            }

            await Assert.ThatAsync(() => pending.WaitAsync(kWait),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            interaction.Verify(value => value.AuthorizeAsync(
                It.IsAny<AuthorizationServerMetadata>(), null, It.IsAny<CancellationToken>()),
                cancelBefore ? Times.Never() : Times.Once());
            VerifyNoAcquisition(access);
        }

        [Test]
        public async Task PolicyEligibilityWithOptionalInteractionNeverAuthorizesOrAcquires()
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
            var source = new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, interaction: interaction.Object);
            using var configuration = new ConnectionIdentityConfiguration(accessTokenSources: [source]);
            UserTokenPolicy policy = Policy();
            var endpoint = new EndpointDescription("opc.tcp://identity.example.test:4840")
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaTcpTransport,
                Server = new ApplicationDescription { ApplicationUri = kResource },
                UserIdentityTokens = [policy]
            };
            var profile = ConnectionProfile.Create(
                endpoint, policy, SubscriptionEngineKind.ChannelV2,
                issuedIdentity: new IssuedIdentityReference { ProviderId = source.Id, AuthorityUri = kAuthority });
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
            var context = new IdentitySelectionContext(
                endpoint, [policy], ServiceMessageContext.Create(telemetry.Object), [SecurityPolicies.Basic256Sha256]);

            CanSatisfyResult result = await configuration.Resolve(profile).CanSatisfyAsync(policy, context)
                .ConfigureAwait(false);

            Assert.That(result.CanSatisfy, Is.True);
            Assert.That(result.RejectionReason, Is.Null);
            interaction.VerifyNoOtherCalls();
            VerifyNoAcquisition(access);
        }

        [Test]
        public async Task CancellationCompletesEvenWhenInteractionIgnoresItsToken()
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            interaction.Setup(value => value.AuthorizeAsync(
                It.IsAny<AuthorizationServerMetadata>(), null, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    entered.TrySetResult();
                    return release.Task;
                });
            var source = new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, interaction: interaction.Object);
            using var cancellation = new CancellationTokenSource();
            Task pending = IdentityTokenInteraction.AuthorizeAsync(source, Policy(), null, cancellation.Token);
            try
            {
                await entered.Task.WaitAsync(kWait).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);

                await Assert.ThatAsync(() => pending.WaitAsync(kWait),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
                VerifyNoAcquisition(access);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [Test]
        public async Task UnresponsiveInteractionExpiresAtExactlyFiveMinutes()
        {
            Mock<IAccessTokenProvider> access = AccessProvider();
            var interaction = new Mock<IIdentityTokenInteraction>(MockBehavior.Strict);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            interaction.Setup(value => value.AuthorizeAsync(
                It.IsAny<AuthorizationServerMetadata>(), null, It.IsAny<CancellationToken>()))
                .Returns(release.Task);
            var source = new ConfiguredAccessTokenSource(
                "authority", "Configured authority", access.Object, interaction: interaction.Object);
            var clock = new Samples.SampleTestClock();
            Task pending = IdentityTokenInteraction.AuthorizeAsync(
                source, Policy(), null, CancellationToken.None, clock.Provider);
            try
            {
                await clock.WaitForTimerAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
                Assert.That(pending.IsCompleted, Is.False);
                clock.Advance(TimeSpan.FromTicks(1));

                await Assert.ThatAsync(() => pending.WaitAsync(kWait),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
                VerifyNoAcquisition(access);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        private static Mock<IAccessTokenProvider> AccessProvider()
        {
            var provider = new Mock<IAccessTokenProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.AuthorityUri).Returns(kAuthority);
            return provider;
        }

        private static void VerifyNoAcquisition(Mock<IAccessTokenProvider> access)
        {
            access.Verify(value => value.AcquireAsync(
                It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static UserTokenPolicy Policy()
        {
            return new UserTokenPolicy(UserTokenType.IssuedToken)
            {
                PolicyId = "jwt",
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                IssuedTokenType = Profiles.JwtUserToken,
                IssuerEndpointUrl = """
                    {
                      "authorityUri": "https://authority.example.test",
                      "ua:resourceUri": "urn:fixture:server",
                      "requestTypes": ["authorization_code"],
                      "scopes": ["read", "subscribe"],
                      "audience": "urn:fixture:audience",
                      "tokenEndpoint": "https://redirect.example.test/token",
                      "authorizationEndpoint": "https://redirect.example.test/authorize",
                      "jwksUri": "https://redirect.example.test/keys",
                      "device_authorization_endpoint": "https://redirect.example.test/device"
                    }
                    """
            };
        }

        private static void AssertSanitized(AuthorizationServerMetadata metadata)
        {
            Assert.That(metadata.AuthorityUri, Is.EqualTo(kAuthority));
            Assert.That(metadata.ResourceUri, Is.EqualTo(kResource));
            Assert.That(metadata.RequestTypes, Is.EqualTo(sRequestTypes));
            Assert.That(metadata.Scopes, Is.EqualTo(sScopes));
            Assert.That(metadata.Audience, Is.EqualTo("urn:fixture:audience"));
            Assert.That(metadata.TokenEndpoint, Is.Null);
            Assert.That(metadata.AuthorizationEndpoint, Is.Null);
            Assert.That(metadata.JwksUri, Is.Null);
            Assert.That(metadata.AdditionalFields, Is.Empty);
        }

        private const string kAuthority = "https://authority.example.test";
        private const string kResource = "urn:fixture:server";
        private static readonly string[] sRequestTypes = ["authorization_code"];
        private static readonly string[] sScopes = ["read", "subscribe"];
        private static readonly DateTimeOffset sNow = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan kWait = TimeSpan.FromSeconds(10);
    }
}
