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
using MQTTnet;
using MQTTnet.Client;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Guard-rail tests for <see cref="MqttClientAdapter"/> that do NOT
    /// require a running broker. Covers the disposed-state
    /// <see cref="ObjectDisposedException"/> paths in
    /// <see cref="MqttClientAdapter.ConnectAsync"/>,
    /// <see cref="MqttClientAdapter.SubscribeAsync"/>,
    /// <see cref="MqttClientAdapter.UnsubscribeAsync"/>, and
    /// <see cref="MqttClientAdapter.PublishAsync"/>, plus the
    /// <see cref="MqttClientAdapter.DisconnectAsync"/> no-op guard when the
    /// client has never connected.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    [CancelAfter(10000)]
    public sealed class MqttClientAdapterGuardTests
    {
        [Test]
        public async Task DisconnectAsync_WhenNotConnected_CompletesWithoutException(
            CancellationToken cancellationToken)
        {
            await using var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            // A freshly created adapter is not connected; DisconnectAsync
            // should detect that and return immediately per
            //   if (m_disposed || !m_client.IsConnected) return;
            await adapter.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            Assert.That(adapter.IsConnected, Is.False);
        }

        [Test]
        public async Task DisposeAsync_CalledTwice_DoesNotThrow()
        {
            var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            await adapter.DisposeAsync().ConfigureAwait(false);
            // Second dispose should be guarded by m_disposed flag.
            await adapter.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConnectAsync_AfterDispose_ThrowsObjectDisposedException(
            CancellationToken cancellationToken)
        {
            var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            await adapter.DisposeAsync().ConfigureAwait(false);

            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtt://127.0.0.1:1883"
            };

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await adapter.ConnectAsync(options, cancellationToken)
                    .ConfigureAwait(false));
        }

        [Test]
        public void ValidateCredentialTransportRejectsPlaintextCredentialsByDefault()
        {
            Assert.That(
                () => MqttClientAdapter.ValidateCredentialTransport(
                    "user",
                    useTls: false,
                    allowCredentialsOverPlaintext: false),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("MQTT credentials require TLS"));
        }

        [Test]
        public void ValidateCredentialTransportAllowsTlsOrExplicitPlaintextOptOut()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => MqttClientAdapter.ValidateCredentialTransport(
                        "user",
                        useTls: true,
                        allowCredentialsOverPlaintext: false),
                    Throws.Nothing);
                Assert.That(
                    () => MqttClientAdapter.ValidateCredentialTransport(
                        "user",
                        useTls: false,
                        allowCredentialsOverPlaintext: true),
                    Throws.Nothing);
            });
        }

        [Test]
        [TestSpec("7.3.4.4")]
        public void ConfigureBrokerTransportWebSocketSchemesUseWebSocketChannel()
        {
            MqttEndpoint wsEndpoint = MqttEndpointParser.Parse("ws://broker.example/mqtt");
            MqttEndpoint wssEndpoint = MqttEndpointParser.Parse("wss://broker.example/mqtt");

#if NET8_0_OR_GREATER
            var wsOptions = MqttClientAdapter.ConfigureBrokerTransport(
                new MqttClientOptionsBuilder(),
                wsEndpoint).Build();
            var wssOptions = MqttClientAdapter.ConfigureBrokerTransport(
                new MqttClientOptionsBuilder(),
                wssEndpoint).Build();

            Assert.Multiple(() =>
            {
                Assert.That(wsOptions.ChannelOptions, Is.TypeOf<MQTTnet.MqttClientWebSocketOptions>());
                Assert.That(wssOptions.ChannelOptions, Is.TypeOf<MQTTnet.MqttClientWebSocketOptions>());
                Assert.That(
                    ((MQTTnet.MqttClientWebSocketOptions)wsOptions.ChannelOptions).Uri,
                    Is.EqualTo("ws://broker.example/mqtt"));
                Assert.That(
                    ((MQTTnet.MqttClientWebSocketOptions)wssOptions.ChannelOptions).Uri,
                    Is.EqualTo("wss://broker.example/mqtt"));
            });
#else
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => MqttClientAdapter.ConfigureBrokerTransport(new MqttClientOptionsBuilder(), wsEndpoint),
                    Throws.TypeOf<NotSupportedException>()
                        .With.Message.Contains("MQTT over WebSocket"));
                Assert.That(
                    () => MqttClientAdapter.ConfigureBrokerTransport(new MqttClientOptionsBuilder(), wssEndpoint),
                    Throws.TypeOf<NotSupportedException>()
                        .With.Message.Contains("MQTT over WebSocket"));
            });
#endif
        }

        [Test]
        [TestSpec("7.3.4.4")]
        public void ConfigureBrokerTransportMqttSchemesUseTcpChannel()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example:1884");
            var options = MqttClientAdapter.ConfigureBrokerTransport(
                new MqttClientOptionsBuilder(),
                endpoint).Build();

#if NET8_0_OR_GREATER
            Assert.That(options.ChannelOptions, Is.TypeOf<MQTTnet.MqttClientTcpOptions>());
#else
            Assert.That(options.ChannelOptions, Is.TypeOf<MQTTnet.Client.MqttClientTcpOptions>());
#endif
        }

        [Test]
        [TestSpec("7.3.4.3")]
        public void ApplyEnhancedAuthenticationSetsMqttV5AuthFields()
        {
            var options = new MqttConnectionOptions
            {
                Endpoint = "mqtts://broker.example",
                ProtocolVersion = MqttProtocolVersion.V500,
                AuthenticationProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json",
                ResourceUri = "urn:broker:resource"
            };
            var mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("broker.example", 8883)
                .Build();

            MqttClientAdapter.ApplyEnhancedAuthentication(mqttOptions, options);

            Assert.Multiple(() =>
            {
                Assert.That(mqttOptions.AuthenticationMethod, Is.EqualTo(options.AuthenticationProfileUri));
                Assert.That(
                    System.Text.Encoding.UTF8.GetString(mqttOptions.AuthenticationData ?? []),
                    Is.EqualTo(options.ResourceUri));
            });
        }

        [Test]
        public async Task SubscribeAsync_AfterDispose_ThrowsObjectDisposedException(
            CancellationToken cancellationToken)
        {
            var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            await adapter.DisposeAsync().ConfigureAwait(false);

            var filters = new List<MqttTopicFilter>
            {
                new MqttTopicFilter("test/topic", MqttQualityOfService.AtMostOnce)
            };

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await adapter.SubscribeAsync(filters, cancellationToken)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task UnsubscribeAsync_AfterDispose_ThrowsObjectDisposedException(
            CancellationToken cancellationToken)
        {
            var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            await adapter.DisposeAsync().ConfigureAwait(false);

            var topics = new List<string> { "test/topic" };

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await adapter.UnsubscribeAsync(topics, cancellationToken)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task PublishAsync_AfterDispose_ThrowsObjectDisposedException(
            CancellationToken cancellationToken)
        {
            var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            await adapter.DisposeAsync().ConfigureAwait(false);

            var message = new MqttMessage(
                Topic: "test/topic",
                Payload: Array.Empty<byte>(),
                Qos: MqttQualityOfService.AtMostOnce,
                Retain: false,
                ContentType: null,
                ResponseTopic: null);

            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await adapter.PublishAsync(message, cancellationToken)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task PublishAsync_WithEmptyTopic_ThrowsArgumentExceptionBeforeDisposedCheck(
            CancellationToken cancellationToken)
        {
            // Even on a fresh (not-disposed) adapter the topic guard fires first.
            await using var adapter = new MqttClientAdapter(
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            var badMessage = new MqttMessage(
                Topic: string.Empty,
                Payload: Array.Empty<byte>(),
                Qos: MqttQualityOfService.AtMostOnce,
                Retain: false,
                ContentType: null,
                ResponseTopic: null);

            Assert.ThrowsAsync<ArgumentException>(
                async () => await adapter.PublishAsync(badMessage, cancellationToken)
                    .ConfigureAwait(false));
        }
    }
}
