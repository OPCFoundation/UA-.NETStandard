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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Server;
using NUnit.Framework;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Direct tests for the MQTTnet-backed
    /// <see cref="MqttClientAdapter"/> + its
    /// <see cref="MqttClientAdapterFactory"/>. Uses an embedded
    /// MQTTnet broker on loopback to exercise the full
    /// subscribe / publish / unsubscribe / disconnect / dispose
    /// surface so the adapter's internal branches are observable.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.3.4.4")]
    [CancelAfter(15000)]
    public sealed class MqttClientAdapterTests
    {
        private static int ReserveEphemeralTcpPort()
        {
            using var probe = new Socket(
                IPAddress.Loopback.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
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

        [Test]
        public void Factory_RejectsNullArguments()
        {
            var factory = new MqttClientAdapterFactory();
            Assert.Throws<ArgumentNullException>(() => ((IMqttClientFactory)factory).CreateAdapter(
                null!,
                NUnitTelemetryContext.Create(),
                TimeProvider.System));
            Assert.Throws<ArgumentNullException>(() => ((IMqttClientFactory)factory).CreateAdapter(
                new MqttConnectionOptions(),
                null!,
                TimeProvider.System));
            Assert.Throws<ArgumentNullException>(() => ((IMqttClientFactory)factory).CreateAdapter(
                new MqttConnectionOptions(),
                NUnitTelemetryContext.Create(),
                null!));
        }

        [Test]
        public async Task Factory_CreateAdapter_ProducesUsableAdapter()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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
            try
            {
                var factory = new MqttClientAdapterFactory();
                var options = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "AdapterTest"
                };
                await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                    options,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                await adapter.ConnectAsync(options, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(adapter.IsConnected, Is.True);

                await adapter.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(adapter.IsConnected, Is.False);
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task SubscribeUnsubscribeRoundTrip_Succeeds()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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
            try
            {
                var factory = new MqttClientAdapterFactory();
                var options = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "SubUnsubTest"
                };
                await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                    options,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                await adapter.ConnectAsync(options, CancellationToken.None)
                    .ConfigureAwait(false);

                const string topic = "opcua/pubsub/json/data/9/8/7";
                var filters = new[]
                {
                    new MqttTopicFilter(topic, MqttQualityOfService.AtLeastOnce)
                };
                await adapter.SubscribeAsync(filters, CancellationToken.None)
                    .ConfigureAwait(false);

                await adapter.UnsubscribeAsync(
                    [topic],
                    CancellationToken.None).ConfigureAwait(false);

                // empty-collection short-circuit
                await adapter.SubscribeAsync(
                    Array.Empty<MqttTopicFilter>(),
                    CancellationToken.None).ConfigureAwait(false);
                await adapter.UnsubscribeAsync(
                    Array.Empty<string>(),
                    CancellationToken.None).ConfigureAwait(false);

                await adapter.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task PublishMessageWithContentTypeAndResponseTopic_Succeeds()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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
            try
            {
                var factory = new MqttClientAdapterFactory();
                var options = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "PubMetaTest"
                };
                await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                    options,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                await adapter.ConnectAsync(options, CancellationToken.None)
                    .ConfigureAwait(false);

                var message = new MqttMessage(
                    "opcua/pubsub/json/data/1/2",
                    new byte[] { 1, 2, 3 },
                    MqttQualityOfService.ExactlyOnce,
                    Retain: false,
                    ContentType: "application/json",
                    ResponseTopic: "opcua/pubsub/json/response/1/2");
                await adapter.PublishAsync(message, CancellationToken.None)
                    .ConfigureAwait(false);

                await adapter.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task DisposeAsync_IsIdempotent_OnConnectedAdapter()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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
            try
            {
                var factory = new MqttClientAdapterFactory();
                var options = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "DisposeTest"
                };
                IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                    options,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                await adapter.ConnectAsync(options, CancellationToken.None)
                    .ConfigureAwait(false);

                await adapter.DisposeAsync().ConfigureAwait(false);
                await adapter.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task PublishWithoutTopic_Throws()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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
            try
            {
                var factory = new MqttClientAdapterFactory();
                var options = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}"
                };
                await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                    options,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);
                await adapter.ConnectAsync(options, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.ThrowsAsync<ArgumentException>(async () => await adapter.PublishAsync(
                    new MqttMessage(string.Empty, Array.Empty<byte>(),
                        MqttQualityOfService.AtMostOnce, false, null, null),
                    CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task ConnectAsync_NullOptions_Throws()
        {
            var factory = new MqttClientAdapterFactory();
            await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                new MqttConnectionOptions { Endpoint = "mqtt://127.0.0.1:1883" },
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.ThrowsAsync<ArgumentNullException>(async () => await adapter
                .ConnectAsync(null!, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task SubscribeAsync_NullTopics_Throws()
        {
            var factory = new MqttClientAdapterFactory();
            await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                new MqttConnectionOptions { Endpoint = "mqtt://127.0.0.1:1883" },
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.ThrowsAsync<ArgumentNullException>(async () => await adapter
                .SubscribeAsync(null!, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task UnsubscribeAsync_NullTopics_Throws()
        {
            var factory = new MqttClientAdapterFactory();
            await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                new MqttConnectionOptions { Endpoint = "mqtt://127.0.0.1:1883" },
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            Assert.ThrowsAsync<ArgumentNullException>(async () => await adapter
                .UnsubscribeAsync(null!, CancellationToken.None).ConfigureAwait(false));
        }

        [Test]
        public async Task ConnectAndDisconnect_RaiseConnectionStateChangedEventsAsync()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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

            try
            {
                var factory = new MqttClientAdapterFactory();
                var options = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "StateEvents"
                };
                await using IMqttClientAdapter adapter = ((IMqttClientFactory)factory).CreateAdapter(
                    options,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);
                var events = new System.Collections.Generic.List<MqttConnectionStateChangedEventArgs>();
                var connected = new TaskCompletionSource<MqttConnectionStateChangedEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var disconnected = new TaskCompletionSource<MqttConnectionStateChangedEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                adapter.ConnectionStateChanged += (_, args) =>
                {
                    events.Add(args);
                    if (args.IsConnected)
                    {
                        connected.TrySetResult(args);
                    }
                    else
                    {
                        disconnected.TrySetResult(args);
                    }
                };

                await adapter.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);
                _ = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await adapter.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                _ = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(events, Has.Count.GreaterThanOrEqualTo(2));
                Assert.That(events[0].IsConnected, Is.True);
                Assert.That(events[^1].IsConnected, Is.False);
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task IncomingMessage_WithPayloadContentTypeAndResponseTopic_RaisesEventAsync()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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

            try
            {
                var factory = new MqttClientAdapterFactory();
                var subscriberOptions = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "Subscriber"
                };
                var publisherOptions = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "Publisher"
                };

                await using IMqttClientAdapter subscriber = ((IMqttClientFactory)factory).CreateAdapter(
                    subscriberOptions,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);
                await using IMqttClientAdapter publisher = ((IMqttClientFactory)factory).CreateAdapter(
                    publisherOptions,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                var received = new TaskCompletionSource<MqttIncomingMessageEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                subscriber.IncomingMessage += (_, args) => received.TrySetResult(args);

                await subscriber.ConnectAsync(subscriberOptions, CancellationToken.None).ConfigureAwait(false);
                await publisher.ConnectAsync(publisherOptions, CancellationToken.None).ConfigureAwait(false);

                const string topic = "opcua/pubsub/json/data/3/4/5";
                await subscriber.SubscribeAsync(
                    [new MqttTopicFilter(topic, MqttQualityOfService.ExactlyOnce)],
                    CancellationToken.None).ConfigureAwait(false);

                var outbound = new MqttMessage(
                    topic,
                    new byte[] { 0x10, 0x20, 0x30 },
                    MqttQualityOfService.ExactlyOnce,
                    Retain: true,
                    ContentType: "application/octet-stream",
                    ResponseTopic: "opcua/pubsub/response");
                await publisher.PublishAsync(outbound, CancellationToken.None).ConfigureAwait(false);

                MqttIncomingMessageEventArgs inbound =
                    await received.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(inbound.Message.Topic, Is.EqualTo(topic));
                    Assert.That(inbound.Message.Payload.ToArray(), Is.EqualTo(new byte[] { 0x10, 0x20, 0x30 }));
                    Assert.That(inbound.Message.Qos, Is.EqualTo(MqttQualityOfService.ExactlyOnce));
                    Assert.That(inbound.Message.ContentType, Is.EqualTo("application/octet-stream"));
                    Assert.That(inbound.Message.ResponseTopic, Is.EqualTo("opcua/pubsub/response"));
                });
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task Publish_WithUserProperties_DeliversMessageAsync()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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

            try
            {
                var factory = new MqttClientAdapterFactory();
                var subscriberOptions = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "PropSubscriber"
                };
                var publisherOptions = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "PropPublisher"
                };

                await using IMqttClientAdapter subscriber = ((IMqttClientFactory)factory).CreateAdapter(
                    subscriberOptions,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);
                await using IMqttClientAdapter publisher = ((IMqttClientFactory)factory).CreateAdapter(
                    publisherOptions,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                var received = new TaskCompletionSource<MqttIncomingMessageEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                subscriber.IncomingMessage += (_, args) => received.TrySetResult(args);

                await subscriber.ConnectAsync(subscriberOptions, CancellationToken.None).ConfigureAwait(false);
                await publisher.ConnectAsync(publisherOptions, CancellationToken.None).ConfigureAwait(false);

                const string topic = "opcua/pubsub/json/data/9/9/9";
                await subscriber.SubscribeAsync(
                    [new MqttTopicFilter(topic, MqttQualityOfService.AtLeastOnce)],
                    CancellationToken.None).ConfigureAwait(false);

                var outbound = new MqttMessage(
                    topic,
                    new byte[] { 0xAA, 0xBB },
                    MqttQualityOfService.AtLeastOnce,
                    Retain: false,
                    ContentType: "application/json",
                    ResponseTopic: null,
                    UserProperties:
                    [
                        new KeyValuePair<string, string>("Temperature", "21.5"),
                        new KeyValuePair<string, string>("Unit", "C")
                    ]);
                await publisher.PublishAsync(outbound, CancellationToken.None).ConfigureAwait(false);

                MqttIncomingMessageEventArgs inbound =
                    await received.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(
                    inbound.Message.Payload.ToArray(),
                    Is.EqualTo(new byte[] { 0xAA, 0xBB }));
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }

        [Test]
        public async Task IncomingMessage_EmptyPayload_RaisesEmptyBufferAsync()
        {
            int port;
            try { port = ReserveEphemeralTcpPort(); }
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

            try
            {
                var factory = new MqttClientAdapterFactory();
                var subscriberOptions = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "EmptyPayloadSub"
                };
                var publisherOptions = new MqttConnectionOptions
                {
                    Endpoint = $"mqtt://127.0.0.1:{port}",
                    ClientId = "EmptyPayloadPub"
                };

                await using IMqttClientAdapter subscriber = ((IMqttClientFactory)factory).CreateAdapter(
                    subscriberOptions,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);
                await using IMqttClientAdapter publisher = ((IMqttClientFactory)factory).CreateAdapter(
                    publisherOptions,
                    NUnitTelemetryContext.Create(),
                    TimeProvider.System);

                var received = new TaskCompletionSource<MqttIncomingMessageEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                subscriber.IncomingMessage += (_, args) => received.TrySetResult(args);

                await subscriber.ConnectAsync(subscriberOptions, CancellationToken.None).ConfigureAwait(false);
                await publisher.ConnectAsync(publisherOptions, CancellationToken.None).ConfigureAwait(false);

                const string topic = "opcua/pubsub/json/empty";
                await subscriber.SubscribeAsync(
                    [new MqttTopicFilter(topic, MqttQualityOfService.AtMostOnce)],
                    CancellationToken.None).ConfigureAwait(false);
                await publisher.PublishAsync(
                    new MqttMessage(
                        topic,
                        Array.Empty<byte>(),
                        MqttQualityOfService.AtMostOnce,
                        Retain: false,
                        ContentType: null,
                        ResponseTopic: null),
                    CancellationToken.None).ConfigureAwait(false);

                MqttIncomingMessageEventArgs inbound =
                    await received.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(inbound.Message.Payload.Length, Is.Zero);
            }
            finally
            {
                await broker.StopAsync().ConfigureAwait(false);
                broker.Dispose();
            }
        }
    }
}
