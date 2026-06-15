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

#if NET8_0_OR_GREATER

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end smoke tests for the WSS Web API / OpenAPI sub-protocol
    /// (OPC UA Part 6 §7.5.2 <c>opcua+openapi</c>; OPC Foundation
    /// profile/2339). Verifies that:
    /// <list type="bullet">
    /// <item>the reference server advertises the WSS OpenAPI endpoint on
    ///   discovery (Phase 1B discovery emission applies to the WSS
    ///   factories too);</item>
    /// <item>a <see cref="ManagedSession"/> over the
    ///   <see cref="WebApiWssTransportChannel"/> opens, activates, and
    ///   reads end-to-end; and</item>
    /// <item>the bearer-token sub-protocol variant
    ///   <c>opcua+openapi+&lt;accesstoken&gt;</c> negotiates correctly
    ///   (sub-protocol name carries the credential because browser
    ///   WebSocket APIs forbid custom HTTP headers).</item>
    /// </list>
    /// </summary>
    [TestFixture]
    [Category("WssOpenApiIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WssOpenApiIntegrationTests
    {
        private const int kMaxTimeout = 30_000;
        private ITelemetryContext m_telemetry;
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
        private string m_pkiRoot;
        private Uri m_baseAddress;

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
            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.Certificate);
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(telemetry: m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            // The WSS factories register at "opc.wss://" — same scheme
            // the OpenAPI channel uses (it strips the "opc." prefix
            // before passing the URL to ClientWebSocket).
            m_baseAddress = new Uri(
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
        public void ReferenceServerEmitsWssOpenApiDiscoveryEndpoint()
        {
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription openApi = endpoints
                .ToArray()
                .FirstOrDefault(ep => Profiles.IsWssOpenApi(ep.TransportProfileUri));
            Assert.That(openApi, Is.Not.Null,
                "Reference server must advertise the WSS OpenAPI sub-profile (profile/2339) " +
                "as a discovery-only twin alongside the SM=None WSS binary endpoint.");
            Assert.That(openApi.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
            Assert.That(openApi.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
        }

        [Test]
        public async Task ManagedSessionOverWssOpenApiOpensActivatesAndReadsAsync()
        {
            ApplicationConfiguration appConfig = m_clientFixture.Config;

            await using ManagedSession session = await new ManagedSessionBuilder(appConfig, m_telemetry)
                .UseWssOpenApiEndpoint(m_baseAddress.ToString())
                .WithUserIdentity(new UserIdentity("user1", "password"u8))
                .WithSessionName("ManagedSessionWssOpenApi-Lifecycle")
                .WithCheckDomain(false)
                .ConnectAsync(default).ConfigureAwait(false);

            Assert.That(session.Connected, Is.True,
                "ManagedSession over WSS OpenAPI must report Connected after ConnectAsync.");
            Assert.That(session.SessionId.IsNull, Is.False,
                "Server must allocate a non-null SessionId.");

            var nodesToRead = new ArrayOf<ReadValueId>(new[]
            {
                new ReadValueId
                {
                    NodeId = VariableIds.Server_NamespaceArray,
                    AttributeId = Attributes.Value
                }
            }.AsMemory());

            ReadResponse response = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                TimestampsToReturn.Both,
                nodesToRead,
                default).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(
                (uint)response.Results[0].StatusCode,
                Is.EqualTo((uint)StatusCodes.Good),
                "Reading Server.NamespaceArray over WSS OpenAPI must succeed.");
        }

        [Test]
        public async Task ManagedSessionOverWssOpenApiBearerNegotiatesSubProtocolAsync()
        {
            // Bearer-token variant: the access token is carried in the
            // sub-protocol name (opcua+openapi+<accesstoken>) per
            // Part 6 §7.5.2 because browser WebSocket APIs forbid
            // custom HTTP headers. The reference server's default
            // ISessionlessIdentityProvider doesn't validate the token
            // (Phase 3 follow-up), so the test only asserts that the
            // sub-protocol negotiates and the session reaches
            // ActivateSession with a UserName identity (which the server
            // does validate). This proves the WSS bearer-token framing
            // is wired end-to-end.
            ApplicationConfiguration appConfig = m_clientFixture.Config;

            await using ManagedSession session = await new ManagedSessionBuilder(appConfig, m_telemetry)
                .UseWssOpenApiEndpoint(m_baseAddress.ToString())
                .WithWebApiAuthentication(opts =>
                    opts.BearerToken = "test-bearer-token-placeholder")
                .WithUserIdentity(new UserIdentity("user1", "password"u8))
                .WithSessionName("ManagedSessionWssOpenApi-Bearer")
                .WithCheckDomain(false)
                .ConnectAsync(default).ConfigureAwait(false);

            Assert.That(session.Connected, Is.True,
                "ManagedSession over the WSS opcua+openapi+<accesstoken> sub-protocol " +
                "must report Connected after ConnectAsync.");
            Assert.That(session.SessionId.IsNull, Is.False);
        }
    }
}

#endif
