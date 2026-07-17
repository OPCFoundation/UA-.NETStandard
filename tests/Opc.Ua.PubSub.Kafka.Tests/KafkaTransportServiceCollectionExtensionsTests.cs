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
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Kafka.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Verifies Kafka transport dependency injection registration and option binding.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka transport DI binding")]
    public sealed class KafkaTransportServiceCollectionExtensionsTests
    {
        [Test]
        public async Task AddKafkaTransportWithCallbackRegistersFactoriesAndOptionsAsync()
        {
            var services = new ServiceCollection();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddKafkaTransport(options =>
            {
                options.Endpoint = "kafka://broker.example.com:9092";
                options.ClientId = "callback-client";
                options.GroupId = "callback-group";
                options.Topics.Prefix = "callback";
                options.DeliveryGuarantee = KafkaQualityOfService.ExactlyOnce;
            }));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            KafkaConnectionOptions options = serviceProvider
                .GetRequiredService<IOptions<KafkaConnectionOptions>>()
                .Value;
            KafkaPubSubTransportFactory[] factories = [.. serviceProvider
                .GetServices<IPubSubTransportFactory>()
                .OfType<KafkaPubSubTransportFactory>()];

            Assert.That(options.ClientId, Is.EqualTo("callback-client"));
            Assert.That(options.GroupId, Is.EqualTo("callback-group"));
            Assert.That(options.Topics.Prefix, Is.EqualTo("callback"));
            Assert.That(options.DeliveryGuarantee, Is.EqualTo(KafkaQualityOfService.ExactlyOnce));
            Assert.That(factories, Has.Length.EqualTo(2));
            Assert.That(
                factories.Select(static f => f.TransportProfileUri),
                Is.EquivalentTo(
                [
                    KafkaProfiles.PubSubKafkaJsonTransport,
                    KafkaProfiles.PubSubKafkaUadpTransport
                ]));
            Assert.That(serviceProvider.GetRequiredService<IKafkaClientFactory>(), Is.Not.Null);
        }

        [Test]
        public async Task AddKafkaTransportWithConfigurationBindsDefaultSectionAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:PubSub:Kafka:Endpoint"] = "kafkas://broker.example.com:19092",
                    ["OpcUa:PubSub:Kafka:BootstrapServers"] = "broker.example.com:19092",
                    ["OpcUa:PubSub:Kafka:ClientId"] = "bound-client",
                    ["OpcUa:PubSub:Kafka:GroupId"] = "bound-group",
                    ["OpcUa:PubSub:Kafka:SecurityProtocol"] = "SaslSsl",
                    ["OpcUa:PubSub:Kafka:SaslMechanism"] = "Plain",
                    ["OpcUa:PubSub:Kafka:UserName"] = "alice",
                    ["OpcUa:PubSub:Kafka:PasswordSecretId"] = "InMemory:kafka-password",
                    ["OpcUa:PubSub:Kafka:AuthenticationProfileUri"] = "profile-uri",
                    ["OpcUa:PubSub:Kafka:ResourceUri"] = "resource-uri",
                    ["OpcUa:PubSub:Kafka:AllowCredentialsOverPlaintext"] = "true",
                    ["OpcUa:PubSub:Kafka:Topics:Prefix"] = "plant.a",
                    ["OpcUa:PubSub:Kafka:DeliveryGuarantee"] = "BestEffort",
                    ["OpcUa:PubSub:Kafka:AutoOffsetReset"] = "Earliest",
                    ["OpcUa:PubSub:Kafka:EnableAutoCommit"] = "false",
                    ["OpcUa:PubSub:Kafka:ConnectTimeout"] = "00:00:03",
                    ["OpcUa:PubSub:Kafka:MessageTimeout"] = "00:00:04",
                    ["OpcUa:PubSub:Kafka:MaxMessageSize"] = "2048",
                    ["OpcUa:PubSub:Kafka:Tls:UseTls"] = "true",
                    ["OpcUa:PubSub:Kafka:Tls:ValidateServerCertificate"] = "false",
                    ["OpcUa:PubSub:Kafka:Tls:CaCertificatePath"] = "ca.pem",
                    ["OpcUa:PubSub:Kafka:Tls:ClientCertificatePath"] = "client.pem",
                    ["OpcUa:PubSub:Kafka:Tls:ClientKeyPath"] = "client.key"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddKafkaTransport(configuration));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            KafkaConnectionOptions options = serviceProvider
                .GetRequiredService<IOptions<KafkaConnectionOptions>>()
                .Value;

            Assert.That(options.Endpoint, Is.EqualTo("kafkas://broker.example.com:19092"));
            Assert.That(options.BootstrapServers, Is.EqualTo("broker.example.com:19092"));
            Assert.That(options.ClientId, Is.EqualTo("bound-client"));
            Assert.That(options.GroupId, Is.EqualTo("bound-group"));
            Assert.That(options.SecurityProtocol, Is.EqualTo(KafkaSecurityProtocol.SaslSsl));
            Assert.That(options.SaslMechanism, Is.EqualTo(KafkaSaslMechanism.Plain));
            Assert.That(options.UserName, Is.EqualTo("alice"));
            Assert.That(options.PasswordSecretId, Is.EqualTo("InMemory:kafka-password"));
            Assert.That(options.AuthenticationProfileUri, Is.EqualTo("profile-uri"));
            Assert.That(options.ResourceUri, Is.EqualTo("resource-uri"));
            Assert.That(options.AllowCredentialsOverPlaintext, Is.True);
            Assert.That(options.Topics.Prefix, Is.EqualTo("plant.a"));
            Assert.That(options.DeliveryGuarantee, Is.EqualTo(KafkaQualityOfService.BestEffort));
            Assert.That(options.AutoOffsetReset, Is.EqualTo(KafkaAutoOffsetReset.Earliest));
            Assert.That(options.EnableAutoCommit, Is.False);
            Assert.That(options.ConnectTimeout, Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(options.MessageTimeout, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(options.MaxMessageSize, Is.EqualTo(2048));
            Assert.That(options.Tls, Is.Not.Null);
            Assert.That(options.Tls!.UseTls, Is.True);
            Assert.That(options.Tls.ValidateServerCertificate, Is.False);
            Assert.That(options.Tls.CaCertificatePath, Is.EqualTo("ca.pem"));
            Assert.That(options.Tls.ClientCertificatePath, Is.EqualTo("client.pem"));
            Assert.That(options.Tls.ClientKeyPath, Is.EqualTo("client.key"));
        }

        [Test]
        public async Task AddKafkaTransportWithExplicitSectionBindsOptionsAsync()
        {
            var services = new ServiceCollection();
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Endpoint"] = "kafka://broker.example.com:9092",
                    ["Custom:Topics:Prefix"] = "custom"
                })
                .Build();

            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddKafkaTransport(configuration.GetSection("Custom")));

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            KafkaConnectionOptions options = serviceProvider
                .GetRequiredService<IOptions<KafkaConnectionOptions>>()
                .Value;

            Assert.That(options.Endpoint, Is.EqualTo("kafka://broker.example.com:9092"));
            Assert.That(options.Topics.Prefix, Is.EqualTo("custom"));
        }

        [Test]
        public async Task AddKafkaTransportFactoriesParticipateInDecoratorPipelineAsync()
        {
            var decorator = new CountingTransportFactoryDecorator();
            var services = new ServiceCollection();
            services.AddSingleton<IPubSubTransportFactoryDecorator>(decorator);

            services.AddOpcUa().AddPubSub(pubsub => pubsub.AddKafkaTransport());

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IPubSubTransportFactory[] factories =
                [.. serviceProvider.GetServices<IPubSubTransportFactory>()];

            Assert.Multiple(() =>
            {
                Assert.That(factories, Has.Length.EqualTo(2));
                Assert.That(decorator.DecoratedCount, Is.EqualTo(2));
                Assert.That(factories.All(static f => f is KafkaPubSubTransportFactory), Is.True);
            });
        }

        [Test]
        public async Task AddKafkaPubSubRegistersPublisherSubscriberAndFactoriesAsync()
        {
            var services = new ServiceCollection();

            services.AddKafkaPubSub(options => options.ClientId = "one-shot-client");

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            KafkaConnectionOptions options = serviceProvider
                .GetRequiredService<IOptions<KafkaConnectionOptions>>()
                .Value;
            IPubSubTransportFactory[] factories =
                [.. serviceProvider.GetServices<IPubSubTransportFactory>()];

            Assert.Multiple(() =>
            {
                Assert.That(serviceProvider.GetService<IPubSubApplication>(), Is.Not.Null);
                Assert.That(factories.OfType<KafkaPubSubTransportFactory>().Count(), Is.EqualTo(2));
                Assert.That(options.ClientId, Is.EqualTo("one-shot-client"));
            });
        }

        private sealed class CountingTransportFactoryDecorator : IPubSubTransportFactoryDecorator
        {
            public int DecoratedCount { get; private set; }

            public IPubSubTransportFactory Decorate(
                IServiceProvider provider,
                IPubSubTransportFactory factory)
            {
                DecoratedCount++;
                return factory;
            }
        }
    }
}
