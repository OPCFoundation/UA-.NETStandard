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

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Tests for the protected methods in the ServerBase class.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [TestFixtureSource(nameof(FixtureArgs))]
    public class ServerBaseTests : ServerBase
    {
        /// <summary>
        /// Various server base/alternate address configurations.
        /// </summary>
        public enum TestConfigurations
        {
            SingleBaseAdresses,
            SingleBaseAdressesWithAlternateHost,
            SingleBaseAdressesWithAlternatePort,
            SingleBaseAddressesWithAlternateHostAndPort,
            DualBaseAddresses,
            DualBaseAddressesWithAlternateHost,
            DualBaseAdressesWithAlternatePort,
            DualBaseAddressesWithAlternateHostAndPort,
            MultipleIPsSamePort
        }

        public const int BaseAddressCount = 6;
        public const int EndpointCount = 12;

        private ApplicationDescription m_serverDescription;
        private ArrayOf<EndpointDescription> m_endpoints;
        private readonly TestConfigurations m_testConfiguration;

        public static readonly object[] FixtureArgs =
        [
            new object[] { TestConfigurations.SingleBaseAdresses },
            new object[] { TestConfigurations.SingleBaseAdressesWithAlternateHost },
            new object[] { TestConfigurations.SingleBaseAdressesWithAlternatePort },
            new object[] { TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort },
            new object[] { TestConfigurations.DualBaseAddresses },
            new object[] { TestConfigurations.DualBaseAddressesWithAlternateHost },
            new object[] { TestConfigurations.DualBaseAdressesWithAlternatePort },
            new object[] { TestConfigurations.DualBaseAddressesWithAlternateHostAndPort },
            new object[] { TestConfigurations.MultipleIPsSamePort }
        ];

        public ServerBaseTests()
            : this(TestConfigurations.SingleBaseAdresses)
        {
        }

        public ServerBaseTests(TestConfigurations configType)
            : base(NUnitTelemetryContext.Create(true))
        {
            m_testConfiguration = configType;
        }

        /// <summary>
        /// An array of Client Urls.
        /// </summary>
        [DatapointSource]
        public static readonly string[] ClientUrls =
        [
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
            "https://UNKNOWNHOSTNAME.COM:51210/UA/SampleServer"
        ];

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Test",
                ApplicationType = ApplicationType.Server,
                ApplicationUri = "urn:localhost:UA:Test",
                ProductUri = "http://opcfoundation.org/UA/TestServer",
                SecurityConfiguration = new SecurityConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };

            // base addresses, uses localhost. specify multiple endpoints per protocol as per config.
            configuration.ServerConfiguration.BaseAddresses =
                configuration.ServerConfiguration.BaseAddresses +=
                Utils.ReplaceLocalhost("opc.https://localhost:62540/UA/SampleServer");
            configuration.ServerConfiguration.BaseAddresses =
                configuration.ServerConfiguration.BaseAddresses +=
                Utils.ReplaceLocalhost("opc.tcp://localhost:62541/UA/SampleServer");
            configuration.ServerConfiguration.BaseAddresses =
                configuration.ServerConfiguration.BaseAddresses +=
                Utils.ReplaceLocalhost("https://localhost:62542/UA/SampleServer");
            if (m_testConfiguration
                is TestConfigurations.DualBaseAddresses
                    or TestConfigurations.DualBaseAddressesWithAlternateHost
                    or TestConfigurations.DualBaseAddressesWithAlternateHostAndPort
                    or TestConfigurations.DualBaseAdressesWithAlternatePort)
            {
                configuration.ServerConfiguration.BaseAddresses +=
                    Utils.ReplaceLocalhost("opc.https://localhost:52640/UA/SampleServer");
                configuration.ServerConfiguration.BaseAddresses +=
                    Utils.ReplaceLocalhost("opc.tcp://localhost:52641/UA/SampleServer");
                configuration.ServerConfiguration.BaseAddresses +=
                    Utils.ReplaceLocalhost("https://localhost:52642/UA/SampleServer");
                Assert.That(
                    configuration.ServerConfiguration.BaseAddresses.Count,
                    Is.EqualTo(BaseAddressCount));
            }
            else if (m_testConfiguration == TestConfigurations.MultipleIPsSamePort)
            {
                // Multiple IP addresses configured on the same ports as the primary base addresses.
                // This tests the scenario where a server listens on multiple network interfaces
                // using the same port number (e.g. 192.168.1.100:62541 and localhost:62541).
                configuration.ServerConfiguration.BaseAddresses +=
                    "opc.https://192.168.1.100:62540/UA/SampleServer";
                configuration.ServerConfiguration.BaseAddresses +=
                    "opc.tcp://192.168.1.100:62541/UA/SampleServer";
                configuration.ServerConfiguration.BaseAddresses +=
                    "https://192.168.1.100:62542/UA/SampleServer";
                Assert.That(
                    configuration.ServerConfiguration.BaseAddresses.Count,
                    Is.EqualTo(BaseAddressCount));
            }
            else
            {
                Assert.That(
                    configuration.ServerConfiguration.BaseAddresses.Count,
                    Is.EqualTo(BaseAddressCount / 2));
            }

            if (m_testConfiguration
                is TestConfigurations.SingleBaseAdressesWithAlternateHost
                    or TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort
                    or TestConfigurations.DualBaseAddressesWithAlternateHost
                    or TestConfigurations.DualBaseAddressesWithAlternateHostAndPort)
            {
                // alternate base addresses, FQDN and IP address
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.https://myhostname.com:62540/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.tcp://myhostname.com:62541/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "https://myhostname.com:62542/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.https://192.168.1.100:62540/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.tcp://192.168.1.100:62541/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "https://192.168.1.100:62542/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.https://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62540/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.tcp://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62541/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "https://[2003:d9:1f0c:5e00:4139:ee31:6cc3:313e]:62542/UA/SampleServer";
            }

            if (m_testConfiguration
                is TestConfigurations.SingleBaseAdressesWithAlternatePort
                    or TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort
                    or TestConfigurations.DualBaseAdressesWithAlternatePort
                    or TestConfigurations.DualBaseAddressesWithAlternateHostAndPort)
            {
                // port forwarded to external address, different port and hostname
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.https://externalhostname.com:50000/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "opc.tcp://externalhostname.com:50001/UA/SampleServer";
                configuration.ServerConfiguration.AlternateBaseAddresses +=
                    "https://externalhostname.com:50002/UA/SampleServer";
            }

            InitializeBaseAddresses(configuration);

            // add security policies.
            configuration.ServerConfiguration.SecurityPolicies =
            [
                new ServerSecurityPolicy(),
                new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.Sign,
                    SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
                },
                new ServerSecurityPolicy
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
                }
            ];

            // ensure at least one user token policy exists.
            var userTokenPolicyAnonymous = new UserTokenPolicy
            {
                TokenType = UserTokenType.Anonymous
            };
            configuration.ServerConfiguration.UserTokenPolicies += userTokenPolicyAnonymous;
            var userTokenPolicyUserName = new UserTokenPolicy
            {
                TokenType = UserTokenType.UserName
            };
            configuration.ServerConfiguration.UserTokenPolicies += userTokenPolicyUserName;
            var userTokenPolicyCertificate = new UserTokenPolicy
            {
                TokenType = UserTokenType.Certificate
            };
            configuration.ServerConfiguration.UserTokenPolicies += userTokenPolicyCertificate;

            // set server description.
            m_serverDescription = new ApplicationDescription
            {
                ApplicationUri = configuration.ApplicationUri,
                ApplicationName = new LocalizedText("en-US", configuration.ApplicationName),
                ApplicationType = configuration.ApplicationType,
                ProductUri = configuration.ProductUri,
                DiscoveryUrls = GetDiscoveryUrls()
            };

            var endpoints = new List<EndpointDescription>();

            // add endpoints.
            foreach (string baseAddress in configuration.ServerConfiguration.BaseAddresses)
            {
                bool isUaTcp = false;
                string transportProfileUri = Profiles.UaTcpTransport;
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

                foreach (ServerSecurityPolicy securityConfiguration in configuration.ServerConfiguration.SecurityPolicies)
                {
                    // Only one secure endpoint for non opc.tcp protocols
                    if (!isUaTcp &&
                        securityConfiguration.SecurityMode != MessageSecurityMode.SignAndEncrypt)
                    {
                        continue;
                    }

                    var endpoint = new EndpointDescription
                    {
                        EndpointUrl = baseAddress,
                        Server = m_serverDescription,
                        TransportProfileUri = transportProfileUri,
                        SecurityMode = securityConfiguration.SecurityMode,
                        SecurityPolicyUri = securityConfiguration.SecurityPolicyUri,
                        SecurityLevel = ServerSecurityPolicy.CalculateSecurityLevel(
                            securityConfiguration.SecurityMode,
                            securityConfiguration.SecurityPolicyUri,
                            LoggerUtils.Null.Logger)
                    };
                    endpoint.UserIdentityTokens = GetUserTokenPolicies(configuration, endpoint);
                    endpoints.Add(endpoint);

                    if (!isUaTcp)
                    {
                        break;
                    }
                }
            }
            m_endpoints = endpoints.ToArrayOf();
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

        /// <summary>
        /// Ensure .
        /// </summary>
        [Test]
        [TestCase(
            "opc.tcp://localhost:51210/UA/SampleServer",
            "opc.tcp://LOCALHOST:51210/UA/SampleServer")]
        public void NormalizeEndpoints(string clientUrl1, string clientUrl2)
        {
            var clientUri1 = new Uri(clientUrl1);
            var clientUri2 = new Uri(clientUrl2);

            Assert.That(clientUri2, Is.EqualTo(clientUri1));
            Assert.That(clientUri2.IdnHost, Is.EqualTo(clientUri1.IdnHost));
            Assert.That(clientUri2.IdnHost, Is.EqualTo(clientUri1.IdnHost));

            Assert.That(
                Utils.NormalizedIPAddress(clientUri2.IdnHost),
                Is.EqualTo(Utils.NormalizedIPAddress(clientUri1.IdnHost)));
        }

        /// <summary>
        /// For any filter applied, ensure there is at least one endpoint returned.
        /// </summary>
        [Theory]
        public void FilterByClientUrlTest(
            [ValueSource(nameof(ClientUrls))] string endpointUrl,
            bool noUrlExtension)
        {
            TestContext.Out.WriteLine("Endpoint Url: {0}", endpointUrl);
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            if (noUrlExtension)
            {
                try
                {
                    parsedEndpointUrl = new Uri(
                        parsedEndpointUrl.GetLeftPart(UriPartial.Authority));
                    TestContext.Out.WriteLine($"Using left part of Url: {parsedEndpointUrl}");
                }
                catch (UriFormatException e)
                {
                    TestContext.Out.WriteLine($"Exception: {e.Message}");
                    Assert.Ignore("Invalid Left Part of URL");
                }
            }
            IList<BaseAddress> filteredBaseAddresses
                = FilterByEndpointUrl(
                parsedEndpointUrl,
                BaseAddresses);
            Assert.That(filteredBaseAddresses, Is.Not.Null);
            Assert.That(filteredBaseAddresses, Is.Not.Empty);
            TestContext.Out.WriteLine($"Filtered endpoints: {filteredBaseAddresses.Count}");
            foreach (BaseAddress baseaddress in filteredBaseAddresses)
            {
                TestContext.Out.WriteLine($"Endpoint: {baseaddress.Url}");
            }
            Assert.That(filteredBaseAddresses, Is.Not.Empty);
            for (int i = 0; i < filteredBaseAddresses.Count; i++)
            {
                for (int v = i + 1; v < filteredBaseAddresses.Count; v++)
                {
                    Assert.That(filteredBaseAddresses[v].Url, Is.Not.EqualTo(filteredBaseAddresses[i].Url));
                }
            }
        }

        /// <summary>
        /// For any filter and translation applied, ensure there is at least one endpoint returned.
        /// </summary>
        [Theory]
        public void TranslateEndpointDescriptionsTest(
            [ValueSource(nameof(ClientUrls))] string endpointUrl,
            bool noUrlExtension)
        {
            IList<BaseAddress> baseAddresses = BaseAddresses;
            Uri parsedEndpointUrl = Utils.ParseUri(endpointUrl);
            if (noUrlExtension)
            {
                try
                {
                    parsedEndpointUrl = new Uri(
                        parsedEndpointUrl.GetLeftPart(UriPartial.Authority));
                }
                catch (UriFormatException e)
                {
                    TestContext.Out.WriteLine($"Exception: {e.Message}");
                    Assert.Ignore("Invalid Left Part of URL");
                }
                parsedEndpointUrl = new Uri(parsedEndpointUrl.GetLeftPart(UriPartial.Authority));
            }
            if (parsedEndpointUrl != null)
            {
                baseAddresses = FilterByEndpointUrl(parsedEndpointUrl, BaseAddresses);
            }
            Assert.That(BaseAddressCount, Is.GreaterThan(0));
            ArrayOf<EndpointDescription> translatedEndpoints = TranslateEndpointDescriptions(
                parsedEndpointUrl,
                baseAddresses,
                m_endpoints,
                m_serverDescription);
            Assert.That(translatedEndpoints.IsNull, Is.False);
            Assert.That(translatedEndpoints.Count, Is.GreaterThan(0));
            foreach (EndpointDescription endpoint in translatedEndpoints)
            {
                TestContext.Out.WriteLine(
                    $"Endpoint: {endpoint.EndpointUrl} {endpoint.SecurityMode} {endpoint.SecurityPolicyUri}");
            }

            // validate results do not contain duplicates.
            foreach (EndpointDescription endpoint in translatedEndpoints)
            {
                ArrayOf<EndpointDescription> matches = translatedEndpoints.Filter(e => e.EndpointUrl == endpoint.EndpointUrl);
                Assert.That(matches.Count, Is.GreaterThanOrEqualTo(1));
            }

            // validate results have matching UserTokenPolicies in baseaddresses
            foreach (EndpointDescription translatedEndpoint in translatedEndpoints)
            {
                var translatedUri = new Uri(translatedEndpoint.EndpointUrl);

                ArrayOf<EndpointDescription> matches = m_endpoints
                     .Filter(endpoint =>
                     {
                         var endpointUri = new Uri(endpoint.EndpointUrl);
                         return endpoint.TransportProfileUri == translatedEndpoint.TransportProfileUri &&
                             endpoint.SecurityMode == translatedEndpoint.SecurityMode &&
                             endpoint.SecurityPolicyUri == translatedEndpoint.SecurityPolicyUri &&
                             endpoint.BinaryEncodingId == translatedEndpoint.BinaryEncodingId &&
                             endpointUri.Scheme == translatedUri.Scheme &&
                             (m_testConfiguration
                                is not TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort
                                and not TestConfigurations.SingleBaseAdressesWithAlternatePort
                                and not TestConfigurations.DualBaseAddressesWithAlternateHostAndPort
                                and not TestConfigurations.DualBaseAdressesWithAlternatePort ?
                                    endpointUri.Port == translatedUri.Port :
                                    endpointUri.Port % 10 == translatedUri.Port % 10) &&
                             Utils.IsEqual(
                                 endpoint.UserIdentityTokens,
                                 translatedEndpoint.UserIdentityTokens);
                     });

                Assert.That(matches.Count, Is.GreaterThan(0));
                EndpointDescription firstMatch = matches[0];
                Assert.That(
                    translatedEndpoint.UserIdentityTokens.Count,
                    Is.EqualTo(firstMatch.UserIdentityTokens.Count));
                for (int i = 0; i < firstMatch.UserIdentityTokens.Count; i++)
                {
                    Assert.That(
                        translatedEndpoint.UserIdentityTokens[i].TokenType,
                        Is.EqualTo(firstMatch.UserIdentityTokens[i].TokenType));
                    Assert.That(
                        translatedEndpoint.UserIdentityTokens[i].PolicyId,
                        Is.EqualTo(firstMatch.UserIdentityTokens[i].PolicyId));
                }
                Assert.That(translatedEndpoint.BinaryEncodingId, Is.EqualTo(firstMatch.BinaryEncodingId));
                Assert.That(translatedEndpoint.SecurityMode, Is.EqualTo(firstMatch.SecurityMode));
                Assert.That(translatedEndpoint.SecurityPolicyUri, Is.EqualTo(firstMatch.SecurityPolicyUri));
                var firstMatchEndpointUrl = new Uri(firstMatch.EndpointUrl);
                var translatedEndpointUrl = new Uri(translatedEndpoint.EndpointUrl);
                if (m_testConfiguration
                    is not TestConfigurations.SingleBaseAddressesWithAlternateHostAndPort
                        and not TestConfigurations.SingleBaseAdressesWithAlternatePort
                        and not TestConfigurations.DualBaseAddressesWithAlternateHostAndPort
                        and not TestConfigurations.DualBaseAdressesWithAlternatePort)
                {
                    Assert.That(translatedEndpointUrl.Port, Is.EqualTo(firstMatchEndpointUrl.Port));
                    Assert.That(
                        translatedEndpointUrl.LocalPath,
                        Is.EqualTo(firstMatchEndpointUrl.LocalPath));
                }
                else if (firstMatchEndpointUrl.Port != translatedEndpointUrl.Port)
                {
                    // ensure port is translated and mapped to the first base address with the same scheme.
                    Assert.That(
                        translatedEndpointUrl.Port % 10,
                        Is.EqualTo(firstMatchEndpointUrl.Port % 10));

                    ArrayOf<EndpointDescription> theSchemes = m_endpoints
                        .Filter(endpoint =>
                            endpoint.EndpointUrl
                                .StartsWith(translatedEndpointUrl.Scheme, StringComparison.Ordinal));
                    Assert.That(firstMatch.EndpointUrl, Is.EqualTo(theSchemes[0].EndpointUrl));
                }
                Assert.That(translatedEndpointUrl.Scheme, Is.EqualTo(firstMatchEndpointUrl.Scheme));

                // validate results have matching scheme with a port index.
                if (translatedEndpointUrl.Scheme == Utils.UriSchemeHttps)
                {
                    Assert.That(translatedEndpointUrl.Port % 10, Is.EqualTo(2));
                }
                if (translatedEndpointUrl.Scheme == Utils.UriSchemeOpcHttps)
                {
                    Assert.That(translatedEndpointUrl.Port % 10, Is.Zero);
                }
                else if (translatedEndpointUrl.Scheme == Utils.UriSchemeOpcTcp)
                {
                    Assert.That(translatedEndpointUrl.Port % 10, Is.EqualTo(1));
                }
            }
        }

        /// <summary>
        /// Verifies that a server configured with multiple IP addresses on the same port
        /// correctly filters and translates endpoints based on the IP address used by the client.
        /// </summary>
        [Test]
        public void MultipleIPsSamePortTest()
        {
            if (m_testConfiguration != TestConfigurations.MultipleIPsSamePort)
            {
                Assert.Ignore("Not applicable for this configuration.");
            }

            // Verify that the base addresses include same-port entries for different IPs.
            Assert.That(BaseAddresses, Has.Count.EqualTo(BaseAddressCount));

            // Verify that opc.tcp has exactly 2 base addresses (localhost and 192.168.1.100)
            // both on the same port - this is the core scenario under test.
            List<BaseAddress> tcpAddresses = BaseAddresses
                .Where(a => a.Url.Scheme == Utils.UriSchemeOpcTcp)
                .ToList();
            Assert.That(tcpAddresses, Has.Count.EqualTo(2),
                "There should be 2 opc.tcp base addresses for the MultipleIPsSamePort configuration.");
            Assert.That(tcpAddresses[0].Url.Port, Is.EqualTo(tcpAddresses[1].Url.Port),
                "Both opc.tcp base addresses should use the same port number.");
            Assert.That(tcpAddresses[0].Url.Host, Is.Not.EqualTo(tcpAddresses[1].Url.Host),
                "The two opc.tcp base addresses should have different host names/IP addresses.");

            // When a client connects using the specific IP address, FilterByEndpointUrl
            // should return only the base addresses for that IP (one per scheme).
            var ipClientUri = new Uri("opc.tcp://192.168.1.100:62541/UA/SampleServer");
            IList<BaseAddress> filteredByIp = FilterByEndpointUrl(ipClientUri, BaseAddresses);

            Assert.That(filteredByIp, Is.Not.Null);
            Assert.That(filteredByIp, Is.Not.Empty);

            foreach (BaseAddress addr in filteredByIp)
            {
                Assert.That(addr.Url.Host, Is.EqualTo("192.168.1.100"),
                    "A client connecting via a specific IP should only receive endpoints for that IP.");
            }

            // Translate the endpoints for the IP-based client and verify the translated
            // endpoint URLs use the client's IP address, not the server's hostname.
            ArrayOf<EndpointDescription> translatedForIp = TranslateEndpointDescriptions(
                ipClientUri, filteredByIp, m_endpoints, m_serverDescription);

            Assert.That(translatedForIp.IsNull, Is.False);
            Assert.That(translatedForIp.Count, Is.GreaterThan(0));

            foreach (EndpointDescription ep in translatedForIp)
            {
                var epUri = new Uri(ep.EndpointUrl);
                Assert.That(epUri.Host, Is.EqualTo("192.168.1.100"),
                    "Translated endpoint URLs should use the IP address that the client connected with.");
            }
        }

        [Test]
        public void RequireEncryptionNullDescriptionReturnsFalse()
        {
            bool result = ServerBase.RequireEncryption(null);
            Assert.That(result, Is.False);
        }

        [Test]
        public void RequireEncryptionNonePolicyReturnsFalse()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.False);
        }

        [Test]
        public void RequireEncryptionBasic256Sha256ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionAes128ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionAes256ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyWithSecuredTokenReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyWithMultipleTokens()
        {
            // One anonymous (None) and one username with encryption
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    },
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyAllTokensNone()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.False);
        }

        [Test]
        public void RequireEncryptionEmptyUserTokens()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = []
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.False);
        }

        [Test]
        public void EndpointDescriptionSecurityLevelDefaults()
        {
            var desc = new EndpointDescription();
            Assert.That(desc.SecurityLevel, Is.Zero);
        }

        [Test]
        public void EndpointDescriptionSecurityModeDefault()
        {
            var desc = new EndpointDescription();
            Assert.That(
                desc.SecurityMode,
                Is.EqualTo(MessageSecurityMode.Invalid));
        }

        [Test]
        public void UserTokenPolicyDefaultValues()
        {
            var policy = new UserTokenPolicy();
            Assert.That(policy.PolicyId, Is.Null);
            Assert.That(
                policy.TokenType,
                Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void UserTokenPolicyWithTokenType()
        {
            var policy = new UserTokenPolicy(UserTokenType.Certificate);
            Assert.That(policy.TokenType, Is.EqualTo(UserTokenType.Certificate));
        }

        [Test]
        public void RequireEncryptionEccNistP256ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionEccBrainpoolReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.ECC_brainpoolP256r1
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyX509TokenWithPolicy()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Certificate,
                        SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EndpointDescriptionServerCertificateDefault()
        {
            var desc = new EndpointDescription();
            Assert.That(desc.ServerCertificate.IsEmpty, Is.True);
        }

        [Test]
        public void EndpointDescriptionEndpointUrlCanBeSet()
        {
            var desc = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            };
            Assert.That(desc.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840"));
        }

        [Test]
        public void EndpointDescriptionTransportProfileUriCanBeSet()
        {
            var desc = new EndpointDescription
            {
                TransportProfileUri = Profiles.UaTcpTransport
            };
            Assert.That(desc.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
        }

        [Test]
        public void RequireEncryptionNonePolicyIssuedTokenWithPolicy()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.IssuedToken,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }
    }
}
