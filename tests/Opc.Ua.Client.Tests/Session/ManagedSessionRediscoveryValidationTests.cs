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

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// A reconnect that is rejected with a status requiring an endpoint refresh
    /// rediscovers the endpoint and replaces the one in use. The rediscovered
    /// endpoint must keep the security mode and policy of the endpoint in use,
    /// and an application validator can reject it, for example when it
    /// replaces a pinned server certificate. Neither check may publish a
    /// rediscovered endpoint that was not accepted.
    /// </summary>
    [TestFixture]
    [Category("ManagedSession")]
    [Parallelizable]
    public sealed class ManagedSessionRediscoveryValidationTests
    {
        private const string kEndpointUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
        private static readonly ByteString s_approvedCertificate = ByteString.From([1, 1, 1]);
        private static readonly ByteString s_renewedCertificate = ByteString.From([4, 4, 4]);

        [Test]
        public void ValidatorCanBeSetOnAManagedSession()
        {
            PropertyInfo? property = typeof(global::Opc.Ua.Client.ManagedSession).GetProperty(
                nameof(global::Opc.Ua.Client.ManagedSession.ValidateRediscoveredEndpoint));

            Assert.That(property, Is.Not.Null);
            Assert.That(property!.SetMethod?.IsPublic, Is.True);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(RediscoveredEndpointValidator)));
        }

        [Test]
        public void RejectedEndpointKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            EndpointDescription? seenCurrent = null;
            EndpointDescription? seenRediscovered = null;

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => RenewCertificateAsync(target),
                    (current, rediscovered, _) =>
                    {
                        seenCurrent = current;
                        seenRediscovered = rediscovered;
                        return new ValueTask<ServiceResult>(
                            new ServiceResult(StatusCodes.BadCertificateInvalid));
                    },
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
                Assert.That(seenCurrent?.ServerCertificate, Is.EqualTo(s_approvedCertificate));
                Assert.That(seenRediscovered?.ServerCertificate, Is.EqualTo(s_renewedCertificate));
                AssertEndpointInUseKept(endpoint);
            });
        }

        [Test]
        public async Task AcceptedEndpointIsUsedAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            int calls = 0;

            await global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                endpoint,
                (target, _) => RenewCertificateAsync(target),
                (_, _, _) =>
                {
                    calls++;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(endpoint.Description.ServerCertificate, Is.EqualTo(s_renewedCertificate));
            });
        }

        [Test]
        public async Task WithoutAValidatorTheRediscoveredEndpointIsUsedAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            await global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                endpoint,
                (target, _) => RenewCertificateAsync(target),
                null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoint.Description.ServerCertificate, Is.EqualTo(s_renewedCertificate));
        }

        [Test]
        public void UncertainResultKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => RenewCertificateAsync(target),
                    (_, _, _) => new ValueTask<ServiceResult>(
                        new ServiceResult(StatusCodes.UncertainNoCommunicationLastUsableValue)),
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                AssertEndpointInUseKept(endpoint);
            });
        }

        [Test]
        public void NullResultKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => RenewCertificateAsync(target),
                    (_, _, _) => new ValueTask<ServiceResult>((ServiceResult)null!),
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                AssertEndpointInUseKept(endpoint);
            });
        }

        [Test]
        public void ThrowingValidatorKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            System.InvalidOperationException exception = Assert.ThrowsAsync<System.InvalidOperationException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => RenewCertificateAsync(target),
                    (_, _, _) => throw new System.InvalidOperationException("validator failed"),
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Is.EqualTo("validator failed"));
                AssertEndpointInUseKept(endpoint);
            });
        }

        [Test]
        public void CancelledValidatorKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            using var cancellation = new CancellationTokenSource();

            Assert.CatchAsync<System.OperationCanceledException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => RenewCertificateAsync(target),
                    async (_, _, ct) =>
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        return ServiceResult.Good;
                    },
                    cancellation.Token));

            AssertEndpointInUseKept(endpoint);
        }

        [Test]
        public void ValidatorThatIgnoresCancellationKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            using var cancellation = new CancellationTokenSource();

            Assert.CatchAsync<System.OperationCanceledException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => RenewCertificateAsync(target),
                    async (_, _, _) =>
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        return ServiceResult.Good;
                    },
                    cancellation.Token));

            AssertEndpointInUseKept(endpoint);
        }

        [Test]
        public void FailedRefreshKeepsTheEndpointInUseWithoutCallingTheValidator()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            int calls = 0;

            Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    async (target, _) =>
                    {
                        await RenewCertificateAsync(target).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                    },
                    (_, _, _) =>
                    {
                        calls++;
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    },
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero);
                AssertEndpointInUseKept(endpoint);
            });
        }

        [Test]
        public async Task RefreshIsValidatedOnADetachedEndpointAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            var validating = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConfiguredEndpoint? refreshed = null;

            Task refresh = global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                endpoint,
                (target, _) =>
                {
                    refreshed = target;
                    return RenewCertificateAsync(target);
                },
                async (_, _, _) =>
                {
                    validating.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return ServiceResult.Good;
                },
                CancellationToken.None);
            await validating.Task.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(refreshed, Is.Not.Null.And.Not.SameAs(endpoint));
                Assert.That(refreshed!.Description, Is.Not.SameAs(endpoint.Description));
                AssertEndpointInUseKept(endpoint);
            });

            release.TrySetResult(true);
            await refresh.ConfigureAwait(false);

            Assert.That(endpoint.Description.ServerCertificate, Is.EqualTo(s_renewedCertificate));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChangedSecurityModeIsRejectedBeforeTheValidator(bool withValidator)
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            int calls = 0;

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => ChangeSecurityAsync(
                        target,
                        MessageSecurityMode.Sign,
                        SecurityPolicies.Basic256Sha256),
                    withValidator ? CountingValidator(() => calls++) : null,
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
                Assert.That(calls, Is.Zero);
                AssertEndpointInUseKept(endpoint);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ChangedSecurityPolicyIsRejectedBeforeTheValidator(bool withValidator)
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            int calls = 0;

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    (target, _) => ChangeSecurityAsync(
                        target,
                        MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicies.Aes256_Sha256_RsaPss),
                    withValidator ? CountingValidator(() => calls++) : null,
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
                Assert.That(calls, Is.Zero);
                AssertEndpointInUseKept(endpoint);
            });
        }

        [Test]
        public void CancelledRefreshWithoutAValidatorKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            using var cancellation = new CancellationTokenSource();

            // Discovery closes its client without the token, so the refresh
            // can complete normally after the reconnect was cancelled.
            Assert.CatchAsync<System.OperationCanceledException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    async (target, _) =>
                    {
                        await RenewCertificateAsync(target).ConfigureAwait(false);
                        await cancellation.CancelAsync().ConfigureAwait(false);
                    },
                    null,
                    cancellation.Token));

            AssertEndpointInUseKept(endpoint);
        }

        [Test]
        public void FailedRefreshWithoutAValidatorKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    endpoint,
                    async (target, _) =>
                    {
                        await RenewCertificateAsync(target).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                    },
                    null,
                    CancellationToken.None));

            AssertEndpointInUseKept(endpoint);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OverlappingRefreshesOnASharedEndpointPublishOnlyAnAcceptedEndpointAsync(
            bool secondAccepts)
        {
            ByteString approved = ByteString.From([1, 1, 1]);
            ByteString firstCandidate = ByteString.From([2, 2, 2]);
            ByteString secondCandidate = ByteString.From([3, 3, 3]);
            ConfiguredEndpoint endpoint = CreateEndpoint();
            endpoint.Description.ServerCertificate = approved;

            var firstValidating = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondValidating = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ByteString firstSawCurrent = default;
            ByteString secondSawCurrent = default;

            Task first = global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                endpoint,
                (target, _) => ReplaceCertificateAsync(target, firstCandidate),
                async (current, _, _) =>
                {
                    firstSawCurrent = current.ServerCertificate;
                    firstValidating.TrySetResult(true);
                    await firstRelease.Task.ConfigureAwait(false);
                    return new ServiceResult(StatusCodes.BadCertificateInvalid);
                },
                CancellationToken.None);
            await firstValidating.Task.ConfigureAwait(false);

            Task second = global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                endpoint,
                (target, _) => ReplaceCertificateAsync(target, secondCandidate),
                async (current, _, _) =>
                {
                    secondSawCurrent = current.ServerCertificate;
                    secondValidating.TrySetResult(true);
                    await secondRelease.Task.ConfigureAwait(false);
                    return secondAccepts
                        ? ServiceResult.Good
                        : new ServiceResult(StatusCodes.BadCertificateInvalid);
                },
                CancellationToken.None);
            await secondValidating.Task.ConfigureAwait(false);
            Assert.That(endpoint.Description.ServerCertificate, Is.EqualTo(approved));

            firstRelease.TrySetResult(true);
            Assert.ThrowsAsync<ServiceResultException>(() => first);
            secondRelease.TrySetResult(true);
            if (secondAccepts)
            {
                await second.ConfigureAwait(false);
            }
            else
            {
                Assert.ThrowsAsync<ServiceResultException>(() => second);
            }

            Assert.Multiple(() =>
            {
                Assert.That(firstSawCurrent, Is.EqualTo(approved));
                Assert.That(secondSawCurrent, Is.EqualTo(approved));
                Assert.That(
                    endpoint.Description.ServerCertificate,
                    Is.EqualTo(secondAccepts ? secondCandidate : approved));
            });
        }

        [Test]
        public void BuilderPutsTheValidatorIntoTheSessionOptions()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            RediscoveredEndpointValidator validator = (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good);
            var builder = new ManagedSessionBuilder(
                new ApplicationConfiguration(telemetry) { ApplicationName = "rediscovery" },
                telemetry);

            ManagedSessionOptions options = builder.WithRediscoveredEndpointValidator(validator).Build();

            Assert.Multiple(() =>
            {
                Assert.That(options.ValidateRediscoveredEndpoint, Is.SameAs(validator));
                Assert.That(
                    () => builder.WithRediscoveredEndpointValidator(null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public async Task CreateAsyncInstallsTheValidatorFromTheSessionOptionsAsync()
        {
            RediscoveredEndpointValidator validator = (_, _, _) => new ValueTask<ServiceResult>(ServiceResult.Good);
            await using var channels = new SessionChannelHarness();
            await using global::Opc.Ua.Client.ManagedSession session =
                await global::Opc.Ua.Client.ManagedSession.CreateAsync(
                    new ManagedSessionOptions
                    {
                        Endpoint = SessionChannelHarness.CreateEndpoint(),
                        ValidateRediscoveredEndpoint = validator
                    },
                    channels.Configuration,
                    new DefaultSessionFactory(channels.Telemetry),
                    telemetry: channels.Telemetry,
                    channelManager: channels.Manager,
                    updateBeforeConnect: false).WaitAsync(System.TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(session.ValidateRediscoveredEndpoint, Is.SameAs(validator));
        }

        private static void AssertEndpointInUseKept(ConfiguredEndpoint endpoint)
        {
            Assert.That(
                endpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(
                endpoint.Description.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(endpoint.Description.ServerCertificate, Is.EqualTo(s_approvedCertificate));
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(
                null!,
                new EndpointDescription
                {
                    EndpointUrl = kEndpointUrl,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                    ServerCertificate = s_approvedCertificate
                });
        }

        private static RediscoveredEndpointValidator CountingValidator(System.Action onCall)
        {
            return (_, _, _) =>
            {
                onCall();
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            };
        }

        private static Task RenewCertificateAsync(ConfiguredEndpoint endpoint)
        {
            return ReplaceCertificateAsync(endpoint, s_renewedCertificate);
        }

        private static Task ChangeSecurityAsync(
            ConfiguredEndpoint endpoint,
            MessageSecurityMode securityMode,
            string securityPolicyUri)
        {
            EndpointDescription description = CoreUtils.Clone(endpoint.Description)!;
            description.SecurityMode = securityMode;
            description.SecurityPolicyUri = securityPolicyUri;
            endpoint.Update(description);
            return Task.CompletedTask;
        }

        private static Task ReplaceCertificateAsync(ConfiguredEndpoint endpoint, ByteString certificate)
        {
            EndpointDescription description = CoreUtils.Clone(endpoint.Description)!;
            description.ServerCertificate = certificate;
            endpoint.Update(description);
            return Task.CompletedTask;
        }
    }
}
