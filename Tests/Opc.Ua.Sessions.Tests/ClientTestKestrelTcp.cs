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

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS

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
            DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry
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
        [Order(105)]
        public Task GetEndpointsOnDiscoveryChannelAsync()
        {
            return m_clientTest.GetEndpointsOnDiscoveryChannelAsync(true);
        }

        /// <summary>
        /// Connect / close a SecurityNone session.
        /// </summary>
        [Test]
        [Order(201)]
        public Task ConnectAndCloseAsyncNoSecurityAsync()
        {
            return m_clientTest.ConnectAndCloseAsync(SecurityPolicies.None);
        }

        /// <summary>
        /// Browse the full address space; representative coverage of
        /// service-level traffic over the Kestrel-TCP listener.
        /// </summary>
        [Theory]
        [Order(400)]
        public Task BrowseFullAddressSpaceSecurityNoneAsync(bool operationLimits)
        {
            return m_clientTest.BrowseFullAddressSpaceAsync(SecurityPolicies.None, operationLimits);
        }
    }
}

#endif // NET8_0_OR_GREATER
