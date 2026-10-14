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
    /// endpoint was applied without a chance to validate it, so a discovery
    /// answer that, for example, downgrades the security mode was taken over
    /// as it was.
    /// </summary>
    [TestFixture]
    [Category("ManagedSession")]
    [Parallelizable]
    public sealed class ManagedSessionRediscoveryValidationTests
    {
        private const string kEndpointUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

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
                    _ => DowngradeAsync(endpoint),
                    (current, rediscovered, _) =>
                    {
                        seenCurrent = current;
                        seenRediscovered = rediscovered;
                        return new ValueTask<ServiceResult>(
                            new ServiceResult(StatusCodes.BadSecurityModeRejected));
                    },
                    CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
                Assert.That(seenCurrent?.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(seenRediscovered?.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
                Assert.That(
                    endpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Assert.That(
                    endpoint.Description.SecurityPolicyUri,
                    Is.EqualTo(SecurityPolicies.Basic256Sha256));
            });
        }

        [Test]
        public async Task AcceptedEndpointIsUsedAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();
            int calls = 0;

            await global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                _ => DowngradeAsync(endpoint),
                (_, _, _) =>
                {
                    calls++;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
            });
        }

        [Test]
        public async Task WithoutAValidatorTheRediscoveredEndpointIsUsedAsync()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            await global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                endpoint,
                _ => DowngradeAsync(endpoint),
                null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public void UncertainResultKeepsTheEndpointInUse()
        {
            ConfiguredEndpoint endpoint = CreateEndpoint();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => global::Opc.Ua.Client.ManagedSession.RefreshAndValidateEndpointAsync(
                    endpoint,
                    _ => DowngradeAsync(endpoint),
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
                    _ => DowngradeAsync(endpoint),
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
                    _ => DowngradeAsync(endpoint),
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
                    _ => DowngradeAsync(endpoint),
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
                    _ => DowngradeAsync(endpoint),
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
                    async _ =>
                    {
                        await DowngradeAsync(endpoint).ConfigureAwait(false);
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
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            return new ConfiguredEndpoint(
                null!,
                new EndpointDescription
                {
                    EndpointUrl = kEndpointUrl,
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                });
        }

        private static Task DowngradeAsync(ConfiguredEndpoint endpoint)
        {
            endpoint.Update(new EndpointDescription
            {
                EndpointUrl = kEndpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });
            return Task.CompletedTask;
        }
    }
}
