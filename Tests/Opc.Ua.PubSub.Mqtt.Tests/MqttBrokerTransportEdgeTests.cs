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
    /// Edge-case coverage for <see cref="MqttBrokerTransport"/>:
    /// constructor argument validation, send-side guard rails, and
    /// dispose semantics per Part 14 §7.3.4.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4", Summary = "MQTT broker transport edge cases")]
    [CancelAfter(10000)]
    public sealed class MqttBrokerTransportEdgeTests
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

        private static MqttBrokerTransport NewTransport(
            FakeMqttClientFactory factory,
            PubSubTransportDirection direction = PubSubTransportDirection.SendReceive)
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            return new MqttBrokerTransport(
                NewConnection(),
                endpoint,
                direction,
                new MqttConnectionOptions
                {
                    Endpoint = "mqtt://broker.example.com:1883"
                },
                factory,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        [Test]
        public void ConstructorRejectsNullConnection()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            Assert.That(
                () => new MqttBrokerTransport(
                    connection: null!,
                    endpoint,
                    PubSubTransportDirection.Send,
                    new MqttConnectionOptions { Endpoint = "mqtt://h:1883" },
                    new FakeMqttClientFactory(),
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsNullOptions()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            Assert.That(
                () => new MqttBrokerTransport(
                    NewConnection(),
                    endpoint,
                    PubSubTransportDirection.Send,
                    options: null!,
                    new FakeMqttClientFactory(),
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsNullClientFactory()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            Assert.That(
                () => new MqttBrokerTransport(
                    NewConnection(),
                    endpoint,
                    PubSubTransportDirection.Send,
                    new MqttConnectionOptions { Endpoint = "mqtt://h:1883" },
                    clientFactory: null!,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            Assert.That(
                () => new MqttBrokerTransport(
                    NewConnection(),
                    endpoint,
                    PubSubTransportDirection.Send,
                    new MqttConnectionOptions { Endpoint = "mqtt://h:1883" },
                    new FakeMqttClientFactory(),
                    telemetry: null!,
                    TimeProvider.System),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsNullTimeProvider()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            Assert.That(
                () => new MqttBrokerTransport(
                    NewConnection(),
                    endpoint,
                    PubSubTransportDirection.Send,
                    new MqttConnectionOptions { Endpoint = "mqtt://h:1883" },
                    new FakeMqttClientFactory(),
                    NUnitTelemetryContext.Create(),
                    timeProvider: null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task SendBeforeOpenThrowsInvalidOperationException()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, "topic/x").ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task SendWithEmptyTopicThrowsArgumentException()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, topic: string.Empty).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendWithNullTopicThrowsArgumentException()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, topic: null).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendWithMultiLevelWildcardThrowsArgumentException()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, topic: "a/#").ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendWithSingleLevelWildcardThrowsArgumentException()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, topic: "a/+/c").ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendWithNullByteInTopicThrowsArgumentException()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, topic: "a/\0/b").ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task SendCancelsWhenTokenAlreadyCancelled()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, "x", cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task SendAfterDisposeThrowsObjectDisposedException()
        {
            var factory = new FakeMqttClientFactory();
            MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);

            byte[] payload = [0x01];

            Assert.That(
                async () => await transport.SendAsync(payload, "topic").ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task OpenAfterDisposeThrowsObjectDisposedException()
        {
            var factory = new FakeMqttClientFactory();
            MqttBrokerTransport transport = NewTransport(factory);
            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task DoubleDisposeIsIdempotent()
        {
            var factory = new FakeMqttClientFactory();
            MqttBrokerTransport transport = NewTransport(factory);

            await transport.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task DoubleCloseIsIdempotent()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReceiveWithoutChannelYieldsNothing()
        {
            // Send-only direction never opens a receive channel.
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(
                factory,
                PubSubTransportDirection.Send);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            int frames = 0;
            await foreach (PubSubTransportFrame _ in transport.ReceiveAsync(cts.Token)
                .ConfigureAwait(false))
            {
                frames++;
            }
            Assert.That(frames, Is.Zero);
        }

        [Test]
        public async Task IncomingMessageIsDispatchedAsFrame()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(
                factory,
                PubSubTransportDirection.Receive);
            transport.Subscriptions.Add(new MqttTopicFilter("data/#", MqttQualityOfService.AtMostOnce));
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            byte[] payload = [0x42, 0x42];
            factory.Adapter.RaiseIncomingMessage(
                new MqttMessage("data/x", payload, MqttQualityOfService.AtMostOnce, false, "application/json", null),
                DateTimeUtc.Now);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            PubSubTransportFrame? received = null;
            await foreach (PubSubTransportFrame frame in transport.ReceiveAsync(cts.Token)
                .ConfigureAwait(false))
            {
                received = frame;
                break;
            }

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Value.Topic, Is.EqualTo("data/x"));
            Assert.That(received.Value.Payload.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task EndpointAndOptionsExposed()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            Assert.Multiple(() =>
            {
                Assert.That(transport.Endpoint.Host, Is.EqualTo("broker.example.com"));
                Assert.That(transport.Endpoint.Port, Is.EqualTo(1883));
                Assert.That(transport.Options.Endpoint, Is.EqualTo("mqtt://broker.example.com:1883"));
                Assert.That(transport.TransportProfileUri, Is.EqualTo(Profiles.PubSubMqttJsonTransport));
            });
        }
    }
}
