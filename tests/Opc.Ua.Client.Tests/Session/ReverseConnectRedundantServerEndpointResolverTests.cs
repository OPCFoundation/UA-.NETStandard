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

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ReverseConnectRedundantServerEndpointResolver"/>,
    /// <see cref="IReverseConnectionAvailability"/> on
    /// <see cref="ReverseConnectManager"/> and the resolver selection of
    /// <see cref="DefaultServerRedundancyHandler"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    public sealed class ReverseConnectRedundantServerEndpointResolverTests
    {
        [Test]
        public async Task MemberWithoutAvailableConnectionIsNotConnectedWithoutWaitingAsync()
        {
            var connections = new Mock<IReverseConnectionAvailability>(MockBehavior.Strict);
            connections.Setup(c => c.GetAvailableEndpointUrls("urn:test:backup")).Returns(Array.Empty<Uri>());
            var resolver = new ReverseConnectRedundantServerEndpointResolver(
                connections.Object, NUnitTelemetryContext.Create());

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resolver.ResolveAsync("urn:test:backup", CreateEndpoint()).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotConnected));
            connections.Verify(
                c => c.WaitForConnectionAsync(It.IsAny<Uri>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ConnectionWithAnotherSchemeIsNotAvailableAsync()
        {
            var connections = new Mock<IReverseConnectionAvailability>(MockBehavior.Strict);
            connections.Setup(c => c.GetAvailableEndpointUrls("urn:test:backup"))
                .Returns(new[] { new Uri("opc.wss://backup:4843") });
            var resolver = new ReverseConnectRedundantServerEndpointResolver(
                connections.Object, NUnitTelemetryContext.Create());

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await resolver.ResolveAsync("urn:test:backup", CreateEndpoint()).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task ResolverRejectsInvalidArgumentsAsync()
        {
            var connections = new Mock<IReverseConnectionAvailability>(MockBehavior.Strict);
            var resolver = new ReverseConnectRedundantServerEndpointResolver(connections.Object);

            Assert.Throws<ArgumentNullException>(() => _ = new ReverseConnectRedundantServerEndpointResolver(null!));
            await Assert.ThatAsync(
                async () => await resolver.ResolveAsync(string.Empty, CreateEndpoint()).ConfigureAwait(false),
                Throws.ArgumentException).ConfigureAwait(false);
            await Assert.ThatAsync(
                async () => await resolver.ResolveAsync("urn:test:backup", null!).ConfigureAwait(false),
                Throws.ArgumentNullException).ConfigureAwait(false);
        }

        [Test]
        public async Task ReverseConnectManagerReportsNoConnectionWhenNoneIsHeldAsync()
        {
            await using var manager = new ReverseConnectManager(NUnitTelemetryContext.Create());
            IReverseConnectionAvailability availability = manager;

            Assert.That(availability.GetAvailableEndpointUrls("urn:test:backup"), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => availability.GetAvailableEndpointUrls(null!));
        }

        [Test]
        public void HandlerResolvesMembersOverMemberConnectionsOnlyWhenSet()
        {
            var connections = new Mock<IReverseConnectionAvailability>(MockBehavior.Strict);

            Assert.That(
                DefaultServerRedundancyHandler.CreateEndpointResolver(
                    new ServerRedundancyOptions { MemberConnections = connections.Object }, null),
                Is.InstanceOf<ReverseConnectRedundantServerEndpointResolver>());
            Assert.That(
                DefaultServerRedundancyHandler.CreateEndpointResolver(new ServerRedundancyOptions(), null),
                Is.InstanceOf<DefaultRedundantServerEndpointResolver>());
            Assert.That(
                DefaultServerRedundancyHandler.CreateEndpointResolver(null, null),
                Is.InstanceOf<DefaultRedundantServerEndpointResolver>());
        }

        private static ConfiguredEndpoint CreateEndpoint()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://primary:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };
            description.Server.ApplicationUri = "urn:test:primary";
            return new ConfiguredEndpoint(null, description, EndpointConfiguration.Create());
        }
    }
}
