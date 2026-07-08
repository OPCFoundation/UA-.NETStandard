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
    /// End-to-end integration tests for the HTTPS-JSON transport profile
    /// (OPC UA Part 6 §7.4.5 — <c>application/opcua+uajson</c> over an
    /// HTTPS POST). The JSON profile does not use UA Secure Conversation,
    /// so it operates exclusively at <see cref="MessageSecurityMode.None"/>
    /// with transport security provided by TLS at the HTTPS layer.
    /// </summary>
    [TestFixture]
    [Category("HttpsJsonIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class HttpsJsonTransportIntegrationTests
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
                UriScheme = Utils.UriSchemeOpcHttps,
                HttpsMutualTls = false,
                MaxChannelCount = 8,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(telemetry: m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            m_endpointUrl = new Uri(
                Utils.ReplaceLocalhost(
                    $"opc.https://localhost:{m_serverFixture.Port.ToString(CultureInfo.InvariantCulture)}/" +
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
        public void ServerExposesHttpsEndpointWithSecurityNone()
        {
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription none = endpoints
                .ToArray()
                .FirstOrDefault(ep =>
                    string.Equals(ep.TransportProfileUri, Profiles.HttpsBinaryTransport, StringComparison.Ordinal) &&
                    ep.SecurityMode == MessageSecurityMode.None);
            Assert.That(none, Is.Not.Null,
                "Reference server did not advertise an unsecured HTTPS endpoint - JSON sub-protocol requires SM None.");
        }

        [Test]
        public async Task GetEndpointsOverHttpsJsonReturnsServerEndpointsAsync()
        {
            // Discovery doesn't currently emit the JSON sub-profile; synthesise
            // the JSON-targeted endpoint description from the existing
            // SM-None HTTPS endpoint so the wire path is fully exercised.
            EndpointDescription httpsNone = m_server.GetEndpoints()
                .ToArray()
                .First(ep =>
                    string.Equals(ep.TransportProfileUri, Profiles.HttpsBinaryTransport, StringComparison.Ordinal) &&
                    ep.SecurityMode == MessageSecurityMode.None);
            var jsonEndpoint = new EndpointDescription
            {
                EndpointUrl = httpsNone.EndpointUrl,
                Server = httpsNone.Server,
                ServerCertificate = httpsNone.ServerCertificate,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.HttpsJsonTransport
            };

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            endpointConfiguration.OperationTimeout = kMaxTimeout;
            TransportChannelSettings settings = CreateClientSettings(jsonEndpoint, endpointConfiguration);

            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry);
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
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<GetEndpointsResponse>());
            var getResp = (GetEndpointsResponse)response;
            Assert.That(getResp.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(getResp.Endpoints.Count, Is.GreaterThan(0));

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
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
