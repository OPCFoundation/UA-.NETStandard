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

#if NET5_0_OR_GREATER

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end integration tests for the WSS+uajson transport profile
    /// (Part 6 §7.5.2 — <c>opcua+uajson</c> sub-protocol over TLS-secured
    /// WebSockets). The JSON sub-protocol does not use UA Secure
    /// Conversation, so it operates exclusively at
    /// <see cref="MessageSecurityMode.None"/> with transport security
    /// provided by TLS at the WebSocket layer.
    /// </summary>
    [TestFixture]
    [Category("WssJsonIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WssJsonTransportIntegrationTests
    {
        private const int kMaxTimeout = 30_000;
        private ITelemetryContext m_telemetry;
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
        private string m_pkiRoot;
        private Uri m_endpointUrl;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                UriScheme = Utils.UriSchemeOpcWss,
                HttpsMutualTls = false,
                MaxChannelCount = 8,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(telemetry: m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            m_endpointUrl = new Uri(
                Utils.ReplaceLocalhost(
                    $"opc.wss://localhost:{m_serverFixture.Port.ToString(CultureInfo.InvariantCulture)}/" +
                    nameof(ReferenceServer)));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_clientFixture?.Dispose();
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            try
            {
                if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public void ServerExposesWssEndpointWithSecurityNone()
        {
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            // Diagnostic dump - helps when this test fails.
            foreach (EndpointDescription ep in endpoints.ToArray())
            {
                TestContext.Out.WriteLine(
                    $"  endpoint: TransportProfileUri={ep.TransportProfileUri}, " +
                    $"SecurityMode={ep.SecurityMode}, Url={ep.EndpointUrl}");
            }
            EndpointDescription none = endpoints
                .ToArray()
                .FirstOrDefault(ep =>
                    string.Equals(ep.TransportProfileUri, Profiles.UaWssTransport, StringComparison.Ordinal) &&
                    ep.SecurityMode == MessageSecurityMode.None);
            Assert.That(none, Is.Not.Null,
                "Reference server did not advertise an unsecured WSS endpoint - JSON sub-protocol requires SM None.");
        }

        [Test]
        public async Task GetEndpointsOverWssJsonReturnsServerEndpointsAsync()
        {
            // Discovery does not yet advertise the JSON sub-protocol explicitly
            // (Part 3 reverted in 469d65b0). Synthesize the JSON-targeted
            // endpoint description from the existing SM-None WSS endpoint so
            // the test still exercises the wire path end-to-end.
            EndpointDescription wssNone = m_server.GetEndpoints()
                .ToArray()
                .First(ep =>
                    string.Equals(ep.TransportProfileUri, Profiles.UaWssTransport, StringComparison.Ordinal) &&
                    ep.SecurityMode == MessageSecurityMode.None);
            var jsonEndpoint = new EndpointDescription
            {
                EndpointUrl = wssNone.EndpointUrl,
                Server = wssNone.Server,
                ServerCertificate = wssNone.ServerCertificate,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaWssJsonTransport
            };

            var endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            endpointConfiguration.OperationTimeout = kMaxTimeout;
            TransportChannelSettings settings = CreateClientSettings(jsonEndpoint, endpointConfiguration);

            using var channel = new WssJsonTransportChannel(m_telemetry);
            await channel.OpenAsync(new Uri(jsonEndpoint.EndpointUrl), settings, CancellationToken.None)
                .ConfigureAwait(false);

            var request = new GetEndpointsRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    TimeoutHint = kMaxTimeout
                },
                EndpointUrl = jsonEndpoint.EndpointUrl
            };

            IServiceResponse response = await channel
                .SendRequestAsync(request)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<GetEndpointsResponse>());
            var getResp = (GetEndpointsResponse)response;
            Assert.That(getResp.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(getResp.Endpoints.Count, Is.GreaterThan(0));

            await channel.CloseAsync().ConfigureAwait(false);
        }

        private TransportChannelSettings CreateClientSettings(
            EndpointDescription endpoint,
            EndpointConfiguration endpointConfiguration)
        {
            return new TransportChannelSettings
            {
                Description = endpoint,
                Configuration = endpointConfiguration,
                NamespaceUris = m_clientFixture.Config.CreateMessageContext().NamespaceUris,
                Factory = m_clientFixture.Config.CreateMessageContext().Factory,
                CertificateValidator = m_clientFixture.Config.CertificateManager
            };
        }
    }
}

#endif // NET5_0_OR_GREATER

