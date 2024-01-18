/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Tests for the protected methods in the ServerBase class.
    /// </summary>
    [TestFixture, Category("Server")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class ServerBaseTests : ServerBase
    {
        ApplicationConfiguration m_configuration;
        ApplicationDescription m_serverDescription;
        EndpointDescriptionCollection m_endpoints;

        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            ApplicationConfiguration configuration = new ApplicationConfiguration {
                ApplicationName = "Test",
                ApplicationType = ApplicationType.Server,
                ApplicationUri = "urn:localhost:UA:Test",
                ProductUri = "http://opcfoundation.org/UA/TestServer",
                SecurityConfiguration = new SecurityConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };

            // base addresses, uses localhost
            configuration.ServerConfiguration.BaseAddresses.Add("opc.https://localhost:62540/UA/SampleServer");
            configuration.ServerConfiguration.BaseAddresses.Add("opc.tcp://localhost:62541/UA/SampleServer");
            configuration.ServerConfiguration.BaseAddresses.Add("https://localhost:62542/UA/SampleServer");

            // alternate base addresses, FQDN and IP address
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://myhostname.com:62540/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://myhostname.com:62541/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://192.168.1.100:62540/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://192.168.1.100:62541/UA/SampleServer");

            // port forwarded to external address, different port and hostname
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://externalhostname.com:50000/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://externalhostname.com:50001/UA/SampleServer");

            InitializeBaseAddresses(configuration);

            m_configuration = configuration;

            // ensure at least one security policy exists.
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());

            // ensure at least one user token policy exists.
            UserTokenPolicy userTokenPolicy = new UserTokenPolicy {
                TokenType = UserTokenType.Anonymous,
                PolicyId = nameof(UserTokenType.Anonymous)
            };
            configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);

            // set server description.
            m_serverDescription = new ApplicationDescription {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationName = new LocalizedText("en-US", configuration.ApplicationName),
                ApplicationType = configuration.ApplicationType,
                ProductUri = configuration.ProductUri,
                DiscoveryUrls = GetDiscoveryUrls()
            };

            m_endpoints = new EndpointDescriptionCollection();

            // add endpoints.
            byte securityLevel = 100;
            foreach (var baseAddress in configuration.ServerConfiguration.BaseAddresses)
            {
                var transportProfileUri = Profiles.UaTcpTransport;
                if (baseAddress.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
                {
                    transportProfileUri = Profiles.UaTcpTransport;
                }
                else if (baseAddress.StartsWith(Utils.UriSchemeOpcHttps, StringComparison.Ordinal))
                {
                    transportProfileUri = Profiles.HttpsBinaryTransport;
                }
                else if (baseAddress.StartsWith(Utils.UriSchemeOpcWss, StringComparison.Ordinal))
                {
                    transportProfileUri = Profiles.UaWssTransport;
                }

                foreach (var securityConfiguration in configuration.ServerConfiguration.SecurityPolicies)
                {
                    EndpointDescription endpoint = new EndpointDescription {
                        EndpointUrl = baseAddress,
                        Server = m_serverDescription,
                        TransportProfileUri = transportProfileUri,
                        SecurityLevel = securityLevel++,
                        SecurityMode = securityConfiguration.SecurityMode,
                        SecurityPolicyUri = securityConfiguration.SecurityPolicyUri
                    };
                    m_endpoints.Add(endpoint);
                }
            }
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {

        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Ensure .
        /// </summary>
        [Test]
        [TestCase("opc.tcp://localhost:51210/UA/SampleServer", "opc.tcp://LOCALHOST:51210/UA/SampleServer")]
        public void NormalizeEndpoints(string clientUrl1, string clientUrl2)
        {
            Uri clientUri1 = new Uri(clientUrl1);
            Uri clientUri2 = new Uri(clientUrl2);

            Assert.AreEqual(clientUri1, clientUri2);
            Assert.AreEqual(clientUri1.DnsSafeHost, clientUri2.DnsSafeHost);
            Assert.AreEqual(clientUri1.IdnHost, clientUri2.IdnHost);

            Utils.NormalizedIPAddress(clientUrl1);
        }

        /// <summary>
        /// For any filter applied, ensure there is at least one endpoint returned.
        /// </summary>
        [Test]
        [TestCase("opc.tcp://localhost:51210/UA/SampleServer")]
        [TestCase("https://localhost:51210/UA/SampleServer")]
        [TestCase("opc.tcp://someserver:62541/UA/SampleServer")]
        [TestCase("urn:someserver:62541:UA:SampleServer")]
        [TestCase("tcp://localhost:62541/UA/SampleServer")]
        [TestCase("opc.tcp:someserver:62541:UA:SampleServer")]
        public void FilterByClientUrlTest(string endpointUrl)
        {
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            var filteredEndpoints = this.FilterByEndpointUrl(parsedEndpointUrl, BaseAddresses);
            Assert.NotNull(filteredEndpoints);
            Assert.Greater(filteredEndpoints.Count, 0);
        }

        /// <summary>
        /// Ensure .
        /// </summary>
        [Test]
        [TestCase("opc.tcp://localhost:51210/UA/SampleServer")]
        public void TranslateEndpointDescriptionsTest(string clientUrl)
        {
            Uri clientUri = new Uri(clientUrl);
            var translatedEndpoints = this.TranslateEndpointDescriptions(clientUri, BaseAddresses, m_endpoints, m_serverDescription);
            Assert.NotNull(translatedEndpoints);
        }
        #endregion
    }
}
