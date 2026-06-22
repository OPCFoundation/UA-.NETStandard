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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// <see cref="IPubSubTransport"/> implementation for the MQTT
    /// broker profiles
    /// (<see cref="Profiles.PubSubMqttJsonTransport"/> and
    /// <see cref="Profiles.PubSubMqttUadpTransport"/>). One instance
    /// represents one
    /// <see cref="PubSubConnectionDataType"/> bound to an
    /// <c>mqtt://</c> or <c>mqtts://</c> broker endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the broker mapping defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>, including the
    /// retained-metadata handling from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
    /// §7.3.4.8</see> and the QoS mapping from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.5">
    /// §7.3.4.5</see>. Payload encoding is opaque to the transport;
    /// the encoding profile is chosen by the writer-group
    /// <c>MessageSettings</c> on the connection
    /// (<see cref="JsonWriterGroupMessageDataType"/> →
    /// <see cref="Profiles.PubSubMqttJsonTransport"/>,
    /// <see cref="UadpWriterGroupMessageDataType"/> →
    /// <see cref="Profiles.PubSubMqttUadpTransport"/>).
    /// </para>
    /// <para>
    /// The transport delegates to an <see cref="IMqttClientAdapter"/>
    /// so MQTTnet's v4 / v5 API drift is invisible to higher layers,
    /// and so unit tests can inject a fake adapter to exercise the
    /// state machine without an actual broker. Per-frame retain flags
    /// are set automatically for topics that match the §7.3.4.7.4
    /// metadata pattern when
    /// <see cref="MqttTopicOptions.RetainMetaDataMessages"/> is on.
    /// </para>
    /// </remarks>
    public sealed class MqttBrokerTransport : IPubSubTransport, IPubSubTopicProvider
    {
        private const string MetaDataTopicSegment = "/metadata/";

        private readonly PubSubConnectionDataType m_connection;
        private readonly MqttEndpoint m_endpoint;
        private readonly PubSubTransportDirection m_direction;
        private readonly MqttConnectionOptions m_options;
        private readonly IMqttClientFactory m_clientFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly IPubSubDiagnostics? m_diagnostics;
        private readonly ILogger m_logger;
        private readonly System.Threading.Lock m_sync = new();
        private readonly string m_transportProfileUri;

        private IMqttClientAdapter? m_adapter;
        private Channel<PubSubTransportFrame>? m_channel;
        private bool m_isConnected;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="MqttBrokerTransport"/>.
        /// </summary>
        /// <param name="connection">
        /// PubSubConnection configuration the transport is bound to.
        /// </param>
        /// <param name="endpoint">
        /// Parsed broker endpoint from
        /// <see cref="MqttEndpointParser.Parse"/>.
        /// </param>
        /// <param name="direction">
        /// Direction the transport services.
        /// </param>
        /// <param name="options">
        /// Resolved connection options (credentials already populated
        /// by the factory).
        /// </param>
        /// <param name="clientFactory">
        /// Factory used to create the underlying MQTT client adapter.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry context for per-instance logger creation.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used for receive-time stamps.
        /// </param>
        /// <param name="diagnostics">
        /// Optional diagnostics sink. Counters are incremented per
        /// inbound / outbound frame when non-<see langword="null"/>.
        /// </param>
        public MqttBrokerTransport(
            PubSubConnectionDataType connection,
            MqttEndpoint endpoint,
            PubSubTransportDirection direction,
            MqttConnectionOptions options,
            IMqttClientFactory clientFactory,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            IPubSubDiagnostics? diagnostics = null)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (clientFactory is null)
            {
                throw new ArgumentNullException(nameof(clientFactory));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            m_connection = connection;
            m_endpoint = endpoint;
            m_direction = direction;
            m_options = options;
            m_clientFactory = clientFactory;
            m_telemetry = telemetry;
            m_timeProvider = timeProvider;
            m_diagnostics = diagnostics;
            m_logger = telemetry.CreateLogger<MqttBrokerTransport>();
            m_transportProfileUri = DetermineTransportProfileUri(connection);
        }

        /// <inheritdoc/>
        public string TransportProfileUri => m_transportProfileUri;

        /// <inheritdoc/>
        public PubSubTransportDirection Direction => m_direction;

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                lock (m_sync)
                {
                    return m_isConnected;
                }
            }
        }

        /// <summary>
        /// Parsed endpoint the transport is bound to. Exposed so
        /// integration tests can confirm host / port selection without
        /// re-parsing the URL.
        /// </summary>
        public MqttEndpoint Endpoint => m_endpoint;

        /// <summary>
        /// Resolved connection options. Exposed for diagnostics and
        /// tests; the password bytes are never serialized.
        /// </summary>
        public MqttConnectionOptions Options => m_options;

        /// <summary>
        /// Topic subscriptions installed on the broker session. May be
        /// supplied by the application layer; callers populate this list
        /// before <see cref="OpenAsync"/>
        /// so the adapter knows what topics to subscribe to.
        /// </summary>
        public IList<MqttTopicFilter> Subscriptions { get; } = new List<MqttTopicFilter>();

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IMqttClientAdapter adapter;
            Channel<PubSubTransportFrame>? channel = null;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(MqttBrokerTransport));
                }
                if (m_adapter is not null)
                {
                    return;
                }
                adapter = m_clientFactory.CreateAdapter(m_options, m_telemetry, m_timeProvider);
                if (HasReceiveDirection)
                {
                    channel = Channel.CreateBounded<PubSubTransportFrame>(
                        new BoundedChannelOptions(GetReceiveQueueCapacity())
                        {
                            FullMode = BoundedChannelFullMode.DropOldest,
                            SingleReader = false,
                            SingleWriter = true
                        });
                    m_channel = channel;
                }
                m_adapter = adapter;
            }

            adapter.IncomingMessage += OnIncomingMessage;
            adapter.ConnectionStateChanged += OnConnectionStateChanged;

            try
            {
                await adapter.ConnectAsync(m_options, cancellationToken).ConfigureAwait(false);
                if (HasReceiveDirection && Subscriptions.Count > 0)
                {
                    var topicList = new List<MqttTopicFilter>(Subscriptions);
                    if (topicList.Count > m_options.MaxConcurrentSubscriptions)
                    {
                        throw new InvalidOperationException(
                            $"Requested {topicList.Count} subscriptions exceeds " +
                            $"MaxConcurrentSubscriptions={m_options.MaxConcurrentSubscriptions}.");
                    }
                    foreach (MqttTopicFilter filter in topicList)
                    {
                        ValidateTopic(filter.Topic, allowWildcards: true);
                    }
                    await adapter.SubscribeAsync(topicList, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                adapter.IncomingMessage -= OnIncomingMessage;
                adapter.ConnectionStateChanged -= OnConnectionStateChanged;
                lock (m_sync)
                {
                    m_adapter = null;
                    m_channel = null;
                }
                channel?.Writer.TryComplete();
                await adapter.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            lock (m_sync)
            {
                m_isConnected = true;
            }
            m_logger.LogInformation(
                "MQTT transport opened: connection='{Connection}' endpoint={Endpoint} direction={Direction} profile={Profile}",
                m_connection.Name,
                m_endpoint,
                m_direction,
                m_transportProfileUri);
            RaiseStateChanged(true, StatusCodes.Good, null);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            IMqttClientAdapter? adapter;
            Channel<PubSubTransportFrame>? channel;
            bool wasConnected;
            lock (m_sync)
            {
                adapter = m_adapter;
                channel = m_channel;
                wasConnected = m_isConnected;
                m_adapter = null;
                m_channel = null;
                m_isConnected = false;
            }

            if (adapter is not null)
            {
                adapter.IncomingMessage -= OnIncomingMessage;
                adapter.ConnectionStateChanged -= OnConnectionStateChanged;
                try
                {
                    await adapter.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(
                        ex,
                        "MQTT disconnect for connection '{Connection}' raised an exception.",
                        m_connection.Name);
                }
                await adapter.DisposeAsync().ConfigureAwait(false);
            }
            channel?.Writer.TryComplete();
            if (wasConnected)
            {
                RaiseStateChanged(false, StatusCodes.Good, "Transport closed.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(topic))
            {
                throw new ArgumentException(
                    "MQTT broker transport requires a topic for every Send.",
                    nameof(topic));
            }
            ValidateTopic(topic, allowWildcards: false);

            IMqttClientAdapter? adapter;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(MqttBrokerTransport));
                }
                adapter = m_adapter;
            }
            if (adapter is null)
            {
                throw new InvalidOperationException(
                    "MQTT transport must be opened before sending.");
            }

            bool isMetaData = IsMetaDataTopic(topic);
            bool retain = isMetaData && m_options.Topics.RetainMetaDataMessages;
            string? contentType = MapContentType(m_transportProfileUri);
            var message = new MqttMessage(
                topic,
                payload,
                m_options.Topics.DefaultQos,
                retain,
                contentType,
                ResponseTopic: null);

            await adapter.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            m_diagnostics?.Increment(PubSubDiagnosticsCounterKind.SentNetworkMessages, 1);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<PubSubTransportFrame> ReceiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Channel<PubSubTransportFrame>? channel;
            lock (m_sync)
            {
                channel = m_channel;
            }
            if (channel is null)
            {
                yield break;
            }
            ChannelReader<PubSubTransportFrame> reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out PubSubTransportFrame frame))
                {
                    yield return frame;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            bool alreadyDisposed;
            lock (m_sync)
            {
                alreadyDisposed = m_disposed;
                m_disposed = true;
            }
            if (alreadyDisposed)
            {
                return;
            }
            await CloseAsync().ConfigureAwait(false);
        }

        private bool HasReceiveDirection =>
            (m_direction & PubSubTransportDirection.Receive) != 0;

        private int GetReceiveQueueCapacity()
        {
            int subscriptions = Subscriptions.Count;
            if (subscriptions <= 0)
            {
                return 256;
            }
            int capacity = subscriptions * 16;
            return capacity < 256 ? 256 : capacity;
        }

        private void OnIncomingMessage(object? sender, MqttIncomingMessageEventArgs e)
        {
            Channel<PubSubTransportFrame>? channel;
            lock (m_sync)
            {
                channel = m_channel;
            }
            if (channel is null)
            {
                return;
            }
            var frame = new PubSubTransportFrame(
                e.Message.Payload,
                e.Message.Topic,
                e.ReceivedAt);
            if (!channel.Writer.TryWrite(frame))
            {
                m_logger.LogWarning(
                    "Dropped inbound MQTT frame for connection '{Connection}': receive queue full.",
                    m_connection.Name);
                return;
            }
            m_diagnostics?.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages, 1);
        }

        private void OnConnectionStateChanged(
            object? sender,
            MqttConnectionStateChangedEventArgs e)
        {
            lock (m_sync)
            {
                m_isConnected = e.IsConnected;
            }
            StatusCode status = e.IsConnected
                ? StatusCodes.Good
                : StatusCodes.BadConnectionClosed;
            RaiseStateChanged(e.IsConnected, status, e.Reason);
        }

        private void RaiseStateChanged(bool isConnected, StatusCode status, string? reason)
        {
            EventHandler<PubSubTransportStateChangedEventArgs>? handler = StateChanged;
            handler?.Invoke(
                this,
                new PubSubTransportStateChangedEventArgs(isConnected, status, reason));
        }

        /// <summary>
        /// Builds the per-DataSetWriter metadata topic for this
        /// connection per Part 14 §7.3.4.7.4. The encoding segment is
        /// chosen from <see cref="TransportProfileUri"/> so the same
        /// MQTT broker transport works for both JSON and UADP MQTT
        /// connections without further configuration.
        /// </summary>
        /// <param name="publisherId">PublisherId.</param>
        /// <param name="writerGroupId">WriterGroupId.</param>
        /// <param name="dataSetWriterId">DataSetWriterId.</param>
        /// <returns>The constructed topic string.</returns>
        public string BuildMetaDataTopic(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId)
        {
            MqttEncoding encoding = string.Equals(
                m_transportProfileUri,
                Profiles.PubSubMqttUadpTransport,
                StringComparison.Ordinal)
                ? MqttEncoding.Uadp
                : MqttEncoding.Json;
            return MqttTopicBuilder.BuildMetaDataTopic(
                m_options.Topics.Prefix,
                encoding,
                publisherId.ToVariant(),
                writerGroupId,
                dataSetWriterId);
        }

        private static bool IsMetaDataTopic(string topic)
        {
            return topic.Contains(MetaDataTopicSegment, StringComparison.Ordinal);
        }

        private static string? MapContentType(string transportProfileUri)
        {
            if (string.Equals(
                transportProfileUri,
                Profiles.PubSubMqttJsonTransport,
                StringComparison.Ordinal))
            {
                return "application/json";
            }
            if (string.Equals(
                transportProfileUri,
                Profiles.PubSubMqttUadpTransport,
                StringComparison.Ordinal))
            {
                return "application/opcua+uadp";
            }
            return null;
        }

        private static void ValidateTopic(string topic, bool allowWildcards)
        {
            if (string.IsNullOrEmpty(topic))
            {
                throw new ArgumentException("Topic must not be empty.", nameof(topic));
            }
            if (topic.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Topic must not contain a null character.",
                    nameof(topic));
            }
            if (!allowWildcards)
            {
                if (topic.Contains('#', StringComparison.Ordinal)
                    || topic.Contains('+', StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Publish topic must not contain wildcards ('#' or '+').",
                        nameof(topic));
                }
            }
        }

        private static string DetermineTransportProfileUri(PubSubConnectionDataType connection)
        {
            if (!connection.WriterGroups.IsNull)
            {
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    string? profile = InferProfileFromMessageSettings(group.MessageSettings);
                    if (profile is not null)
                    {
                        return profile;
                    }
                }
            }
            if (!string.IsNullOrEmpty(connection.TransportProfileUri))
            {
                if (string.Equals(
                    connection.TransportProfileUri,
                    Profiles.PubSubMqttUadpTransport,
                    StringComparison.Ordinal))
                {
                    return Profiles.PubSubMqttUadpTransport;
                }
                if (string.Equals(
                    connection.TransportProfileUri,
                    Profiles.PubSubMqttJsonTransport,
                    StringComparison.Ordinal))
                {
                    return Profiles.PubSubMqttJsonTransport;
                }
            }
            return Profiles.PubSubMqttJsonTransport;
        }

        private static string? InferProfileFromMessageSettings(ExtensionObject settings)
        {
            if (settings.IsNull)
            {
                return null;
            }
            IEncodeable? decoded = ExtensionObject.ToEncodeable(settings);
            return decoded switch
            {
                UadpWriterGroupMessageDataType => Profiles.PubSubMqttUadpTransport,
                JsonWriterGroupMessageDataType => Profiles.PubSubMqttJsonTransport,
                _ => null
            };
        }
    }
}
