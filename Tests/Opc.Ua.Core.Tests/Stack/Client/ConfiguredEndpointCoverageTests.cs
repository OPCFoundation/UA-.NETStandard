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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConfiguredEndpointCoverageTests
    {
        [Test]
        public void DefaultConstructorCreatesValidInstance()
        {
            var endpoint = new ConfiguredEndpoint();
            Assert.That(endpoint.Description, Is.Not.Null);
            Assert.That(endpoint.BinaryEncodingSupport, Is.EqualTo(BinaryEncodingSupport.Optional));
            Assert.That(endpoint.UpdateBeforeConnect, Is.True);
        }

        [Test]
        public void ConstructorWithCollectionAndDescription()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };
            var endpoint = new ConfiguredEndpoint(null, description);

            Assert.That(endpoint.Description, Is.Not.Null);
            Assert.That(endpoint.Description.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840"));
            Assert.That(endpoint.Configuration, Is.Not.Null);
            Assert.That(endpoint.Collection, Is.Null);
        }

        [Test]
        public void ConstructorWithCollectionDescriptionAndConfiguration()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            var configuration = EndpointConfiguration.Create();
            var endpoint = new ConfiguredEndpoint(null, description, configuration);

            Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(endpoint.Configuration, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithNullDescriptionThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ConfiguredEndpoint(null, (EndpointDescription)null));
        }

        [Test]
        public void ConstructorWithApplicationDescription()
        {
            var server = new ApplicationDescription
            {
                ApplicationName = new LocalizedText("TestServer"),
                ApplicationUri = "urn:test:server",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://localhost:4840/discovery"]
            };
            var config = EndpointConfiguration.Create();
            var endpoint = new ConfiguredEndpoint(server, config);

            Assert.That(endpoint.Description, Is.Not.Null);
            Assert.That(endpoint.Description.Server, Is.SameAs(server));
            Assert.That(endpoint.Description.EndpointUrl, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConstructorWithNullApplicationDescriptionThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ConfiguredEndpoint((ApplicationDescription)null, EndpointConfiguration.Create()));
        }

        [Test]
        public void ConstructorWithHttpsDiscoveryUrl()
        {
            var server = new ApplicationDescription
            {
                ApplicationName = new LocalizedText("TestServer"),
                ApplicationUri = "urn:test:server",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["https://localhost:4840/discovery"]
            };
            var endpoint = new ConfiguredEndpoint(server, EndpointConfiguration.Create());

            Assert.That(endpoint.Description.EndpointUrl, Does.Not.EndWith("/discovery"));
        }

        [Test]
        public void UpdateWithEndpointDescription()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            });

            var newDescription = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://remotehost:4841",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            endpoint.Update(newDescription);

            Assert.That(endpoint.Description.EndpointUrl, Is.EqualTo("opc.tcp://remotehost:4841"));
            Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        [Test]
        public void UpdateWithNullDescriptionThrows()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.Throws<ArgumentNullException>(() => endpoint.Update((EndpointDescription)null));
        }

        [Test]
        public void UpdateWithEndpointConfiguration()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            var config = EndpointConfiguration.Create();
            endpoint.Update(config);

            Assert.That(endpoint.Configuration, Is.Not.Null);
        }

        [Test]
        public void UpdateWithNullConfigurationThrows()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.Throws<ArgumentNullException>(() => endpoint.Update((EndpointConfiguration)null));
        }

        [Test]
        public void UpdateWithConfiguredEndpoint()
        {
            var source = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://remotehost:4841",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });
            source.UpdateBeforeConnect = false;

            var target = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            target.Update(source);

            Assert.That(target.Description.EndpointUrl, Is.EqualTo("opc.tcp://remotehost:4841"));
            Assert.That(target.UpdateBeforeConnect, Is.False);
        }

        [Test]
        public void UpdateWithNullConfiguredEndpointThrows()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.Throws<ArgumentNullException>(() => endpoint.Update((ConfiguredEndpoint)null));
        }

        [Test]
        public void ToStringReturnsFormattedString()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            string result = endpoint.ToString();
            Assert.That(result, Does.Contain("opc.tcp://localhost:4840"));
            Assert.That(result, Does.Contain("SignAndEncrypt"));
        }

        [Test]
        public void ToStringWithInvalidFormatThrows()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.Throws<FormatException>(() => endpoint.ToString("X", null));
        }

        [Test]
        public void EndpointUrlPropertyGetterReturnsUri()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Uri url = endpoint.EndpointUrl;
            Assert.That(url, Is.Not.Null);
            Assert.That(url.Host, Is.EqualTo("localhost"));
            Assert.That(url.Port, Is.EqualTo(4840));
        }

        [Test]
        public void EndpointUrlPropertySetterUpdatesDescription()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            endpoint.EndpointUrl = new Uri("opc.tcp://newhost:5000");
            Assert.That(endpoint.Description.EndpointUrl, Does.Contain("newhost"));
        }

        [Test]
        public void EndpointUrlPropertySetterWithNull()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            endpoint.EndpointUrl = null;
            Assert.That(endpoint.EndpointUrl, Is.Null);
        }

        [Test]
        public void EndpointUrlPropertyReturnsNullForEmptyUrl()
        {
            var endpoint = new ConfiguredEndpoint();
            Assert.That(endpoint.EndpointUrl, Is.Null);
        }

        [Test]
        public void SelectedUserTokenPolicyReturnsNullWhenNoTokens()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.That(endpoint.SelectedUserTokenPolicy, Is.Null);
        }

        [Test]
        public void SelectedUserTokenPolicyReturnsCorrectPolicy()
        {
            var policy = new UserTokenPolicy(UserTokenType.Anonymous);
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens = [policy]
            });
            endpoint.SelectedUserTokenPolicyIndex = 0;

            Assert.That(endpoint.SelectedUserTokenPolicy, Is.Not.Null);
            Assert.That(endpoint.SelectedUserTokenPolicy.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void SelectedUserTokenPolicyIndexOutOfRangeReturnsNull()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens = [
                    new UserTokenPolicy(UserTokenType.Anonymous)
                ]
            });
            endpoint.SelectedUserTokenPolicyIndex = 99;
            Assert.That(endpoint.SelectedUserTokenPolicy, Is.Null);
        }

        [Test]
        public void NeedUpdateFromServerReturnsFalseWithNoneSecurity()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = [
                    new UserTokenPolicy(UserTokenType.Anonymous)
                ]
            });
            endpoint.SelectedUserTokenPolicyIndex = 0;

            Assert.That(endpoint.NeedUpdateFromServer(), Is.False);
        }

        [Test]
        public void GetDiscoveryUrlWithHttpScheme()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "https://localhost:4840"
            });
            var discoveryUrl = endpoint.GetDiscoveryUrl(new Uri("https://localhost:4840"));
            Assert.That(discoveryUrl, Is.Not.Null);
            Assert.That(discoveryUrl.ToString(), Does.Contain("discovery"));
        }

        [Test]
        public void GetDiscoveryUrlWithOpcTcpScheme()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            var discoveryUrl = endpoint.GetDiscoveryUrl(new Uri("opc.tcp://localhost:4840"));
            Assert.That(discoveryUrl, Is.Not.Null);
        }

        [Test]
        public void GetDiscoveryUrlWithServerDiscoveryUrls()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                Server = new ApplicationDescription
                {
                    DiscoveryUrls = [
                        "opc.tcp://localhost:4840/discovery"
                    ]
                }
            });
            var discoveryUrl = endpoint.GetDiscoveryUrl(new Uri("opc.tcp://localhost:4840"));
            Assert.That(discoveryUrl, Is.Not.Null);
        }

        [Test]
        public void GetDiscoveryUrlWithNullUsesEndpointUrl()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            var discoveryUrl = endpoint.GetDiscoveryUrl(null);
            Assert.That(discoveryUrl, Is.Not.Null);
        }

        [Test]
        public void BinaryEncodingSupportProperty()
        {
            var endpoint = new ConfiguredEndpoint();
            endpoint.BinaryEncodingSupport = BinaryEncodingSupport.Required;
            Assert.That(endpoint.BinaryEncodingSupport, Is.EqualTo(BinaryEncodingSupport.Required));

            endpoint.BinaryEncodingSupport = BinaryEncodingSupport.None;
            Assert.That(endpoint.BinaryEncodingSupport, Is.EqualTo(BinaryEncodingSupport.None));
        }

        [Test]
        public void UserIdentityProperty()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.That(endpoint.UserIdentity, Is.Null);
            var token = new AnonymousIdentityToken();
            endpoint.UserIdentity = token;
            Assert.That(endpoint.UserIdentity, Is.SameAs(token));
        }

        [Test]
        public void ReverseConnectProperty()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.That(endpoint.ReverseConnect, Is.Null);
        }

        [Test]
        public void ExtensionsProperty()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.That(endpoint.Extensions.IsNull, Is.True);
        }

        [Test]
        public void UpdateBeforeConnectDefaultIsTrue()
        {
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            });
            Assert.That(endpoint.UpdateBeforeConnect, Is.True);
        }

        [Test]
        public void ConstructorWithCollectionUsesDefaultConfiguration()
        {
            var collection = new ConfiguredEndpointCollection();
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            };
            var endpoint = new ConfiguredEndpoint(collection, description);
            Assert.That(endpoint.Collection, Is.SameAs(collection));
            Assert.That(endpoint.Configuration, Is.Not.Null);
        }

        [Test]
        public void DiscoverySuffixConstant()
        {
            Assert.That(ConfiguredEndpoint.DiscoverySuffix, Is.EqualTo("/discovery"));
        }
    }
}
