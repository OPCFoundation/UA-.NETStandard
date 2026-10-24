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
using Opc.Ua.Client;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Tests omitted-configuration bootstrap using real certificate validation and an offline connection boundary.
    /// </summary>
    [TestFixture]
    public sealed class ServerSessionSecurityRegressionTests
    {
        /// <summary>
        /// An otherwise valid impersonator cannot cause identity activation or application request dispatch.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF12FallbackRejectsUntrustedPeerBeforeActivationOrRead()
        {
            using Certificate peer = CreatePeer("expected.invalid");
            await using var bootstrap = new OfflineBootstrap(peer, trustPeer: false);

            await Assert.ThatAsync(
                async () => await bootstrap.ReadAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(bootstrap.Activations, Is.Zero);
            Assert.That(bootstrap.Initializations, Is.EqualTo(1));
            bootstrap.ConnectedSession.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Explicit peer trust does not authorize a certificate for a different endpoint domain.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF12FallbackRejectsTrustedWrongHostBeforeActivationOrRead()
        {
            using Certificate peer = CreatePeer("different.invalid");
            await using var bootstrap = new OfflineBootstrap(peer, trustPeer: true);

            await Assert.ThatAsync(
                async () => await bootstrap.ReadAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadCertificateHostNameInvalid));
            Assert.That(bootstrap.Activations, Is.Zero);
            bootstrap.ConnectedSession.Verify(s => s.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A trusted matching server retains the normal authenticated publisher-read workflow.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF12FallbackAcceptsExplicitlyTrustedMatchingPeer()
        {
            using Certificate peer = CreatePeer("expected.invalid");
            await using var bootstrap = new OfflineBootstrap(peer, trustPeer: true);

            ArrayOf<DataValue> values = await bootstrap.ReadAsync().ConfigureAwait(false);

            Assert.That(values[0].WrappedValue, Is.EqualTo(Variant.From(42)));
            Assert.That(bootstrap.Activations, Is.EqualTo(1));
            Assert.That(bootstrap.ConnectionOptions?.CheckDomain, Is.True);
            Assert.That(bootstrap.Configuration?.SecurityConfiguration.AutoAcceptUntrustedCertificates, Is.False);
        }

        /// <summary>
        /// Supplied secure and development policies are local to their connection and cannot weaken defaults.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF12ExplicitConfigurationDoesNotWeakenLaterFallback(bool autoAccept)
        {
            using Certificate peer = CreatePeer("expected.invalid");
            ITelemetryContext telemetry = AdapterTestHelpers.Telemetry();
            using var telemetryScope = telemetry as IDisposable;
            var supplied = new ApplicationConfiguration(telemetry)
            {
                SecurityConfiguration = new SecurityConfiguration { AutoAcceptUntrustedCertificates = autoAccept },
                TransportQuotas = new TransportQuotas(),
                ClientConfiguration = new ClientConfiguration()
            };
            await using (var development = new OfflineBootstrap(peer, trustPeer: !autoAccept, supplied))
            {
                ArrayOf<DataValue> values = await development.ReadAsync().ConfigureAwait(false);
                Assert.That(values[0].WrappedValue, Is.EqualTo(Variant.From(42)));
                Assert.That(development.Configuration, Is.SameAs(supplied));
                Assert.That(development.Initializations, Is.Zero, "The supplied configuration must not be rebuilt.");
                Assert.That(supplied.SecurityConfiguration.AutoAcceptUntrustedCertificates, Is.EqualTo(autoAccept));
            }
            await using var fallback = new OfflineBootstrap(peer, trustPeer: false);
            await Assert.ThatAsync(
                async () => await fallback.ReadAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(fallback.Activations, Is.Zero);
        }

        /// <summary>
        /// Creates an in-memory, currently valid OPC UA peer certificate with the requested DNS identity.
        /// </summary>
        private static Certificate CreatePeer(string domain)
        {
            return CertificateBuilder.Create("CN=Adapter Peer")
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension("urn:adapter:peer", [domain]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Uses the production bootstrap and managed-session options, substituting only PKI storage and transport.
        /// </summary>
        private sealed class OfflineBootstrap : IAsyncDisposable
        {
            /// <summary>
            /// Creates isolated explicit trust and an authenticated connection boundary without filesystem PKI.
            /// </summary>
            public OfflineBootstrap(Certificate peer, bool trustPeer, ApplicationConfiguration? supplied = null)
            {
                m_telemetry = AdapterTestHelpers.Telemetry();
                m_manager = new CertificateManager(m_telemetry);
                ConnectedSession = new Mock<ISession>();
                ConnectedSession.SetupGet(s => s.Connected).Returns(true);
                ConnectedSession.Setup(s => s.ReadAsync(
                        It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                        It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ReadResponse { Results = [new DataValue(42)] });
                if (supplied is not null)
                {
                    ConfigureMemoryTrust(supplied, peer, trustPeer);
                }
                m_session = new ServerSession(
                    new ServerConnectionOptions
                    {
                        EndpointUrl = "opc.tcp://expected.invalid:4840",
                        UserName = "adapter-operator",
                        ApplicationConfiguration = supplied
                    },
                    m_telemetry,
                    (configuration, _) =>
                    {
                        Initializations++;
                        ConfigureMemoryTrust(configuration, peer, trustPeer);
                        return default;
                    },
                    (configuration, _) =>
                    {
                        Configuration = configuration;
                        return new ValueTask<EndpointDescription>(new EndpointDescription
                        {
                            EndpointUrl = "opc.tcp://expected.invalid:4840",
                            SecurityMode = MessageSecurityMode.SignAndEncrypt,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                            Server = new ApplicationDescription { ApplicationUri = "urn:adapter:peer" }
                        });
                    },
                    async (builder, ct) =>
                    {
                        ManagedSessionOptions options = builder.Build();
                        ConnectionOptions = options;
                        CertificateValidationResult validation = await m_manager.ValidateAsync(
                            peer, TrustListIdentifier.Peers, ct).ConfigureAwait(false);
                        if (!validation.IsValid)
                        {
                            throw new ServiceResultException(validation.StatusCode);
                        }
                        m_manager.ValidateApplicationUri(peer, options.Endpoint!);
                        if (options.CheckDomain)
                        {
                            m_manager.ValidateDomains(peer, options.Endpoint!);
                        }
                        Activations++;
                        return ConnectedSession.Object;
                    });
            }

            /// <summary>
            /// The fake already-authenticated service dispatcher.
            /// </summary>
            public Mock<ISession> ConnectedSession { get; }

            /// <summary>
            /// The real configuration delivered to the connection boundary.
            /// </summary>
            public ApplicationConfiguration? Configuration { get; private set; }

            /// <summary>
            /// The production managed-session options delivered to the connection boundary.
            /// </summary>
            public ManagedSessionOptions? ConnectionOptions { get; private set; }

            /// <summary>
            /// The number of identity activations permitted after certificate validation.
            /// </summary>
            public int Activations { get; private set; }

            /// <summary>
            /// The number of automatic fallback configurations initialized.
            /// </summary>
            public int Initializations { get; private set; }

            /// <summary>
            /// Drives an actual adapter service caller through lazy connection.
            /// </summary>
            public ValueTask<ArrayOf<DataValue>> ReadAsync()
            {
                return m_session.ReadAsync(
                    [new ReadValueId { NodeId = new NodeId(1u), AttributeId = Attributes.Value }]);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                await m_session.DisposeAsync().ConfigureAwait(false);
                await m_manager.DisposeAsync().ConfigureAwait(false);
                (m_telemetry as IDisposable)?.Dispose();
            }

            /// <summary>
            /// Replaces filesystem locations only, preserving the bootstrap's real certificate acceptance policy.
            /// </summary>
            private void ConfigureMemoryTrust(ApplicationConfiguration configuration, Certificate peer, bool trusted)
            {
                SecurityConfiguration security = configuration.SecurityConfiguration;
                security.TrustedPeerCertificates = new CertificateTrustList
                {
                    TrustedCertificates = trusted ? [new CertificateIdentifier { RawData = peer.RawData }] : []
                };
                security.TrustedIssuerCertificates = new CertificateTrustList();
                security.RejectedCertificateStore = new CertificateTrustList();
                m_manager.MapFromSecurityConfiguration(security);
                configuration.CertificateManager = m_manager;
            }

            /// <summary>
            /// The adapter under test.
            /// </summary>
            private readonly ServerSession m_session;

            /// <summary>
            /// The actual peer trust and endpoint identity validator.
            /// </summary>
            private readonly CertificateManager m_manager;

            /// <summary>
            /// Test-owned telemetry.
            /// </summary>
            private readonly ITelemetryContext m_telemetry;
        }
    }
}
