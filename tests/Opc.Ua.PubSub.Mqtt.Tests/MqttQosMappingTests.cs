// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
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

            MqttMessage[] published = [.. factory.Adapter.PublishedMessages];
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
