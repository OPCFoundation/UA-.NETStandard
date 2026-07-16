/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Verifies Kafka connection option defaults and configuration binding.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka connection options")]
    public sealed class KafkaConnectionOptionsTests
    {
        [Test]
        public void DefaultsMatchSecureBrokerTransportDefaults()
        {
            var options = new KafkaConnectionOptions();

            Assert.That(options.Endpoint, Is.EqualTo(string.Empty));
            Assert.That(options.BootstrapServers, Is.EqualTo(string.Empty));
            Assert.That(options.ClientId, Is.Null);
            Assert.That(options.GroupId, Is.Null);
            Assert.That(options.SecurityProtocol, Is.EqualTo(KafkaSecurityProtocol.Plaintext));
            Assert.That(options.SaslMechanism, Is.EqualTo(KafkaSaslMechanism.None));
            Assert.That(options.UserName, Is.Null);
            Assert.That(options.PasswordSecretId, Is.Null);
            Assert.That(options.AuthenticationProfileUri, Is.Null);
            Assert.That(options.ResourceUri, Is.Null);
            Assert.That(options.AllowCredentialsOverPlaintext, Is.False);
            Assert.That(options.Tls, Is.Null);
            Assert.That(options.DeliveryGuarantee, Is.EqualTo(KafkaQualityOfService.AtLeastOnce));
            Assert.That(options.AutoOffsetReset, Is.EqualTo(KafkaAutoOffsetReset.Latest));
            Assert.That(options.EnableAutoCommit, Is.True);
            Assert.That(options.Topics.Prefix, Is.EqualTo("opcua"));
            Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.MessageTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.MaxMessageSize, Is.EqualTo(1048576));
        }

        [Test]
        public void ConfigurationBindingPopulatesScalarAndNestedProperties()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Endpoint"] = "kafkas://broker.example.com:19092",
                    ["BootstrapServers"] = "broker.example.com:19092",
                    ["ClientId"] = "pub-client",
                    ["GroupId"] = "sub-group",
                    ["SecurityProtocol"] = "SaslSsl",
                    ["SaslMechanism"] = "ScramSha512",
                    ["UserName"] = "alice",
                    ["PasswordSecretId"] = "InMemory:kafka-password",
                    ["AuthenticationProfileUri"] = "http://opcfoundation.org/UA/Security/UserToken/Server/Password",
                    ["ResourceUri"] = "kafka-resource",
                    ["AllowCredentialsOverPlaintext"] = "true",
                    ["DeliveryGuarantee"] = "ExactlyOnce",
                    ["AutoOffsetReset"] = "Earliest",
                    ["EnableAutoCommit"] = "false",
                    ["Topics:Prefix"] = "plant.a",
                    ["ConnectTimeout"] = "00:00:04",
                    ["MessageTimeout"] = "00:00:06",
                    ["MaxMessageSize"] = "4096",
                    ["Tls:UseTls"] = "true",
                    ["Tls:ValidateServerCertificate"] = "false",
                    ["Tls:CaCertificatePath"] = "certs/ca.pem",
                    ["Tls:ClientCertificatePath"] = "certs/client.pem",
                    ["Tls:ClientKeyPath"] = "certs/client.key"
                })
                .Build();
            var options = new KafkaConnectionOptions();

            configuration.Bind(options);

            Assert.That(options.Endpoint, Is.EqualTo("kafkas://broker.example.com:19092"));
            Assert.That(options.BootstrapServers, Is.EqualTo("broker.example.com:19092"));
            Assert.That(options.ClientId, Is.EqualTo("pub-client"));
            Assert.That(options.GroupId, Is.EqualTo("sub-group"));
            Assert.That(options.SecurityProtocol, Is.EqualTo(KafkaSecurityProtocol.SaslSsl));
            Assert.That(options.SaslMechanism, Is.EqualTo(KafkaSaslMechanism.ScramSha512));
            Assert.That(options.UserName, Is.EqualTo("alice"));
            Assert.That(options.PasswordSecretId, Is.EqualTo("InMemory:kafka-password"));
            Assert.That(options.AuthenticationProfileUri, Does.Contain("Password"));
            Assert.That(options.ResourceUri, Is.EqualTo("kafka-resource"));
            Assert.That(options.AllowCredentialsOverPlaintext, Is.True);
            Assert.That(options.DeliveryGuarantee, Is.EqualTo(KafkaQualityOfService.ExactlyOnce));
            Assert.That(options.AutoOffsetReset, Is.EqualTo(KafkaAutoOffsetReset.Earliest));
            Assert.That(options.EnableAutoCommit, Is.False);
            Assert.That(options.Topics.Prefix, Is.EqualTo("plant.a"));
            Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(options.MessageTimeout, Is.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(options.MaxMessageSize, Is.EqualTo(4096));
            Assert.That(options.Tls, Is.Not.Null);
            Assert.That(options.Tls!.UseTls, Is.True);
            Assert.That(options.Tls.ValidateServerCertificate, Is.False);
            Assert.That(options.Tls.CaCertificatePath, Is.EqualTo("certs/ca.pem"));
            Assert.That(options.Tls.ClientCertificatePath, Is.EqualTo("certs/client.pem"));
            Assert.That(options.Tls.ClientKeyPath, Is.EqualTo("certs/client.key"));
        }

        [Test]
        public void OptionsTypeDoesNotExposePlainPasswordProperty()
        {
            PropertyInfo[] properties = typeof(KafkaConnectionOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);
            IEnumerable<string> propertyNames = properties.Select(static p => p.Name);

            Assert.That(propertyNames, Does.Not.Contain("Password"));
            Assert.That(propertyNames, Does.Contain("PasswordSecretId"));
        }

        [Test]
        public void TlsOptionsDefaultsValidateBrokerCertificate()
        {
            var tls = new KafkaTlsOptions();

            Assert.That(tls.UseTls, Is.False);
            Assert.That(tls.ValidateServerCertificate, Is.True);
            Assert.That(tls.CaCertificatePath, Is.Null);
            Assert.That(tls.ClientCertificatePath, Is.Null);
            Assert.That(tls.ClientKeyPath, Is.Null);
        }
    }
}
