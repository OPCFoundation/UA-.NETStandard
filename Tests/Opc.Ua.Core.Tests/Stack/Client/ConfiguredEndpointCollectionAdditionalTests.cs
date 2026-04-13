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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests
{
    [TestFixture]
    [Category("ConfiguredEndpointCollectionAdditional")]
    [Parallelizable]
    public class ConfiguredEndpointCollectionAdditionalTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestEndpointCol_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            try { Directory.Delete(m_tempDir, true); }
            catch { }
        }

        private ConfiguredEndpointCollection CreateCollection()
        {
            return new ConfiguredEndpointCollection(EndpointConfiguration.Create());
        }

        private EndpointDescription CreateEndpoint(string url)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = url,
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = new ArrayOf<string>(new[] { url })
                }
            };
        }

        [Test]
        public void CreateEndpointFromOpcTcpUrl()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Create("opc.tcp://localhost:4840");
            Assert.That(ep, Is.Not.Null);
            Assert.That(ep.Description.EndpointUrl, Does.Contain("opc.tcp://localhost:4840"));
            Assert.That(ep.Description.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
            Assert.That(ep.UpdateBeforeConnect, Is.True);
        }

        [Test]
        public void CreateEndpointFromHttpsUrl()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Create("https://localhost:4843");
            Assert.That(ep, Is.Not.Null);
            Assert.That(ep.Description.TransportProfileUri, Is.EqualTo(Profiles.HttpsBinaryTransport));
        }

        [Test]
        public void CreateEndpointFromOpcWssUrl()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Create("opc.wss://localhost:4844");
            Assert.That(ep, Is.Not.Null);
            Assert.That(ep.Description.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
        }

        [Test]
        public void CreateEndpointWithSecurityParameters()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Create(
                "opc.tcp://localhost:4840- [SignAndEncrypt:Basic256Sha256:Binary]");
            Assert.That(ep, Is.Not.Null);
            Assert.That(ep.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(ep.Configuration.UseBinaryEncoding, Is.True);
        }

        [Test]
        public void CreateEndpointWithInvalidSecurityModeFallsBackToNone()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Create(
                "opc.tcp://localhost:4840- [InvalidMode:Basic256Sha256:Binary]");
            Assert.That(ep.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public void CreateEndpointWithPartialParameters()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Create(
                "opc.tcp://localhost:4840- [Sign]");
            Assert.That(ep, Is.Not.Null);
            Assert.That(ep.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
        }

        [Test]
        public void IndexOfReturnsCorrectIndex()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            Assert.That(col.IndexOf(ep), Is.EqualTo(0));
        }

        [Test]
        public void IndexOfReturnsMinusOneForUnknown()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            var other = new ConfiguredEndpoint(null, CreateEndpoint("opc.tcp://other:4840"));
            Assert.That(col.IndexOf(other), Is.EqualTo(-1));
        }

        [Test]
        public void ContainsReturnsTrueForExistingEndpoint()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            Assert.That(col.Contains(ep), Is.True);
        }

        [Test]
        public void ContainsReturnsFalseForMissingEndpoint()
        {
            var col = CreateCollection();
            var other = new ConfiguredEndpoint(null, CreateEndpoint("opc.tcp://other:4840"));
            Assert.That(col.Contains(other), Is.False);
        }

        [Test]
        public void InsertAtIndex()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            var ep2 = new ConfiguredEndpoint(null, CreateEndpoint("opc.tcp://server2:4840"));
            col.Insert(0, ep2);
            Assert.That(col[0], Is.SameAs(ep2));
        }

        [Test]
        public void RemoveAtIndex()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            Assert.That(col.Count, Is.EqualTo(1));
            col.RemoveAt(0);
            Assert.That(col.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveAtThrowsOnInvalidIndex()
        {
            var col = CreateCollection();
            Assert.That(() => col.RemoveAt(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => col.RemoveAt(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void IndexerSetThrowsNotImplemented()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            Assert.That(
                () => col[0] = new ConfiguredEndpoint(null, CreateEndpoint("opc.tcp://x:1")),
                Throws.TypeOf<NotImplementedException>());
        }

        [Test]
        public void ClearRemovesAllEndpoints()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            col.Add(CreateEndpoint("opc.tcp://server2:4840"));
            Assert.That(col.Count, Is.EqualTo(2));
            col.Clear();
            Assert.That(col.Count, Is.EqualTo(0));
        }

        [Test]
        public void CopyToArray()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            col.Add(CreateEndpoint("opc.tcp://server2:4840"));

            var array = new ConfiguredEndpoint[2];
            col.CopyTo(array, 0);
            Assert.That(array[0], Is.Not.Null);
            Assert.That(array[1], Is.Not.Null);
        }

        [Test]
        public void IsReadOnlyReturnsFalse()
        {
            var col = CreateCollection();
            Assert.That(col.IsReadOnly, Is.False);
        }

        [Test]
        public void GetEnumeratorIteratesAllEndpoints()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            col.Add(CreateEndpoint("opc.tcp://server2:4840"));

            int count = 0;
            foreach (ConfiguredEndpoint ep in col)
            {
                count++;
                Assert.That(ep, Is.Not.Null);
            }
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void AddEndpointDescriptionOnly()
        {
            var col = CreateCollection();
            EndpointDescription desc = CreateEndpoint("opc.tcp://server1:4840");
            ConfiguredEndpoint ep = col.Add(desc);
            Assert.That(ep, Is.Not.Null);
            Assert.That(col.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddDuplicateEndpointDescriptionThrows()
        {
            var col = CreateCollection();
            EndpointDescription desc = CreateEndpoint("opc.tcp://server1:4840");
            col.Add(desc);
            Assert.That(() => col.Add(desc), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RemoveEndpoint()
        {
            var col = CreateCollection();
            ConfiguredEndpoint ep = col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            Assert.That(col.Remove(ep), Is.True);
            Assert.That(col.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveNullThrows()
        {
            var col = CreateCollection();
            Assert.That(() => col.Remove(null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AddNullConfiguredEndpointThrows()
        {
            var col = CreateCollection();
            Assert.That(() => col.Add((ConfiguredEndpoint)null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RemoveServerByUri()
        {
            var col = CreateCollection();
            EndpointDescription desc = CreateEndpoint("opc.tcp://server1:4840");
            col.Add(desc);
            col.RemoveServer("opc.tcp://server1:4840");
            Assert.That(col.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveServerNullThrows()
        {
            var col = CreateCollection();
            Assert.That(() => col.RemoveServer(null), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void GetEndpointsReturnsMatchingEndpoints()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            col.Add(CreateEndpoint("opc.tcp://server2:4840"));

            List<ConfiguredEndpoint> results = col.GetEndpoints("opc.tcp://server1:4840");
            Assert.That(results.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetEndpointsReturnsEmptyForUnknown()
        {
            var col = CreateCollection();
            List<ConfiguredEndpoint> results = col.GetEndpoints("urn:unknown");
            Assert.That(results.Count, Is.EqualTo(0));
        }

        [Test]
        public void GetServersReturnsUniqueServers()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));
            col.Add(CreateEndpoint("opc.tcp://server2:4840"));

            ArrayOf<ApplicationDescription> servers = col.GetServers();
            Assert.That(servers.Count, Is.EqualTo(2));
        }

        [Test]
        public void DiscoveryUrlsGetSet()
        {
            var col = CreateCollection();
            var urls = new ArrayOf<string>(new[] { "opc.tcp://discovery:4840" });
            col.DiscoveryUrls = urls;
            Assert.That(col.DiscoveryUrls.Count, Is.EqualTo(1));
        }

        [Test]
        public void DiscoveryUrlsNullSetsDefaults()
        {
            var col = CreateCollection();
            col.DiscoveryUrls = default;
            Assert.That(col.DiscoveryUrls.IsNull, Is.False);
        }

        [Test]
        public void DefaultConfigurationIsNotNull()
        {
            var col = CreateCollection();
            Assert.That(col.DefaultConfiguration, Is.Not.Null);
        }

        [Test]
        public void SetApplicationDescriptionNullServerThrows()
        {
            var col = CreateCollection();
            Assert.That(
                () => col.SetApplicationDescription("urn:test", null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SetApplicationDescriptionEmptyUriThrows()
        {
            var col = CreateCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "",
                DiscoveryUrls = new ArrayOf<string>(new[] { "opc.tcp://localhost:4840" })
            };
            Assert.That(
                () => col.SetApplicationDescription("urn:test", server),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SetApplicationDescriptionNoDiscoveryUrlsThrows()
        {
            var col = CreateCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:test:server"
            };
            Assert.That(
                () => col.SetApplicationDescription("urn:test", server),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void SetApplicationDescriptionCreatesPlaceholderForNewServer()
        {
            var col = CreateCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:test:newserver",
                DiscoveryUrls = new ArrayOf<string>(new[] { "opc.tcp://localhost:4840" })
            };
            col.SetApplicationDescription("urn:test:newserver", server);
            Assert.That(col.Count, Is.EqualTo(1));
        }

        [Test]
        public void SetApplicationDescriptionUpdatesExistingEndpoints()
        {
            var col = CreateCollection();
            EndpointDescription desc = CreateEndpoint("opc.tcp://server1:4840");
            col.Add(desc);

            var updatedServer = new ApplicationDescription
            {
                ApplicationUri = "opc.tcp://server1:4840",
                ApplicationName = new LocalizedText("Updated"),
                DiscoveryUrls = new ArrayOf<string>(new[] { "opc.tcp://server1:4840" })
            };
            col.SetApplicationDescription("opc.tcp://server1:4840", updatedServer);

            Assert.That(col[0].Description.Server.ApplicationName.Text, Is.EqualTo("Updated"));
        }

        [Test]
        public void SetApplicationDescriptionWithHttpDiscoveryUrl()
        {
            var col = CreateCollection();
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:test:http",
                DiscoveryUrls = new ArrayOf<string>(
                    new[] { "https://localhost:4843/discovery" })
            };
            col.SetApplicationDescription("urn:test:http", server);
            Assert.That(col.Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveAndLoadStreamRoundTrip()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));

            using (var stream = new MemoryStream())
            {
                col.Save(stream);
                stream.Position = 0;

                ConfiguredEndpointCollection loaded =
                    ConfiguredEndpointCollection.Load(stream, m_telemetry);
                Assert.That(loaded.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void SaveAndLoadFileRoundTrip()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));

            string filePath = Path.Combine(m_tempDir, "endpoints.xml");
            col.Save(filePath);

            Assert.That(File.Exists(filePath), Is.True);

            ConfiguredEndpointCollection loaded =
                ConfiguredEndpointCollection.Load(filePath, m_telemetry);
            Assert.That(loaded.Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveAndLoadWithAppConfigRoundTrip()
        {
            var appConfig = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app",
                ApplicationType = ApplicationType.Client
            };
            var col = new ConfiguredEndpointCollection(appConfig);
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));

            string filePath = Path.Combine(m_tempDir, "endpoints_appconfig.xml");
            col.Save(filePath);

            ConfiguredEndpointCollection loaded =
                ConfiguredEndpointCollection.Load(appConfig, filePath, m_telemetry);
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded.DefaultConfiguration, Is.Not.Null);
        }

        [Test]
        public void LoadWithOverrideConfiguration()
        {
            var col = CreateCollection();
            col.Add(CreateEndpoint("opc.tcp://server1:4840"));

            string filePath = Path.Combine(m_tempDir, "endpoints_override.xml");
            col.Save(filePath);

            var appConfig = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app",
                ApplicationType = ApplicationType.Client
            };

            ConfiguredEndpointCollection loaded =
                ConfiguredEndpointCollection.Load(appConfig, filePath, true, m_telemetry);
            Assert.That(loaded.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorFromApplicationConfiguration()
        {
            var appConfig = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration()
            };
            appConfig.ClientConfiguration.WellKnownDiscoveryUrls =
                new ArrayOf<string>(new[] { "opc.tcp://discovery:4840" });

            var col = new ConfiguredEndpointCollection(appConfig);
            Assert.That(col.DefaultConfiguration, Is.Not.Null);
            Assert.That(col.DiscoveryUrls.Count, Is.GreaterThan(0));
        }

        [Test]
        public void AddEndpointWithConfiguration()
        {
            var col = CreateCollection();
            EndpointDescription desc = CreateEndpoint("opc.tcp://server1:4840");
            var endpointConfig = EndpointConfiguration.Create();
            endpointConfig.UseBinaryEncoding = false;

            ConfiguredEndpoint ep = col.Add(desc, endpointConfig);
            Assert.That(ep, Is.Not.Null);
            Assert.That(col.Count, Is.EqualTo(1));
        }
    }
}
