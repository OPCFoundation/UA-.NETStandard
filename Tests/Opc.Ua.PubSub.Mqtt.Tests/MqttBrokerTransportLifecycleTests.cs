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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Lifecycle and state-event tests for
    /// <see cref="MqttBrokerTransport"/> using a fake adapter so the
    /// state machine is exercised without an actual broker.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.4")]
    [CancelAfter(10000)]
    public sealed class MqttBrokerTransportLifecycleTests
    {
        private static PubSubConnectionDataType NewConnection(string name = "Conn")
        {
            var conn = new PubSubConnectionDataType
            {
                Name = name,
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
            PubSubTransportDirection direction = PubSubTransportDirection.Send,
            MqttConnectionOptions? options = null,
            PubSubConnectionDataType? connection = null)
        {
            PubSubConnectionDataType conn = connection ?? NewConnection();
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883");
            return new MqttBrokerTransport(
                conn,
                endpoint,
                direction,
                options ?? new MqttConnectionOptions
                {
                    Endpoint = "mqtt://broker.example.com:1883"
                },
                factory,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
        }

        [Test]
        public async Task OpenCloseCycle_Succeeds()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            Assert.That(transport.IsConnected, Is.False);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.True);
            Assert.That(factory.Adapter.ConnectCount, Is.EqualTo(1));

            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        [TestSpec("7.3.4.7.7")]
        public async Task OpenAsync_WithConfiguredLastWill_PassesWillToAdapter()
        {
            var factory = new FakeMqttClientFactory();
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://broker.example.com:1883"
            };
            await using MqttBrokerTransport transport = NewTransport(factory, options: options);
            byte[] payload = [1, 2, 3];

            transport.ConfigureLastWill("opcua/json/status/publisher", payload, retain: true);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(options.WillTopic, Is.EqualTo("opcua/json/status/publisher"));
            Assert.That(options.WillPayload, Is.EqualTo(payload));
            Assert.That(options.WillRetain, Is.True);
        }

        [Test]
        public async Task Open_OnAlreadyOpenedTransport_IsIdempotent()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(factory.Adapter.ConnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Close_OnUnopenedTransport_DoesNotThrow()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.False);
            Assert.That(factory.Adapter.DisconnectCount, Is.Zero);
        }

        [Test]
        public async Task DoubleClose_IsIdempotent()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StateChanged_FiresOnOpenAndClose()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            var events = new List<PubSubTransportStateChangedEventArgs>();
            transport.StateChanged += (_, e) => events.Add(e);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(events, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(events[0].IsConnected, Is.True);
            // Last event should be a disconnect notification.
            Assert.That(events[^1].IsConnected, Is.False);
        }

        [Test]
        public async Task StateChanged_PropagatesAdapterEvents()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            var events = new List<PubSubTransportStateChangedEventArgs>();
            transport.StateChanged += (_, e) => events.Add(e);

            factory.Adapter.RaiseConnectionStateChanged(false, "broker reset");
            factory.Adapter.RaiseConnectionStateChanged(true, "reconnected");

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].IsConnected, Is.False);
            Assert.That(events[0].Reason, Is.EqualTo("broker reset"));
            Assert.That(events[1].IsConnected, Is.True);

            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task SendAsync_WithoutOpen_Throws()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await transport.SendAsync(
                    new byte[] { 1, 2, 3 },
                    "opcua/pubsub/json/data/1/2").ConfigureAwait(false));
        }

        [Test]
        public async Task SendAsync_WithNullTopic_Throws()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.ThrowsAsync<ArgumentException>(
                async () => await transport.SendAsync(
                    new byte[] { 1, 2, 3 },
                    topic: null).ConfigureAwait(false));
        }

        [Test]
        public async Task SendAsync_WithWildcardTopic_Throws()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.ThrowsAsync<ArgumentException>(
                async () => await transport.SendAsync(
                    new byte[] { 1, 2, 3 },
                    topic: "opcua/pubsub/json/data/+/2").ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await transport.SendAsync(
                    new byte[] { 1, 2, 3 },
                    topic: "opcua/pubsub/#").ConfigureAwait(false));
        }

        [Test]
        public async Task SendAsync_RoutesPayloadThroughAdapter()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            byte[] payload = [9, 8, 7, 6];
            const string topic = "opcua/pubsub/json/data/42/1/3";
            await transport.SendAsync(payload, topic).ConfigureAwait(false);

            Assert.That(factory.Adapter.PublishedMessages, Has.Count.EqualTo(1));
            ((System.Collections.Concurrent.ConcurrentQueue<MqttMessage>)factory.Adapter.PublishedMessages)
                .TryPeek(out MqttMessage first);
            Assert.That(first.Topic, Is.EqualTo(topic));
            Assert.That(first.Payload.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task DisposeAsync_IsIdempotent()
        {
            var factory = new FakeMqttClientFactory();
            MqttBrokerTransport transport = NewTransport(factory);
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            await transport.DisposeAsync().ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);

            Assert.That(factory.Adapter.DisconnectCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReceiveAsync_DeliversIncomingMessages()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(
                factory,
                direction: PubSubTransportDirection.Receive);
            transport.Subscriptions.Add(
                new MqttTopicFilter("opcua/pubsub/json/data/#", MqttQualityOfService.AtLeastOnce));
            await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // Push one message into the fake adapter
            factory.Adapter.RaiseIncomingMessage(
                new MqttMessage(
                    "opcua/pubsub/json/data/1/2/3",
                    new byte[] { 1, 2, 3 },
                    MqttQualityOfService.AtLeastOnce,
                    Retain: false,
                    ContentType: "application/json",
                    ResponseTopic: null),
                DateTimeUtc.From(DateTime.UtcNow));

            PubSubTransportFrame? frame = null;
            await foreach (PubSubTransportFrame f in transport.ReceiveAsync(cts.Token))
            {
                frame = f;
                break;
            }

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Topic, Is.EqualTo("opcua/pubsub/json/data/1/2/3"));
            Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public async Task OpenAsync_TooManySubscriptions_Throws()
        {
            var factory = new FakeMqttClientFactory();
            await using MqttBrokerTransport transport = NewTransport(
                factory,
                direction: PubSubTransportDirection.Receive,
                options: new MqttConnectionOptions
                {
                    Endpoint = "mqtt://broker.example.com:1883",
                    MaxConcurrentSubscriptions = 2
                });
            for (int i = 0; i < 5; i++)
            {
                transport.Subscriptions.Add(
                    new MqttTopicFilter(
                        $"opcua/pubsub/json/data/{i}/+",
                        MqttQualityOfService.AtLeastOnce));
            }

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await transport.OpenAsync(CancellationToken.None).ConfigureAwait(false));
        }
    }
}
