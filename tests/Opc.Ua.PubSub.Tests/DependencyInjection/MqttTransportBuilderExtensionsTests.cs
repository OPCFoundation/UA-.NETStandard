// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for
    /// <see cref="MqttTransportServiceCollectionExtensions"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.3.4", Summary = "MQTT broker transport DI registration")]
    public class MqttTransportBuilderExtensionsTests
    {
        private static (IPubSubBuilder Builder, ServiceCollection Services) CreatePubSubBuilder()
        {
            var services = new ServiceCollection();
            services.AddSingleton(NUnitTelemetryContext.Create());
            IPubSubBuilder captured = null!;
            services.AddOpcUa().AddPubSub(pubsub => captured = pubsub);
            return (captured, services);
        }

        [Test]
        public void AddMqttTransportRegistersBothFactories()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddMqttTransport();
            ServiceProvider sp = services.BuildServiceProvider();
            MqttPubSubTransportFactory[] mqttFactories =
                [.. sp.GetServices<IPubSubTransportFactory>().OfType<MqttPubSubTransportFactory>()];
            // Both Json and UADP MQTT profiles registered.
            Assert.That(mqttFactories, Has.Length.EqualTo(2));
            Assert.That(
                mqttFactories.Any(f =>
                    f.TransportProfileUri == Profiles.PubSubMqttJsonTransport),
                Is.True);
            Assert.That(
                mqttFactories.Any(f =>
                    f.TransportProfileUri == Profiles.PubSubMqttUadpTransport),
                Is.True);
        }

        [Test]
        public void AddMqttTransportBindsOptions()
        {
            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddMqttTransport(o => o.Endpoint = "mqtt://test-broker");
            ServiceProvider sp = services.BuildServiceProvider();
            MqttConnectionOptions options =
                sp.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;
            Assert.That(options.Endpoint, Is.EqualTo("mqtt://test-broker"));
        }

        [Test]
        public void AddMqttTransportNullBuilderThrows()
        {
            IPubSubBuilder? builder = null;
            Assert.That(
                () => builder!.AddMqttTransport(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullBuilderIConfigurationOverloadThrows()
        {
            IPubSubBuilder? builder = null;
            IConfiguration cfg = new ConfigurationBuilder().Build();
            Assert.That(
                () => builder!.AddMqttTransport(cfg),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullBuilderIConfigurationSectionOverloadThrows()
        {
            IPubSubBuilder? builder = null;
            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection([])
                .Build()
                .GetSection("X");
            Assert.That(
                () => builder!.AddMqttTransport(section),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullConfigurationThrows()
        {
            (IPubSubBuilder builder, _) = CreatePubSubBuilder();
            Assert.That(
                () => builder.AddMqttTransport(configuration: null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullSectionThrows()
        {
            (IPubSubBuilder builder, _) = CreatePubSubBuilder();
            Assert.That(
                () => builder.AddMqttTransport(section: null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportFromIConfigurationBindsDefaultSection()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{MqttTransportServiceCollectionExtensions.DefaultConfigurationSection}:Endpoint"] = "mqtt://b"
                })
                .Build();

            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddMqttTransport(configuration);

            ServiceProvider sp = services.BuildServiceProvider();
            MqttConnectionOptions options =
                sp.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;
            Assert.That(options.Endpoint, Is.EqualTo("mqtt://b"));
        }

        [Test]
        public void AddMqttTransportFromSectionBindsValues()
        {
            IConfigurationRoot root = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MyMqtt:Endpoint"] = "mqtts://broker:8883"
                })
                .Build();
            IConfigurationSection section = root.GetSection("MyMqtt");

            (IPubSubBuilder builder, ServiceCollection services) = CreatePubSubBuilder();
            builder.AddMqttTransport(section);

            ServiceProvider sp = services.BuildServiceProvider();
            MqttConnectionOptions options =
                sp.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;
            Assert.That(options.Endpoint, Is.EqualTo("mqtts://broker:8883"));
        }
    }
}
