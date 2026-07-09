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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Verifies the metadata retain semantics required by
    /// Part 14 §7.3.4.8 — metadata messages published to a topic
    /// matching the <c>/metadata/</c> shape must carry the MQTT
    /// retain flag when
    /// <see cref="MqttTopicOptions.RetainMetaDataMessages"/> is
    /// enabled.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.8")]
    [CancelAfter(5000)]
    public sealed class MqttRetainedMetaDataTests
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
            MqttConnectionOptions options)
        {
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

        [Test]
        public async Task MetaDataTopic_RetainsByDefault()
        {
            var factory = new FakeMqttClientFactory();
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883"
            };

            await using MqttBrokerTransport transport = NewTransport(factory, options);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            string topic = MqttTopicBuilder.BuildMetaDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant((uint)42),
                writerGroupId: 1,
                dataSetWriterId: 3);
            await transport.SendAsync(new byte[] { 1, 2 }, topic).ConfigureAwait(false);

            MqttMessage[] msgs = [.. factory.Adapter.PublishedMessages];
            Assert.That(msgs, Has.Length.EqualTo(1));
            Assert.That(msgs[0].Topic, Does.Contain("/metadata/"));
            Assert.That(msgs[0].Retain, Is.True);
        }

        [Test]
        public async Task MetaDataTopic_RetainSuppressedWhenOptionDisabled()
        {
            var factory = new FakeMqttClientFactory();
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883",
                Topics = new MqttTopicOptions
                {
                    RetainMetaDataMessages = false
                }
            };

            await using MqttBrokerTransport transport = NewTransport(factory, options);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            string topic = MqttTopicBuilder.BuildMetaDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant((uint)42),
                writerGroupId: 1,
                dataSetWriterId: 3);
            await transport.SendAsync(new byte[] { 1, 2 }, topic).ConfigureAwait(false);

            MqttMessage[] msgs = [.. factory.Adapter.PublishedMessages];
            Assert.That(msgs, Has.Length.EqualTo(1));
            Assert.That(msgs[0].Retain, Is.False);
        }

        [Test]
        public async Task DataTopic_DoesNotRetain()
        {
            var factory = new FakeMqttClientFactory();
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883"
            };

            await using MqttBrokerTransport transport = NewTransport(factory, options);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            string topic = MqttTopicBuilder.BuildDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant((uint)42),
                writerGroupId: 1,
                dataSetWriterId: 3);
            await transport.SendAsync(new byte[] { 1, 2 }, topic).ConfigureAwait(false);

            MqttMessage[] msgs = [.. factory.Adapter.PublishedMessages];
            Assert.That(msgs, Has.Length.EqualTo(1));
            Assert.That(msgs[0].Topic, Does.Contain("/data/"));
            Assert.That(msgs[0].Retain, Is.False);
        }

        [Test]
        public async Task ContentType_MatchesJsonProfile()
        {
            var factory = new FakeMqttClientFactory();
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883"
            };

            await using MqttBrokerTransport transport = NewTransport(factory, options);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            string topic = MqttTopicBuilder.BuildDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant((uint)42),
                1,
                null);
            await transport.SendAsync(new byte[] { 1 }, topic).ConfigureAwait(false);

            MqttMessage[] msgs = [.. factory.Adapter.PublishedMessages];
            Assert.That(msgs[0].ContentType, Is.EqualTo("application/json"));
        }

        [Test]
        public async Task ContentType_MatchesUadpProfile()
        {
            var factory = new FakeMqttClientFactory();
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883"
            };
            var connection = new PubSubConnectionDataType
            {
                Name = "Conn",
                TransportProfileUri = Profiles.PubSubMqttUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "mqtt://broker.example.com:1883"
                })
            };
            connection.WriterGroups = connection.WriterGroups.AddItem(new WriterGroupDataType
            {
                Name = "WG1",
                MessageSettings = new ExtensionObject(
                    new UadpWriterGroupMessageDataType())
            });

            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            await using var transport = new MqttBrokerTransport(
                connection,
                endpoint,
                PubSubTransportDirection.Send,
                options,
                factory,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            string topic = MqttTopicBuilder.BuildDataTopic(
                "opcua/pubsub",
                MqttEncoding.Uadp,
                new Variant((uint)42),
                1,
                null);
            await transport.SendAsync(new byte[] { 1 }, topic).ConfigureAwait(false);

            MqttMessage[] msgs = [.. factory.Adapter.PublishedMessages];
            Assert.That(transport.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttUadpTransport));
            Assert.That(msgs[0].ContentType, Is.EqualTo("application/opcua+uadp"));
        }
    }
}
