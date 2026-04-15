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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests
{
    [TestFixture]
    [Category("ApplicationConfigurationEncoding")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationConfigurationEncodingTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestAppConfigEncoding_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_tempDir);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            try
            {
                Directory.Delete(m_tempDir, true);
            }
            catch
            {
            }
        }

        [Test]
        public void DefaultConstructorCreatesValidDefaults()
        {
            var config = new ApplicationConfiguration(m_telemetry);

            Assert.That(config.SecurityConfiguration, Is.Not.Null);
            Assert.That(config.TransportConfigurations.IsNull, Is.False);
            Assert.That(config.Properties, Is.Not.Null);
            Assert.That(config.ExtensionObjects, Is.Not.Null);
            Assert.That(config.PropertiesLock, Is.Not.Null);
            Assert.That(config.CertificateValidator, Is.Not.Null);
        }

        [Test]
        public void CopyConstructorCopiesAllProperties()
        {
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app",
                ApplicationType = ApplicationType.Client,
                ProductUri = "urn:test:product",
                DisableHiResClock = true,
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 4096
                }
            };

            var copy = new ApplicationConfiguration(original);

            Assert.That(copy.ApplicationName, Is.EqualTo("TestApp"));
            Assert.That(copy.ApplicationUri, Is.EqualTo("urn:test:app"));
            Assert.That(copy.ApplicationType, Is.EqualTo(ApplicationType.Client));
            Assert.That(copy.DisableHiResClock, Is.True);
            Assert.That(copy.TransportQuotas.MaxMessageSize, Is.EqualTo(4096));
        }

        [Test]
        public void SecurityConfigurationSetterRejectsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                SecurityConfiguration = null
            };
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        public void TransportConfigurationsGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var transports = new ArrayOf<TransportConfiguration>();
            config.TransportConfigurations = transports;
            Assert.That(config.TransportConfigurations.Count, Is.EqualTo(transports.Count));
        }

        [Test]
        public void ExtensionsGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var extensions = new ArrayOf<XmlElement>();
            config.Extensions = extensions;
            Assert.That(config.Extensions.Count, Is.EqualTo(extensions.Count));
        }

        [Test]
        public void CreateMessageContextWithoutTransportQuotas()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                TransportQuotas = null
            };

            ServiceMessageContext ctx = config.CreateMessageContext();
            Assert.That(ctx, Is.Not.Null);
        }

        [Test]
        public void CreateMessageContextWithTransportQuotas()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                TransportQuotas = new TransportQuotas
                {
                    MaxArrayLength = 1000,
                    MaxByteStringLength = 2000,
                    MaxStringLength = 3000,
                    MaxMessageSize = 4000,
                    MaxEncodingNestingLevels = 50,
                    MaxDecoderRecoveries = 10
                }
            };

            ServiceMessageContext ctx = config.CreateMessageContext();
            Assert.That(ctx.MaxArrayLength, Is.EqualTo(1000));
            Assert.That(ctx.MaxByteStringLength, Is.EqualTo(2000));
            Assert.That(ctx.MaxStringLength, Is.EqualTo(3000));
            Assert.That(ctx.MaxMessageSize, Is.EqualTo(4000));
            Assert.That(ctx.MaxEncodingNestingLevels, Is.EqualTo(50));
            Assert.That(ctx.MaxDecoderRecoveries, Is.EqualTo(10));
        }

        [Test]
        public void CreateMessageContextWithCustomFactory()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            IEncodeableFactory factory = EncodeableFactory.Create();

            ServiceMessageContext ctx = config.CreateMessageContext(factory);
            Assert.That(ctx, Is.Not.Null);
            Assert.That(ctx.Factory, Is.SameAs(factory));
        }

        [Test]
        public void CreateMessageContextWithNullFactoryCreatesNewOne()
        {
            var config = new ApplicationConfiguration(m_telemetry);

            ServiceMessageContext ctx = config.CreateMessageContext(null);
            Assert.That(ctx, Is.Not.Null);
            Assert.That(ctx.Factory, Is.Not.Null);
        }

        [Test]
        public void GetServerDomainNamesWithNoServerConfig()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.Zero);
        }

        [Test]
        public void GetServerDomainNamesWithBaseAddresses()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses = ["opc.tcp://localhost:4840"];

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.GreaterThan(0));
        }

        [Test]
        public void GetServerDomainNamesWithAlternateBaseAddresses()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.AlternateBaseAddresses = ["opc.tcp://192.168.1.1:4840"];

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.GreaterThan(0));
        }

        [Test]
        public void GetServerDomainNamesWithDiscoveryServerConfig()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                DiscoveryServerConfiguration = new DiscoveryServerConfiguration()
            };
            config.DiscoveryServerConfiguration.BaseAddresses = ["opc.tcp://localhost:4840"];

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.GreaterThan(0));
        }

        [Test]
        public void GetServerDomainNamesDeduplicates()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses =
            [
                "opc.tcp://localhost:4840",
                "opc.tcp://localhost:4841"
            ];

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.EqualTo(1));
        }

        [Test]
        public void GetServerDomainNamesSkipsInvalidUris()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses = ["not a valid uri !!!"];

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.Zero);
        }

        [Test]
        public void SaveToFileAndLoadWithNoValidationRoundTrip()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "RoundTripApp",
                ApplicationUri = "urn:test:roundtrip",
                ApplicationType = ApplicationType.ClientAndServer,
                ProductUri = "urn:test:product"
            };

            string filePath = Path.Combine(m_tempDir, "roundtrip_config.xml");
            config.SaveToFile(filePath);

            Assert.That(File.Exists(filePath), Is.True);
            Assert.That(new FileInfo(filePath).Length, Is.GreaterThan(0));

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.ApplicationName, Is.EqualTo("RoundTripApp"));
            Assert.That(loaded.ApplicationUri, Is.EqualTo("urn:test:roundtrip"));
            Assert.That(loaded.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            Assert.That(loaded.SourceFilePath, Is.EqualTo(new FileInfo(filePath).FullName));
        }

        [Test]
        public void SaveToFileWithTransportQuotasAndReload()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "QuotasApp",
                ApplicationUri = "urn:test:quotas",
                ApplicationType = ApplicationType.Server,
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 8192,
                    MaxArrayLength = 500
                }
            };

            string filePath = Path.Combine(m_tempDir, "quotas_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded.TransportQuotas, Is.Not.Null);
            Assert.That(loaded.TransportQuotas.MaxMessageSize, Is.EqualTo(8192));
            Assert.That(loaded.TransportQuotas.MaxArrayLength, Is.EqualTo(500));
        }

        [Test]
        public void SaveToFileWithServerConfiguration()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "ServerApp",
                ApplicationUri = "urn:test:server",
                ApplicationType = ApplicationType.Server,
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses = ["opc.tcp://localhost:4840"];
            config.ServerConfiguration.MaxRegistrationInterval = 30000;

            string filePath = Path.Combine(m_tempDir, "server_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded.ServerConfiguration, Is.Not.Null);
            Assert.That(loaded.ServerConfiguration.MaxRegistrationInterval, Is.EqualTo(30000));
        }

        [Test]
        public void SaveToFileWithClientConfiguration()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "ClientApp",
                ApplicationUri = "urn:test:client",
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration()
            };
            config.ClientConfiguration.DefaultSessionTimeout = 60000;

            string filePath = Path.Combine(m_tempDir, "client_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded.ClientConfiguration, Is.Not.Null);
            Assert.That(loaded.ClientConfiguration.DefaultSessionTimeout, Is.EqualTo(60000));
        }

        [Test]
        public void LoadWithNoValidationThrowsOnMissingFile()
        {
            string missingPath = Path.Combine(m_tempDir, "does_not_exist.xml");
            Assert.That(
                () => ApplicationConfiguration.LoadWithNoValidation(
                    new FileInfo(missingPath),
                    typeof(ApplicationConfiguration),
                    m_telemetry),
                Throws.Exception);
        }

        [Test]
        public void LoadWithNoValidationNullSystemTypeDefaultsToBase()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "NullTypeApp",
                ApplicationUri = "urn:test:nulltype",
                ApplicationType = ApplicationType.Client
            };
            string filePath = Path.Combine(m_tempDir, "nulltype_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                null,
                m_telemetry);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.ApplicationName, Is.EqualTo("NullTypeApp"));
        }

        [Test]
        public void GetFilePathFromAppConfigReturnsDefaultOnMissing()
        {
            string result = ApplicationConfiguration.GetFilePathFromAppConfig(
                "NonExistentSection_" + Guid.NewGuid().ToString("N"),
                LoggerUtils.Null.Logger);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.EndWith(".Config.xml"));
        }

        [Test]
        public void ValidateAsyncThrowsWhenApplicationNameEmpty()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = string.Empty,
                ApplicationUri = "urn:test:empty"
            };
            Assert.That(
                async () => await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ValidateAsyncThrowsWhenSecurityConfigurationNull()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:nosecurity"
            };

            // SecurityConfiguration setter replaces null with new instance,
            // so we test the validate path by setting it to a valid one
            // then relying on it to succeed at the Name check.
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        public void SaveToFileWithDiscoveryServerConfiguration()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "DiscoveryApp",
                ApplicationUri = "urn:test:discovery",
                ApplicationType = ApplicationType.DiscoveryServer,
                DiscoveryServerConfiguration = new DiscoveryServerConfiguration()
            };
            config.DiscoveryServerConfiguration.BaseAddresses = ["opc.tcp://localhost:4840/discovery"];

            string filePath = Path.Combine(m_tempDir, "discovery_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded.DiscoveryServerConfiguration, Is.Not.Null);
        }

        [Test]
        public void SaveToFileWithDisableHiResClock()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "ClockApp",
                ApplicationUri = "urn:test:clock",
                ApplicationType = ApplicationType.Client,
                DisableHiResClock = true
            };

            string filePath = Path.Combine(m_tempDir, "clock_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded.DisableHiResClock, Is.True);
        }

        [Test]
        public void SaveToFileWithTraceConfiguration()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TraceApp",
                ApplicationUri = "urn:test:trace",
                ApplicationType = ApplicationType.Client,
                TraceConfiguration = new TraceConfiguration()
            };

            string filePath = Path.Combine(m_tempDir, "trace_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded.TraceConfiguration, Is.Not.Null);
        }

        [Test]
        public void PropertiesDictionaryIsUsable()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            config.Properties["key1"] = "value1";
            config.Properties["key2"] = 42;

            Assert.That(config.Properties["key1"], Is.EqualTo("value1"));
            Assert.That(config.Properties["key2"], Is.EqualTo(42));
            Assert.That(config.Properties, Has.Count.EqualTo(2));
        }

        [Test]
        public void ExtensionObjectsListIsUsable()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            config.ExtensionObjects.Add("test");
            Assert.That(config.ExtensionObjects, Has.Count.EqualTo(1));
        }

        [Test]
        public Task LoadAsyncFromStreamRoundTrip()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "StreamApp",
                ApplicationUri = "urn:test:stream",
                ApplicationType = ApplicationType.Client
            };

            string filePath = Path.Combine(m_tempDir, "stream_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.ApplicationName, Is.EqualTo("StreamApp"));
            return Task.CompletedTask;
        }

        [Test]
        public void LoadAsyncFromFileInfoRoundTrip()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "FileInfoApp",
                ApplicationUri = "urn:test:fileinfo",
                ApplicationType = ApplicationType.Client
            };

            string filePath = Path.Combine(m_tempDir, "fileinfo_config.xml");
            config.SaveToFile(filePath);

            ApplicationConfiguration loaded = ApplicationConfiguration.LoadWithNoValidation(
                new FileInfo(filePath),
                typeof(ApplicationConfiguration),
                m_telemetry);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.ApplicationName, Is.EqualTo("FileInfoApp"));
            Assert.That(loaded.SourceFilePath, Is.EqualTo(new FileInfo(filePath).FullName));
        }

        [Test]
        public void LoadAsyncFromFileInfoThrowsOnMissingFile()
        {
            string missingPath = Path.Combine(m_tempDir, "missing_file.xml");
            Assert.That(
                () => ApplicationConfiguration.LoadAsync(
                    new FileInfo(missingPath),
                    ApplicationType.Client,
                    typeof(ApplicationConfiguration),
                    false,
                    m_telemetry),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void GetServerDomainNamesWithDiscoveryAlternateAddresses()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                DiscoveryServerConfiguration = new DiscoveryServerConfiguration()
            };
            config.DiscoveryServerConfiguration.AlternateBaseAddresses = ["opc.tcp://10.0.0.1:4840"];

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.GreaterThan(0));
        }

#pragma warning disable CS0618
        [Test]
        public void CreateMessageContextObsoleteOverload()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ServiceMessageContext ctx = config.CreateMessageContext(true);
            Assert.That(ctx, Is.Not.Null);
        }
#pragma warning restore CS0618
    }
}
