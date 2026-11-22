// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET8_0_OR_GREATER

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Representative subset of the <see cref="ClientTest"/> matrix run
    /// against a server whose <c>opc.tcp</c> listener binding has been
    /// swapped to the Kestrel-hosted <see cref="KestrelTcpTransportListenerFactory"/>
    /// (Phase G4). This validates the Kestrel-TCP listener is wire-
    /// compatible with the standard client-server flow (connect, browse,
    /// read, write, subscribe) without changing the raw-socket
    /// <c>TcpTransportListener</c> default. The fixture only runs a
    /// curated subset to keep the test runtime bounded; broader matrix
    /// coverage is left to the existing TCP fixture.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("KestrelTcp")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class ClientTestKestrelTcp
    {
        private readonly ClientTest m_clientTest;

        public ClientTestKestrelTcp()
        {
            m_clientTest = new ClientTest(Utils.UriSchemeOpcTcp);
        }

        /// <summary>
        /// Build a per-fixture <see cref="ITransportBindingRegistry"/>
        /// that swaps the <c>opc.tcp</c> listener for the Kestrel-TCP
        /// factory. The client side continues to use the raw-socket TCP
        /// transport channel - this fixture validates the Kestrel-TCP
        /// LISTENER, not the channel.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            var registry = DefaultTransportBindingRegistry
                .WithDefaultTcp();
            registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
            m_clientTest.TransportBindingRegistry = registry;

            m_clientTest.SupportsExternalServerUrl = true;
            await m_clientTest.OneTimeSetUpCoreAsync(true).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public Task OneTimeTearDownAsync()
        {
            return m_clientTest.OneTimeTearDownAsync();
        }

        [SetUp]
        public Task SetUpAsync()
        {
            return m_clientTest.SetUpAsync();
        }

        [TearDown]
        public Task TearDownAsync()
        {
            return m_clientTest.TearDownAsync();
        }

        /// <summary>
        /// Smoke: GetEndpoints over Kestrel-TCP listener.
        /// </summary>
        [Test]
        public Task GetEndpointsOnDiscoveryChannelAsync()
        {
            return m_clientTest.GetEndpointsOnDiscoveryChannelAsync(true);
        }

        /// <summary>
        /// Connect / close a SecurityNone session.
        /// </summary>
        [Test]
        [DependsOnTest(nameof(GetEndpointsOnDiscoveryChannelAsync), AllowFailure = true)]
        public Task ConnectAndCloseAsyncNoSecurityAsync()
        {
            return m_clientTest.ConnectAndCloseAsync(SecurityPolicies.None);
        }

        /// <summary>
        /// Browse the full address space; representative coverage of
        /// service-level traffic over the Kestrel-TCP listener.
        /// </summary>
        [Theory]
        [DependsOnTest(nameof(ConnectAndCloseAsyncNoSecurityAsync), AllowFailure = true)]
        public Task BrowseFullAddressSpaceSecurityNoneAsync(bool operationLimits)
        {
            return m_clientTest.BrowseFullAddressSpaceAsync(SecurityPolicies.None, operationLimits);
        }
    }
}

#endif // NET8_0_OR_GREATER
