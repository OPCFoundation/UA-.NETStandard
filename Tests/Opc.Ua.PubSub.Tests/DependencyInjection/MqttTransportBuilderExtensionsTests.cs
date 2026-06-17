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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.Transports;

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
        [Test]
        public void AddMqttTransportRegistersBothFactories()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
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
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddMqttTransport(o => o.Endpoint = "mqtt://test-broker");
            ServiceProvider sp = services.BuildServiceProvider();
            MqttConnectionOptions options =
                sp.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;
            Assert.That(options.Endpoint, Is.EqualTo("mqtt://test-broker"));
        }

        [Test]
        public void AddMqttTransportNullBuilderThrows()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddMqttTransport(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullBuilderIConfigurationOverloadThrows()
        {
            IOpcUaBuilder? builder = null;
            IConfiguration cfg = new ConfigurationBuilder().Build();
            Assert.That(
                () => builder!.AddMqttTransport(cfg),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullBuilderIConfigurationSectionOverloadThrows()
        {
            IOpcUaBuilder? builder = null;
            IConfigurationSection section = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build()
                .GetSection("X");
            Assert.That(
                () => builder!.AddMqttTransport(section),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullConfigurationThrows()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddMqttTransport(configuration: null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddMqttTransportNullSectionThrows()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
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

            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
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

            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddMqttTransport(section);

            ServiceProvider sp = services.BuildServiceProvider();
            MqttConnectionOptions options =
                sp.GetRequiredService<IOptions<MqttConnectionOptions>>().Value;
            Assert.That(options.Endpoint, Is.EqualTo("mqtts://broker:8883"));
        }
    }
}
