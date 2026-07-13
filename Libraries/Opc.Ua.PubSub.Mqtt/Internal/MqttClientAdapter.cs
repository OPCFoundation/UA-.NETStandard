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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using Opc.Ua.Security.Certificates;
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
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly IMqttTrustedIssuerResolver? m_trustedIssuerResolver;
        private readonly Lock m_sync = new();
        private X509Certificate2Collection? m_trustChain;
        private bool m_disposed;

        public MqttClientAdapter(
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            IMqttTrustedIssuerResolver? trustedIssuerResolver = null)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<MqttClientAdapter>();
            m_timeProvider = timeProvider;
            m_trustedIssuerResolver = trustedIssuerResolver;
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

            MqttEndpoint endpoint = MqttEndpointParser.Parse(options.Endpoint);
            MqttClientOptionsBuilder builder = ConfigureBrokerTransport(new MqttClientOptionsBuilder(), endpoint)
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
                byte[] passwordBytes = options.PasswordBytes ?? [];
                builder = builder.WithCredentials(options.UserName, passwordBytes);
            }
            X509Certificate2Collection? trustChain = useTls
                ? await ResolveTrustChainAsync(options.Tls, ct).ConfigureAwait(false)
                : null;
            SwapTrustChain(trustChain);
            if (useTls)
            {
                builder = ConfigureTls(builder, options.Tls, trustChain);
            }

            MqttClientOptions mqttOptions = builder.Build();
            ApplyEnhancedAuthentication(mqttOptions, options);
            if (!string.IsNullOrEmpty(options.WillTopic))
            {
                mqttOptions.WillTopic = options.WillTopic;
                mqttOptions.WillPayload = options.WillPayload ?? [];
                mqttOptions.WillQualityOfServiceLevel = MapQos(options.WillQos);
                mqttOptions.WillRetain = options.WillRetain;
            }
            m_logger.MqttConnecting(endpoint.Host, endpoint.Port, useTls, options.ProtocolVersion);
            await m_client.ConnectAsync(mqttOptions, ct).ConfigureAwait(false);
        }

        internal static MqttClientOptionsBuilder ConfigureBrokerTransport(
            MqttClientOptionsBuilder builder,
            MqttEndpoint endpoint)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (endpoint.Uri.Scheme is MqttEndpointParser.WsScheme or MqttEndpointParser.WssScheme)
            {
#if NET8_0_OR_GREATER
                return builder.WithWebSocketServer(o => o.WithUri(endpoint.Uri.AbsoluteUri));
#else
                // TODO: enable MQTT-over-WebSocket when the legacy MQTTnet target TFMs expose it.
                throw new NotSupportedException(
                    "MQTT over WebSocket is not available with MQTTnet 4.x target TFMs.");
#endif
            }

            return builder.WithTcpServer(endpoint.Host, endpoint.Port);
        }

        internal static void ApplyEnhancedAuthentication(
            MqttClientOptions mqttOptions,
            MqttConnectionOptions options)
        {
            if (string.IsNullOrEmpty(options.AuthenticationProfileUri))
            {
                return;
            }
            if (options.ProtocolVersion != MqttProtocolVersion.V500)
            {
                throw new InvalidOperationException(
                    "MQTT AuthenticationProfileUri requires MQTT 5.0 enhanced authentication.");
            }
#if NET8_0_OR_GREATER
            mqttOptions.AuthenticationMethod = options.AuthenticationProfileUri;
            mqttOptions.AuthenticationData = string.IsNullOrEmpty(options.ResourceUri)
                ? null
                : System.Text.Encoding.UTF8.GetBytes(options.ResourceUri);
#else
            // TODO(B11): MQTTnet 4.x (used by the netstandard/net48 target TFMs)
            // exposes no MqttClientOptions AuthenticationMethod,
            // AuthenticationData, or EnhancedAuthenticationHandler API. Enhanced
            // AUTH/SASL is wired for MQTTnet 5.x TFMs above; older TFMs require a
            // client-library upgrade or adapter-specific extension point.
            throw new NotSupportedException(
                "MQTT enhanced authentication is not available with MQTTnet 4.x target TFMs.");
#endif
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
                m_logger.MqttDisconnectRaisedException(ex);
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
            m_logger.MqttSubscribed(topics.Count);
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

            MqttApplicationMessageBuilder builder = new MqttApplicationMessageBuilder()
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

            MqttApplicationMessage applicationMessage = builder.Build();
            if (message.UserProperties is { Count: > 0 } userProperties)
            {
                applicationMessage.UserProperties ??=
                    new List<MqttUserProperty>(userProperties.Count);
                foreach (KeyValuePair<string, string> property in userProperties)
                {
                    applicationMessage.UserProperties.Add(
                        CreateUserProperty(property.Key, property.Value));
                }
            }

            await m_client.PublishAsync(applicationMessage, ct).ConfigureAwait(false);
        }

        private static MqttUserProperty CreateUserProperty(string name, string value)
        {
#if NET8_0_OR_GREATER
            // MQTTnet v5 marks the string-valued ctor obsolete in favour of the
            // pre-encoded UTF-8 value; user-property values are UTF-8 on the wire.
            return new MqttUserProperty(
                name, new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes(value)));
#else
            return new MqttUserProperty(name, value);
#endif
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
                m_logger.MqttDisconnectDuringDisposeRaisedException(ex);
            }

            m_client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
            m_client.ConnectedAsync -= OnConnectedAsync;
            m_client.DisconnectedAsync -= OnDisconnectedAsync;
            m_client.Dispose();
            SwapTrustChain(null);
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
                    payloadCopy = [];
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
                m_logger.FailedToDeliverInboundMqttMessage(ex);
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

        private async ValueTask<X509Certificate2Collection?> ResolveTrustChainAsync(
            MqttTlsOptions? tls,
            CancellationToken ct)
        {
            string[]? subjects = tls?.TrustedIssuerCertificateSubjects;
            if (m_trustedIssuerResolver is null || subjects is null || subjects.Length == 0)
            {
                return null;
            }

            using CertificateCollection trustedIssuers = await m_trustedIssuerResolver
                .ResolveAsync(subjects, m_telemetry, ct)
                .ConfigureAwait(false);
            if (trustedIssuers.Count == 0)
            {
                return null;
            }

            // AsX509Certificate2Collection returns independent copies the caller owns; the
            // adapter keeps them alive for the connection and disposes them on Dispose.
            return trustedIssuers.AsX509Certificate2Collection();
        }

        private void SwapTrustChain(X509Certificate2Collection? trustChain)
        {
            X509Certificate2Collection? previous;
            lock (m_sync)
            {
                previous = m_trustChain;
                m_trustChain = trustChain;
            }
            DisposeTrustChain(previous);
        }

        private static void DisposeTrustChain(X509Certificate2Collection? trustChain)
        {
            if (trustChain is null)
            {
                return;
            }
            foreach (X509Certificate2 certificate in trustChain)
            {
                certificate.Dispose();
            }
        }

        private static MqttClientOptionsBuilder ConfigureTls(
            MqttClientOptionsBuilder builder,
            MqttTlsOptions? tls,
            X509Certificate2Collection? trustChain)
        {
            bool allowUntrusted = tls is not null && !tls.ValidateServerCertificate;
            return builder.WithTlsOptions(o =>
            {
                o.UseTls()
                    .WithAllowUntrustedCertificates(allowUntrusted);
                if (trustChain is not null && trustChain.Count > 0)
                {
#if NET8_0_OR_GREATER
                    o.WithTrustChain(trustChain);
#else
                    bool validate = tls is null || tls.ValidateServerCertificate;
                    o.WithCertificateValidationHandler(context =>
                        ValidateAgainstTrustChain(context.Certificate, trustChain, validate));
#endif
                }
            });
        }

#if !NET8_0_OR_GREATER
        private static bool ValidateAgainstTrustChain(
            X509Certificate certificate,
            X509Certificate2Collection trustChain,
            bool validate)
        {
            if (!validate)
            {
                return true;
            }
            if (certificate is null)
            {
                return false;
            }

            using var brokerCertificate = new X509Certificate2(certificate);
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.AddRange(trustChain);

            // Build populates ChainStatus/ChainElements; the return value is ignored because
            // MQTTnet v4 (net4x / netstandard2.1) cannot set a custom root trust store and a
            // self-signed configured CA always reports UntrustedRoot.
            _ = chain.Build(brokerCertificate);
            foreach (X509ChainStatus status in chain.ChainStatus)
            {
                if (status.Status is X509ChainStatusFlags.NoError
                    or X509ChainStatusFlags.UntrustedRoot
                    or X509ChainStatusFlags.PartialChain)
                {
                    continue;
                }

                return false;
            }

            // Accept the broker certificate only when its chain actually terminates at one of
            // the configured trusted issuer certificates.
            foreach (X509ChainElement element in chain.ChainElements)
            {
                foreach (X509Certificate2 ca in trustChain)
                {
                    if (string.Equals(
                        element.Certificate.Thumbprint,
                        ca.Thumbprint,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
#endif
    }

    /// <summary>
    /// Source-generated log messages for MqttClientAdapter.
    /// </summary>
    internal static partial class MqttClientAdapterLog
    {
        [LoggerMessage(EventId = PubSubMqttEventIds.MqttClientAdapter + 0, Level = LogLevel.Debug,
            Message = "MQTT connecting to {Host}:{Port} (TLS={UseTls}, version={Version}).")]
        public static partial void MqttConnecting(
            this ILogger logger,
            string host,
            int port,
            bool useTls,
            MqttProtocolVersion version);

        [LoggerMessage(EventId = PubSubMqttEventIds.MqttClientAdapter + 1, Level = LogLevel.Debug,
            Message = "MQTT disconnect raised an exception.")]
        public static partial void MqttDisconnectRaisedException(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubMqttEventIds.MqttClientAdapter + 2, Level = LogLevel.Debug,
            Message = "MQTT subscribed to {Count} topic(s).")]
        public static partial void MqttSubscribed(this ILogger logger, int count);

        [LoggerMessage(EventId = PubSubMqttEventIds.MqttClientAdapter + 3, Level = LogLevel.Debug,
            Message = "MQTT disconnect during dispose raised an exception.")]
        public static partial void MqttDisconnectDuringDisposeRaisedException(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(EventId = PubSubMqttEventIds.MqttClientAdapter + 4, Level = LogLevel.Warning,
            Message = "Failed to deliver inbound MQTT message.")]
        public static partial void FailedToDeliverInboundMqttMessage(this ILogger logger, Exception exception);
    }

}
