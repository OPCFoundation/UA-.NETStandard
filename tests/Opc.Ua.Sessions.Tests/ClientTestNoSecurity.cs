/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Client tests which require security None and are otherwise skipped,
    /// starts the server with additional security None profile.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    public class ClientTestNoSecurity
    {
        private readonly ClientTest m_clientTest;

        public static readonly object[] FixtureArgs =
        [
            new object[] { Utils.UriSchemeOpcTcp }
            // https protocol security None is not supported
            // new object [] { Utils.UriSchemeHttps},
            // new object [] { Utils.UriSchemeOpcHttps},
        ];

        public ClientTestNoSecurity()
        {
            m_clientTest = new ClientTest(Utils.UriSchemeOpcTcp);
        }

        public ClientTestNoSecurity(string uriScheme)
        {
            m_clientTest = new ClientTest(uriScheme);
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public Task OneTimeSetUpAsync()
        {
            m_clientTest.SupportsExternalServerUrl = true;
            return m_clientTest.OneTimeSetUpCoreAsync(true);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public Task OneTimeTearDownAsync()
        {
            return m_clientTest.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public Task SetUpAsync()
        {
            return m_clientTest.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public Task TearDownAsync()
        {
            return m_clientTest.TearDownAsync();
        }

        /// <summary>
        /// GetEndpoints on the discovery channel,
        /// the oversized message can pass because security None is enabled.
        /// </summary>
        [Test]
        public Task GetEndpointsOnDiscoveryChannelAsync()
        {
            return m_clientTest.GetEndpointsOnDiscoveryChannelAsync(true);
        }

        [Test]
        [DependsOnTest(nameof(ConnectJWTAsync), AllowFailure = true)]
        public Task ReconnectJWTSecurityNoneAsync()
        {
            return m_clientTest.ReconnectJWTAsync(SecurityPolicies.None);
        }

        [Test]
        [DependsOnTest(nameof(ConnectAndCloseAsyncNoSecurityAsync), AllowFailure = true)]
        public Task ConnectJWTAsync()
        {
            return m_clientTest.ConnectJWTAsync(SecurityPolicies.None);
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets
        /// </summary>
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(120_000)]
        [DependsOnTest(nameof(ReconnectJWTSecurityNoneAsync), AllowFailure = true)]
        public Task ReconnectSessionOnAlternateChannelWithSavedSessionSecretsSecurityNoneAsync(
            bool anonymous,
            CancellationToken ct)
        {
            return m_clientTest.ReconnectSessionOnAlternateChannelWithSavedSessionSecretsAsync(
                SecurityPolicies.None,
                anonymous,
                ct);
        }

        [Theory]
        [DependsOnTest(nameof(ReconnectSessionOnAlternateChannelWithSavedSessionSecretsSecurityNoneAsync), AllowFailure = true)]
        public Task BrowseFullAddressSpaceSecurityNoneAsync(bool operationLimits)
        {
            return m_clientTest.BrowseFullAddressSpaceAsync(SecurityPolicies.None, operationLimits);
        }

        [Test]
        [DependsOnTest(nameof(GetEndpointsOnDiscoveryChannelAsync), AllowFailure = true)]
        public Task ConnectAndCloseAsyncNoSecurityAsync()
        {
            return m_clientTest.ConnectAndCloseAsync(SecurityPolicies.None);
        }
    }
}
