// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Verifies <see cref="MqttConnectionOptions"/> defaults,
    /// <c>IConfiguration</c> binding, and the security guarantee that
    /// no plain-text <c>Password</c> field is present.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.4")]
    public sealed class MqttConnectionOptionsTests
    {
        [Test]
        public void Defaults_MatchSpecGuidance()
        {
            var options = new MqttConnectionOptions();

            Assert.That(options.Endpoint, Is.EqualTo(string.Empty));
            Assert.That(options.ClientId, Is.Null);
            Assert.That(options.ProtocolVersion, Is.EqualTo(MqttProtocolVersion.V500));
            Assert.That(options.CleanSession, Is.True);
            Assert.That(options.KeepAlivePeriod, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.UserName, Is.Null);
            Assert.That(options.PasswordSecretId, Is.Null);
            Assert.That(options.Tls, Is.Null);
            Assert.That(options.AllowCredentialsOverPlaintext, Is.False);
            Assert.That(options.Topics, Is.Not.Null);
            Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.MaxConcurrentSubscriptions, Is.EqualTo(64));
        }

        [Test]
        public void TopicOptions_DefaultsMatchPart14()
        {
            var topics = new MqttTopicOptions();
            Assert.That(topics.Prefix, Is.EqualTo("opcua"));
            Assert.That(topics.RetainMetaDataMessages, Is.True);
            Assert.That(topics.RetainDiscoveryMessages, Is.True);
            Assert.That(topics.DefaultQos, Is.EqualTo(MqttQualityOfService.AtLeastOnce));
        }

        [Test]
        public void IConfiguration_Binding_PopulatesScalarProperties()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Endpoint"] = "mqtts://broker.example.com:8883",
                    ["ClientId"] = "Publisher1",
                    ["ProtocolVersion"] = "V311",
                    ["CleanSession"] = "false",
                    ["KeepAlivePeriod"] = "00:00:45",
                    ["UserName"] = "alice",
                    ["PasswordSecretId"] = "Default:mqtt-password",
                    ["AllowCredentialsOverPlaintext"] = "true",
                    ["ConnectTimeout"] = "00:00:05",
                    ["MaxConcurrentSubscriptions"] = "16",
                    ["Topics:Prefix"] = "custom/pubsub",
                    ["Topics:DefaultQos"] = "ExactlyOnce",
                    ["Topics:RetainMetaDataMessages"] = "false"
                })
                .Build();

            var options = new MqttConnectionOptions();
            configuration.Bind(options);

            Assert.That(options.Endpoint, Is.EqualTo("mqtts://broker.example.com:8883"));
            Assert.That(options.ClientId, Is.EqualTo("Publisher1"));
            Assert.That(options.ProtocolVersion, Is.EqualTo(MqttProtocolVersion.V311));
            Assert.That(options.CleanSession, Is.False);
            Assert.That(options.KeepAlivePeriod, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(options.UserName, Is.EqualTo("alice"));
            Assert.That(options.PasswordSecretId, Is.EqualTo("Default:mqtt-password"));
            Assert.That(options.AllowCredentialsOverPlaintext, Is.True);
            Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(options.MaxConcurrentSubscriptions, Is.EqualTo(16));
            Assert.That(options.Topics.Prefix, Is.EqualTo("custom/pubsub"));
            Assert.That(options.Topics.DefaultQos, Is.EqualTo(MqttQualityOfService.ExactlyOnce));
            Assert.That(options.Topics.RetainMetaDataMessages, Is.False);
        }

        [Test]
        public void OptionsType_DoesNotExposePlainPasswordProperty()
        {
            PropertyInfo[] properties = typeof(MqttConnectionOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);
            IEnumerable<string> propertyNames = properties.Select(p => p.Name);

            Assert.That(
                propertyNames,
                Does.Not.Contain("Password"),
                "MqttConnectionOptions must not expose a plain-text 'Password' field; " +
                "use PasswordSecretId and ISecretRegistry instead.");
            Assert.That(propertyNames, Does.Contain("PasswordSecretId"));
        }

        [Test]
        public void TlsOptions_DefaultsAreSecure()
        {
            var tls = new MqttTlsOptions();
            Assert.That(tls.UseTls, Is.False);
            Assert.That(tls.ValidateServerCertificate, Is.True);
            Assert.That(tls.ClientCertificateSubject, Is.Null);
            Assert.That(tls.AllowedCipherSuites, Is.Null);
        }
    }
}
