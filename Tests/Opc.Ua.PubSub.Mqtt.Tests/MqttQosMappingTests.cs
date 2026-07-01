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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Asserts the MQTT QoS mapping from Part 14 §7.3.4.5: the
    /// stack's <see cref="MqttQualityOfService"/> values 0/1/2
    /// round-trip to the outbound <see cref="MqttMessage.Qos"/>
    /// without translation loss.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.5")]
    [CancelAfter(5000)]
    public sealed class MqttQosMappingTests
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
                MessageSettings = new ExtensionObject(
                    new JsonWriterGroupMessageDataType())
            });
            return conn;
        }

        private static MqttBrokerTransport NewTransport(
            FakeMqttClientFactory factory,
            MqttQualityOfService qos)
        {
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883",
                Topics = new MqttTopicOptions
                {
                    DefaultQos = qos
                }
            };
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            return new MqttBrokerTransport(
                NewConnection(),
                endpoint,
                PubSubTransportDirection.Send,
                options,
                factory,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        [TestCase(MqttQualityOfService.AtMostOnce, 0)]
        [TestCase(MqttQualityOfService.AtLeastOnce, 1)]
        [TestCase(MqttQualityOfService.ExactlyOnce, 2)]
        public async Task DefaultQos_PropagatesToOutboundMessage(
            MqttQualityOfService qos,
            int expectedNumericValue)
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory, qos);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            const string topic = "opcua/pubsub/json/data/1/2";
            await transport.SendAsync(new byte[] { 1 }, topic).ConfigureAwait(false);

            MqttMessage[] published = factory.Adapter.PublishedMessages.ToArray();
            Assert.That(published, Has.Length.EqualTo(1));
            Assert.That(published[0].Qos, Is.EqualTo(qos));
            Assert.That((int)published[0].Qos, Is.EqualTo(expectedNumericValue));
        }

        [Test]
        public void EnumValues_MatchPart14Encoding()
        {
            Assert.That((int)MqttQualityOfService.AtMostOnce, Is.Zero);
            Assert.That((int)MqttQualityOfService.AtLeastOnce, Is.EqualTo(1));
            Assert.That((int)MqttQualityOfService.ExactlyOnce, Is.EqualTo(2));
        }
    }
}
