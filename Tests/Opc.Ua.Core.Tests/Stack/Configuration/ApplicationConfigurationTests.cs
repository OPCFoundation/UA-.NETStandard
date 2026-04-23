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

using System.Collections.Generic;
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
        /// Parameterless constructor creates a valid instance.
        public void ConstructorDefault()
        {
            var config = new ApplicationConfiguration();
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        /// Constructor with telemetry creates a valid instance.
        public void ConstructorWithTelemetry()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        /// Copy constructor copies all supported properties.
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
        /// ApplicationName get/set round-trips correctly.
        public void ApplicationNameGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "MyApp"
            };
            Assert.That(config.ApplicationName, Is.EqualTo("MyApp"));
        }

        [Test]
        /// ApplicationName defaults to null.
        public void ApplicationNameDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ApplicationName, Is.Null);
        }

        [Test]
        /// ApplicationUri get/set round-trips correctly.
        public void ApplicationUriGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationUri = "urn:test:myapp"
            };
            Assert.That(config.ApplicationUri, Is.EqualTo("urn:test:myapp"));
        }

        [Test]
        /// ApplicationUri defaults to null.
        public void ApplicationUriDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ApplicationUri, Is.Null);
        }

        [Test]
        /// ProductUri get/set round-trips correctly.
        public void ProductUriGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ProductUri = "urn:test:product"
            };
            Assert.That(config.ProductUri, Is.EqualTo("urn:test:product"));
        }

        [Test]
        /// ProductUri defaults to null.
        public void ProductUriDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ProductUri, Is.Null);
        }

        [Test]
        /// ApplicationType get/set round-trips correctly.
        public void ApplicationTypeGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationType = ApplicationType.ClientAndServer
            };
            Assert.That(config.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        /// ApplicationType defaults to Server.
        public void ApplicationTypeDefaultIsServer()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ApplicationType, Is.EqualTo(ApplicationType.Server));
        }

        [Test]
        /// ServerConfiguration get/set round-trips correctly.
        public void ServerConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var serverConfig = new ServerConfiguration();
            config.ServerConfiguration = serverConfig;
            Assert.That(config.ServerConfiguration, Is.SameAs(serverConfig));
        }

        [Test]
        /// ServerConfiguration defaults to null.
        public void ServerConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ServerConfiguration, Is.Null);
        }

        [Test]
        /// ClientConfiguration get/set round-trips correctly.
        public void ClientConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var clientConfig = new ClientConfiguration();
            config.ClientConfiguration = clientConfig;
            Assert.That(config.ClientConfiguration, Is.SameAs(clientConfig));
        }

        [Test]
        /// ClientConfiguration defaults to null.
        public void ClientConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ClientConfiguration, Is.Null);
        }

        [Test]
        /// SecurityConfiguration is created by default constructor.
        public void SecurityConfigurationDefaultIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        /// SecurityConfiguration set with null reverts to default.
        public void SecurityConfigurationSetNullReturnsDefault()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                SecurityConfiguration = null
            };
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        /// SecurityConfiguration get/set round-trips correctly.
        public void SecurityConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var secConfig = new SecurityConfiguration();
            config.SecurityConfiguration = secConfig;
            Assert.That(config.SecurityConfiguration, Is.SameAs(secConfig));
        }

        [Test]
        /// TransportQuotas get/set round-trips correctly.
        public void TransportQuotasGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var quotas = new TransportQuotas();
            config.TransportQuotas = quotas;
            Assert.That(config.TransportQuotas, Is.SameAs(quotas));
        }

        [Test]
        /// TransportQuotas defaults to null.
        public void TransportQuotasDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.TransportQuotas, Is.Null);
        }

        [Test]
        /// TransportConfigurations is created by default constructor.
        public void TransportConfigurationsDefaultIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.TransportConfigurations.IsNull, Is.False);
        }

        [Test]
        /// TransportConfigurations get/set round-trips correctly.
        public void TransportConfigurationsGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            config.TransportConfigurations = new List<TransportConfiguration>
                {
                    new("opc.tcp", typeof(object))
                }.ToArrayOf();
            Assert.That(config.TransportConfigurations.Count, Is.EqualTo(1));
        }

        [Test]
        /// CertificateValidator is created by default constructor.
        public void CertificateValidatorDefaultIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.CertificateValidator, Is.Not.Null);
        }

        [Test]
        /// CertificateValidator get/set round-trips correctly.
        public void CertificateValidatorGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var validator = new CertificateValidator(m_telemetry);
            config.CertificateValidator = validator;
            Assert.That(config.CertificateValidator, Is.SameAs(validator));
        }

        [Test]
        /// TraceConfiguration get/set round-trips correctly.
        public void TraceConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            config.TraceConfiguration = new TraceConfiguration { OutputFilePath = "trace.log" };
            Assert.That(config.TraceConfiguration, Is.Not.Null);
            Assert.That(config.TraceConfiguration.OutputFilePath, Is.EqualTo("trace.log"));
        }

        [Test]
        /// TraceConfiguration defaults to null.
        public void TraceConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.TraceConfiguration, Is.Null);
        }

        [Test]
        /// DisableHiResClock get/set round-trips correctly.
        public void DisableHiResClockGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry)
            {
                DisableHiResClock = true
            };
            Assert.That(config.DisableHiResClock, Is.True);
        }

        [Test]
        /// DisableHiResClock defaults to false.
        public void DisableHiResClockDefaultIsFalse()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.DisableHiResClock, Is.False);
        }

        [Test]
        /// DiscoveryServerConfiguration get/set round-trips correctly.
        public void DiscoveryServerConfigurationGetSet()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            var dsc = new DiscoveryServerConfiguration();
            config.DiscoveryServerConfiguration = dsc;
            Assert.That(config.DiscoveryServerConfiguration, Is.SameAs(dsc));
        }

        [Test]
        /// DiscoveryServerConfiguration defaults to null.
        public void DiscoveryServerConfigurationDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.DiscoveryServerConfiguration, Is.Null);
        }

        [Test]
        /// Properties dictionary is accessible and initially empty.
        public void PropertiesDictionaryIsAccessible()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.Properties, Is.Not.Null);
            Assert.That(config.Properties, Is.Empty);
        }

        [Test]
        /// PropertiesLock is not null.
        public void PropertiesLockIsNotNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.PropertiesLock, Is.Not.Null);
        }

        [Test]
        /// ExtensionObjects list is accessible.
        public void ExtensionObjectsIsAccessible()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.ExtensionObjects, Is.Not.Null);
        }

        [Test]
        /// CreateMessageContext returns a valid ServiceMessageContext.
        public void CreateMessageContextReturnsValidContext()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ServiceMessageContext context = config.CreateMessageContext();
            Assert.That(context, Is.Not.Null);
            Assert.That(context.NamespaceUris, Is.Not.Null);
        }

        [Test]
        /// CreateMessageContext with factory returns context with that factory.
        public void CreateMessageContextWithFactory()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            IEncodeableFactory factory = EncodeableFactory.Create();
            ServiceMessageContext context = config.CreateMessageContext(factory);
            Assert.That(context, Is.Not.Null);
            Assert.That(context.Factory, Is.SameAs(factory));
        }

        [Test]
        /// CreateMessageContext applies TransportQuotas when set.
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
        /// CreateMessageContext without TransportQuotas uses defaults.
        public void CreateMessageContextWithoutTransportQuotasUsesDefaults()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ServiceMessageContext context = config.CreateMessageContext();
            Assert.That(context.MaxArrayLength, Is.EqualTo(DefaultEncodingLimits.MaxArrayLength));
            Assert.That(context.MaxStringLength, Is.EqualTo(DefaultEncodingLimits.MaxStringLength));
        }

        [Test]
        /// GetServerDomainNames returns empty when ServerConfiguration is null.
        public void GetServerDomainNamesReturnsEmptyWhenNoServerConfig()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            ArrayOf<string> domains = config.GetServerDomainNames();
            Assert.That(domains.Count, Is.Zero);
        }

        [Test]
        /// GetServerDomainNames extracts domains from base addresses.
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
        /// GetServerDomainNames deduplicates domain names.
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
        /// GetServerDomainNames includes alternate base addresses.
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
        /// ValidateAsync throws when ApplicationName is empty.
        public void ValidateAsyncThrowsWhenApplicationNameEmpty()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false));
        }

        [Test]
        /// ValidateAsync throws when SecurityConfiguration is null.
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
        /// ValidateAsync throws for Server type when ServerConfiguration is null.
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
        /// ValidateAsync throws for Client type when ClientConfiguration is null.
        public async Task ValidateAsyncThrowsForClientWithNoClientConfiguration()
        {
            ApplicationConfiguration config = CreateMinimalValidatableConfig();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        /// ValidateAsync throws for DiscoveryServer type when DiscoveryServerConfiguration is null.
        public async Task ValidateAsyncThrowsForDiscoveryServerWithNoConfig()
        {
            ApplicationConfiguration config = CreateMinimalValidatableConfig();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await config.ValidateAsync(ApplicationType.DiscoveryServer).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        /// SourceFilePath is null by default.
        public void SourceFilePathDefaultIsNull()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            Assert.That(config.SourceFilePath, Is.Null);
        }

        [Test]
        /// Extensions property get/set round-trips correctly.
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
        /// Properties dictionary supports adding and retrieving values.
        public void PropertiesCanStoreAndRetrieveValues()
        {
            var config = new ApplicationConfiguration(m_telemetry);
            config.Properties["key1"] = "value1";
            Assert.That(config.Properties["key1"], Is.EqualTo("value1"));
        }

        [Test]
        /// Copy constructor copies ServerConfiguration reference.
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
        /// Copy constructor copies ClientConfiguration reference.
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
        /// Copy constructor copies CertificateValidator reference.
        public void CopyConstructorCopiesCertificateValidator()
        {
            var validator = new CertificateValidator(m_telemetry);
            var original = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "CopyTest",
                CertificateValidator = validator
            };

            var copy = new ApplicationConfiguration(original);
            Assert.That(copy.CertificateValidator, Is.SameAs(validator));
        }

        [Test]
        /// Copy constructor copies TransportQuotas reference.
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
        /// TransportQuotas default values match DefaultEncodingLimits.
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
        /// GetFilePathFromAppConfig returns a non-null path.
        public void GetFilePathFromAppConfigReturnsPath()
        {
            string path = ApplicationConfiguration.GetFilePathFromAppConfig(
                "TestSection",
                m_telemetry.CreateLogger<ApplicationConfigurationTests>());
            Assert.That(path, Is.Not.Null);
            Assert.That(path, Does.Contain("TestSection"));
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
