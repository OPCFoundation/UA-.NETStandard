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

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
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
    public class MqttTransportBuilderExtensionsTests
    {
        [Test]
        public void AddMqttTransport_RegistersBothFactories()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddMqttTransport();
            ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<IPubSubTransportFactory> factories =
                sp.GetServices<IPubSubTransportFactory>();
            IEnumerable<MqttPubSubTransportFactory> mqttFactories =
                factories.OfType<MqttPubSubTransportFactory>();
            // Both Json and UADP MQTT profiles registered.
            Assert.That(mqttFactories.Count(), Is.EqualTo(2));
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
        public void AddMqttTransport_BindsOptions()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddMqttTransport(o => o.Endpoint = "mqtt://test-broker");
            ServiceProvider sp = services.BuildServiceProvider();
            MqttConnectionOptions options =
                sp.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<MqttConnectionOptions>>().Value;
            Assert.That(options.Endpoint, Is.EqualTo("mqtt://test-broker"));
        }

        [Test]
        public void AddMqttTransport_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddMqttTransport(),
                Throws.ArgumentNullException);
        }
    }
}
