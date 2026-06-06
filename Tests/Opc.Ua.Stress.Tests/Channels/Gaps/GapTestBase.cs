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
using System.Threading.Tasks;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Tests;

// CA2000: gap-test factories return disposables to callers that release them in each test.
#pragma warning disable CA2000

namespace Opc.Ua.Stress.Tests.Channels.Gaps
{
    /// <summary>
    /// Minimal shared setup for Layer-5 channel-manager production-gap tests.
    /// </summary>
    public abstract class GapTestBase
    {
        protected static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(5);
        protected static readonly TimeSpan ObservationWindow = TimeSpan.FromMilliseconds(250);

        protected static ClientChannelManager CreateManager(FakeChannelBindings bindings)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ClientChannelManager(
                CreateApplicationConfiguration(telemetry),
                telemetry,
                bindings,
                CreateNoDelayReconnectPolicy());
        }

        protected static ConfiguredEndpoint CreateEndpoint(
            string endpointUrl = "opc.tcp://localhost:4840")
        {
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

            return new ConfiguredEndpoint(
                collection: null,
                description,
                CreateEndpointConfiguration())
            {
                UpdateBeforeConnect = false
            };
        }

        protected static ValueTask<ParticipantReconnectResult> ReconnectResultAsync(
            ParticipantReconnectResult result)
        {
            return new ValueTask<ParticipantReconnectResult>(result);
        }

        private static ApplicationConfiguration CreateApplicationConfiguration(
            ITelemetryContext telemetry)
        {
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Opc.Ua.Stress.Tests.Channels.Gaps",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:Opc.Ua.Stress.Tests.Channels.Gaps",
                ProductUri = "urn:localhost:Opc.Ua.Stress.Tests.Channels.Gaps",
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

        private static EndpointConfiguration CreateEndpointConfiguration()
        {
            return new EndpointConfiguration
            {
                OperationTimeout = 6000,
                UseBinaryEncoding = true,
                MaxMessageSize = 1_048_576,
                MaxStringLength = 1_048_576,
                MaxByteStringLength = 1_048_576,
                MaxArrayLength = 65_535
            };
        }

        private static ExponentialBackoffChannelReconnectPolicy CreateNoDelayReconnectPolicy()
        {
            return new ExponentialBackoffChannelReconnectPolicy
            {
                MinDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                MaxAttempts = 1
            };
        }
    }
}
