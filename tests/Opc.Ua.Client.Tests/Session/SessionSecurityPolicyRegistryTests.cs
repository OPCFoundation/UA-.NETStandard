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

#nullable enable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Proves that the security policy registry an application composes is the
    /// policy set the client session resolves against, rather than the
    /// process-wide <see cref="SecurityPolicies.Default"/> fallback.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Security")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class SessionSecurityPolicyRegistryTests
    {
        /// <summary>
        /// The session rejects an endpoint policy its own registry does not
        /// carry, even though the built-in set does. Before the registry was
        /// injected, the session always answered from
        /// <see cref="SecurityPolicies.Default"/>.
        /// </summary>
        [Test]
        public void OpenAsyncResolvesTheEndpointPolicyThroughTheInjectedRegistry()
        {
            var registry = new Mock<ISecurityPolicyRegistry>();
            registry
                .Setup(r => r.GetDisplayName(SecurityPolicies.None))
                .Returns((string?)null)
                .Verifiable(Times.Once);

            Assert.That(
                SecurityPolicies.Default.GetDisplayName(SecurityPolicies.None),
                Is.Not.Null,
                "The built-in policy set carries the endpoint's policy.");

            using Session session = CreateSession(registry.Object);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => session.OpenAsync(
                    "test",
                    60000,
                    new UserIdentity(),
                    default,
                    false,
                    false,
                    CancellationToken.None))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            registry.Verify();
        }

        /// <summary>
        /// The same endpoint opens past the policy gate when no registry is
        /// injected, so the rejection above is caused by the registry and not
        /// by the endpoint.
        /// </summary>
        [Test]
        public void OpenAsyncAcceptsTheEndpointPolicyWhenNoRegistryIsInjected()
        {
            using Session session = CreateSession(securityPolicies: null);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => session.OpenAsync(
                    "test",
                    60000,
                    new UserIdentity(),
                    default,
                    false,
                    false,
                    CancellationToken.None))!;

            Assert.That(
                exception.StatusCode,
                Is.Not.EqualTo(StatusCodes.BadSecurityChecksFailed),
                "The built-in policy set accepts the endpoint's policy.");
        }

        [Test]
        public void ChannelManagerSessionFactoryCarriesTheSecurityPolicyRegistry()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var manager = new Mock<IClientChannelManager>();
            using var registry = new SecurityPolicies(telemetry);

            var factory = new ChannelManagerSessionFactory(
                manager.Object,
                telemetry,
                securityPolicies: registry);

            Assert.Multiple(() =>
            {
                Assert.That(factory.SecurityPolicyRegistry, Is.SameAs(registry));
                Assert.That(
                    new ChannelManagerSessionFactory(manager.Object, telemetry)
                        .SecurityPolicyRegistry,
                    Is.Null,
                    "A factory with no registry falls back to the built-in policy set.");
            });
        }

        [Test]
        public void ManagedSessionReadsTheRegistryFromCustomFactoryProviders()
        {
            var registry = new Mock<ISecurityPolicyRegistry>();
            var factory = new Mock<ISessionFactory>();
            factory
                .As<ISecurityPolicyRegistryProvider>()
                .SetupGet(provider => provider.SecurityPolicyRegistry)
                .Returns(registry.Object);

            MethodInfo resolver = typeof(Opc.Ua.Client.ManagedSession).GetMethod(
                "ResolveSecurityPolicies",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            Assert.That(
                resolver.Invoke(null, [factory.Object]),
                Is.SameAs(registry.Object));
        }

        [Test]
        public void UseSecurityPoliciesRejectsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var builder = new ManagedSessionBuilder(CreateConfiguration(telemetry), telemetry);

            Assert.Throws<ArgumentNullException>(() => builder.UseSecurityPolicies(null!));
        }

        /// <summary>
        /// A container that registers an application policy hands the composed
        /// registry to the session factory it resolves, so a session created
        /// from the container can negotiate that policy.
        /// </summary>
        [Test]
        public void ContainerHandsTheComposedRegistryToTheSessionFactory()
        {
            var policy = new SecurityPolicyInfo(SecurityPolicyInfo.Basic256Sha256)
            {
                Name = "InjectedClientPolicy",
                Uri = SecurityPolicies.BaseUri + "InjectedClientPolicy",
                IsDefault = false
            };

            var services = new ServiceCollection();
            services.AddOpcUa()
                .AddSecurityPolicy(policy)
                .AddClient(options => options.Configuration = CreateConfiguration(
                    NUnitTelemetryContext.Create()));

            using ServiceProvider provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ISecurityPolicyRegistry>();
            var factory = provider.GetRequiredService<ISessionFactory>() as DefaultSessionFactory;

            Assert.That(factory, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(factory!.SecurityPolicyRegistry, Is.SameAs(registry));
                Assert.That(registry.GetInfo(policy.Uri), Is.SameAs(policy));
                Assert.That(
                    SecurityPolicies.Default.GetInfo(policy.Uri),
                    Is.Null,
                    "The policy must be unknown outside the application that registered it.");
            });
        }

        /// <summary>
        /// Builds a session bound to a channel that never answers, so the open
        /// call fails at the first check that rejects it.
        /// </summary>
        private static Session CreateSession(
            ISecurityPolicyRegistry? securityPolicies)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(c => c.MessageContext)
                .Returns(ServiceMessageContext.Create(telemetry));
            channel
                .SetupGet(c => c.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            channel
                .Setup(c => c.SendRequestAsync(
                    It.IsAny<IServiceRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>((_, _) =>
                    throw new ServiceResultException(StatusCodes.BadNotConnected));

            var endpoint = new ConfiguredEndpoint(
                collection: null,
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens = [new UserTokenPolicy()]
                },
                configuration: null);

            return new Session(
                channel.Object,
                CreateConfiguration(telemetry),
                endpoint,
                clientCertificate: null,
                securityPolicies: securityPolicies);
        }

        private static ApplicationConfiguration CreateConfiguration(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:client",
                ApplicationName = "test",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
