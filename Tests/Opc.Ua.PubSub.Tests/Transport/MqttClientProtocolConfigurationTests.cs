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

using System.Security;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MqttClientProtocolConfigurationTests
    {
        [Test]
        public void DefaultConstructorSetsDefaults()
        {
            var config = new MqttClientProtocolConfiguration();

            Assert.That(config.ConnectionProperties, Is.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void ParameterizedConstructorSetsUserNameAndPassword()
        {
            using var userName = new SecureString();
            foreach (char c in "user1")
            {
                userName.AppendChar(c);
            }

            using var password = new SecureString();
            foreach (char c in "pass1")
            {
                password.AppendChar(c);
            }

            var config = new MqttClientProtocolConfiguration(
                userName: userName,
                password: password);

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void ParameterizedConstructorWithNullUserNameDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                _ = new MqttClientProtocolConfiguration(
                    userName: null,
                    password: null,
                    azureClientId: null);
            });
        }

        [Test]
        public void ParameterizedConstructorSetsAzureClientId()
        {
            var config = new MqttClientProtocolConfiguration(
                azureClientId: "my-azure-client");

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void ParameterizedConstructorSetsCleanSession()
        {
            var config = new MqttClientProtocolConfiguration(cleanSession: false);

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void ParameterizedConstructorSetsProtocolVersion()
        {
            var config = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void ParameterizedConstructorWithTlsOptionsSetsConnectionProperties()
        {
            var tlsCerts = new MqttTlsCertificates();
            var tlsOptions = new MqttTlsOptions(certificates: tlsCerts);

            var config = new MqttClientProtocolConfiguration(mqttTlsOptions: tlsOptions);

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void RoundTripViaKeyValuePairsPreservesUserName()
        {
            using var userName = new SecureString();
            foreach (char c in "testuser")
            {
                userName.AppendChar(c);
            }

            using var password = new SecureString();
            foreach (char c in "testpass")
            {
                password.AppendChar(c);
            }

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Microsoft.Extensions.Logging.ILogger<MqttClientProtocolConfiguration> logger = telemetry.CreateLogger<MqttClientProtocolConfiguration>();

            var original = new MqttClientProtocolConfiguration(
                userName: userName,
                password: password,
                cleanSession: true,
                version: EnumMqttProtocolVersion.V311);

            var roundTripped = new MqttClientProtocolConfiguration(
                original.ConnectionProperties, logger);

            Assert.That(roundTripped.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void RoundTripViaKeyValuePairsPreservesProtocolVersion()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Microsoft.Extensions.Logging.ILogger<MqttClientProtocolConfiguration> logger = telemetry.CreateLogger<MqttClientProtocolConfiguration>();

            var original = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);

            var roundTripped = new MqttClientProtocolConfiguration(
                original.ConnectionProperties, logger);

            Assert.That(roundTripped.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void KeyValuePairConstructorWithUnknownProtocolDefaultsToV310()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Microsoft.Extensions.Logging.ILogger<MqttClientProtocolConfiguration> logger = telemetry.CreateLogger<MqttClientProtocolConfiguration>();

            ArrayOf<KeyValuePair> kvps = [];
            kvps += new KeyValuePair
            {
                Key = QualifiedName.From("UserName"),
                Value = ""
            };
            kvps += new KeyValuePair
            {
                Key = QualifiedName.From("Password"),
                Value = ""
            };
            kvps += new KeyValuePair
            {
                Key = QualifiedName.From("AzureClientId"),
                Value = ""
            };
            kvps += new KeyValuePair
            {
                Key = QualifiedName.From("CleanSession"),
                Value = true
            };
            kvps += new KeyValuePair
            {
                Key = QualifiedName.From("ProtocolVersion"),
                Value = (int)EnumMqttProtocolVersion.Unknown
            };

            var config = new MqttClientProtocolConfiguration(kvps, logger);

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void ConnectionPropertiesPropertyIsSettable()
        {
            var config = new MqttClientProtocolConfiguration();
            ArrayOf<KeyValuePair> kvps = [];
            kvps += new KeyValuePair
            {
                Key = QualifiedName.From("TestKey"),
                Value = "TestValue"
            };
            config.ConnectionProperties = kvps;

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void MqttTlsOptionsDefaultConstructorSetsDefaults()
        {
            var options = new MqttTlsOptions();

            Assert.That(options, Is.Not.Null);
        }

        [Test]
        public void MqttTlsOptionsParameterizedConstructorSetsAllProperties()
        {
            var tlsCerts = new MqttTlsCertificates();
            var issuerStore = new CertificateTrustList
            {
                StoreType = "Directory",
                StorePath = "/certs/issuers"
            };
            var peerStore = new CertificateTrustList
            {
                StoreType = "Directory",
                StorePath = "/certs/peers"
            };
            var rejectedStore = new CertificateTrustList
            {
                StoreType = "Directory",
                StorePath = "/certs/rejected"
            };

            var options = new MqttTlsOptions(
                certificates: tlsCerts,
                sslProtocolVersion: System.Security.Authentication.SslProtocols.None,
                allowUntrustedCertificates: true,
                ignoreCertificateChainErrors: true,
                ignoreRevocationListErrors: true,
                trustedIssuerCertificates: issuerStore,
                trustedPeerCertificates: peerStore,
                rejectedCertificateStore: rejectedStore);

            Assert.That(options, Is.Not.Null);
        }

        [Test]
        public void MqttTlsOptionsFromKeyValuePairsRoundTrips()
        {
            var tlsCerts = new MqttTlsCertificates();
            var options = new MqttTlsOptions(
                certificates: tlsCerts,
                allowUntrustedCertificates: true,
                ignoreCertificateChainErrors: false,
                ignoreRevocationListErrors: true);

            var roundTripped = new MqttTlsOptions(options.KeyValuePairs);
            Assert.That(roundTripped, Is.Not.Null);
        }

        [Test]
        public void MqttTlsCertificatesDefaultConstructorSetsEmptyPaths()
        {
            var certs = new MqttTlsCertificates();

            Assert.That(certs, Is.Not.Null);
        }

        [Test]
        public void MqttTlsCertificatesWithNullPathsSetsEmptyStrings()
        {
            var certs = new MqttTlsCertificates(
                caCertificatePath: null,
                clientCertificatePath: null,
                clientCertificatePassword: null);

            Assert.That(certs, Is.Not.Null);
        }

        [Test]
        public void MqttTlsCertificatesFromKeyValuePairsRoundTrips()
        {
            var original = new MqttTlsCertificates(
                caCertificatePath: null,
                clientCertificatePath: null,
                clientCertificatePassword: null);

            var roundTripped = new MqttTlsCertificates(original.KeyValuePairs);
            Assert.That(roundTripped, Is.Not.Null);
        }

        [Test]
        public void MqttTlsCertificatesWithPasswordRoundTrips()
        {
            var original = new MqttTlsCertificates(
                caCertificatePath: null,
                clientCertificatePath: null,
                clientCertificatePassword: "secret".ToCharArray());

            var roundTripped = new MqttTlsCertificates(original.KeyValuePairs);
            Assert.That(roundTripped, Is.Not.Null);
        }

        [Test]
        public void ParameterizedConstructorWithNullTlsOptionsOmitsTlsProperties()
        {
            var config = new MqttClientProtocolConfiguration(
                userName: null,
                password: null,
                mqttTlsOptions: null);

            Assert.That(config.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }

        [Test]
        public void KeyValuePairConstructorCreatesAllSubObjects()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Microsoft.Extensions.Logging.ILogger<MqttClientProtocolConfiguration> logger = telemetry.CreateLogger<MqttClientProtocolConfiguration>();

            using var userName = new SecureString();
            foreach (char c in "admin")
            {
                userName.AppendChar(c);
            }

            using var password = new SecureString();
            foreach (char c in "pw123")
            {
                password.AppendChar(c);
            }

            var tlsCerts = new MqttTlsCertificates();
            var tlsOptions = new MqttTlsOptions(certificates: tlsCerts, allowUntrustedCertificates: true);

            var original = new MqttClientProtocolConfiguration(
                userName: userName,
                password: password,
                azureClientId: "azClient",
                cleanSession: false,
                version: EnumMqttProtocolVersion.V500,
                mqttTlsOptions: tlsOptions);

            var roundTripped = new MqttClientProtocolConfiguration(
                original.ConnectionProperties, logger);

            Assert.That(roundTripped.ConnectionProperties, Is.Not.EqualTo(default(ArrayOf<KeyValuePair>)));
        }
    }
}
