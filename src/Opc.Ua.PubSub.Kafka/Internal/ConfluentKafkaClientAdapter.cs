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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Confluent.Kafka-backed implementation of
    /// <see cref="IKafkaClientAdapter"/>.
    /// </summary>
    /// <remarks>
    /// The adapter owns a lazily created <see cref="IProducer{TKey,TValue}"/>
    /// (built on first produce) and a lazily created
    /// <see cref="IConsumer{TKey,TValue}"/> plus a background consume
    /// loop (built on first subscribe), so a send-only or receive-only
    /// connection never instantiates the unused half. Confluent.Kafka
    /// wraps native librdkafka via P/Invoke and is therefore not
    /// NativeAOT/trim-safe. Select it only for JIT-compiled hosts through
    /// <c>WithConfluentKafkaClient()</c>.
    /// </remarks>
    internal sealed class ConfluentKafkaClientAdapter : IKafkaClientAdapter
    {
        private const string ContentTypeHeader = "content-type";

        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_sync = new();
        private readonly List<string> m_subscribedTopics = [];

        private KafkaConnectionOptions? m_options;
        private IProducer<byte[], byte[]>? m_producer;
        private IConsumer<byte[], byte[]>? m_consumer;
        private CancellationTokenSource? m_loopCts;
        private Task? m_consumeTask;
        private bool m_connected;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="ConfluentKafkaClientAdapter"/>.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock for receive-time stamps.</param>
        public ConfluentKafkaClientAdapter(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_logger = telemetry.CreateLogger<ConfluentKafkaClientAdapter>();
            m_timeProvider = timeProvider;
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                lock (m_sync)
                {
                    return m_connected;
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler<KafkaIncomingMessageEventArgs>? IncomingMessage;

        /// <inheritdoc/>
        public event EventHandler<KafkaConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <inheritdoc/>
        public ValueTask ConnectAsync(KafkaConnectionOptions options, CancellationToken ct)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            ValidateCredentialTransport(options);

            bool raiseConnected;
            lock (m_sync)
            {
                m_options = options;
                m_loopCts ??= new CancellationTokenSource();
                raiseConnected = !m_connected;
                m_connected = true;
            }
            m_logger.KafkaAdapterConnected(
                string.IsNullOrEmpty(options.BootstrapServers) ? options.Endpoint : options.BootstrapServers,
                options.SecurityProtocol);
            if (raiseConnected)
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    new KafkaConnectionStateChangedEventArgs(isConnected: true, reason: null));
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask DisconnectAsync(CancellationToken ct)
        {
            await ShutdownAsync(raiseEvent: true).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask SubscribeAsync(IReadOnlyList<string> topics, CancellationToken ct)
        {
            if (topics is null)
            {
                throw new ArgumentNullException(nameof(topics));
            }
            ThrowIfDisposed();
            if (topics.Count == 0)
            {
                return default;
            }

            lock (m_sync)
            {
                IConsumer<byte[], byte[]> consumer = EnsureConsumer();
                bool changed = false;
                foreach (string topic in topics)
                {
                    if (!m_subscribedTopics.Contains(topic))
                    {
                        m_subscribedTopics.Add(topic);
                        changed = true;
                    }
                }
                if (changed)
                {
                    consumer.Subscribe(m_subscribedTopics);
                }
                m_consumeTask ??= Task.Factory.StartNew(
                    () => ConsumeLoop(consumer, m_options!.EnableAutoCommit, m_loopCts!.Token),
                    m_loopCts!.Token,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
            m_logger.KafkaSubscribed(topics.Count);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask UnsubscribeAsync(IReadOnlyList<string> topics, CancellationToken ct)
        {
            if (topics is null)
            {
                throw new ArgumentNullException(nameof(topics));
            }
            ThrowIfDisposed();
            if (topics.Count == 0)
            {
                return default;
            }

            lock (m_sync)
            {
                if (m_consumer is null)
                {
                    return default;
                }
                bool changed = false;
                foreach (string topic in topics)
                {
                    if (m_subscribedTopics.Remove(topic))
                    {
                        changed = true;
                    }
                }
                if (changed)
                {
                    if (m_subscribedTopics.Count == 0)
                    {
                        m_consumer.Unsubscribe();
                    }
                    else
                    {
                        m_consumer.Subscribe(m_subscribedTopics);
                    }
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask ProduceAsync(KafkaMessage message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(message.Topic))
            {
                throw new ArgumentException("Kafka produce requires a topic.", nameof(message));
            }
            ThrowIfDisposed();

            IProducer<byte[], byte[]> producer;
            lock (m_sync)
            {
                producer = EnsureProducer();
            }

            var headers = new Headers();
            if (!string.IsNullOrEmpty(message.ContentType))
            {
                headers.Add(ContentTypeHeader, System.Text.Encoding.UTF8.GetBytes(message.ContentType));
            }
            if (message.Headers is not null)
            {
                foreach (KeyValuePair<string, string> header in message.Headers)
                {
                    headers.Add(header.Key, System.Text.Encoding.UTF8.GetBytes(header.Value));
                }
            }

            var record = new Message<byte[], byte[]>
            {
                Key = message.Key.IsEmpty ? null! : message.Key.ToArray(),
                Value = message.Value.IsEmpty ? [] : message.Value.ToArray(),
                Headers = headers
            };
            await producer.ProduceAsync(message.Topic, record, ct).ConfigureAwait(false);
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
            await ShutdownAsync(raiseEvent: false).ConfigureAwait(false);
        }

        private async ValueTask ShutdownAsync(bool raiseEvent)
        {
            CancellationTokenSource? loopCts;
            Task? consumeTask;
            IConsumer<byte[], byte[]>? consumer;
            IProducer<byte[], byte[]>? producer;
            bool wasConnected;
            lock (m_sync)
            {
                loopCts = m_loopCts;
                m_loopCts = null;
                consumeTask = m_consumeTask;
                m_consumeTask = null;
                consumer = m_consumer;
                m_consumer = null;
                producer = m_producer;
                m_producer = null;
                wasConnected = m_connected;
                m_connected = false;
                m_subscribedTopics.Clear();
            }

            if (loopCts is not null)
            {
                try
                {
                    loopCts.Cancel();
                }
                catch (Exception ex)
                {
                    m_logger.KafkaConsumeLoopCancellationRaisedException(ex);
                }
            }
            if (consumeTask is not null)
            {
                try
                {
                    await consumeTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.KafkaConsumeLoopTerminatedWithException(ex);
                }
            }
            if (consumer is not null)
            {
                try
                {
                    consumer.Close();
                }
                catch (Exception ex)
                {
                    m_logger.KafkaConsumerCloseRaisedException(ex);
                }
                consumer.Dispose();
            }
            if (producer is not null)
            {
                try
                {
                    producer.Flush(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    m_logger.KafkaProducerFlushRaisedException(ex);
                }
                producer.Dispose();
            }
            loopCts?.Dispose();

            if (raiseEvent && wasConnected)
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    new KafkaConnectionStateChangedEventArgs(
                        isConnected: false,
                        reason: "Kafka adapter disconnected."));
            }
        }

        private IProducer<byte[], byte[]> EnsureProducer()
        {
            if (m_producer is not null)
            {
                return m_producer;
            }
            ProducerConfig config = CreateProducerConfig(m_options!);
            m_producer = new ProducerBuilder<byte[], byte[]>(config)
                .SetLogHandler((_, message) => OnLog(message))
                .SetErrorHandler((_, error) => OnError(error))
                .Build();
            return m_producer;
        }

        private IConsumer<byte[], byte[]> EnsureConsumer()
        {
            if (m_consumer is not null)
            {
                return m_consumer;
            }
            ConsumerConfig config = CreateConsumerConfig(m_options!);
            m_consumer = new ConsumerBuilder<byte[], byte[]>(config)
                .SetLogHandler((_, message) => OnLog(message))
                .SetErrorHandler((_, error) => OnError(error))
                .Build();
            return m_consumer;
        }

        private void ConsumeLoop(
            IConsumer<byte[], byte[]> consumer,
            bool enableAutoCommit,
            CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    ConsumeResult<byte[], byte[]> result;
                    try
                    {
                        result = consumer.Consume(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ConsumeException ex)
                    {
                        m_logger.KafkaConsumeError(ex, ex.Error.Code, ex.Error.Reason);
                        continue;
                    }
                    if (result?.Message is null || result.IsPartitionEOF)
                    {
                        continue;
                    }
                    try
                    {
                        DispatchRecord(result);
                        if (!enableAutoCommit)
                        {
                            consumer.Commit(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.FailedToDispatchInboundKafkaRecord(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.KafkaConsumeLoopTerminatedUnexpectedly(ex);
            }
        }

        private void DispatchRecord(ConsumeResult<byte[], byte[]> result)
        {
            string? contentType = null;
            Dictionary<string, string>? extraHeaders = null;
            Headers headers = result.Message.Headers;
            if (headers is not null && headers.Count > 0)
            {
                foreach (IHeader header in headers)
                {
                    string value = System.Text.Encoding.UTF8.GetString(header.GetValueBytes());
                    if (string.Equals(header.Key, ContentTypeHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = value;
                        continue;
                    }
                    extraHeaders ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    extraHeaders[header.Key] = value;
                }
            }
            byte[] key = result.Message.Key ?? [];
            byte[] value2 = result.Message.Value ?? [];
            var message = new KafkaMessage(result.Topic, key, value2, contentType, extraHeaders);
            var args = new KafkaIncomingMessageEventArgs(
                message,
                DateTimeUtc.From(m_timeProvider.GetUtcNow()));
            IncomingMessage?.Invoke(this, args);
        }

        private void OnLog(LogMessage message)
        {
            m_logger.LibrdkafkaTrace(message.Facility, message.Message);
        }

        private void OnError(Error error)
        {
            if (error.IsFatal)
            {
                m_logger.KafkaFatalError(error.Code, error.Reason);
            }
            else
            {
                m_logger.KafkaError(error.Code, error.Reason);
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(ConfluentKafkaClientAdapter));
            }
        }

        private static ProducerConfig CreateProducerConfig(KafkaConnectionOptions options)
        {
            var config = new ProducerConfig();
            ApplyCommonConfig(config, options);
            KafkaDeliveryGuarantee guarantee = options.DeliveryGuarantee.ToDeliveryGuarantee();
            config.Acks = MapAcks(guarantee.Acks);
            config.EnableIdempotence = guarantee.EnableIdempotence;
            config.MessageTimeoutMs = (int)options.MessageTimeout.TotalMilliseconds;
            config.MessageMaxBytes = options.MaxMessageSize;
            return config;
        }

        private static ConsumerConfig CreateConsumerConfig(KafkaConnectionOptions options)
        {
            var config = new ConsumerConfig();
            ApplyCommonConfig(config, options);
            config.GroupId = ResolveGroupId(options);
            config.AutoOffsetReset = MapAutoOffsetReset(options.AutoOffsetReset);
            config.EnableAutoCommit = options.EnableAutoCommit;
            return config;
        }

        private static void ApplyCommonConfig(ClientConfig config, KafkaConnectionOptions options)
        {
            string bootstrap = options.BootstrapServers;
            if (string.IsNullOrEmpty(bootstrap) && !string.IsNullOrEmpty(options.Endpoint))
            {
                bootstrap = KafkaEndpointParser.Parse(options.Endpoint).BootstrapServers;
            }
            config.BootstrapServers = bootstrap;
            if (!string.IsNullOrEmpty(options.ClientId))
            {
                config.ClientId = options.ClientId;
            }
            config.SecurityProtocol = MapSecurityProtocol(options.SecurityProtocol);
            if (options.SaslMechanism != KafkaSaslMechanism.None)
            {
                config.SaslMechanism = MapSaslMechanism(options.SaslMechanism);
                if (!string.IsNullOrEmpty(options.UserName))
                {
                    config.SaslUsername = options.UserName;
                }
                if (options.PasswordBytes is { Length: > 0 } passwordBytes)
                {
                    config.SaslPassword = System.Text.Encoding.UTF8.GetString(passwordBytes);
                }
            }
            KafkaTlsOptions? tls = options.Tls;
            if (tls is not null)
            {
                if (!string.IsNullOrEmpty(tls.CaCertificatePath))
                {
                    config.SslCaLocation = tls.CaCertificatePath;
                }
                if (!string.IsNullOrEmpty(tls.ClientCertificatePath))
                {
                    config.SslCertificateLocation = tls.ClientCertificatePath;
                }
                if (!string.IsNullOrEmpty(tls.ClientKeyPath))
                {
                    config.SslKeyLocation = tls.ClientKeyPath;
                }
                config.EnableSslCertificateVerification = tls.ValidateServerCertificate;
            }
        }

        private static void ValidateCredentialTransport(KafkaConnectionOptions options)
        {
            if (options.SaslMechanism == KafkaSaslMechanism.None ||
                string.IsNullOrEmpty(options.UserName))
            {
                return;
            }
            bool useTls = options.SecurityProtocol
                    is KafkaSecurityProtocol.Ssl
                    or KafkaSecurityProtocol.SaslSsl ||
                (options.Tls?.UseTls ?? false);
            if (!useTls && !options.AllowCredentialsOverPlaintext)
            {
                throw new InvalidOperationException(
                    "Kafka SASL credentials require TLS unless " +
                    "KafkaConnectionOptions.AllowCredentialsOverPlaintext is set.");
            }
        }

        private static string ResolveGroupId(KafkaConnectionOptions options)
        {
            if (!string.IsNullOrEmpty(options.GroupId))
            {
                return options.GroupId;
            }
            string prefix = string.IsNullOrEmpty(options.ClientId) ? "opcua-pubsub" : options.ClientId;
            return string.Concat(
                prefix,
                "-",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        }

        private static Acks MapAcks(KafkaAcks acks)
        {
            return acks switch
            {
                KafkaAcks.None => Acks.None,
                KafkaAcks.Leader => Acks.Leader,
                KafkaAcks.All => Acks.All,
                _ => Acks.All
            };
        }

        private static SecurityProtocol MapSecurityProtocol(KafkaSecurityProtocol protocol)
        {
            return protocol switch
            {
                KafkaSecurityProtocol.Plaintext => SecurityProtocol.Plaintext,
                KafkaSecurityProtocol.Ssl => SecurityProtocol.Ssl,
                KafkaSecurityProtocol.SaslPlaintext => SecurityProtocol.SaslPlaintext,
                KafkaSecurityProtocol.SaslSsl => SecurityProtocol.SaslSsl,
                _ => SecurityProtocol.Plaintext
            };
        }

        private static SaslMechanism MapSaslMechanism(KafkaSaslMechanism mechanism)
        {
            return mechanism switch
            {
                KafkaSaslMechanism.Plain => SaslMechanism.Plain,
                KafkaSaslMechanism.ScramSha256 => SaslMechanism.ScramSha256,
                KafkaSaslMechanism.ScramSha512 => SaslMechanism.ScramSha512,
                KafkaSaslMechanism.OAuthBearer => SaslMechanism.OAuthBearer,
                _ => SaslMechanism.Plain
            };
        }

        private static AutoOffsetReset MapAutoOffsetReset(KafkaAutoOffsetReset autoOffsetReset)
        {
            return autoOffsetReset switch
            {
                KafkaAutoOffsetReset.Earliest => AutoOffsetReset.Earliest,
                KafkaAutoOffsetReset.Latest => AutoOffsetReset.Latest,
                _ => AutoOffsetReset.Latest
            };
        }
    }

    /// <summary>
    /// Source-generated log messages for ConfluentKafkaClientAdapter.
    /// </summary>
    internal static partial class ConfluentKafkaClientAdapterLog
    {
        [LoggerMessage(EventId = PubSubKafkaEventIds.ConfluentKafkaClientAdapter + 0, Level = LogLevel.Warning,
            Message = "Kafka consume error {Code}: {Reason}")]
        public static partial void KafkaConsumeError(
            this ILogger logger,
            Exception exception,
            ErrorCode code,
            string? reason);

        [LoggerMessage(EventId = PubSubKafkaEventIds.ConfluentKafkaClientAdapter + 1, Level = LogLevel.Trace,
            Message = "librdkafka [{Facility}] {Message}")]
        public static partial void LibrdkafkaTrace(this ILogger logger, string facility, string message);

        [LoggerMessage(EventId = PubSubKafkaEventIds.ConfluentKafkaClientAdapter + 2, Level = LogLevel.Error,
            Message = "Kafka fatal error {Code}: {Reason}")]
        public static partial void KafkaFatalError(this ILogger logger, ErrorCode code, string? reason);

        [LoggerMessage(EventId = PubSubKafkaEventIds.ConfluentKafkaClientAdapter + 3, Level = LogLevel.Warning,
            Message = "Kafka error {Code}: {Reason}")]
        public static partial void KafkaError(this ILogger logger, ErrorCode code, string? reason);
    }
}
