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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Server;
using NUnit.Framework;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Loopback integration test that brings up an in-process
    /// MQTTnet broker on a random localhost port, opens a publisher
    /// + subscriber <see cref="MqttBrokerTransport"/> pair against
    /// it, and asserts the payload round-trip across the MQTT
    /// connection / publish / subscribe path. Exercises the
    /// connection properties from Part 14 §7.3.4.4 and the QoS
    /// mapping from §7.3.4.5.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.3.4.4")]
    [TestSpec("7.3.4.5")]
    [CancelAfter(20000)]
    public sealed class MqttBrokerTransportIntegrationTests
    {
        private static int ReserveEphemeralTcpPort(IPAddress bindAddress)
        {
            using var probe = new Socket(
                bindAddress.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(bindAddress, 0));
            return ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        private static MqttServer? TryStartBroker(int port)
        {
            try
            {
#if MQTTNET_V5
                var factory = new MqttServerFactory();
#else
                var factory = new MQTTnet.MqttFactory();
#endif
                MqttServerOptions options = factory.CreateServerOptionsBuilder()
                    .WithDefaultEndpoint()
                    .WithDefaultEndpointPort(port)
                    .Build();
                MqttServer server = factory.CreateMqttServer(options);
                server.StartAsync().GetAwaiter().GetResult();
                return server;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static PubSubConnectionDataType NewConnection(
            string url,
            bool publisher)
        {
            var connection = new PubSubConnectionDataType
            {
                Name = publisher ? "Pub" : "Sub",
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url
                })
            };
            if (publisher)
            {
                connection.WriterGroups = connection.WriterGroups.AddItem(new WriterGroupDataType
                {
                    Name = "WG1",
                    MessageSettings = new ExtensionObject(
                        new JsonWriterGroupMessageDataType())
                });
            }
            else
            {
                connection.ReaderGroups = connection.ReaderGroups.AddItem(new ReaderGroupDataType
                {
                    Name = "RG1"
                });
            }
            return connection;
        }

        private static MqttBrokerTransport NewTransport(
            string url,
            PubSubConnectionDataType connection,
            PubSubTransportDirection direction,
            string clientId)
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse(url);
            var options = new MqttConnectionOptions
            {
                Endpoint = url,
                ClientId = clientId,
                CleanSession = true,
                KeepAlivePeriod = TimeSpan.FromSeconds(10),
                ConnectTimeout = TimeSpan.FromSeconds(5),
                Topics = new MqttTopicOptions
                {
                    DefaultQos = MqttQualityOfService.AtLeastOnce
                }
            };
            return new MqttBrokerTransport(
                connection,
                endpoint,
                direction,
                options,
                new MqttClientAdapterFactory(),
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        [Test]
        public async Task PublisherSubscriber_RoundTripsPayloadViaEmbeddedBroker()
        {
            int port;
            try
            {
                port = ReserveEphemeralTcpPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback TCP socket bind failed: {ex.Message}");
                return;
            }

            MqttServer? broker = TryStartBroker(port);
            if (broker is null)
            {
                Assert.Ignore("Embedded MQTTnet broker could not start on loopback.");
                return;
            }

            string url = $"mqtt://127.0.0.1:{port}";
            const string topic = "opcua/pubsub/json/data/42/1/3";
            PubSubConnectionDataType pubConn = NewConnection(url, publisher: true);
            PubSubConnectionDataType subConn = NewConnection(url, publisher: false);

            await using MqttBrokerTransport publisher = NewTransport(
                url,
                pubConn,
                PubSubTransportDirection.Send,
                "PubClient");
            await using MqttBrokerTransport subscriber = NewTransport(
                url,
                subConn,
                PubSubTransportDirection.Receive,
                "SubClient");
            subscriber.Subscriptions.Add(
                new MqttTopicFilter(topic, MqttQualityOfService.AtLeastOnce));

            try
            {
                await subscriber.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                await publisher.OpenAsync(CancellationToken.None).ConfigureAwait(false);

                byte[] payload = [0xCA, 0xFE, 0xBA, 0xBE];
                using var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                Task<PubSubTransportFrame?> receiveTask = ReceiveOneAsync(subscriber, receiveCts.Token);

                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                await publisher.SendAsync(payload, topic).ConfigureAwait(false);

                PubSubTransportFrame? frame = await receiveTask.ConfigureAwait(false);
                Assert.That(frame, Is.Not.Null, "Subscriber did not receive any frame.");
                Assert.That(frame!.Value.Topic, Is.EqualTo(topic));
                Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task RetainedMetadata_DeliveredToLateSubscriber()
        {
            int port;
            try
            {
                port = ReserveEphemeralTcpPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback TCP socket bind failed: {ex.Message}");
                return;
            }

            MqttServer? broker = TryStartBroker(port);
            if (broker is null)
            {
                Assert.Ignore("Embedded MQTTnet broker could not start on loopback.");
                return;
            }

            string url = $"mqtt://127.0.0.1:{port}";
            string metaTopic = MqttTopicBuilder.BuildMetaDataTopic(
                "opcua/pubsub",
                MqttEncoding.Json,
                new Variant((uint)1),
                writerGroupId: 1,
                dataSetWriterId: 2);
            PubSubConnectionDataType pubConn = NewConnection(url, publisher: true);

            byte[] meta = [0x01, 0x02, 0x03];

            try
            {
                await using (MqttBrokerTransport publisher = NewTransport(
                    url,
                    pubConn,
                    PubSubTransportDirection.Send,
                    "MetaPub"))
                {
                    await publisher.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                    await publisher.SendAsync(meta, metaTopic).ConfigureAwait(false);
                    // Allow broker time to persist the retained message.
                    await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                }

                PubSubConnectionDataType subConn = NewConnection(url, publisher: false);
                await using MqttBrokerTransport subscriber = NewTransport(
                    url,
                    subConn,
                    PubSubTransportDirection.Receive,
                    "MetaSub");
                subscriber.Subscriptions.Add(
                    new MqttTopicFilter(metaTopic, MqttQualityOfService.AtLeastOnce));
                await subscriber.OpenAsync(CancellationToken.None).ConfigureAwait(false);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PubSubTransportFrame? frame = await ReceiveOneAsync(subscriber, cts.Token)
                    .ConfigureAwait(false);
                Assert.That(frame, Is.Not.Null, "Late subscriber did not receive retained metadata.");
                Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(meta));
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        private static async Task<PubSubTransportFrame?> ReceiveOneAsync(
            MqttBrokerTransport transport,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (PubSubTransportFrame f in transport.ReceiveAsync(cancellationToken))
                {
                    return f;
                }
            }
            catch (OperationCanceledException)
            {
            }
            return null;
        }
    }
}
