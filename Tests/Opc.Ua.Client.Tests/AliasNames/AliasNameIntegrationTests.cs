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

// CA2007: NUnit tests do not run in a synchronization context; the
// ConfigureAwait noise inside the integration setup adds nothing.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.AliasNames
{
    /// <summary>
    /// End-to-end Part 17 integration tests against the live
    /// <c>ReferenceServer</c> — exercises the standard well-known
    /// <c>TagVariables</c>/<c>Topics</c> categories whose
    /// <c>FindAlias</c> methods are wired via the
    /// <c>DiagnosticsNodeManager</c> late binder against the in-memory
    /// store registered by <c>ReferenceServer.ConfigureAliasNameStore</c>.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class AliasNameIntegrationTests
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
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot);

            m_clientFixture = new ClientFixture(telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot);
            m_url = new Uri(Utils.UriSchemeOpcTcp + "://localhost:"
                + m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));

            try
            {
                m_session = await m_clientFixture.ConnectAsync(
                    m_url, SecurityPolicies.None);
            }
            catch (Exception e)
            {
                Assert.Ignore(
                    "AliasName integration setup failed: " + e.Message);
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

        [Test]
        public async Task FindAliasOnStandardTagVariablesReturnsSeededEntriesAsync()
        {
            AliasNameClient client = AliasNameClient
                .OpenStandardTagVariables(m_session);

            IReadOnlyList<AliasNameDataType> result =
                await client.FindAliasAsync("%", referenceTypeFilter: null);

            Assert.That(result, Is.Not.Empty,
                "ReferenceServer should seed at least one TagVariables alias.");
            var names = new HashSet<string>();
            foreach (AliasNameDataType a in result)
            {
                names.Add(a.AliasName.Name!);
            }
            Assert.That(names, Does.Contain("TIC101_Setpoint"));
        }

        [Test]
        public async Task FindAliasWithPrefixWildcardMatchesAsync()
        {
            AliasNameClient client = AliasNameClient
                .OpenStandardTagVariables(m_session);

            IReadOnlyList<AliasNameDataType> result =
                await client.FindAliasAsync("TIC%", null);

            foreach (AliasNameDataType a in result)
            {
                Assert.That(a.AliasName.Name, Does.StartWith("TIC"));
            }
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        public async Task FindAliasOnTopicsReturnsServerEventsAsync()
        {
            AliasNameClient client = AliasNameClient
                .OpenStandardTopics(m_session);

            IReadOnlyList<AliasNameDataType> result =
                await client.FindAliasAsync("ServerEvents", null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].AliasName.Name, Is.EqualTo("ServerEvents"));
            Assert.That(result[0].ReferencedNodes.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task ResolverRoundTripsAliasToNodeIdAsync()
        {
            AliasNameClient client = AliasNameClient
                .OpenStandardTagVariables(m_session);
            await using var resolver = new AliasNameResolver(client);

            IReadOnlyList<ExpandedNodeId> targets =
                await resolver.ResolveAsync("Pump1_Status");
            Assert.That(targets, Is.Not.Empty);

            string reverse = await resolver.ResolveAliasNameAsync(targets[0]);
            Assert.That(reverse, Is.EqualTo("Pump1_Status"));
        }
    }
}
