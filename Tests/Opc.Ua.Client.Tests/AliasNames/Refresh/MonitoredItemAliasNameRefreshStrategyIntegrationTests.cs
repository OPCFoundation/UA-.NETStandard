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
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.AliasNames.Refresh;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.AliasNames.Refresh
{
    /// <summary>
    /// End-to-end test for <see cref="MonitoredItemAliasNameRefreshStrategy"/>
    /// against the live <c>ReferenceServer</c>. Validates that the
    /// strategy successfully creates a subscription + monitored item on
    /// the well-known <c>Aliases.LastChange</c> property and cleans up
    /// on dispose.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MonitoredItemAliasNameRefreshStrategyIntegrationTests
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
                    "MonitoredItem strategy integration setup failed: " + e.Message);
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
        public async Task StrategyCreatesAndDisposesSubscriptionAsync()
        {
            int initialCount = m_session.SubscriptionCount;
            AliasNameClient client = AliasNameClient.OpenStandardAliases(m_session);

            AliasNameResolver resolver = new(
                client,
                new AliasNameResolverOptions
                {
                    RefreshMode = AliasNameResolverRefreshMode
                        .AutoOnLastChangeMonitoredItem,
                    PublishingIntervalMs = 500,
                    LastChangeSamplingIntervalMs = 500,
                });
            try
            {
                await resolver.EnsureLoadedAsync();
                Assert.That(m_session.SubscriptionCount,
                    Is.EqualTo(initialCount + 1),
                    "MonitoredItem strategy must add one subscription.");
            }
            finally
            {
                await resolver.DisposeAsync();
            }

            // Allow a moment for the RemoveSubscription/Delete round-trips.
            for (int i = 0; i < 20
                && m_session.SubscriptionCount != initialCount; i++)
            {
                await Task.Delay(50);
            }
            Assert.That(m_session.SubscriptionCount, Is.EqualTo(initialCount),
                "Owned subscription must be removed on DisposeAsync.");
        }

        [Test]
        public async Task ManualResolverDoesNotCreateSubscriptionAsync()
        {
            int initialCount = m_session.SubscriptionCount;
            AliasNameClient client = AliasNameClient.OpenStandardAliases(m_session);

            await using var resolver = new AliasNameResolver(
                client,
                new AliasNameResolverOptions
                {
                    RefreshMode = AliasNameResolverRefreshMode.Manual
                });
            await resolver.EnsureLoadedAsync();

            Assert.That(m_session.SubscriptionCount, Is.EqualTo(initialCount));
        }
    }
}
