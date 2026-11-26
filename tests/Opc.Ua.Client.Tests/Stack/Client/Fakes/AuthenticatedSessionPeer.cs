// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Supplies a scripted UA peer with a real application certificate and signed session responses.
    /// </summary>
    internal sealed class AuthenticatedSessionPeer : IDisposable
    {
        /// <summary>
        /// Provisions the expected peer and an in-memory client certificate.
        /// </summary>
        public AuthenticatedSessionPeer(string applicationUri, ApplicationConfiguration configuration)
        {
            ApplicationUri = applicationUri;
            m_server = CertificateBuilder.Create("CN=Scripted Redundancy Server")
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .CreateForRSA();
            using Certificate client = CertificateBuilder.Create("CN=Scripted Redundancy Client").CreateForRSA();
            using var issuers = new CertificateCollection();
            m_client = new CertificateEntry(client, issuers, ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var manager = new Mock<ICertificateManager>();
            manager.SetupGet(value => value.CertificateChanges).Returns(Mock.Of<IObservable<CertificateChangeEvent>>());
            manager.Setup(value => value.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256))
                .Returns(() => m_client.AddRef());
            manager.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), It.IsAny<TrustListIdentifier?>(),
                    It.IsAny<Security.Certificates.CertificateValidationOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            manager.Setup(value => value.ValidateAsync(
                    It.IsAny<Certificate>(), It.IsAny<TrustListIdentifier?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            configuration.CertificateManager = manager.Object;
            configuration.SecurityConfiguration.NonceLength = 32;
        }

        /// <summary>
        /// Gets the provisioned peer identity.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets the independently provisioned options for tests that exercise redundancy after admission.
        /// </summary>
        public ServerRedundancyOptions RedundancyOptions => new()
        {
            ExpectedServerApplicationUri = ApplicationUri,
            ProbeMemberServiceLevel = false,
            ServiceLevelMonitoringInterval = null
        };

        /// <summary>
        /// Configures the selected endpoint with the certificate the transport will authenticate.
        /// </summary>
        public ConfiguredEndpoint ConfigureEndpoint(ConfiguredEndpoint endpoint)
        {
            endpoint.Description.Server.ApplicationUri = ApplicationUri;
            endpoint.Description.ServerCertificate = ByteString.From(m_server.RawData);
            endpoint.Description.SecurityMode = MessageSecurityMode.Sign;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            return endpoint;
        }

        /// <summary>
        /// Exposes the certificates of the established channel.
        /// </summary>
        public void ConfigureChannel(ScriptedChannel channel)
        {
            channel.Mock.SetupGet(value => value.ServerChannelCertificate).Returns(m_server.RawData);
            channel.Mock.SetupGet(value => value.ClientChannelCertificate).Returns(m_client.Certificate.RawData);
        }

        /// <summary>
        /// Creates a session factory over the signed script with an optional redundancy read response.
        /// </summary>
        public ISessionFactory CreateSessionFactory(
            ITelemetryContext telemetry,
            TimeProvider time,
            Func<ReadResponse>? readRedundancy = null,
            Action<Session>? configureSession = null)
        {
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateChannelAsync(
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(), false, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApplicationConfiguration configuration, ITransportWaitingConnection _,
                    ConfiguredEndpoint endpoint, bool _, bool _, CancellationToken _) =>
                {
                    var script = new ScriptedChannel(configuration.CreateMessageContext(), time);
                    script.OpenForEndpoint(endpoint);
                    ConfigureChannel(script);
                    script.RequestHandler = (request, _) => new ValueTask<IServiceResponse>(
                        readRedundancy != null && request is ReadRequest { NodesToRead.Count: > 0 } read &&
                        read.NodesToRead[0].NodeId == VariableIds.Server_ServerRedundancy_RedundancySupport
                            ? readRedundancy()
                            : CreateResponse(script, request));
                    return script.Channel;
                });
            factory.Setup(value => value.Create(
                    It.IsAny<ITransportChannel>(), It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<ConfiguredEndpoint>(), null, null, It.IsAny<ArrayOf<EndpointDescription>>(),
                    It.IsAny<ArrayOf<string>>()))
                .Returns((ITransportChannel channel, ApplicationConfiguration configuration,
                    ConfiguredEndpoint endpoint, Certificate? _, CertificateCollection? _,
                    ArrayOf<EndpointDescription> availableEndpoints, ArrayOf<string> discoveryProfileUris) =>
                {
                    var session = new Session(channel, configuration, endpoint, m_client.Certificate.AddRef(),
                        availableEndpoints: availableEndpoints, discoveryProfileUris: discoveryProfileUris,
                        engineFactory: DefaultSubscriptionEngineFactory.Instance, timeProvider: time);
                    configureSession?.Invoke(session);
                    return session;
                });
            return factory.Object;
        }

        /// <summary>
        /// Supplies session nonces and the application signature for the scripted peer.
        /// </summary>
        public IServiceResponse CreateResponse(ScriptedChannel channel, IServiceRequest request)
        {
            IServiceResponse response = channel.CreateResponse(request);
            if (request is CreateSessionRequest create && response is CreateSessionResponse created)
            {
                created.ServerNonce = ByteString.From(Nonce.CreateRandomNonceData(32));
                created.ServerCertificate = ByteString.From(m_server.RawData);
                byte[] signedData = Utils.Append(create.ClientCertificate.ToArray(), create.ClientNonce.ToArray());
                created.ServerSignature = SecurityPolicies.Default.CreateSignatureData(
                    SecurityPolicies.Basic256Sha256, m_server, signedData);
            }
            else if (response is ActivateSessionResponse activated)
            {
                activated.ServerNonce = ByteString.From(Nonce.CreateRandomNonceData(32));
            }
            return response;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_client.Dispose();
            m_server.Dispose();
        }

        private readonly Certificate m_server;
        private readonly CertificateEntry m_client;
    }
}
