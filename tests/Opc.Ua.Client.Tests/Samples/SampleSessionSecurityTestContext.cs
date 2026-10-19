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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Samples
{
    /// <summary>
    /// Uses real in-memory certificate trust and hostname validation without discovery, sockets or PKI files.
    /// </summary>
    internal sealed class SampleSessionSecurityTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates one trusted application certificate and a recording session factory.
        /// </summary>
        internal SampleSessionSecurityTestContext(bool matchingHost)
        {
            Telemetry = NUnitTelemetryContext.Create();
            ServerCertificate = CertificateBuilder.Create("CN=Sample Security Regression")
                .AddExtension(new X509SubjectAltNameExtension(
                    "urn:sample:advertised-server",
                    [matchingHost ? "requested.example" : "other-trusted.example"]))
                .CreateForRSA();
            var security = new SecurityConfiguration
            {
                UseValidatedCertificates = true,
                TrustedPeerCertificates = new CertificateTrustList
                {
                    TrustedCertificates = [new CertificateIdentifier { RawData = ServerCertificate.RawData }]
                }
            };
            Manager = CertificateManagerFactory.Create(security, Telemetry);
            Configuration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationName = "SampleSecurityRegression",
                ApplicationUri = "urn:sample:security-client",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = security,
                ClientConfiguration = new ClientConfiguration(),
                CertificateManager = Manager
            };
            Description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://advertised.example:62553/AdvertisedPath",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = ByteString.From(ServerCertificate.RawData),
                Server = new ApplicationDescription
                {
                    ApplicationUri = "urn:sample:advertised-server",
                    DiscoveryUrls = ["opc.tcp://other-trusted.example:62553/Discovery"]
                },
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "username",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    },
                    new UserTokenPolicy { PolicyId = "anonymous", TokenType = UserTokenType.Anonymous }
                ]
            };
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(Telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<ConfiguredEndpoint>(),
                    It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<uint>(),
                    It.IsAny<IUserIdentity?>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Returns((ApplicationConfiguration configuration, ConfiguredEndpoint endpoint,
                    bool update, bool checkDomain, string _, uint _, IUserIdentity? identity,
                    ArrayOf<string> _, CancellationToken ct) =>
                    CreateSessionAsync(configuration, null, endpoint, update, checkDomain, identity, ct));
            factory.Setup(value => value.CreateAsync(
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<ConfiguredEndpoint>(), It.IsAny<bool>(), It.IsAny<bool>(),
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<IUserIdentity?>(),
                    It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Returns((ApplicationConfiguration configuration, ITransportWaitingConnection connection,
                    ConfiguredEndpoint endpoint, bool update, bool checkDomain, string _, uint _,
                    IUserIdentity? identity, ArrayOf<string> _, CancellationToken ct) =>
                    CreateSessionAsync(configuration, connection, endpoint, update, checkDomain, identity, ct));
            Factory = factory.Object;
        }

        /// <summary>
        /// Gets the original operator-supplied URL.
        /// </summary>
        internal static string RequestedUrl => "opc.tcp://requested.example:4840/RequestedPath";

        /// <summary>
        /// Gets the telemetry shared by the samples and local sessions.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the trusted certificate, owned by this fixture.
        /// </summary>
        internal Certificate ServerCertificate { get; }

        /// <summary>
        /// Gets the in-memory certificate manager.
        /// </summary>
        internal CertificateManager Manager { get; }

        /// <summary>
        /// Gets a client configuration with no persistent certificate stores.
        /// </summary>
        internal ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Gets the adversarial but internally consistent discovery result.
        /// </summary>
        internal EndpointDescription Description { get; }

        /// <summary>
        /// Gets the session-creation boundary used by the actual sample connection code.
        /// </summary>
        internal ISessionFactory Factory { get; }

        /// <summary>
        /// Gets the number of factory invocations, before activation.
        /// </summary>
        internal int Attempts { get; private set; }

        /// <summary>
        /// Gets the number of identities admitted after hostname validation.
        /// </summary>
        internal int Activations { get; private set; }

        /// <summary>
        /// Gets the endpoint that the sample passed to its session factory.
        /// </summary>
        internal ConfiguredEndpoint? Endpoint { get; private set; }

        /// <summary>
        /// Gets the identity admitted for the correctly identified peer.
        /// </summary>
        internal IUserIdentity? Identity { get; private set; }

        /// <summary>
        /// Gets the reverse connection passed by the caller.
        /// </summary>
        internal ITransportWaitingConnection? Connection { get; private set; }

        /// <summary>
        /// Gets whether the caller requested the factory's independent hostname validation.
        /// </summary>
        internal bool CheckDomain { get; private set; }

        /// <summary>
        /// Gets whether discovery refresh was requested.
        /// </summary>
        internal bool UpdateBeforeConnect { get; private set; }

        /// <summary>
        /// Gets all requests that reached a locally activated transport.
        /// </summary>
        internal List<IServiceRequest> Requests { get; } = [];

        /// <summary>
        /// Establishes that the attacker's certificate is trusted, also priming the validation cache.
        /// </summary>
        internal async Task AssertTrustedAsync()
        {
            CertificateValidationResult validation = await Manager.ValidateAsync(ServerCertificate)
                .ConfigureAwait(false);
            Assert.That(validation.IsValid, Is.True);
            Assert.That(validation.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Reads a real service value after a successful connection.
        /// </summary>
        internal static async Task AssertMarkerReadAsync(ISession session)
        {
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = s_markerNode, AttributeId = Attributes.Value }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (Session session in m_sessions)
                {
                    session.SessionCreated(NodeId.Null, NodeId.Null);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    await Manager.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    ServerCertificate.Dispose();
                }
            }
        }

        /// <summary>
        /// Validates identity locally, then constructs a real Session on a non-networked transport.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The sample endpoint has no transport configuration.
        /// </exception>
        private async Task<ISession> CreateSessionAsync(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection? connection,
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect,
            bool checkDomain,
            IUserIdentity? identity,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Attempts++;
            Endpoint = endpoint;
            Connection = connection;
            CheckDomain = checkDomain;
            UpdateBeforeConnect = updateBeforeConnect;
            using var certificate = Certificate.FromRawData(endpoint.Description.ServerCertificate);
            CertificateValidationResult validation = await configuration.CertificateManager
                .ValidateAsync(certificate, ct: ct).ConfigureAwait(false);
            validation.ThrowIfInvalid();
            configuration.CertificateManager.ValidateApplicationUri(certificate, endpoint);
            if (checkDomain)
            {
                configuration.CertificateManager.ValidateDomains(certificate, endpoint, serverValidation: true);
            }
            EndpointConfiguration transportConfiguration = endpoint.Configuration
                ?? throw new InvalidOperationException("The sample endpoint has no transport configuration.");
            Identity = identity;
            Activations++;
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(value => value.MessageContext).Returns(configuration.CreateMessageContext());
            channel.SetupGet(value => value.EndpointDescription).Returns(endpoint.Description);
            channel.SetupGet(value => value.EndpointConfiguration).Returns(transportConfiguration);
            channel.SetupGet(value => value.OperationTimeout).Returns(10_000);
            channel.Setup(value => value.SendRequestAsync(
                    It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>(DispatchAsync);
            var session = new Session(
                channel.Object, configuration, endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            session.SessionCreated(new NodeId("sample-session", 1), new NodeId("sample-token", 1));
            m_sessions.Add(session);
            return session;
        }

        /// <summary>
        /// Handles only deterministic service traffic from the locally created sessions.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The locally created session sends an unsupported service request.
        /// </exception>
        private ValueTask<IServiceResponse> DispatchAsync(IServiceRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            var header = new ResponseHeader { RequestHandle = request.RequestHeader.RequestHandle };
            IServiceResponse response = request switch
            {
                ReadRequest read => new ReadResponse
                {
                    ResponseHeader = header,
                    Results = read.NodesToRead.ConvertAll(static node => node.NodeId == s_markerNode
                        ? new DataValue(Variant.From(42))
                        : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown))
                },
                CloseSessionRequest => new CloseSessionResponse { ResponseHeader = header },
                _ => throw new InvalidOperationException($"Unexpected local request: {request.TypeId}.")
            };
            return new ValueTask<IServiceResponse>(response);
        }

        /// <summary>The only successful value available from the synthetic peer.</summary>
        private static readonly NodeId s_markerNode = new("sample-marker", 1);

        /// <summary>Sessions owned by the fixture, including partially consumed connection results.</summary>
        private readonly List<Session> m_sessions = [];
    }
}
