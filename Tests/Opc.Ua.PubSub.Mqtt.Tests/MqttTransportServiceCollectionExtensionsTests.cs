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
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    [TestFixture]
    [TestSpec("7.3.4.4", Summary = "MQTT transport DI binding")]
    public sealed class MqttTransportServiceCollectionExtensionsTests
    {
        private static readonly string[] s_expectedCipherSuites =
        [
            "TLS_AES_128_GCM_SHA256",
            "TLS_AES_256_GCM_SHA384"
        ];

        [Test]
        public async Task AddMqttTransport_IConfiguration_BindsOptionsAndRegistersBothFactoriesAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:Mqtt:Endpoint"] = "mqtts://broker.example.com:8883",
                    ["OpcUa:PubSub:Mqtt:ClientId"] = "bound-client",
                    ["OpcUa:PubSub:Mqtt:ProtocolVersion"] = "V311",
                    ["OpcUa:PubSub:Mqtt:CleanSession"] = "false",
                    ["OpcUa:PubSub:Mqtt:KeepAlivePeriod"] = "00:00:15",
                    ["OpcUa:PubSub:Mqtt:UserName"] = "alice",
                    ["OpcUa:PubSub:Mqtt:PasswordSecretId"] = "InMemory:mqtt-password",
                    ["OpcUa:PubSub:Mqtt:ConnectTimeout"] = "00:00:05",
                    ["OpcUa:PubSub:Mqtt:MaxConcurrentSubscriptions"] = "17",
                    ["OpcUa:PubSub:Mqtt:MaxNetworkMessageSize"] = "12345",
                    ["OpcUa:PubSub:Mqtt:Tls:UseTls"] = "true",
                    ["OpcUa:PubSub:Mqtt:Tls:ValidateServerCertificate"] = "false",
                    ["OpcUa:PubSub:Mqtt:Tls:ClientCertificateSubject"] = "CN=pubsub-client",
                    ["OpcUa:PubSub:Mqtt:Tls:AllowedCipherSuites:0"] = "TLS_AES_128_GCM_SHA256",
                    ["OpcUa:PubSub:Mqtt:Tls:AllowedCipherSuites:1"] = "TLS_AES_256_GCM_SHA384",
                    ["OpcUa:PubSub:Mqtt:Topics:Prefix"] = "corp/site-a",
                    ["OpcUa:PubSub:Mqtt:Topics:RetainMetaDataMessages"] = "false",
                    ["OpcUa:PubSub:Mqtt:Topics:RetainDiscoveryMessages"] = "true",
                    ["OpcUa:PubSub:Mqtt:Topics:DefaultQos"] = "ExactlyOnce"
                })
                .Build();

            services.AddOpcUa().AddMqttTransport(configuration);

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            MqttConnectionOptions options =
                serviceProvider.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;
            MqttPubSubTransportFactory[] factories = serviceProvider
                .GetServices<IPubSubTransportFactory>()
                .OfType<MqttPubSubTransportFactory>()
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(options.Endpoint, Is.EqualTo("mqtts://broker.example.com:8883"));
                Assert.That(options.ClientId, Is.EqualTo("bound-client"));
                Assert.That(options.ProtocolVersion, Is.EqualTo(MqttProtocolVersion.V311));
                Assert.That(options.CleanSession, Is.False);
                Assert.That(options.KeepAlivePeriod, Is.EqualTo(TimeSpan.FromSeconds(15)));
                Assert.That(options.UserName, Is.EqualTo("alice"));
                Assert.That(options.PasswordSecretId, Is.EqualTo("InMemory:mqtt-password"));
                Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(options.MaxConcurrentSubscriptions, Is.EqualTo(17));
                Assert.That(options.MaxNetworkMessageSize, Is.EqualTo(12345));
                Assert.That(options.Tls, Is.Not.Null);
                Assert.That(options.Tls!.UseTls, Is.True);
                Assert.That(options.Tls.ValidateServerCertificate, Is.False);
                Assert.That(options.Tls.ClientCertificateSubject, Is.EqualTo("CN=pubsub-client"));
                Assert.That(options.Tls.AllowedCipherSuites, Is.EquivalentTo(s_expectedCipherSuites));
                Assert.That(options.Topics.Prefix, Is.EqualTo("corp/site-a"));
                Assert.That(options.Topics.RetainMetaDataMessages, Is.False);
                Assert.That(options.Topics.RetainDiscoveryMessages, Is.True);
                Assert.That(options.Topics.DefaultQos, Is.EqualTo(MqttQualityOfService.ExactlyOnce));
                Assert.That(factories, Has.Length.EqualTo(2));
                Assert.That(
                    factories.Select(static f => f.TransportProfileUri),
                    Is.EquivalentTo(new[]
                    {
                        Profiles.PubSubMqttJsonTransport,
                        Profiles.PubSubMqttUadpTransport
                    }));
            });
        }

        [Test]
        public async Task AddMqttTransport_IConfigurationSection_BindsExplicitSectionAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Endpoint"] = "mqtt://broker.example.com:1883",
                    ["Custom:Topics:Prefix"] = "custom/topic"
                })
                .Build();

            services.AddOpcUa().AddMqttTransport(configuration.GetSection("Custom"));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            MqttConnectionOptions options =
                serviceProvider.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.Endpoint, Is.EqualTo("mqtt://broker.example.com:1883"));
                Assert.That(options.Topics.Prefix, Is.EqualTo("custom/topic"));
            });
        }
    }
}
