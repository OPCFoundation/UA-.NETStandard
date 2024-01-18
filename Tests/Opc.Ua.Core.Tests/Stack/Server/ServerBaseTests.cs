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
        public const int BaseAddressCount = 6;
        public const int EndpointCount = 12;
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

            // base addresses, uses localhost. specify multiple endpoints per protocol.
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.https://localhost:62540/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.tcp://localhost:62541/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("https://localhost:62542/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.https://localhost:52640/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.tcp://localhost:52641/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("https://localhost:52642/UA/SampleServer"));
            Assert.AreEqual(BaseAddressCount, configuration.ServerConfiguration.BaseAddresses.Count);

            // alternate base addresses, FQDN and IP address
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://myhostname.com:62540/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://myhostname.com:62541/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("https://myhostname.com:62542/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://192.168.1.100:62540/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://192.168.1.100:62541/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("https://192.168.1.100:62542/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62540/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62541/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("https://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62542/UA/SampleServer");

            // port forwarded to external address, different port and hostname
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://externalhostname.com:50000/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://externalhostname.com:50001/UA/SampleServer");
            configuration.ServerConfiguration.AlternateBaseAddresses.Add("https://externalhostname.com:50002/UA/SampleServer");

            InitializeBaseAddresses(configuration);

            m_configuration = configuration;

            // ensure at least one security policy exists.
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy() {
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
            });

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
                    transportProfileUri = Profiles.HttpsBinaryTransport;
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

            Assert.AreEqual(Utils.NormalizedIPAddress(clientUri1.IdnHost), Utils.NormalizedIPAddress(clientUri2.IdnHost));
        }

        /// <summary>
        /// For any filter applied, ensure there is at least one endpoint returned.
        /// </summary>
        [Test]
        [TestCase("urn:someserver:62541:UA:SampleServer")]
        [TestCase("tcp://localhost:62541/UA/SampleServer")]
        [TestCase("opc.tcp://[ffe8:1234::8]:51210/UA/SampleServer", 2)]
        [TestCase("opc.tcp://[ffe8:1234::8%3]:51210/UA/SampleServer", 2)]
        [TestCase("opc.tcp://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62541/UA/SampleServer")]
        [TestCase("opc.tcp://myhostname.com:51210/UA/SampleServer")]
        [TestCase("opc.tcp://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer", 2)]
        [TestCase("opc.tcp://EXTERNALHOSTNAME.COM:51210/UA/SampleServer")]
        [TestCase("opc.tcp://localhost:51210/UA/SampleServer")]
        [TestCase("opc.tcp://someserver:62541/UA/SampleServer", 2)]
        [TestCase("opc.tcp://192.168.1.100:62541/UA/SampleServer")]
        [TestCase("opc.https://someserver:62541/UA/SampleServer", 2)]
        [TestCase("opc.https://someserver:62540/UA/SampleServer", 2)]
        [TestCase("opc.https://localhost:51210/UA/SampleServer")]
        [TestCase("opc.https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer", 2)]
        [TestCase("opc.tcp:someserver:62541:UA:SampleServer", 2)]
        [TestCase("https://someserver:62541/UA/SampleServer", 2)]
        [TestCase("https://localhost:51210/UA/SampleServer")]
        [TestCase("https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer", 2)]
        public void FilterByClientUrlTest(string endpointUrl, int baseAddressCount = BaseAddressCount)
        {
            TestContext.WriteLine("Endpoint Url: {0}", endpointUrl);
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            var filteredBaseAddresses = this.FilterByEndpointUrl(parsedEndpointUrl, BaseAddresses);
            Assert.NotNull(filteredBaseAddresses);
            Assert.Greater(filteredBaseAddresses.Count, 0);
            TestContext.WriteLine($"Filtered endpoints: {filteredBaseAddresses.Count}");
            foreach (var baseaddress in filteredBaseAddresses)
            {
                TestContext.WriteLine($"Endpoint: {baseaddress.Url}");
            }
            Assert.AreEqual(baseAddressCount, filteredBaseAddresses.Count);
        }

        /// <summary>
        /// Ensure .
        /// </summary>
        [Test]
        [TestCase("opc.tcp://localhost:51210/UA/SampleServer")]
        [TestCase("opc.tcp://externalhostname.com:50001/UA/SampleServer", 6)]
        [TestCase("opc.tcp://externalhostname.com:52541/UA/SampleServer", 6)]
        [TestCase("opc.tcp://[ffe8:1234::8]:51210/UA/SampleServer", 4)]
        [TestCase("opc.tcp://[ffe8:1234::8%3]:51210/UA/SampleServer", 4)]
        [TestCase("opc.tcp://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62541/UA/SampleServer", 6)]
        [TestCase("opc.tcp://myhostname.com:51210/UA/SampleServer", 6)]
        [TestCase("opc.tcp://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer", 4)]
        [TestCase("opc.tcp://EXTERNALHOSTNAME.COM:51210/UA/SampleServer", 6)]
        [TestCase("opc.tcp://localhost:51210/UA/SampleServer")]
        [TestCase("opc.tcp://someserver:62541/UA/SampleServer", 4)]
        [TestCase("opc.tcp://192.168.1.100:62541/UA/SampleServer", 6)]
        [TestCase("opc.tcp:someserver:62541:UA:SampleServer", 4)]
        [TestCase("opc.https://externalhostname.com:50000/UA/SampleServer", 6)]
        [TestCase("opc.https://localhost:51210/UA/SampleServer")]
        [TestCase("opc.https://someserver:62541/UA/SampleServer", 4)]
        [TestCase("opc.https://someserver:62540/UA/SampleServer", 4)]
        [TestCase("opc.https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer", 4)]
        [TestCase("urn:someserver:62541:UA:SampleServer")]
        [TestCase("tcp://localhost:62541/UA/SampleServer")]
        [TestCase("https://someserver:62541/UA/SampleServer", 4)]
        [TestCase("https://localhost:51210/UA/SampleServer")]
        [TestCase("https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer", 4)]
        public void TranslateEndpointDescriptionsTest(string endpointUrl, int count = EndpointCount)
        {
            var baseAddresses = BaseAddresses;
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            if (parsedEndpointUrl != null)
            {
                baseAddresses = this.FilterByEndpointUrl(parsedEndpointUrl, BaseAddresses);
            }
            Assert.Greater(BaseAddressCount, 0);
            var translatedEndpoints = this.TranslateEndpointDescriptions(parsedEndpointUrl, baseAddresses, m_endpoints, m_serverDescription);
            Assert.NotNull(translatedEndpoints);
            Assert.Greater(translatedEndpoints.Count, 0);
            foreach (var endpoint in translatedEndpoints)
            {
                TestContext.WriteLine($"Endpoint: {endpoint.EndpointUrl} {endpoint.SecurityMode} {endpoint.SecurityPolicyUri}");
            }
            Assert.AreEqual(count, translatedEndpoints.Count);
        }
        #endregion
    }
}
