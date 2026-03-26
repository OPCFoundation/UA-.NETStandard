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

using Moq;
using Opc.Ua.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Session with channel mock
    /// </summary>
    public sealed class SessionMock : Session
    {
        /// <summary>
        /// Get private field m_serverNonce from base class using reflection
        /// </summary>
        internal byte[] ServerNonce =>
            (byte[])typeof(Session)
                .GetField(
                    "m_serverNonce",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance)
                .GetValue(this);

        /// <summary>
        /// Create the mock
        /// </summary>
        internal SessionMock(
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

        /// <summary>
        /// Create default mock
        /// </summary>
        /// <returns></returns>
        public static SessionMock Create(EndpointDescription endpoint = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var channel = new Mock<ITransportChannel>();
            channel
                .SetupGet(s => s.MessageContext)
                .Returns(new ServiceMessageContext(telemetry));
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

            return new SessionMock(channel, configuration,
                new ConfiguredEndpoint(null, endpoint ??
                    new EndpointDescription
                    {
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        EndpointUrl = "opc.tcp://localhost:4840",
                        UserIdentityTokens =
                        [
                            new UserTokenPolicy()
                        ]
                    }));
        }

        internal void SetConnected()
        {
            SessionCreated(NodeId.Parse("s=connected"), NodeId.Parse("s=auth"));
            RenewUserIdentity += Sut_RenewUserIdentity;
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
