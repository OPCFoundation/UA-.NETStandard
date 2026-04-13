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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    [TestFixture]
    [Category("ConfiguredEndpointCollectionCoverage")]
    [Parallelizable]
    public class ConfiguredEndpointCollectionCoverageTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void SaveAndLoadStreamRoundTrip()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            ConfiguredEndpoint ep2 = collection.Create("opc.tcp://server2:4840");
            collection.Add(ep1);
            collection.Add(ep2);

            using var ms = new MemoryStream();
            collection.Save(ms);

            ms.Position = 0;
            ConfiguredEndpointCollection loaded =
                ConfiguredEndpointCollection.Load(ms, m_telemetry);

            Assert.That(loaded.Count, Is.EqualTo(2));
        }

        [Test]
        public void IndexOfReturnsCorrectIndex()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            ConfiguredEndpoint ep2 = collection.Create("opc.tcp://server2:4840");
            collection.Add(ep1);
            collection.Add(ep2);

            Assert.That(collection.IndexOf(ep1), Is.EqualTo(0));
            Assert.That(collection.IndexOf(ep2), Is.EqualTo(1));
        }

        [Test]
        public void IndexOfNotFoundReturnsMinusOne()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            ConfiguredEndpoint ep2 = collection.Create("opc.tcp://server2:4840");
            collection.Add(ep1);

            Assert.That(collection.IndexOf(ep2), Is.EqualTo(-1));
        }

        [Test]
        public void InsertAtIndex()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            ConfiguredEndpoint ep2 = collection.Create("opc.tcp://server2:4840");
            ConfiguredEndpoint ep3 = collection.Create("opc.tcp://server3:4840");
            collection.Add(ep1);
            collection.Add(ep3);

            collection.Insert(1, ep2);

            Assert.That(collection.Count, Is.EqualTo(3));
            Assert.That(collection[1], Is.SameAs(ep2));
        }

        [Test]
        public void RemoveAtValidIndex()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            ConfiguredEndpoint ep2 = collection.Create("opc.tcp://server2:4840");
            collection.Add(ep1);
            collection.Add(ep2);

            collection.RemoveAt(0);

            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(collection[0], Is.SameAs(ep2));
        }

        [Test]
        public void RemoveAtNegativeIndexThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            collection.Add(ep1);

            Assert.Throws<ArgumentOutOfRangeException>(() => collection.RemoveAt(-1));
        }

        [Test]
        public void RemoveAtOutOfRangeThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            collection.Add(ep1);

            Assert.Throws<ArgumentOutOfRangeException>(() => collection.RemoveAt(5));
        }

        [Test]
        public void CopyToArray()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep1 = collection.Create("opc.tcp://server1:4840");
            ConfiguredEndpoint ep2 = collection.Create("opc.tcp://server2:4840");
            collection.Add(ep1);
            collection.Add(ep2);

            var array = new ConfiguredEndpoint[4];
            collection.CopyTo(array, 1);

            Assert.That(array[0], Is.Null);
            Assert.That(array[1], Is.Not.Null);
            Assert.That(array[2], Is.Not.Null);
            Assert.That(array[3], Is.Null);
        }

        [Test]
        public void RemoveServerRemovesAllMatchingEndpoints()
        {
            var collection = new ConfiguredEndpointCollection();
            string serverUri = "urn:server1:opcua";

            var desc1 = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://server1:4840",
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    DiscoveryUrls = ["opc.tcp://server1:4840"]
                }
            };
            var desc2 = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://server1:4841",
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    DiscoveryUrls = ["opc.tcp://server1:4841"]
                }
            };
            collection.Add(desc1);
            collection.Add(desc2);

            collection.RemoveServer(serverUri);

            Assert.That(collection.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveServerNullThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            Assert.Throws<ArgumentNullException>(() => collection.RemoveServer(null));
        }

        [Test]
        public void SetApplicationDescriptionNullServerThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            Assert.Throws<ArgumentNullException>(
                () => collection.SetApplicationDescription("urn:test", null));
        }

        [Test]
        public void SetApplicationDescriptionEmptyUriThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = null,
                DiscoveryUrls = ["opc.tcp://server:4840"]
            };
            Assert.Throws<ArgumentException>(
                () => collection.SetApplicationDescription("urn:test", server));
        }

        [Test]
        public void SetApplicationDescriptionNoDiscoveryUrlsThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:test:app"
            };
            Assert.Throws<ArgumentException>(
                () => collection.SetApplicationDescription("urn:test", server));
        }

        [Test]
        public void SetApplicationDescriptionCreatesPlaceholder()
        {
            var collection = new ConfiguredEndpointCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:newserver:opcua",
                DiscoveryUrls = ["opc.tcp://newserver:4840"]
            };

            collection.SetApplicationDescription("urn:newserver:opcua", server);

            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(
                collection[0].Description.Server.ApplicationUri,
                Is.EqualTo("urn:newserver:opcua"));
        }

        [Test]
        public void SetApplicationDescriptionUpdatesExistingEndpoints()
        {
            var collection = new ConfiguredEndpointCollection();
            string serverUri = "urn:myserver:opcua";

            var desc = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://myserver:4840",
                Server = new ApplicationDescription
                {
                    ApplicationUri = serverUri,
                    DiscoveryUrls = ["opc.tcp://myserver:4840"]
                }
            };
            collection.Add(desc);

            var updatedServer = new ApplicationDescription
            {
                ApplicationUri = serverUri,
                ApplicationName = new LocalizedText("NewName"),
                DiscoveryUrls = ["opc.tcp://myserver:4840"]
            };
            collection.SetApplicationDescription(serverUri, updatedServer);

            Assert.That(
                collection[0].Description.Server.ApplicationName.Text,
                Is.EqualTo("NewName"));
        }

        [Test]
        public void ConfiguredEndpointUpdateEndpointDescription()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");
            collection.Add(ep);

            var newDesc = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://server:4841",
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                TransportProfileUri = Profiles.UaTcpTransport
            };

            ep.Update(newDesc);

            Assert.That(ep.Description.EndpointUrl, Is.EqualTo("opc.tcp://server:4841"));
            Assert.That(
                ep.Description.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
        }

        [Test]
        public void ConfiguredEndpointUpdateEndpointDescriptionNullThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");

            Assert.Throws<ArgumentNullException>(
                () => ep.Update((EndpointDescription)null));
        }

        [Test]
        public void ConfiguredEndpointUpdateConfigurationNullThrows()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");

            Assert.Throws<ArgumentNullException>(
                () => ep.Update((EndpointConfiguration)null));
        }

        [Test]
        public void ConfiguredEndpointUpdateConfiguration()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");
            collection.Add(ep);

            var config = new EndpointConfiguration
            {
                OperationTimeout = 60000,
                UseBinaryEncoding = true
            };

            ep.Update(config);

            Assert.That(ep.Configuration.OperationTimeout, Is.EqualTo(60000));
        }

        [Test]
        public void NeedUpdateFromServerNoSecurityNoCert()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");
            ep.Description.SecurityPolicyUri = SecurityPolicies.None;
            ep.Description.ServerCertificate = ByteString.Empty;
            ep.Description.UserIdentityTokens =
            [
                new UserTokenPolicy(UserTokenType.Anonymous)
            ];

            Assert.That(ep.NeedUpdateFromServer(), Is.False);
        }

        [Test]
        public void NeedUpdateFromServerWithSecurityNoCert()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");
            ep.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            ep.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            ep.Description.ServerCertificate = ByteString.Empty;
            ep.Description.UserIdentityTokens =
            [
                new UserTokenPolicy(UserTokenType.Anonymous)
            ];

            Assert.That(ep.NeedUpdateFromServer(), Is.True);
        }

        [Test]
        public void NeedUpdateFromServerWithSecurityHasCert()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://server:4840");
            ep.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            ep.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            ep.Description.ServerCertificate = (ByteString)new byte[] { 0x30, 0x82 };
            ep.Description.UserIdentityTokens =
            [
                new UserTokenPolicy(UserTokenType.Anonymous)
            ];

            Assert.That(ep.NeedUpdateFromServer(), Is.False);
        }

        [Test]
        public void SaveLoadPreservesEndpointUrls()
        {
            var collection = new ConfiguredEndpointCollection();
            ConfiguredEndpoint ep = collection.Create("opc.tcp://roundtrip:4840");
            ep.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            ep.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            collection.Add(ep);

            using var ms = new MemoryStream();
            collection.Save(ms);

            ms.Position = 0;
            ConfiguredEndpointCollection loaded =
                ConfiguredEndpointCollection.Load(ms, m_telemetry);

            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(
                loaded[0].Description.EndpointUrl,
                Does.StartWith("opc.tcp://roundtrip:4840"));
            Assert.That(
                loaded[0].Description.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
        }

        [Test]
        public void SetApplicationDescriptionWithHttpsDiscoveryTrimsDiscoverySuffix()
        {
            var collection = new ConfiguredEndpointCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:httpsserver:opcua",
                DiscoveryUrls = ["https://httpsserver:443/discovery"]
            };

            collection.SetApplicationDescription("urn:httpsserver:opcua", server);

            Assert.That(collection.Count, Is.EqualTo(1));
            Assert.That(
                collection[0].Description.EndpointUrl,
                Does.Not.EndWith("/discovery"));
        }
    }
}
