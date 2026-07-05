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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Tests for promoting DataSet fields into MQTT 5.0 User Properties
    /// through the <see cref="IPubSubHeaderTransport"/> capability on
    /// <see cref="MqttBrokerTransport"/>.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.4")]
    [CancelAfter(10000)]
    public sealed class MqttHeaderTransportTests
    {
        private static PubSubConnectionDataType NewConnection()
        {
            var conn = new PubSubConnectionDataType
            {
                Name = "Conn",
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://broker.example.com:1883"
                })
            };
            conn.WriterGroups = conn.WriterGroups.AddItem(new WriterGroupDataType
            {
                Name = "WG1",
                MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType())
            });
            return conn;
        }

        private static MqttBrokerTransport NewTransport(FakeMqttClientFactory factory)
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            return new MqttBrokerTransport(
                NewConnection(),
                endpoint,
                PubSubTransportDirection.Send,
                new MqttConnectionOptions { Endpoint = "mqtt://broker.example.com:1883" },
                factory,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        [Test]
        public async Task MqttBrokerTransport_ImplementsHeaderCapability()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            Assert.That(transport, Is.InstanceOf<IPubSubHeaderTransport>());
        }

        [Test]
        public async Task SendAsync_WithProperties_EmitsUserProperties()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            PubSubMessageProperty[] properties =
            [
                new("Temperature", "21.5"),
                new("Unit", "C")
            ];

            await transport.SendAsync(new byte[] { 1, 2, 3 }, "data/x", properties)
                .ConfigureAwait(false);

            MqttMessage message = factory.Adapter.PublishedMessages.Single();
            Assert.That(message.UserProperties, Is.Not.Null);
            Assert.That(message.UserProperties!, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(message.UserProperties![0].Key, Is.EqualTo("Temperature"));
                Assert.That(message.UserProperties![0].Value, Is.EqualTo("21.5"));
                Assert.That(message.UserProperties![1].Key, Is.EqualTo("Unit"));
                Assert.That(message.UserProperties![1].Value, Is.EqualTo("C"));
            });
        }

        [Test]
        public async Task SendAsync_NoProperties_EmitsNoUserProperties()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport
                .SendAsync(new byte[] { 1 }, "data/x", default(ArrayOf<PubSubMessageProperty>))
                .ConfigureAwait(false);

            MqttMessage message = factory.Adapter.PublishedMessages.Single();
            Assert.That(message.UserProperties, Is.Null);
        }

        [Test]
        public async Task PlainSendAsync_EmitsNoUserProperties()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport.SendAsync(new byte[] { 1 }, "data/x").ConfigureAwait(false);

            MqttMessage message = factory.Adapter.PublishedMessages.Single();
            Assert.That(message.UserProperties, Is.Null);
        }
    }
}
