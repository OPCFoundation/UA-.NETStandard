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
 *
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
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
#if NET8_0_OR_GREATER
// MQTTnet v5: client types live in the MQTTnet root namespace.
#else
using MQTTnet.Client;
#endif

namespace Opc.Ua.PubSub.Mqtt.Internal
{
    /// <summary>
    /// MQTTnet-backed implementation of <see cref="IMqttClientAdapter"/>.
    /// </summary>
    /// <remarks>
    /// The adapter compiles against MQTTnet v5 on net8.0+ (root namespace)
    /// and MQTTnet v4 on netstandard / net4x (the legacy
    /// <c>MQTTnet.Client</c> namespace). The two arms expose identical
    /// observable behaviour through <see cref="IMqttClientAdapter"/>.
    /// </remarks>
    internal sealed class MqttClientAdapter : IMqttClientAdapter
    {
        private readonly IMqttClient m_client;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly System.Threading.Lock m_sync = new();
        private bool m_disposed;

        public MqttClientAdapter(
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_logger = telemetry.CreateLogger<MqttClientAdapter>();
            m_timeProvider = timeProvider;
#if NET8_0_OR_GREATER
            var factory = new MqttClientFactory();
#else
            var factory = new MqttFactory();
#endif
            m_client = factory.CreateMqttClient();
            m_client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
            m_client.ConnectedAsync += OnConnectedAsync;
            m_client.DisconnectedAsync += OnDisconnectedAsync;
        }

        /// <inheritdoc/>
        public bool IsConnected => m_client.IsConnected;

        /// <inheritdoc/>
        public event EventHandler<MqttIncomingMessageEventArgs>? IncomingMessage;

        /// <inheritdoc/>
        public event EventHandler<MqttConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(
            MqttConnectionOptions options,
            CancellationToken ct)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            ThrowIfDisposed();

            var endpoint = MqttEndpointParser.Parse(options.Endpoint);
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(endpoint.Host, endpoint.Port)
                .WithKeepAlivePeriod(options.KeepAlivePeriod)
                .WithCleanSession(options.CleanSession)
                .WithProtocolVersion(MapProtocolVersion(options.ProtocolVersion))
                .WithTimeout(options.ConnectTimeout);

            if (!string.IsNullOrEmpty(options.ClientId))
            {
                builder = builder.WithClientId(options.ClientId);
            }
            bool useTls = options.Tls?.UseTls ?? endpoint.UseTls;
            ValidateCredentialTransport(options.UserName, useTls, options.AllowCredentialsOverPlaintext);
            if (!string.IsNullOrEmpty(options.UserName))
            {
                byte[] passwordBytes = options.PasswordBytes ?? Array.Empty<byte>();
                builder = builder.WithCredentials(options.UserName, passwordBytes);
            }
            // TODO(B4): apply MQTT Last-Will status payload once the multi-target
            // MQTTnet adapter exposes a stable builder API for Part 14 §7.3.4.7.7.
            // TODO(B11): map AuthenticationProfileUri/ResourceUri to MQTT v5 AUTH
            // packets for Part 14 §7.3.4.3; current implementation preserves the
            // existing UserName/PasswordSecretId path.

            if (useTls)
            {
                builder = ConfigureTls(builder, options.Tls);
            }

            var mqttOptions = builder.Build();
            m_logger.LogDebug(
                "MQTT connecting to {Host}:{Port} (TLS={UseTls}, version={Version}).",
                endpoint.Host,
                endpoint.Port,
                useTls,
                options.ProtocolVersion);
            await m_client.ConnectAsync(mqttOptions, ct).ConfigureAwait(false);
        }

        internal static void ValidateCredentialTransport(
            string? userName,
            bool useTls,
            bool allowCredentialsOverPlaintext)
        {
            if (!string.IsNullOrEmpty(userName) &&
                !useTls &&
                !allowCredentialsOverPlaintext)
            {
                throw new InvalidOperationException(
                    "MQTT credentials require TLS. Use mqtts:// or enable " +
                    "AllowCredentialsOverPlaintext only for explicitly accepted plaintext deployments.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisconnectAsync(CancellationToken ct)
        {
            if (m_disposed || !m_client.IsConnected)
            {
                return;
            }
            try
            {
                await m_client.DisconnectAsync(new MqttClientDisconnectOptions(), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "MQTT disconnect raised an exception.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask SubscribeAsync(
            IReadOnlyList<MqttTopicFilter> topics,
            CancellationToken ct)
        {
            if (topics is null)
            {
                throw new ArgumentNullException(nameof(topics));
            }
            if (topics.Count == 0)
            {
                return;
            }
            ThrowIfDisposed();

            var optionsBuilder = new MqttClientSubscribeOptionsBuilder();
            foreach (MqttTopicFilter topic in topics)
            {
                optionsBuilder = optionsBuilder.WithTopicFilter(
                    topic.Topic,
                    MapQos(topic.Qos));
            }
            await m_client.SubscribeAsync(optionsBuilder.Build(), ct).ConfigureAwait(false);
            m_logger.LogDebug("MQTT subscribed to {Count} topic(s).", topics.Count);
        }

        /// <inheritdoc/>
        public async ValueTask UnsubscribeAsync(
            IReadOnlyList<string> topics,
            CancellationToken ct)
        {
            if (topics is null)
            {
                throw new ArgumentNullException(nameof(topics));
            }
            if (topics.Count == 0)
            {
                return;
            }
            ThrowIfDisposed();

            var optionsBuilder = new MqttClientUnsubscribeOptionsBuilder();
            foreach (string topic in topics)
            {
                optionsBuilder = optionsBuilder.WithTopicFilter(topic);
            }
            await m_client.UnsubscribeAsync(optionsBuilder.Build(), ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask PublishAsync(MqttMessage message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(message.Topic))
            {
                throw new ArgumentException(
                    "MQTT publish requires a topic.",
                    nameof(message));
            }
            ThrowIfDisposed();

            var builder = new MqttApplicationMessageBuilder()
                .WithTopic(message.Topic)
                .WithQualityOfServiceLevel(MapQos(message.Qos))
                .WithRetainFlag(message.Retain);

            if (MemoryMarshal.TryGetArray(message.Payload, out ArraySegment<byte> segment))
            {
                builder = builder.WithPayloadSegment(segment);
            }
            else
            {
                builder = builder.WithPayload(message.Payload.ToArray());
            }

            if (!string.IsNullOrEmpty(message.ContentType))
            {
                builder = builder.WithContentType(message.ContentType);
            }
            if (!string.IsNullOrEmpty(message.ResponseTopic))
            {
                builder = builder.WithResponseTopic(message.ResponseTopic);
            }

            await m_client.PublishAsync(builder.Build(), ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_sync)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            try
            {
                if (m_client.IsConnected)
                {
                    await m_client.DisconnectAsync(
                        new MqttClientDisconnectOptions(),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "MQTT disconnect during dispose raised an exception.");
            }

            m_client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            m_client.ConnectedAsync -= OnConnectedAsync;
            m_client.DisconnectedAsync -= OnDisconnectedAsync;
            m_client.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(MqttClientAdapter));
            }
        }

        private Task OnApplicationMessageReceivedAsync(
            MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                MqttApplicationMessage app = args.ApplicationMessage;
#if NET8_0_OR_GREATER
                ReadOnlySequence<byte> sequence = app.Payload;
                byte[] payloadCopy;
                if (sequence.IsEmpty)
                {
                    payloadCopy = Array.Empty<byte>();
                }
                else
                {
                    payloadCopy = new byte[sequence.Length];
                    sequence.CopyTo(payloadCopy.AsSpan());
                }
#else
                ArraySegment<byte> segment = app.PayloadSegment;
                byte[] payloadCopy = new byte[segment.Count];
                if (segment.Count > 0 && segment.Array is not null)
                {
                    Buffer.BlockCopy(
                        segment.Array,
                        segment.Offset,
                        payloadCopy,
                        0,
                        segment.Count);
                }
#endif

                var message = new MqttMessage(
                    app.Topic,
                    payloadCopy,
                    MapQos(app.QualityOfServiceLevel),
                    app.Retain,
                    app.ContentType,
                    app.ResponseTopic);
                var eventArgs = new MqttIncomingMessageEventArgs(
                    message,
                    DateTimeUtc.From(m_timeProvider.GetUtcNow()));
                IncomingMessage?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Failed to deliver inbound MQTT message.");
            }
            return Task.CompletedTask;
        }

        private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
        {
            var eventArgs = new MqttConnectionStateChangedEventArgs(
                isConnected: true,
                reason: args.ConnectResult?.ReasonString);
            ConnectionStateChanged?.Invoke(this, eventArgs);
            return Task.CompletedTask;
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            var eventArgs = new MqttConnectionStateChangedEventArgs(
                isConnected: false,
                reason: args.ReasonString ?? args.Reason.ToString());
            ConnectionStateChanged?.Invoke(this, eventArgs);
            return Task.CompletedTask;
        }

        private static MqttQualityOfServiceLevel MapQos(MqttQualityOfService qos)
        {
            return qos switch
            {
                MqttQualityOfService.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
                MqttQualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
                MqttQualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MqttQualityOfServiceLevel.AtLeastOnce
            };
        }

        private static MqttQualityOfService MapQos(MqttQualityOfServiceLevel qos)
        {
            return qos switch
            {
                MqttQualityOfServiceLevel.AtMostOnce => MqttQualityOfService.AtMostOnce,
                MqttQualityOfServiceLevel.AtLeastOnce => MqttQualityOfService.AtLeastOnce,
                MqttQualityOfServiceLevel.ExactlyOnce => MqttQualityOfService.ExactlyOnce,
                _ => MqttQualityOfService.AtLeastOnce
            };
        }

        private static MQTTnet.Formatter.MqttProtocolVersion MapProtocolVersion(
            MqttProtocolVersion version)
        {
            return version switch
            {
                MqttProtocolVersion.V310 => MQTTnet.Formatter.MqttProtocolVersion.V310,
                MqttProtocolVersion.V311 => MQTTnet.Formatter.MqttProtocolVersion.V311,
                MqttProtocolVersion.V500 => MQTTnet.Formatter.MqttProtocolVersion.V500,
                _ => MQTTnet.Formatter.MqttProtocolVersion.V500
            };
        }

        private static MqttClientOptionsBuilder ConfigureTls(
            MqttClientOptionsBuilder builder,
            MqttTlsOptions? tls)
        {
#if NET8_0_OR_GREATER
            return builder.WithTlsOptions(o =>
            {
                o.UseTls();
                if (tls is not null)
                {
                    o.WithAllowUntrustedCertificates(!tls.ValidateServerCertificate);
                }
                else
                {
                    o.WithAllowUntrustedCertificates(false);
                }
            });
#else
            return builder.WithTlsOptions(o =>
            {
                o.UseTls();
                if (tls is not null)
                {
                    o.WithAllowUntrustedCertificates(!tls.ValidateServerCertificate);
                }
                else
                {
                    o.WithAllowUntrustedCertificates(false);
                }
            });
#endif
        }
    }
}
