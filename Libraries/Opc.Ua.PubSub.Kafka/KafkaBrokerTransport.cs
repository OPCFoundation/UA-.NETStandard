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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Kafka.Internal;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// <see cref="IPubSubTransport"/> implementation for the Apache Kafka
    /// broker profiles
    /// (<see cref="KafkaProfiles.PubSubKafkaJsonTransport"/> and
    /// <see cref="KafkaProfiles.PubSubKafkaUadpTransport"/>). One instance
    /// represents one <see cref="PubSubConnectionDataType"/> bound to a
    /// <c>kafka://</c> or <c>kafkas://</c> broker endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the broker mapping defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>, including the
    /// delivery-guarantee mapping realised through the producer
    /// <c>acks</c> / <c>enable.idempotence</c> settings. Payload encoding
    /// is opaque to the transport; the encoding profile is chosen by the
    /// writer-group <c>MessageSettings</c> on the connection
    /// (<see cref="JsonWriterGroupMessageDataType"/> →
    /// <see cref="KafkaProfiles.PubSubKafkaJsonTransport"/>,
    /// <see cref="UadpWriterGroupMessageDataType"/> →
    /// <see cref="KafkaProfiles.PubSubKafkaUadpTransport"/>).
    /// </para>
    /// <para>
    /// The transport delegates to an <see cref="IKafkaClientAdapter"/> so
    /// the Confluent.Kafka client surface is invisible to higher layers,
    /// and so unit tests can inject a fake adapter to exercise the state
    /// machine without an actual broker. Each produced record carries a
    /// partition key derived from the PublisherId so records from a
    /// publisher preserve ordering within a partition.
    /// </para>
    /// </remarks>
    public sealed class KafkaBrokerTransport
        : IPubSubTransport, IPubSubTopicProvider, IPubSubHeaderTransport
    {
        private readonly PubSubConnectionDataType m_connection;
        private readonly KafkaEndpoint m_endpoint;
        private readonly PubSubTransportDirection m_direction;
        private readonly KafkaConnectionOptions m_options;
        private readonly IKafkaClientFactory m_clientFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly IPubSubDiagnostics? m_diagnostics;
        private readonly ILogger m_logger;
        private readonly System.Threading.Lock m_sync = new();
        private readonly string m_transportProfileUri;
        private readonly byte[] m_partitionKey;

        private IKafkaClientAdapter? m_adapter;
        private Channel<PubSubTransportFrame>? m_channel;
        private bool m_isConnected;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="KafkaBrokerTransport"/>.
        /// </summary>
        /// <param name="connection">
        /// PubSubConnection configuration the transport is bound to.
        /// </param>
        /// <param name="endpoint">
        /// Parsed broker endpoint from
        /// <see cref="KafkaEndpointParser.Parse"/>.
        /// </param>
        /// <param name="direction">
        /// Direction the transport services.
        /// </param>
        /// <param name="options">
        /// Resolved connection options (credentials already populated by
        /// the factory).
        /// </param>
        /// <param name="clientFactory">
        /// Factory used to create the underlying Kafka client adapter.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry context for per-instance logger creation.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used for receive-time stamps.
        /// </param>
        /// <param name="diagnostics">
        /// Optional diagnostics sink. Counters are incremented per inbound
        /// / outbound frame when non-<see langword="null"/>.
        /// </param>
        public KafkaBrokerTransport(
            PubSubConnectionDataType connection,
            KafkaEndpoint endpoint,
            PubSubTransportDirection direction,
            KafkaConnectionOptions options,
            IKafkaClientFactory clientFactory,
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
            m_logger = telemetry.CreateLogger<KafkaBrokerTransport>();
            m_transportProfileUri = DetermineTransportProfileUri(connection);
            m_partitionKey = BuildPartitionKey(connection);
            Subscriptions = new List<string>();
            AddDefaultSubscriptions();
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
        /// integration tests can confirm bootstrap server selection
        /// without re-parsing the URL.
        /// </summary>
        public KafkaEndpoint Endpoint => m_endpoint;

        /// <summary>
        /// Resolved connection options. Exposed for diagnostics and
        /// tests; the password bytes are never serialized.
        /// </summary>
        public KafkaConnectionOptions Options => m_options;

        /// <summary>
        /// Kafka topics the subscriber consumes from. Populated from the
        /// connection's reader-group broker transport settings
        /// (QueueName / MetaDataQueueName); callers may append additional
        /// topics before <see cref="OpenAsync"/>.
        /// </summary>
        public IList<string> Subscriptions { get; }

        /// <inheritdoc/>
        public event EventHandler<PubSubTransportStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public async ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IKafkaClientAdapter adapter;
            Channel<PubSubTransportFrame>? channel = null;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(KafkaBrokerTransport));
                }
                if (m_adapter is not null)
                {
                    return;
                }
                adapter = m_clientFactory.Create(m_telemetry, m_timeProvider);
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
                    var topicList = new List<string>(Subscriptions);
                    foreach (string topic in topicList)
                    {
                        ValidateTopic(topic);
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
                "Kafka transport opened: connection='{Connection}' bootstrap={Bootstrap} direction={Direction} profile={Profile}",
                m_connection.Name,
                m_endpoint.BootstrapServers,
                m_direction,
                m_transportProfileUri);
            RaiseStateChanged(true, StatusCodes.Good, null);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            IKafkaClientAdapter? adapter;
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
                        "Kafka disconnect for connection '{Connection}' raised an exception.",
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
        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic = null,
            CancellationToken cancellationToken = default)
            => ProduceInternalAsync(payload, topic, null, cancellationToken);

        /// <inheritdoc/>
        public ValueTask SendAsync(
            ReadOnlyMemory<byte> payload,
            string? topic,
            ArrayOf<PubSubMessageProperty> properties,
            CancellationToken cancellationToken = default)
            => ProduceInternalAsync(payload, topic, ToHeaders(properties), cancellationToken);

        private static Dictionary<string, string>? ToHeaders(
            ArrayOf<PubSubMessageProperty> properties)
        {
            if (properties.Count == 0)
            {
                return null;
            }
            var headers = new Dictionary<string, string>(properties.Count, StringComparer.Ordinal);
            for (int i = 0; i < properties.Count; i++)
            {
                PubSubMessageProperty property = properties[i];
                headers[property.Name] = property.Value;
            }
            return headers;
        }

        private async ValueTask ProduceInternalAsync(
            ReadOnlyMemory<byte> payload,
            string? topic,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(topic))
            {
                throw new ArgumentException(
                    "Kafka broker transport requires a topic for every Send.",
                    nameof(topic));
            }
            ValidateTopic(topic);

            IKafkaClientAdapter? adapter;
            lock (m_sync)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(KafkaBrokerTransport));
                }
                adapter = m_adapter;
            }
            if (adapter is null)
            {
                throw new InvalidOperationException(
                    "Kafka transport must be opened before sending.");
            }

            string? contentType = MapContentType(m_transportProfileUri);
            var message = new KafkaMessage(
                topic,
                m_partitionKey,
                payload,
                contentType,
                headers);

            await adapter.ProduceAsync(message, cancellationToken).ConfigureAwait(false);
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

        /// <inheritdoc/>
        public string BuildMetaDataTopic(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId)
        {
            if (TryFindWriter(writerGroupId, dataSetWriterId, out DataSetWriterDataType? writer)
                && writer is not null
                && TryReadBrokerWriterSettings(
                    writer.TransportSettings, out _, out string? metadataQueue)
                && !string.IsNullOrEmpty(metadataQueue))
            {
                return metadataQueue;
            }
            return BuildDefaultTopic(
                ResolveEncoding(m_transportProfileUri),
                "metadata",
                publisherId.ToString(),
                writerGroupId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                dataSetWriterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public string BuildDataTopic(
            PublisherId publisherId,
            WriterGroupDataType writerGroup,
            ushort? dataSetWriterId)
        {
            if (writerGroup is null)
            {
                throw new ArgumentNullException(nameof(writerGroup));
            }
            if (dataSetWriterId.HasValue
                && TryFindWriter(writerGroup.WriterGroupId, dataSetWriterId.Value, out DataSetWriterDataType? writer)
                && writer is not null
                && TryReadBrokerWriterSettings(writer.TransportSettings, out string? queue, out _)
                && !string.IsNullOrEmpty(queue))
            {
                return queue;
            }
            if (TryReadBrokerGroupSettings(writerGroup.TransportSettings, out string? groupQueue)
                && !string.IsNullOrEmpty(groupQueue))
            {
                return groupQueue;
            }
            string? writerSegment = dataSetWriterId.HasValue
                ? dataSetWriterId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            return BuildDefaultTopic(
                ResolveEncoding(m_transportProfileUri),
                "data",
                publisherId.ToString(),
                writerGroup.WriterGroupId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                writerSegment);
        }

        /// <inheritdoc/>
        public string BuildDiscoveryTopic(PublisherId publisherId, string messageTypeSegment)
        {
            return BuildDefaultTopic(
                ResolveEncoding(m_transportProfileUri),
                messageTypeSegment,
                publisherId.ToString(),
                additional1: null,
                additional2: null);
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

        private void OnIncomingMessage(object? sender, KafkaIncomingMessageEventArgs e)
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
                e.Message.Value,
                e.Message.Topic,
                e.ReceivedAt);
            if (!channel.Writer.TryWrite(frame))
            {
                m_logger.LogWarning(
                    "Dropped inbound Kafka frame for connection '{Connection}': receive queue full.",
                    m_connection.Name);
                return;
            }
            m_diagnostics?.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages, 1);
        }

        private void OnConnectionStateChanged(
            object? sender,
            KafkaConnectionStateChangedEventArgs e)
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

        private void AddDefaultSubscriptions()
        {
            if (!HasReceiveDirection || m_connection.ReaderGroups.IsNull)
            {
                return;
            }
            foreach (ReaderGroupDataType group in m_connection.ReaderGroups)
            {
                if (group.DataSetReaders.IsNull)
                {
                    continue;
                }
                foreach (DataSetReaderDataType reader in group.DataSetReaders)
                {
                    if (TryReadBrokerReaderSettings(
                            reader.TransportSettings,
                            out string? queue,
                            out string? metadataQueue))
                    {
                        AddSubscription(queue);
                        AddSubscription(metadataQueue);
                    }
                }
            }
        }

        private void AddSubscription(string? topic)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return;
            }
            foreach (string existing in Subscriptions)
            {
                if (string.Equals(existing, topic, StringComparison.Ordinal))
                {
                    return;
                }
            }
            Subscriptions.Add(topic);
        }

        private bool TryFindWriter(
            ushort writerGroupId,
            ushort dataSetWriterId,
            out DataSetWriterDataType? writer)
        {
            writer = null;
            if (m_connection.WriterGroups.IsNull)
            {
                return false;
            }
            foreach (WriterGroupDataType group in m_connection.WriterGroups)
            {
                if (group.WriterGroupId != writerGroupId || group.DataSetWriters.IsNull)
                {
                    continue;
                }
                foreach (DataSetWriterDataType candidate in group.DataSetWriters)
                {
                    if (candidate.DataSetWriterId == dataSetWriterId)
                    {
                        writer = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryReadBrokerGroupSettings(
            ExtensionObject settings,
            out string? queueName)
        {
            queueName = null;
            if (!settings.TryGetValue(out BrokerWriterGroupTransportDataType? broker) || broker is null)
            {
                return false;
            }
            queueName = broker.QueueName;
            return true;
        }

        private static bool TryReadBrokerWriterSettings(
            ExtensionObject settings,
            out string? queueName,
            out string? metaDataQueueName)
        {
            queueName = null;
            metaDataQueueName = null;
            if (!settings.TryGetValue(out BrokerDataSetWriterTransportDataType? broker) || broker is null)
            {
                return false;
            }
            queueName = broker.QueueName;
            metaDataQueueName = broker.MetaDataQueueName;
            return true;
        }

        private static bool TryReadBrokerReaderSettings(
            ExtensionObject settings,
            out string? queueName,
            out string? metaDataQueueName)
        {
            queueName = null;
            metaDataQueueName = null;
            if (!settings.TryGetValue(out BrokerDataSetReaderTransportDataType? broker) || broker is null)
            {
                return false;
            }
            queueName = broker.QueueName;
            metaDataQueueName = broker.MetaDataQueueName;
            return true;
        }

        private string BuildDefaultTopic(
            KafkaEncoding encoding,
            string messageType,
            string publisherId,
            string? additional1,
            string? additional2)
        {
            var builder = new StringBuilder();
            AppendSegment(builder, m_options.Topics.Prefix);
            AppendSegment(builder, encoding.ToTopicSegment());
            AppendSegment(builder, messageType);
            AppendSegment(builder, publisherId);
            AppendSegment(builder, additional1);
            AppendSegment(builder, additional2);
            return builder.ToString();
        }

        private static void AppendSegment(StringBuilder builder, string? segment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return;
            }
            if (builder.Length > 0)
            {
                builder.Append('.');
            }
            builder.Append(SanitizeSegment(segment));
        }

        private static string SanitizeSegment(string segment)
        {
            var builder = new StringBuilder(segment.Length);
            foreach (char c in segment)
            {
                bool allowed = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c is '_' or '-';
                builder.Append(allowed ? c : '_');
            }
            return builder.ToString();
        }

        private static byte[] BuildPartitionKey(PubSubConnectionDataType connection)
        {
            if (!connection.PublisherId.IsNull)
            {
                string publisherId = PublisherId.From(connection.PublisherId).ToString();
                if (!string.IsNullOrEmpty(publisherId))
                {
                    return System.Text.Encoding.UTF8.GetBytes(publisherId);
                }
            }
            if (!string.IsNullOrEmpty(connection.Name))
            {
                return System.Text.Encoding.UTF8.GetBytes(connection.Name);
            }
            return Array.Empty<byte>();
        }

        private static void ValidateTopic(string topic)
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
        }

        private static KafkaEncoding ResolveEncoding(string transportProfileUri)
        {
            return string.Equals(
                transportProfileUri,
                KafkaProfiles.PubSubKafkaUadpTransport,
                StringComparison.Ordinal)
                ? KafkaEncoding.Uadp
                : KafkaEncoding.Json;
        }

        private static string? MapContentType(string transportProfileUri)
        {
            if (string.Equals(
                transportProfileUri,
                KafkaProfiles.PubSubKafkaJsonTransport,
                StringComparison.Ordinal))
            {
                return "application/json";
            }
            if (string.Equals(
                transportProfileUri,
                KafkaProfiles.PubSubKafkaUadpTransport,
                StringComparison.Ordinal))
            {
                return "application/opcua+uadp";
            }
            return null;
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
                    KafkaProfiles.PubSubKafkaUadpTransport,
                    StringComparison.Ordinal))
                {
                    return KafkaProfiles.PubSubKafkaUadpTransport;
                }
                if (string.Equals(
                    connection.TransportProfileUri,
                    KafkaProfiles.PubSubKafkaJsonTransport,
                    StringComparison.Ordinal))
                {
                    return KafkaProfiles.PubSubKafkaJsonTransport;
                }
            }
            return KafkaProfiles.PubSubKafkaJsonTransport;
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
                UadpWriterGroupMessageDataType => KafkaProfiles.PubSubKafkaUadpTransport,
                JsonWriterGroupMessageDataType => KafkaProfiles.PubSubKafkaJsonTransport,
                _ => null
            };
        }
    }
}
