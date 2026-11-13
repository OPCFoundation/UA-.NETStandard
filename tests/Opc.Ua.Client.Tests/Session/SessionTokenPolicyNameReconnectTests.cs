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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// A user token policy advertised by a short policy name keeps its
    /// binding across a reconnect, also when a policy with a different
    /// security policy comes first in the endpoint description.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("Client")]
    [Category("Session")]
    public sealed class SessionTokenPolicyNameReconnectTests
    {
        private const string kShortNamePolicyId = "username-short-name";

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "utpn-" + Guid.NewGuid().ToString("N")[..8]);

            m_server = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_server.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ServerConfiguration serverConfiguration = m_server.Config.ServerConfiguration!;

            // A UserName policy without token encryption comes first ...
            serverConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.UserName)
            {
                PolicyId = "username-none",
                SecurityPolicyUri = SecurityPolicies.None
            };

            // ... followed by one that names its policy by the short name.
            serverConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.UserName)
            {
                PolicyId = kShortNamePolicyId,
                SecurityPolicyUri = "Basic256Sha256"
            };

            await m_server.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_telemetry = NUnitTelemetryContext.Create();
            m_client = new ClientFixture(m_telemetry);
            await m_client.LoadClientConfigurationAsync(m_pkiRoot, "TokenPolicyNameClient")
                .ConfigureAwait(false);

            var serverUrl = new Uri(
                $"{Utils.UriSchemeOpcTcp}://localhost:{m_server.Port}/{nameof(ReferenceServer)}");
            m_endpoints = await m_client.GetEndpointsAsync(serverUrl).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// The session activates with the short-name policy and reactivates
        /// with the same policy after a reconnect.
        /// </summary>
        [Test]
        public async Task ShortNamePolicyKeepsItsBindingAcrossReconnectAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            EndpointDescription description = CoreUtils.Clone(m_endpoints.ToList().First(candidate =>
                candidate.SecurityMode == MessageSecurityMode.None &&
                candidate.EndpointUrl!.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal)))!;
            UserTokenPolicy[] userNamePolicies = description.UserIdentityTokens.ToList()
                .Where(policy => policy.TokenType == UserTokenType.UserName)
                .ToArray();
            TestContext.Out.WriteLine(string.Join(", ", userNamePolicies.Select(policy =>
                $"{policy.PolicyId}={policy.SecurityPolicyUri}")));
            // the server assigns the policy ids; find the policies by their URI.
            Assert.That(
                userNamePolicies.Select(policy => policy.SecurityPolicyUri).ToArray(),
                Is.EqualTo(new[] { SecurityPolicies.None, "Basic256Sha256" }),
                "the server advertises both policies in configuration order");
            string shortNamePolicyId = userNamePolicies[1].PolicyId!;

            var identity = new UserIdentity("user1", "password"u8) { PolicyId = shortNamePolicyId };
            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(m_client.Config));
            using ISession session = await new DefaultSessionFactory(m_telemetry).CreateAsync(
                m_client.Config,
                endpoint,
                false,
                false,
                "token-policy-name",
                60000,
                identity,
                default,
                timeout.Token).ConfigureAwait(false);

            await session.ReconnectAsync(timeout.Token).ConfigureAwait(false);

            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));

            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }

        private ServerFixture<ReferenceServer> m_server = null!;
        private ClientFixture m_client = null!;
        private ITelemetryContext m_telemetry = null!;
        private ArrayOf<EndpointDescription> m_endpoints;
        private string m_pkiRoot = null!;
    }
}
