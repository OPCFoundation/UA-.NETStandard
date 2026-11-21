// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using Moq;
using Opc.Ua.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Session with channel mock
    /// </summary>
    public sealed class SessionMock : Session
    {
        /// <summary>
        /// Get private field m_serverNonce from base class using reflection
        /// </summary>
        public ByteString ServerNonce =>
            (ByteString)typeof(Session)
                .GetField(
                    "m_serverNonce",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)!
                .GetValue(this)!;

        /// <summary>
        /// Create the mock
        /// </summary>
        public SessionMock(
            Mock<ITransportChannel> channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint)
            : base(
                  channel.Object,
                  configuration,
                  endpoint,
                  null)
        {
            Channel = channel;
        }

        public SessionMock(
            Mock<ITransportChannel> channel,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ArrayOf<EndpointDescription> availableEndpoints,
            ArrayOf<string> discoveryProfileUris = default)
            : base(
                  channel.Object,
                  configuration,
                  endpoint,
                  clientCertificate: null,
                  clientCertificateChain: null,
                  availableEndpoints: availableEndpoints,
                  discoveryProfileUris: discoveryProfileUris,
                  engineFactory: null)
        {
            Channel = channel;
        }

        /// <summary>
        /// Create default mock
        /// </summary>
        /// <returns></returns>
        public static SessionMock Create(
            EndpointDescription? endpoint = null,
            ArrayOf<EndpointDescription> availableEndpoints = default,
            ArrayOf<string> discoveryProfileUris = default)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(s => s.MessageContext)
                .Returns(ServiceMessageContext.Create(telemetry));
            channel
                .SetupGet(s => s.SupportedFeatures)
                .Returns(TransportChannelFeatures.Reconnect);
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration() // TODO: Reasonable defaults!
            };

            endpoint ??= new EndpointDescription
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    new UserTokenPolicy()
                ]
            };

            // TODO: Allow mocking of application certificate loading
            var application = new ApplicationInstance(configuration, telemetry);
            if (endpoint.SecurityMode != MessageSecurityMode.None)
            {
                application.CheckApplicationInstanceCertificatesAsync(true).AsTask().GetAwaiter().GetResult();
            }

            var configuredEndpoint = new ConfiguredEndpoint(
                null!,
                endpoint ??
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    EndpointUrl = "opc.tcp://localhost:4840",
                    UserIdentityTokens =
                        [
                            new UserTokenPolicy()
                        ]
                });

            if (availableEndpoints.IsEmpty && discoveryProfileUris.IsEmpty)
            {
                return new SessionMock(channel, configuration, configuredEndpoint);
            }

            return new SessionMock(
                channel,
                configuration,
                configuredEndpoint,
                availableEndpoints,
                discoveryProfileUris);
        }

        public void SetConnected()
        {
            SessionCreated(NodeId.Parse("s=connected"), NodeId.Parse("s=auth"));
            RenewUserIdentity += Sut_RenewUserIdentity;
        }

        /// <summary>
        /// Marks the session connected and records a successful keep-alive response.
        /// </summary>
        public void SetConnectedAndResponsive()
        {
            SetConnected();
            OnKeepAlive(ServerState.Running, DateTime.UtcNow);
        }

        private IUserIdentity Sut_RenewUserIdentity(ISession session, IUserIdentity identity)
        {
            return identity ?? new UserIdentity();
        }

        /// <summary>
        /// Channel mock
        /// </summary>
        public Mock<ITransportChannel> Channel { get; }
    }
}
