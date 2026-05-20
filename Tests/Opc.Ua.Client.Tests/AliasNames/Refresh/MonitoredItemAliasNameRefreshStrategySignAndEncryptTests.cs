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

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.AliasNames.Refresh;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.AliasNames.Refresh
{
    /// <summary>
    /// End-to-end test that drives <see cref="MonitoredItemAliasNameRefreshStrategy"/>
    /// over a <c>SignAndEncrypt</c> session against the live
    /// <c>ReferenceServer</c>. Confirms the strategy's monitored item on
    /// <c>Aliases.LastChange</c> fires the resolver cache invalidation
    /// when the server-side store mutates, regardless of message security
    /// (the previous integration test only covered <c>SecurityPolicies.None</c>).
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MonitoredItemAliasNameRefreshStrategySignAndEncryptTests
    {
        private ServerFixture<ReferenceServer> m_serverFixture;
#pragma warning disable NUnit1032
        private ClientFixture m_clientFixture;
#pragma warning restore NUnit1032
        private ReferenceServer m_server;
        private ISession m_session;
        private string m_pkiRoot;
        private Uri m_url;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = false,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };

            // Load the server configuration first so we can append a
            // UserName UserTokenPolicy before the endpoints are bound —
            // the default ReferenceServer fixture only advertises
            // Anonymous + Certificate, which makes the sysadmin/demo
            // username login fail with "Endpoint does not support the
            // user identity type provided."
            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot);
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);

            m_server = await m_serverFixture.StartAsync(m_pkiRoot);

            m_clientFixture = new ClientFixture(telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot);
            m_url = new Uri(Utils.UriSchemeOpcTcp + "://localhost:"
                + m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));

            try
            {
                var userIdentity = new UserIdentity("sysadmin", "demo"u8);
                m_session = await m_clientFixture.ConnectAsync(
                    m_url,
                    SecurityPolicies.Basic256Sha256,
                    default,
                    userIdentity);
            }
            catch (Exception e)
            {
                Assert.Ignore(
                    "SignAndEncrypt MI strategy integration setup failed: " + e.Message);
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync();
                m_session.Dispose();
                m_session = null;
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync();
            }
            m_clientFixture?.Dispose();
            m_server?.Dispose();
        }

        /// <summary>
        /// Drives a full end-to-end refresh cycle over <c>SignAndEncrypt</c>:
        /// resolver opens, MI is armed on <c>Aliases.LastChange</c>, the
        /// server-side store gains a new alias (which bumps
        /// <c>LastChange</c>), the MI notification invalidates the cache,
        /// and the next resolve picks the alias up without any explicit
        /// poll or refresh from the test.
        /// </summary>
        [Test]
        public async Task MonitoredItemStrategyInvalidatesCacheUnderSignAndEncryptAsync()
        {
            Assert.That(m_session.MessageContext, Is.Not.Null);
            Assert.That(m_session.Endpoint.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt),
                "Test must run over a SignAndEncrypt session to be meaningful.");

            int initialSubscriptions = m_session.SubscriptionCount;

            AliasNameClient client = AliasNameClient.OpenStandardAliases(m_session);
            await using var resolver = new AliasNameResolver(
                client,
                new AliasNameResolverOptions
                {
                    RefreshMode = AliasNameResolverRefreshMode
                        .AutoOnLastChangeMonitoredItem,
                    PublishingIntervalMs = 200,
                    LastChangeSamplingIntervalMs = 200,
                });

            await resolver.EnsureLoadedAsync();
            Assert.That(m_session.SubscriptionCount,
                Is.EqualTo(initialSubscriptions + 1),
                "MonitoredItem strategy must add one subscription.");

            // Sanity: the new alias name does not exist yet.
            string newAlias = "MITrigger_"
                + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            IReadOnlyList<ExpandedNodeId> before = await resolver.ResolveAsync(newAlias);
            Assert.That(before, Is.Empty,
                "Newly-generated alias name must not pre-exist in cache.");

            // Server-side mutation: add an alias directly via the in-memory
            // store. This bumps the LastChange version which the
            // DiagnosticsNodeManager publishes through Aliases.LastChange.
            var provider = (IAliasNameStoreRegistryProvider)m_server.CurrentInstance;
            IAliasNameStore store = provider.AliasNameStoreRegistry
                .GetStoreForCategory(ObjectIds.Aliases)
                ?? throw new InvalidOperationException(
                    "Expected a registered alias-name store rooted at "
                    + "ObjectIds.Aliases.");

            int refServerNsIndex = m_server.CurrentInstance.NamespaceUris
                .GetIndex(Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            Assert.That(refServerNsIndex, Is.GreaterThanOrEqualTo(0),
                "ReferenceServer namespace must be registered.");
            ushort refServerNs = (ushort)refServerNsIndex;

            var target = new ExpandedNodeId(
                "Scalar_Static_Double", refServerNs);
            var request = new AliasAddRequest(
                newAlias,
                target,
                TargetServer: null,
                TargetReferenceType: ReferenceTypeIds.AliasFor);

            StatusCode[] addResults = await store.AddAliasesAsync(
                ObjectIds.Aliases,
                [request],
                CancellationToken.None);
            Assert.That(addResults, Has.Length.EqualTo(1));
            Assert.That(addResults[0], Is.EqualTo((StatusCode)StatusCodes.Good),
                "Server-side AddAliasesAsync must succeed for the new alias.");

            // Wait for the MonitoredItem invalidation to arrive and the
            // resolver to refetch via FindAlias. Give the publishing
            // pipeline + service round-trip ample time.
            IReadOnlyList<ExpandedNodeId> resolved = [];
            for (int i = 0; i < 100; i++)
            {
                resolved = await resolver.ResolveAsync(newAlias);
                if (resolved.Count > 0)
                {
                    break;
                }
                await Task.Delay(100);
            }

            Assert.That(resolved, Has.Count.EqualTo(1),
                "MonitoredItem invalidation should have triggered a "
                + "refetch picking up the new alias.");
            Assert.That(resolved[0], Is.EqualTo(target),
                "Refetched alias must point at the server-side target.");
        }
    }
}
