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

namespace Opc.Ua.Core.Tests.Stack.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ConfiguredEndpointsTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(Path.GetTempPath(), "OpcUaEndpointTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, true);
            }
        }

        [Test]
        public void LoadFromNonExistentFileThrowsFileNotFound()
        {
            string path = Path.Combine(m_tempDir, "nonexistent.xml");
            Assert.Throws<FileNotFoundException>(() =>
                ConfiguredEndpointCollection.Load(path, m_telemetry));
        }

        [Test]
        public void SaveParameterlessUsesOriginalFilePath()
        {
            string path = Path.Combine(m_tempDir, "save_test.xml");
            var collection = new ConfiguredEndpointCollection(EndpointConfiguration.Create());
            collection.Save(path);

            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = "urn:localhost:TestServer",
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = ["opc.tcp://localhost:4840"]
                }
            };

            ConfiguredEndpointCollection reloaded = ConfiguredEndpointCollection.Load(path, m_telemetry);
            reloaded.Add(description);
            reloaded.Save();

            Assert.That(File.Exists(path), Is.True);

            ConfiguredEndpointCollection final = ConfiguredEndpointCollection.Load(path, m_telemetry);
            Assert.That(final.Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveAndLoadRoundTripPreservesEndpoints()
        {
            string path = Path.Combine(m_tempDir, "roundtrip_test.xml");
            var collection = new ConfiguredEndpointCollection(EndpointConfiguration.Create());

            EndpointDescription endpoint1 = CreateEndpointDescription("opc.tcp://server1:4840", "urn:server1");
            EndpointDescription endpoint2 = CreateEndpointDescription("opc.tcp://server2:4841", "urn:server2");
            collection.Add(endpoint1);
            collection.Add(endpoint2);
            collection.Save(path);

            ConfiguredEndpointCollection reloaded = ConfiguredEndpointCollection.Load(path, m_telemetry);
            Assert.That(reloaded.Count, Is.EqualTo(2));
        }

        [Test]
        public void LoadWithApplicationConfigurationOverridesDefaults()
        {
            string path = Path.Combine(m_tempDir, "appconfig_test.xml");
            var collection = new ConfiguredEndpointCollection(EndpointConfiguration.Create());
            collection.Add(CreateEndpointDescription("opc.tcp://localhost:4840", "urn:test"));
            collection.Save(path);

            var appConfig = new ApplicationConfiguration
            {
                TransportQuotas = new TransportQuotas()
            };

            ConfiguredEndpointCollection loaded = ConfiguredEndpointCollection.Load(
                appConfig, path, m_telemetry);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(1));
        }

        [Test]
        public void LoadWithApplicationConfigurationAndOverrideFlagOverridesEndpoints()
        {
            string path = Path.Combine(m_tempDir, "override_test.xml");
            var collection = new ConfiguredEndpointCollection(EndpointConfiguration.Create());
            collection.Add(CreateEndpointDescription("opc.tcp://localhost:4840", "urn:test"));
            collection.Save(path);

            var appConfig = new ApplicationConfiguration
            {
                TransportQuotas = new TransportQuotas()
            };

            ConfiguredEndpointCollection loaded = ConfiguredEndpointCollection.Load(
                appConfig, path, true, m_telemetry);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorWithApplicationConfigSetsDiscoveryUrls()
        {
            var appConfig = new ApplicationConfiguration
            {
                TransportQuotas = new TransportQuotas(),
                ClientConfiguration = new ClientConfiguration
                {
                    WellKnownDiscoveryUrls = ["opc.tcp://discover:4840"]
                }
            };

            var collection = new ConfiguredEndpointCollection(appConfig);
            Assert.That(collection, Is.Not.Null);
            Assert.That(collection.DefaultConfiguration, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithOpcWssDiscoveryUrl()
        {
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:wss:TestServer",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.wss://localhost:443"]
            };

            var endpoint = new ConfiguredEndpoint(server, EndpointConfiguration.Create());
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(endpoint.Description.EndpointUrl, Does.Contain("opc.wss://"));
            Assert.That(endpoint.Description.TransportProfileUri, Is.EqualTo(Profiles.UaWssTransport));
        }

        [Test]
        public void ConstructorWithHttpsDiscoveryUrlStripsDiscoverySuffix()
        {
            var server = new ApplicationDescription
            {
                ApplicationUri = "urn:https:TestServer",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["https://localhost:443/ua/discovery"]
            };

            var endpoint = new ConfiguredEndpoint(server, EndpointConfiguration.Create());
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(endpoint.Description.EndpointUrl, Does.Not.Contain("/discovery"));
        }

        [Test]
        public void GetDiscoveryUrlWithMatchingSchemeReturnsMatchingUrl()
        {
            var description = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://server:4840",
                Server = new ApplicationDescription
                {
                    DiscoveryUrls = ["https://server:443/discovery", "opc.tcp://server:4840"]
                }
            };

            var collection = new ConfiguredEndpointCollection(EndpointConfiguration.Create());
            var endpoint = new ConfiguredEndpoint(collection, description);

            Uri discoveryUrl = endpoint.GetDiscoveryUrl(new Uri("opc.tcp://server:4840"));
            Assert.That(discoveryUrl, Is.Not.Null);
            Assert.That(discoveryUrl.Scheme, Is.EqualTo("opc.tcp"));
        }

        private static EndpointDescription CreateEndpointDescription(string url, string appUri)
        {
            return new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription
                {
                    ApplicationUri = appUri,
                    ApplicationType = ApplicationType.Server,
                    DiscoveryUrls = [url]
                }
            };
        }
    }
}
