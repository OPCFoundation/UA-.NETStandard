/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    internal sealed class SessionChannelHarness : IAsyncDisposable
    {
        public SessionChannelHarness(
            ITelemetryContext? telemetry = null,
            TimeProvider? timeProvider = null,
            IChannelReconnectPolicy? reconnectPolicy = null,
            Action<ScriptedChannel>? configureChannel = null)
        {
            Telemetry = telemetry ?? NUnitTelemetryContext.Create();
            TimeProvider = timeProvider ?? TimeProvider.System;
            m_configureChannel = configureChannel;
            Configuration = CreateConfiguration(Telemetry);
            var bindings = new Mock<ITransportChannelBindings>();
            bindings.Setup(b => b.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                .Returns(() => CreateManagedChannel());
            Manager = new ClientChannelManager(
                Configuration,
                Telemetry,
                bindings.Object,
                reconnectPolicy ?? new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 1,
                    ParticipantTimeout = Timeout.InfiniteTimeSpan
                },
                TimeProvider);
        }

        public ApplicationConfiguration Configuration { get; }

        public ClientChannelManager Manager { get; }

        public ITelemetryContext Telemetry { get; }

        public TimeProvider TimeProvider { get; }

        public List<ScriptedChannel> CreatedChannels { get; } = [];

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync().ConfigureAwait(false);
        }

        public Task<Session> CreateSessionAsync(ConfiguredEndpoint endpoint)
        {
            return Session.CreateAsync(
                Manager,
                Configuration,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "ClientChannelManagerManagedTests",
                sessionTimeout: 60000,
                identity: new UserIdentity(),
                timeProvider: TimeProvider,
                ct: default);
        }

        public ScriptedChannel CreateOpenedStandaloneChannel(ConfiguredEndpoint endpoint)
        {
            var channel = new ScriptedChannel(Configuration.CreateMessageContext(), TimeProvider);
            channel.OpenForEndpoint(endpoint);
            return channel;
        }

        public static ConfiguredEndpoint CreateEndpoint(string endpointUrl = "opc.tcp://localhost:4840")
        {
            var endpointConfiguration = new EndpointConfiguration
            {
                OperationTimeout = 6000
            };
            var description = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            description.Server.ApplicationUri = endpointUrl;
            description.Server.ApplicationType = ApplicationType.Server;

            return new ConfiguredEndpoint(null, description, endpointConfiguration)
            {
                UpdateBeforeConnect = false
            };
        }

        private static ApplicationConfiguration CreateConfiguration(ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "ClientChannelManagerManagedTests",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:ClientChannelManagerManagedTests",
                ProductUri = "urn:localhost:ClientChannelManagerManagedTests",
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = 60000,
                    MinSubscriptionLifetime = 10000
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = 6000,
                    MaxMessageSize = 1_048_576,
                    MaxStringLength = 1_048_576,
                    MaxByteStringLength = 1_048_576,
                    MaxArrayLength = 65_535
                }
            };
        }

        private ITransportChannel CreateManagedChannel()
        {
            var channel = new ScriptedChannel(Configuration.CreateMessageContext(), TimeProvider);
            m_configureChannel?.Invoke(channel);
            CreatedChannels.Add(channel);
            return channel.Channel;
        }

        private readonly Action<ScriptedChannel>? m_configureChannel;
    }
}
