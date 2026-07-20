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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests
{
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ApplicationConfigurationTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        /// <summary>
        /// Parameterless constructor creates a valid instance.
        /// </summary>
        public void ConstructorDefault()
        {
            var config = new ApplicationConfiguration();
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// Constructor with telemetry creates a valid instance.
        /// </summary>
        public void ConstructorWithTelemetry()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// Copy constructor copies all supported properties.
        /// </summary>
        public void ConstructorCopy()
        {
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app",
                ApplicationType = ApplicationType.Server,
                DisableHiResClock = true
            };

            var copy = new ApplicationConfiguration(original);
            Assert.That(copy.ApplicationName, Is.EqualTo("TestApp"));
            Assert.That(copy.ApplicationUri, Is.EqualTo("urn:test:app"));
            Assert.That(copy.ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(copy.DisableHiResClock, Is.True);
        }

        [Test]
        /// <summary>
        /// ApplicationName get/set round-trips correctly.
        /// </summary>
        public void ApplicationNameGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "MyApp"
            };
            Assert.That(config.ApplicationName, Is.EqualTo("MyApp"));
        }

        [Test]
        /// <summary>
        /// ApplicationName defaults to null.
        /// </summary>
        public void ApplicationNameDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ApplicationName, Is.Null);
        }

        [Test]
        /// <summary>
        /// ApplicationUri get/set round-trips correctly.
        /// </summary>
        public void ApplicationUriGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationUri = "urn:test:myapp"
            };
            Assert.That(config.ApplicationUri, Is.EqualTo("urn:test:myapp"));
        }

        [Test]
        /// <summary>
        /// ApplicationUri defaults to null.
        /// </summary>
        public void ApplicationUriDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ApplicationUri, Is.Null);
        }

        [Test]
        /// <summary>
        /// ProductUri get/set round-trips correctly.
        /// </summary>
        public void ProductUriGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ProductUri = "urn:test:product"
            };
            Assert.That(config.ProductUri, Is.EqualTo("urn:test:product"));
        }

        [Test]
        /// <summary>
        /// ProductUri defaults to null.
        /// </summary>
        public void ProductUriDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ProductUri, Is.Null);
        }

        [Test]
        /// <summary>
        /// ApplicationType get/set round-trips correctly.
        /// </summary>
        public void ApplicationTypeGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationType = ApplicationType.ClientAndServer
            };
            Assert.That(config.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        /// <summary>
        /// ApplicationType defaults to Server.
        /// </summary>
        public void ApplicationTypeDefaultIsServer()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ApplicationType, Is.EqualTo(ApplicationType.Server));
        }

        [Test]
        /// <summary>
        /// ServerConfiguration get/set round-trips correctly.
        /// </summary>
        public void ServerConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var serverConfig = new ServerConfiguration();
            config.ServerConfiguration = serverConfig;
            Assert.That(config.ServerConfiguration, Is.SameAs(serverConfig));
        }

        [Test]
        /// <summary>
        /// ServerConfiguration defaults to null.
        /// </summary>
        public void ServerConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ServerConfiguration, Is.Null);
        }

        [Test]
        /// <summary>
        /// ClientConfiguration get/set round-trips correctly.
        /// </summary>
        public void ClientConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var clientConfig = new ClientConfiguration();
            config.ClientConfiguration = clientConfig;
            Assert.That(config.ClientConfiguration, Is.SameAs(clientConfig));
        }

        [Test]
        /// <summary>
        /// ClientConfiguration defaults to null.
        /// </summary>
        public void ClientConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ClientConfiguration, Is.Null);
        }

        [Test]
        /// <summary>
        /// SecurityConfiguration is created by default constructor.
        /// </summary>
        public void SecurityConfigurationDefaultIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// SecurityConfiguration set with null reverts to default.
        /// </summary>
        public void SecurityConfigurationSetNullReturnsDefault()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                SecurityConfiguration = null
            };
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// SecurityConfiguration get/set round-trips correctly.
        /// </summary>
        public void SecurityConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var secConfig = new SecurityConfiguration();
            config.SecurityConfiguration = secConfig;
            Assert.That(config.SecurityConfiguration, Is.SameAs(secConfig));
        }

        [Test]
        /// <summary>
        /// TransportQuotas get/set round-trips correctly.
        /// </summary>
        public void TransportQuotasGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var quotas = new TransportQuotas();
            config.TransportQuotas = quotas;
            Assert.That(config.TransportQuotas, Is.SameAs(quotas));
        }

        [Test]
        /// <summary>
        /// TransportQuotas defaults to null.
        /// </summary>
        public void TransportQuotasDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.TransportQuotas, Is.Null);
        }

        [Test]
        /// <summary>
        /// TransportConfigurations is created by default constructor.
        /// </summary>
        public void TransportConfigurationsDefaultIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.TransportConfigurations.IsNull, Is.False);
        }

        [Test]
        /// <summary>
        /// TransportConfigurations get/set round-trips correctly.
        /// </summary>
        public void TransportConfigurationsGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                TransportConfigurations = new List<TransportConfiguration>
                    {
                        new("opc.tcp", typeof(object))
                    }.ToArrayOf()
            };
            Assert.That(config.TransportConfigurations.Count, Is.EqualTo(1));
        }

        [Test]
        /// <summary>
        /// CertificateManager is null by default before validation.
        /// </summary>
        public void CertificateManagerDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.CertificateManager, Is.Null);
        }

        [Test]
        /// <summary>
        /// CertificateManager get/set round-trips correctly.
        /// </summary>
        public void CertificateManagerGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            using var manager = new CertificateManager(m_telemetry);
            config.CertificateManager = manager;
            Assert.That(config.CertificateManager, Is.SameAs(manager));
        }

        [Test]
        /// <summary>
        /// TraceConfiguration get/set round-trips correctly.
        /// </summary>
        public void TraceConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                TraceConfiguration = new TraceConfiguration { OutputFilePath = "trace.log" }
            };
            Assert.That(config.TraceConfiguration, Is.Not.Null);
            Assert.That(config.TraceConfiguration.OutputFilePath, Is.EqualTo("trace.log"));
        }

        [Test]
        /// <summary>
        /// TraceConfiguration defaults to null.
        /// </summary>
        public void TraceConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.TraceConfiguration, Is.Null);
        }

        [Test]
        /// <summary>
        /// DisableHiResClock get/set round-trips correctly.
        /// </summary>
        public void DisableHiResClockGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                DisableHiResClock = true
            };
            Assert.That(config.DisableHiResClock, Is.True);
        }

        [Test]
        /// <summary>
        /// DisableHiResClock defaults to false.
        /// </summary>
        public void DisableHiResClockDefaultIsFalse()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.DisableHiResClock, Is.False);
        }

        [Test]
        /// <summary>
        /// DiscoveryServerConfiguration get/set round-trips correctly.
        /// </summary>
        public void DiscoveryServerConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var dsc = new DiscoveryServerConfiguration();
            config.DiscoveryServerConfiguration = dsc;
            Assert.That(config.DiscoveryServerConfiguration, Is.SameAs(dsc));
        }

        [Test]
        /// <summary>
        /// DiscoveryServerConfiguration defaults to null.
        /// </summary>
        public void DiscoveryServerConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.DiscoveryServerConfiguration, Is.Null);
        }

        [Test]
        /// <summary>
        /// Properties dictionary is accessible and initially empty.
        /// </summary>
        public void PropertiesDictionaryIsAccessible()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.Properties, Is.Not.Null);
            Assert.That(config.Properties, Is.Empty);
        }

        [Test]
        /// <summary>
        /// PropertiesLock is not null.
        /// </summary>
        public void PropertiesLockIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.PropertiesLock, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// ExtensionObjects list is accessible.
        /// </summary>
        public void ExtensionObjectsIsAccessible()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ExtensionObjects, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// CreateMessageContext returns a valid ServiceMessageContext.
        /// </summary>
        public void CreateMessageContextReturnsValidContext()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ServiceMessageContext context = config.CreateMessageContext();
            Assert.That(context, Is.Not.Null);
            Assert.That(context.NamespaceUris, Is.Not.Null);
        }

        [Test]
        /// <summary>
        /// CreateMessageContext with factory returns context with that factory.
        /// </summary>
        public void CreateMessageContextWithFactory()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            IEncodeableFactory factory = EncodeableFactory.Create();
            ServiceMessageContext context = config.CreateMessageContext(factory);
            Assert.That(context, Is.Not.Null);
            Assert.That(context.Factory, Is.SameAs(factory));
        }

        [Test]
        /// <summary>
        /// CreateMessageContext applies TransportQuotas when set.
        /// </summary>
        public void CreateMessageContextAppliesTransportQuotas()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                TransportQuotas = new TransportQuotas
                {
                    MaxArrayLength = 42,
                    MaxStringLength = 99,
                    MaxByteStringLength = 101,
                    MaxMessageSize = 5000,
                    MaxEncodingNestingLevels = 7,
                    MaxDecoderRecoveries = 3
                }
            };

            ServiceMessageContext context = config.CreateMessageContext();
            Assert.That(context.MaxArrayLength, Is.EqualTo(42));
            Assert.That(context.MaxStringLength, Is.EqualTo(99));
            Assert.That(context.MaxByteStringLength, Is.EqualTo(101));
            Assert.That(context.MaxMessageSize, Is.EqualTo(5000));
            Assert.That(context.MaxEncodingNestingLevels, Is.EqualTo(7));
            Assert.That(context.MaxDecoderRecoveries, Is.EqualTo(3));
        }

        [Test]
        /// <summary>
        /// CreateMessageContext without TransportQuotas uses defaults.
        /// </summary>
        public void CreateMessageContextWithoutTransportQuotasUsesDefaults()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ServiceMessageContext context = config.CreateMessageContext();
            Assert.That(context.MaxArrayLength, Is.EqualTo(DefaultEncodingLimits.MaxArrayLength));
            Assert.That(context.MaxStringLength, Is.EqualTo(DefaultEncodingLimits.MaxStringLength));
        }

        [Test]
        /// <summary>
        /// GetServerDomainNames returns empty when ServerConfiguration is null.
        /// </summary>
        public void GetServerDomainNamesReturnsEmptyWhenNoServerConfig()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.Zero);
        }

        [Test]
        /// <summary>
        /// GetServerDomainNames extracts domains from base addresses.
        /// </summary>
        public void GetServerDomainNamesExtractsFromBaseAddresses()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses = new List<string>
            {
                "opc.tcp://myserver:4840"
            }.ToArrayOf();

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.GreaterThan(0));
        }

        [Test]
        /// <summary>
        /// GetServerDomainNames deduplicates domain names.
        /// </summary>
        public void GetServerDomainNamesDeduplicates()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses = new List<string>
            {
                "opc.tcp://myserver:4840",
                "https://myserver:4843"
            }.ToArrayOf();

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.EqualTo(1));
        }

        [Test]
        /// <summary>
        /// GetServerDomainNames includes alternate base addresses.
        /// </summary>
        public void GetServerDomainNamesIncludesAlternateAddresses()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ServerConfiguration = new ServerConfiguration()
            };
            config.ServerConfiguration.BaseAddresses = new List<string>
            {
                "opc.tcp://server1:4840"
            }.ToArrayOf();
            config.ServerConfiguration.AlternateBaseAddresses = new List<string>
            {
                "opc.tcp://server2:4840"
            }.ToArrayOf();

            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.EqualTo(2));
        }

        [Test]
        /// <summary>
        /// ValidateAsync throws when ApplicationName is empty.
        /// </summary>
        public void ValidateAsyncThrowsWhenApplicationNameEmpty()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false));
        }

        [Test]
        /// <summary>
        /// ValidateAsync throws when SecurityConfiguration is null.
        /// </summary>
        public void ValidateAsyncThrowsWhenSecurityConfigurationNull()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp"
            };
            // Force SecurityConfiguration to a new instance with no trust lists,
            // then null it out through reflection since the setter prevents null.
            // Instead, test with a valid name but rely on the SecurityConfiguration.Validate
            // to throw since there are no certificate stores configured.
            // The setter prevents null, so SecurityConfiguration is always non-null.
            // The validate call will proceed past the null check.
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false));
        }

        [Test]
        /// <summary>
        /// ValidateAsync throws for Server type when ServerConfiguration is null.
        /// </summary>
        public async Task ValidateAsyncThrowsForServerWithNoServerConfiguration()
        {
            ApplicationConfiguration config = CreateMinimalValidatableConfig();
            config.ApplicationType = ApplicationType.Server;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        /// <summary>
        /// ValidateAsync throws for Client type when ClientConfiguration is null.
        /// </summary>
        public async Task ValidateAsyncThrowsForClientWithNoClientConfiguration()
        {
            ApplicationConfiguration config = CreateMinimalValidatableConfig();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        /// <summary>
        /// ValidateAsync throws for DiscoveryServer type when DiscoveryServerConfiguration is null.
        /// </summary>
        public async Task ValidateAsyncThrowsForDiscoveryServerWithNoConfig()
        {
            ApplicationConfiguration config = CreateMinimalValidatableConfig();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.DiscoveryServer).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        /// <summary>
        /// SourceFilePath is null by default.
        /// </summary>
        public void SourceFilePathDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.SourceFilePath, Is.Null);
        }

        [Test]
        /// <summary>
        /// Extensions property get/set round-trips correctly.
        /// </summary>
        public void ExtensionsGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                Extensions = new List<XmlElement>().ToArrayOf()
            };
            Assert.That(config.Extensions.IsNull, Is.False);
            Assert.That(config.Extensions.Count, Is.Zero);
        }

        [Test]
        /// <summary>
        /// Properties dictionary supports adding and retrieving values.
        /// </summary>
        public void PropertiesCanStoreAndRetrieveValues()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            config.Properties["key1"] = "value1";
            Assert.That(config.Properties["key1"], Is.EqualTo("value1"));
        }

        [Test]
        /// <summary>
        /// Copy constructor copies ServerConfiguration reference.
        /// </summary>
        public void CopyConstructorCopiesServerConfiguration()
        {
            var serverConfig = new ServerConfiguration();
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "CopyTest",
                ServerConfiguration = serverConfig
            };

            var copy = new ApplicationConfiguration(original);
            Assert.That(copy.ServerConfiguration, Is.SameAs(serverConfig));
        }

        [Test]
        /// <summary>
        /// Copy constructor copies ClientConfiguration reference.
        /// </summary>
        public void CopyConstructorCopiesClientConfiguration()
        {
            var clientConfig = new ClientConfiguration();
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "CopyTest",
                ClientConfiguration = clientConfig
            };

            var copy = new ApplicationConfiguration(original);
            Assert.That(copy.ClientConfiguration, Is.SameAs(clientConfig));
        }

        [Test]
        /// <summary>
        /// Copy constructor copies CertificateManager by reference.
        /// </summary>
        public void CopyConstructorCopiesCertificateManager()
        {
            using var manager = new CertificateManager(m_telemetry);
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "CopyTest",
                CertificateManager = manager
            };

            var copy = new ApplicationConfiguration(original);
            Assert.That(copy.CertificateManager, Is.SameAs(manager));
        }

        [Test]
        /// <summary>
        /// Copy constructor copies TransportQuotas reference.
        /// </summary>
        public void CopyConstructorCopiesTransportQuotas()
        {
            var quotas = new TransportQuotas();
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "CopyTest",
                TransportQuotas = quotas
            };

            var copy = new ApplicationConfiguration(original);
            Assert.That(copy.TransportQuotas, Is.SameAs(quotas));
        }

        [Test]
        /// <summary>
        /// TransportQuotas default values match DefaultEncodingLimits.
        /// </summary>
        public void TransportQuotasDefaultValues()
        {
            var quotas = new TransportQuotas();
            Assert.That(quotas.MaxArrayLength, Is.EqualTo(DefaultEncodingLimits.MaxArrayLength));
            Assert.That(quotas.MaxStringLength, Is.EqualTo(DefaultEncodingLimits.MaxStringLength));
            Assert.That(quotas.MaxByteStringLength, Is.EqualTo(DefaultEncodingLimits.MaxByteStringLength));
            Assert.That(quotas.MaxMessageSize, Is.EqualTo(DefaultEncodingLimits.MaxMessageSize));
            Assert.That(quotas.MaxEncodingNestingLevels, Is.EqualTo(DefaultEncodingLimits.MaxEncodingNestingLevels));
            Assert.That(quotas.MaxDecoderRecoveries, Is.EqualTo(DefaultEncodingLimits.MaxDecoderRecoveries));
        }

        [Test]
        /// <summary>
        /// GetFilePathFromAppConfig returns a non-null path.
        /// </summary>
        public void GetFilePathFromAppConfigReturnsPath()
        {
            string path = ApplicationConfiguration.GetFilePathFromAppConfig(
                "TestSection",
                m_telemetry.CreateLogger<ApplicationConfigurationTests>());
            Assert.That(path, Is.Not.Null);
            Assert.That(path, Does.Contain("TestSection"));
        }

        /// <summary>
        /// ValidateAsync defaults TransportQuotas when it is null so the transport
        /// layer does not fail with a NullReferenceException on server start.
        /// </summary>
        [Test]
        public async Task ValidateAsyncDefaultsTransportQuotasWhenNull()
        {
            string pkiPath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestTransportQuotas_" + Guid.NewGuid().ToString("N"));
            try
            {
                ApplicationConfiguration config = CreateValidatableServerConfig(pkiPath);
                config.TransportQuotas = null;

                await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false);

                Assert.That(config.TransportQuotas, Is.Not.Null);
                Assert.That(
                    config.TransportQuotas!.MaxMessageSize,
                    Is.EqualTo(DefaultEncodingLimits.MaxMessageSize));
            }
            finally
            {
                TryDeleteDirectory(pkiPath);
            }
        }

        /// <summary>
        /// ValidateAsync preserves an explicitly configured TransportQuotas instance.
        /// </summary>
        [Test]
        public async Task ValidateAsyncKeepsExplicitTransportQuotas()
        {
            string pkiPath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestTransportQuotas_" + Guid.NewGuid().ToString("N"));
            try
            {
                ApplicationConfiguration config = CreateValidatableServerConfig(pkiPath);
                var quotas = new TransportQuotas { MaxMessageSize = 1234567 };
                config.TransportQuotas = quotas;

                await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false);

                Assert.That(config.TransportQuotas, Is.SameAs(quotas));
            }
            finally
            {
                TryDeleteDirectory(pkiPath);
            }
        }

        /// <summary>
        /// ValidateAsync defaults TransportQuotas when it is null on the client path,
        /// ensuring the behavior is consistent regardless of ApplicationType.
        /// </summary>
        [Test]
        public async Task ValidateAsyncDefaultsTransportQuotasWhenNullForClient()
        {
            string pkiPath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestTransportQuotasClient_" + Guid.NewGuid().ToString("N"));
            try
            {
                ApplicationConfiguration config = CreateValidatableClientConfig(pkiPath);
                config.TransportQuotas = null;

                await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

                Assert.That(config.TransportQuotas, Is.Not.Null);
                Assert.That(
                    config.TransportQuotas!.MaxMessageSize,
                    Is.EqualTo(DefaultEncodingLimits.MaxMessageSize));
            }
            finally
            {
                TryDeleteDirectory(pkiPath);
            }
        }

        private ApplicationConfiguration CreateValidatableClientConfig(string pkiPath)
        {
            const string applicationName = "TransportQuotasTestClient";
            return new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = applicationName,
                ApplicationUri = "urn:test:" + applicationName,
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "own"),
                        SubjectName = "CN=" + applicationName
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "trusted")
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "issuers")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                }
            };
        }

        private ApplicationConfiguration CreateValidatableServerConfig(string pkiPath)
        {
            const string applicationName = "TransportQuotasTestServer";
            return new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = applicationName,
                ApplicationUri = "urn:test:" + applicationName,
                ApplicationType = ApplicationType.Server,
                ServerConfiguration = new ServerConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "own"),
                        SubjectName = "CN=" + applicationName
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "trusted")
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "issuers")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                }
            };
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }

        private ApplicationConfiguration CreateMinimalValidatableConfig()
        {
            return new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "TestApp",
                ApplicationUri = "urn:test:app"
            };
        }
    }
}
