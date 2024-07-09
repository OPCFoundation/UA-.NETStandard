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
using System.Linq;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Tests for the protected methods in the ServerBase class.
    /// </summary>
    [TestFixture, Category("Server")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    [TestFixtureSource(nameof(FixtureArgs))]
    public class ServerBaseTests : ServerBase
    {
        /// <summary>
        /// Various server base/alternate address configurations.
        /// </summary>
        public enum TestConfigurations : int
        {
            SingleBaseAdresses,
            SingleBaseAdressesWithAlternateHost,
            SingleBaseAdressesWithAlternatePort,
            SingleBaseAddressesWithAlternateHostAndPort,
            DualBaseAddresses,
            DualBaseAddressesWithAlternateHost,
            DualBaseAdressesWithAlternatePort,
            DualBaseAddressesWithAlternateHostAndPort,
        };

        public const int BaseAddressCount = 6;
        public const int EndpointCount = 12;
        ApplicationConfiguration m_configuration;
        ApplicationDescription m_serverDescription;
        EndpointDescriptionCollection m_endpoints;
        TestConfigurations m_testConfiguration;

        public static readonly object[] FixtureArgs = new object[] {
            new object[] { TestConfigurations.SingleBaseAdresses },
            new object[] { TestConfigurations.SingleBaseAdressesWithAlternateHost },
            new object[] { TestConfigurations.SingleBaseAdressesWithAlternatePort },
            new object[] { TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort },
            new object[] { TestConfigurations.DualBaseAddresses },
            new object[] { TestConfigurations.DualBaseAddressesWithAlternateHost },
            new object[] { TestConfigurations.DualBaseAdressesWithAlternatePort },
            new object[] { TestConfigurations.DualBaseAddressesWithAlternateHostAndPort },
        };

        public ServerBaseTests()
        {
            m_testConfiguration = TestConfigurations.SingleBaseAdresses;
        }

        public ServerBaseTests(TestConfigurations configType)
        {
            m_testConfiguration = configType;
        }

        #region DataPointSource
        /// <summary>
        /// An array of Client Urls.
        /// </summary>
        [DatapointSource]
        public static readonly string[] ClientUrls = new string[] {
            "opc.tcp://localhost:51210/UA/SampleServer",
            "opc.tcp://externalhostname.com:50001/UA/SampleServer",
            "opc.tcp://externalhostname.com:52541/UA/SampleServer",
            //"opc.tcp://[fe80:1234::8]:51210/UA/SampleServer",
            //"opc.tcp://[fe80:1234::8%3]:51210/UA/SampleServer",
            "opc.tcp://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62541/UA/SampleServer",
            "opc.tcp://myhostname.com:51210/UA/SampleServer",
            "opc.tcp://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer",
            "opc.tcp://EXTERNALHOSTNAME.COM:51210/UA/SampleServer",
            "opc.tcp://localhost:51210/UA/SampleServer",
            "opc.tcp://someserver:62541/UA/SampleServer",
            "opc.tcp://192.168.1.100:62541/UA/SampleServer",
            "opc.tcp:someserver:62541:UA:SampleServer",
            "opc.https://externalhostname.com:50000/UA/SampleServer",
            "opc.https://localhost:51210/UA/SampleServer",
            "opc.https://someserver:62541/UA/SampleServer",
            "opc.https://someserver:62540/UA/SampleServer",
            "opc.https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer",
            "urn:someserver:62541:UA:SampleServer",
            "tcp://localhost:62541/UA/SampleServer",
            "https://someserver:62541/UA/SampleServer",
            "https://localhost:51210/UA/SampleServer",
            "https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer",
        };
        #endregion

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

            // base addresses, uses localhost. specify multiple endpoints per protocol as per config.
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.https://localhost:62540/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.tcp://localhost:62541/UA/SampleServer"));
            configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("https://localhost:62542/UA/SampleServer"));
            if (m_testConfiguration == TestConfigurations.DualBaseAddresses ||
                m_testConfiguration == TestConfigurations.DualBaseAddressesWithAlternateHost ||
                m_testConfiguration == TestConfigurations.DualBaseAddressesWithAlternateHostAndPort ||
                m_testConfiguration == TestConfigurations.DualBaseAdressesWithAlternatePort)
            {
                configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.https://localhost:52640/UA/SampleServer"));
                configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("opc.tcp://localhost:52641/UA/SampleServer"));
                configuration.ServerConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost("https://localhost:52642/UA/SampleServer"));
                Assert.AreEqual(BaseAddressCount, configuration.ServerConfiguration.BaseAddresses.Count);
            }
            else
            {
                Assert.AreEqual(BaseAddressCount / 2, configuration.ServerConfiguration.BaseAddresses.Count);
            }

            if (m_testConfiguration == TestConfigurations.SingleBaseAdressesWithAlternateHost ||
                m_testConfiguration == TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort ||
                m_testConfiguration == TestConfigurations.DualBaseAddressesWithAlternateHost ||
                m_testConfiguration == TestConfigurations.DualBaseAddressesWithAlternateHostAndPort)
            {
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
            }

            if (m_testConfiguration == TestConfigurations.SingleBaseAdressesWithAlternatePort ||
                m_testConfiguration == TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort ||
                m_testConfiguration == TestConfigurations.DualBaseAdressesWithAlternatePort ||
                m_testConfiguration == TestConfigurations.DualBaseAddressesWithAlternateHostAndPort)
            {
                // port forwarded to external address, different port and hostname
                configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.https://externalhostname.com:50000/UA/SampleServer");
                configuration.ServerConfiguration.AlternateBaseAddresses.Add("opc.tcp://externalhostname.com:50001/UA/SampleServer");
                configuration.ServerConfiguration.AlternateBaseAddresses.Add("https://externalhostname.com:50002/UA/SampleServer");
            }

            InitializeBaseAddresses(configuration);

            m_configuration = configuration;

            // add security policies.
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy());
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy() {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy() {
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
            });
            configuration.ServerConfiguration.SecurityPolicies.Add(new ServerSecurityPolicy() {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
            });


            // ensure at least one user token policy exists.
            UserTokenPolicy userTokenPolicyAnonymous = new UserTokenPolicy {
                TokenType = UserTokenType.Anonymous,
            };
            configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicyAnonymous);
            UserTokenPolicy userTokenPolicyUserName = new UserTokenPolicy {
                TokenType = UserTokenType.UserName,
            };
            configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicyUserName);
            UserTokenPolicy userTokenPolicyCertificate = new UserTokenPolicy {
                TokenType = UserTokenType.Certificate,
            };
            configuration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicyCertificate);

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
            foreach (var baseAddress in configuration.ServerConfiguration.BaseAddresses)
            {
                bool isUaTcp = false;
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
                else
                {
                    isUaTcp = true;
                }

                foreach (var securityConfiguration in configuration.ServerConfiguration.SecurityPolicies)
                {
                    // Only one secure endpoint for non opc.tcp protocols
                    if (!isUaTcp && securityConfiguration.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                    {
                        continue;
                    }

                    EndpointDescription endpoint = new EndpointDescription {
                        EndpointUrl = baseAddress,
                        Server = m_serverDescription,
                        TransportProfileUri = transportProfileUri,
                        SecurityMode = securityConfiguration.SecurityMode,
                        SecurityPolicyUri = securityConfiguration.SecurityPolicyUri,
                        SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(securityConfiguration.SecurityMode, securityConfiguration.SecurityPolicyUri),
                    };
                    endpoint.UserIdentityTokens = GetUserTokenPolicies(configuration, endpoint);
                    m_endpoints.Add(endpoint);

                    if (!isUaTcp)
                    {
                        break;
                    }
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
        [Theory]
        public void FilterByClientUrlTest(
            [ValueSource(nameof(ClientUrls))] string endpointUrl,
            bool noUrlExtension
            )
        {
            TestContext.WriteLine("Endpoint Url: {0}", endpointUrl);
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            if (noUrlExtension)
            {
                try
                {
                    parsedEndpointUrl = new Uri(parsedEndpointUrl.GetLeftPart(UriPartial.Authority));
                    TestContext.WriteLine($"Using left part of Url: {parsedEndpointUrl}");
                }
                catch (UriFormatException e)
                {
                    TestContext.WriteLine($"Exception: {e.Message}");
                    Assert.Ignore("Invalid Left Part of URL");
                }
            }
            var filteredBaseAddresses = this.FilterByEndpointUrl(parsedEndpointUrl, BaseAddresses);
            Assert.NotNull(filteredBaseAddresses);
            Assert.Greater(filteredBaseAddresses.Count, 0);
            TestContext.WriteLine($"Filtered endpoints: {filteredBaseAddresses.Count}");
            foreach (var baseaddress in filteredBaseAddresses)
            {
                TestContext.WriteLine($"Endpoint: {baseaddress.Url}");
            }
            Assert.Greater(filteredBaseAddresses.Count, 0);
            for (int i = 0; i < filteredBaseAddresses.Count; i++)
            {
                for (int v = i + 1; v < filteredBaseAddresses.Count; v++)
                {
                    Assert.AreNotEqual(filteredBaseAddresses[i].Url, filteredBaseAddresses[v].Url);
                }
            }
        }

        /// <summary>
        /// For any filter and translation applied, ensure there is at least one endpoint returned.
        /// </summary>
        [Theory]
        public void TranslateEndpointDescriptionsTest(
            [ValueSource(nameof(ClientUrls))] string endpointUrl,
            bool noUrlExtension
            )
        {
            var baseAddresses = BaseAddresses;
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            if (noUrlExtension)
            {
                try
                {
                    parsedEndpointUrl = new Uri(parsedEndpointUrl.GetLeftPart(UriPartial.Authority));
                }
                catch (UriFormatException e)
                {
                    TestContext.WriteLine($"Exception: {e.Message}");
                    Assert.Ignore("Invalid Left Part of URL");
                }
                parsedEndpointUrl = new Uri(parsedEndpointUrl.GetLeftPart(UriPartial.Authority));
            }
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

            // validate results do not contain duplicates.
            foreach (var endpoint in translatedEndpoints)
            {
                var matches = translatedEndpoints.Where(e => e.EndpointUrl == endpoint.EndpointUrl).ToList();
                Assert.NotNull(matches);
                Assert.GreaterOrEqual(matches.Count, 1);
            }

            // validate results have matching UserTokenPolicies in baseaddresses
            foreach (var translatedEndpoint in translatedEndpoints)
            {
                var matches = m_endpoints.Where(endpoint => Utils.IsEqual(endpoint.UserIdentityTokens, translatedEndpoint.UserIdentityTokens)).ToList();
                Assert.NotNull(matches);
                Assert.AreEqual(matches.Count, 1);
                var firstMatch = matches.First();
                Assert.AreEqual(firstMatch.UserIdentityTokens.Count, translatedEndpoint.UserIdentityTokens.Count);
                for (int i = 0; i < firstMatch.UserIdentityTokens.Count; i++)
                {
                    Assert.AreEqual(firstMatch.UserIdentityTokens[i].TokenType, translatedEndpoint.UserIdentityTokens[i].TokenType);
                    Assert.AreEqual(firstMatch.UserIdentityTokens[i].PolicyId, translatedEndpoint.UserIdentityTokens[i].PolicyId);
                }
                Assert.AreEqual(firstMatch.BinaryEncodingId, translatedEndpoint.BinaryEncodingId);
                Assert.AreEqual(firstMatch.SecurityMode, translatedEndpoint.SecurityMode);
                Assert.AreEqual(firstMatch.SecurityPolicyUri, translatedEndpoint.SecurityPolicyUri);
                var firstMatchEndpointUrl = new Uri(firstMatch.EndpointUrl);
                var translatedEndpointUrl = new Uri(translatedEndpoint.EndpointUrl);
                if (m_testConfiguration != TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort &&
                    m_testConfiguration != TestConfigurations.SingleBaseAdressesWithAlternatePort &&
                    m_testConfiguration != TestConfigurations.DualBaseAddressesWithAlternateHostAndPort &&
                    m_testConfiguration != TestConfigurations.DualBaseAdressesWithAlternatePort)
                {
                    Assert.AreEqual(firstMatchEndpointUrl.Port, translatedEndpointUrl.Port);
                    Assert.AreEqual(firstMatchEndpointUrl.LocalPath, translatedEndpointUrl.LocalPath);
                }
                else if (firstMatchEndpointUrl.Port != translatedEndpointUrl.Port)
                {
                    // ensure port is translated and mapped to the first base address with the same scheme.
                    Assert.AreEqual(firstMatchEndpointUrl.Port % 10, translatedEndpointUrl.Port % 10);

                    var theSchemes = m_endpoints.Where(endpoint => endpoint.EndpointUrl.StartsWith(translatedEndpointUrl.Scheme, StringComparison.Ordinal)).ToList();
                    Assert.AreEqual(theSchemes.First().EndpointUrl, firstMatch.EndpointUrl);
                }
                Assert.AreEqual(firstMatchEndpointUrl.Scheme, translatedEndpointUrl.Scheme);

                // validate results have matching scheme with a port index.
                if (translatedEndpointUrl.Scheme == Utils.UriSchemeHttps)
                {
                    Assert.IsTrue(translatedEndpointUrl.Port % 10 == 2);
                }
                if (translatedEndpointUrl.Scheme == Utils.UriSchemeOpcHttps)
                {
                    Assert.IsTrue(translatedEndpointUrl.Port % 10 == 0);
                }
                else if (translatedEndpointUrl.Scheme == Utils.UriSchemeOpcTcp)
                {
                    Assert.IsTrue(translatedEndpointUrl.Port % 10 == 1);
                }
            }
            #endregion
        }
    }
}
