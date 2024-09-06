/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;
using NUnit.Framework;


namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests which require security None and are otherwise skipped,
    /// starts the server with additional security None profile.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]

    public class ClientTestNoSecurity
    {
        private ClientTest _clientTest { get; set; }

        public static readonly object[] FixtureArgs = {
            new object [] { Utils.UriSchemeOpcTcp},
            // https protocol security None is not supported
            // new object [] { Utils.UriSchemeHttps},
            // new object [] { Utils.UriSchemeOpcHttps},
        };

        public ClientTestNoSecurity()
        {
            _clientTest = new ClientTest(Utils.UriSchemeOpcTcp);
        }

        public ClientTestNoSecurity(string uriScheme)
        {
            _clientTest = new ClientTest(uriScheme);
        }

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public Task OneTimeSetUp()
        {
            _clientTest.SupportsExternalServerUrl = true;
            return _clientTest.OneTimeSetUpAsync(null, true);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public Task OneTimeTearDownAsync()
        {
            return _clientTest.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public Task SetUp()
        {
            return _clientTest.SetUp();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public Task TearDown()
        {
            return _clientTest.TearDown();
        }
        #endregion

        [Test, Order(230)]
        public Task ReconnectJWTSecurityNone()
        {
            return _clientTest.ReconnectJWT(SecurityPolicies.None);
        }

        [Test, Order(220)]
        public Task ConnectJWT()
        {
            return _clientTest.ConnectJWT(SecurityPolicies.None);
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets
        /// </summary>
        [Test, Order(260)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public Task ReconnectSessionOnAlternateChannelWithSavedSessionSecretsSecurityNone(bool anonymous, bool asyncReconnect)
        {
            return _clientTest.ReconnectSessionOnAlternateChannelWithSavedSessionSecrets(SecurityPolicies.None, anonymous, asyncReconnect);
        }

        [Theory, Order(400)]
        public Task BrowseFullAddressSpaceSecurityNone(bool operationLimits)
        {
            return _clientTest.BrowseFullAddressSpace(SecurityPolicies.None, operationLimits);
        }

        [Test, Order(201)]
        public Task ConnectAndCloseAsyncNoSecurity()
        {
            return _clientTest.ConnectAndCloseAsync(SecurityPolicies.None);
        }
    }
}
