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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Verifies captured transport observation isolation and terminal response ownership.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class SessionBindingResponseTests
    {
        /// <summary>
        /// Verifies that a closed public session client reports its normal channel failure.
        /// </summary>
        [Test]
        public async Task ClosedPublicSessionClientRejectsCaptureWithoutDereferencingItsChannelAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var channel = new Mock<ITransportChannel>();
            using var client = new SessionClient(channel.Object, telemetry);
            await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await client.CreateBindingAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecureChannelClosed)).ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await client.CreateBindingAsync(cancelled.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that response rejection returns pooled storage exactly once.
        /// </summary>
        /// <param name="cancel">Whether cancellation takes precedence over invalidation.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void RejectedBoundResponseReleasesPooledStorage(bool cancel)
        {
            var response = new Mock<IServiceResponse>();
            Mock<IPooledEncodeable> pooled = response.As<IPooledEncodeable>();
            using var cancellation = new CancellationTokenSource();
            if (cancel)
            {
                cancellation.Cancel();
                Assert.That(
                    () => TransportChannelBinding.ValidateResponse(
                        response.Object, static () => false, cancellation.Token),
                    Throws.InstanceOf<OperationCanceledException>());
            }
            else
            {
                Assert.That(
                    () => TransportChannelBinding.ValidateResponse(
                        response.Object, static () => false, cancellation.Token),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.StatusCode))
                        .EqualTo(StatusCodes.BadSecurityChecksFailed));
            }
            pooled.Verify(value => value.Reuse(), Times.Once);
        }

        /// <summary>
        /// Verifies that successful responses retain their caller-owned pooled storage.
        /// </summary>
        [Test]
        public void AcceptedBoundResponseRetainsPooledStorage()
        {
            var response = new Mock<IServiceResponse>();
            Mock<IPooledEncodeable> pooled = response.As<IPooledEncodeable>();
            Assert.That(
                TransportChannelBinding.ValidateResponse(response.Object, static () => true, CancellationToken.None),
                Is.SameAs(response.Object));
            pooled.Verify(value => value.Reuse(), Times.Never);
        }

        /// <summary>
        /// Verifies that endpoint, policy and quota changes cannot rewrite captured observations.
        /// </summary>
        [Test]
        public void CapturedTransportOwnsEndpointPolicyAndConfigurationCopies()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                Server = new ApplicationDescription { ApplicationUri = "urn:original" },
                UserIdentityTokens = [new UserTokenPolicy { PolicyId = "original" }]
            };
            var configuration = EndpointConfiguration.Create();
            int originalTimeout = configuration.OperationTimeout;
            var source = new Mock<ITransportChannel>();
            source.SetupGet(value => value.EndpointDescription).Returns(endpoint);
            source.SetupGet(value => value.EndpointConfiguration).Returns(configuration);
            source.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.Create(telemetry));
            source.SetupGet(value => value.ChannelThumbprint).Returns([]);
            source.SetupGet(value => value.ClientChannelCertificate).Returns([]);
            source.SetupGet(value => value.ServerChannelCertificate).Returns([]);
            using var binding = new TransportChannelBinding(
                source.Object, static () => true,
                static (_, _, _) => throw new InvalidOperationException("Dispatch is not part of this test."));

            endpoint.Server.ApplicationUri = "urn:changed";
            endpoint.UserIdentityTokens[0].PolicyId = "changed";
            configuration.OperationTimeout = originalTimeout + 1;
            Assert.Multiple(() =>
            {
                Assert.That(binding.EndpointDescription, Is.Not.SameAs(endpoint));
                Assert.That(binding.EndpointDescription.Server.ApplicationUri, Is.EqualTo("urn:original"));
                Assert.That(binding.EndpointDescription.UserIdentityTokens[0].PolicyId, Is.EqualTo("original"));
                Assert.That(binding.EndpointConfiguration, Is.Not.SameAs(configuration));
                Assert.That(binding.EndpointConfiguration.OperationTimeout, Is.EqualTo(originalTimeout));
            });
        }
    }
}
