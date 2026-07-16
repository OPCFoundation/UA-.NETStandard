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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end integration tests for the WSS+uacp transport profile
    /// (Part 6 §7.5.2 — <c>opcua+uacp</c> sub-protocol over TLS-secured
    /// WebSockets). Spins up the standard reference server bound to an
    /// <c>opc.wss://</c> base address, connects with the regular OPC UA
    /// client stack, and exercises the discovery / SecureChannel /
    /// Browse / Close paths to prove the new wire path is functionally
    /// equivalent to <c>opc.tcp://</c>.
    /// </summary>
    [TestFixture]
    [Category("WssIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WssTransportIntegrationTests
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
                SecurityNone = false,
                UriScheme = Utils.UriSchemeOpcWss,
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
        public void ServerExposesWssEndpointDescription()
        {
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription wss = endpoints
                .ToArray()
                .FirstOrDefault(ep => string.Equals(
                    ep.TransportProfileUri,
                    Profiles.UaWssTransport,
                    StringComparison.Ordinal));
            Assert.That(wss, Is.Not.Null, "Reference server did not advertise a WSS endpoint.");
            Assert.That(wss.EndpointUrl, Does.StartWith("opc.wss://").Or.StartWith("wss://"));
        }

        [Test]
        public async Task GetEndpointsViaWssReturnsAtLeastOneSecureEndpointAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create(m_clientFixture.Config);
            endpointConfiguration.OperationTimeout = kMaxTimeout;
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                m_clientFixture.Config,
                m_endpointUrl,
                endpointConfiguration).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(default)
                .ConfigureAwait(false);
            await client.CloseAsync().ConfigureAwait(false);

            Assert.That(endpoints.Count, Is.GreaterThan(0));
            Assert.That(
                endpoints.ToArray(),
                Has.Some.Matches<EndpointDescription>(
                    ep => string.Equals(
                        ep.TransportProfileUri,
                        Profiles.UaWssTransport,
                        StringComparison.Ordinal)));
        }

        [Test]
        public async Task ConnectAndBrowseServerNodeAsync()
        {
            using ISession session = await m_clientFixture
                .ConnectAsync(m_endpointUrl.ToString())
                .ConfigureAwait(false);
            Assert.That(session, Is.Not.Null);
            Assert.That(session.Connected, Is.True);
            Assert.That(
                session.ConfiguredEndpoint?.Description?.TransportProfileUri,
                Is.EqualTo(Profiles.UaWssTransport));

            // The fixture configures the server with the default
            // HttpsMutualTls = true, so a successful SecureChannel here
            // implies the client's application TLS certificate cleared the
            // mutual-TLS handshake at the WebSocket layer in addition to
            // the OPC UA UASC OpenSecureChannel that follows it.
            Assert.That(
                session.ConfiguredEndpoint?.Description?.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));

            ArrayOf<ReferenceDescription> refs = await session
                .FetchReferencesAsync(new NodeId(Objects.Server))
                .ConfigureAwait(false);
            Assert.That(refs.Count, Is.GreaterThan(0));

            await session.CloseAsync().ConfigureAwait(false);
        }
    }
}

#endif // NET5_0_OR_GREATER
