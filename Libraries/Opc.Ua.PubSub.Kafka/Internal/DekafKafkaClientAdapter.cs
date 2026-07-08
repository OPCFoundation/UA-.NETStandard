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

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Dekaf;
using Dekaf.Consumer;
using Dekaf.Producer;
using Dekaf.Security;
using Dekaf.Serialization;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Kafka.Internal
{
    /// <summary>
    /// Dekaf-backed implementation of <see cref="IKafkaClientAdapter"/>.
    /// </summary>
    /// <remarks>
    /// The adapter owns a lazily created <see cref="IKafkaProducer{TKey,TValue}"/>
    /// (built on first produce) and a lazily created
    /// <see cref="IKafkaConsumer{TKey,TValue}"/> plus a background consume
    /// loop (built on first subscribe), so a send-only or receive-only
    /// connection never instantiates the unused half. Dekaf is a pure-managed
    /// .NET 10 Kafka client and is therefore usable by NativeAOT publishers.
    /// </remarks>
    internal sealed class DekafKafkaClientAdapter : IKafkaClientAdapter
    {
        private const string ContentTypeHeader = "content-type";

        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_sync = new();
        private readonly SemaphoreSlim m_clientGate = new(1, 1);
        private readonly List<string> m_subscribedTopics = [];

        private KafkaConnectionOptions? m_options;
        private IKafkaProducer<byte[], byte[]>? m_producer;
        private IKafkaConsumer<byte[], byte[]>? m_consumer;
        private CancellationTokenSource? m_loopCts;
        private Task? m_consumeTask;
        private bool m_connected;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="DekafKafkaClientAdapter"/>.
        /// </summary>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock for receive-time stamps.</param>
        public DekafKafkaClientAdapter(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_logger = telemetry.CreateLogger<DekafKafkaClientAdapter>();
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
            ValidateDekafSupport(options);

            bool raiseConnected;
            lock (m_sync)
            {
                m_options = options;
                m_loopCts ??= new CancellationTokenSource();
                raiseConnected = !m_connected;
                m_connected = true;
            }
            m_logger.LogDebug(
                "Kafka adapter connected to {BootstrapServers} (protocol={Protocol}).",
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
        public async ValueTask SubscribeAsync(IReadOnlyList<string> topics, CancellationToken ct)
        {
            if (topics is null)
            {
                throw new ArgumentNullException(nameof(topics));
            }
            ThrowIfDisposed();
            if (topics.Count == 0)
            {
                return;
            }

            await m_clientGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                IKafkaConsumer<byte[], byte[]> consumer = await EnsureConsumerAsync(ct).ConfigureAwait(false);
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
                    consumer.Subscribe([.. m_subscribedTopics]);
                }
                if (m_consumeTask is null)
                {
                    CancellationToken loopToken = m_loopCts!.Token;
                    bool enableAutoCommit = m_options!.EnableAutoCommit;
                    m_consumeTask = Task.Factory.StartNew(
                        () => ConsumeLoopAsync(consumer, enableAutoCommit, loopToken),
                        loopToken,
                        TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                        TaskScheduler.Default).Unwrap();
                }
            }
            finally
            {
                m_clientGate.Release();
            }
            m_logger.LogDebug("Kafka subscribed to {Count} topic(s).", topics.Count);
        }

        /// <inheritdoc/>
        public async ValueTask UnsubscribeAsync(IReadOnlyList<string> topics, CancellationToken ct)
        {
            if (topics is null)
            {
                throw new ArgumentNullException(nameof(topics));
            }
            ThrowIfDisposed();
            if (topics.Count == 0)
            {
                return;
            }

            await m_clientGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_consumer is null)
                {
                    return;
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
                        m_consumer.Subscribe([.. m_subscribedTopics]);
                    }
                }
            }
            finally
            {
                m_clientGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask ProduceAsync(KafkaMessage message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(message.Topic))
            {
                throw new ArgumentException("Kafka produce requires a topic.", nameof(message));
            }
            ThrowIfDisposed();

            Headers headers = CreateHeaders(message);
            byte[] key = message.Key.IsEmpty ? null! : message.Key.ToArray();
            byte[] value = message.Value.IsEmpty ? [] : message.Value.ToArray();
            var record = new ProducerMessage<byte[], byte[]>
            {
                Topic = message.Topic,
                Key = key,
                Value = value,
                Headers = headers
            };

            await m_clientGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                IKafkaProducer<byte[], byte[]> producer = await EnsureProducerAsync(ct).ConfigureAwait(false);
                await producer.ProduceAsync(record, ct).ConfigureAwait(false);
            }
            finally
            {
                m_clientGate.Release();
            }
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
            m_clientGate.Dispose();
        }

        private async ValueTask ShutdownAsync(bool raiseEvent)
        {
            await m_clientGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                CancellationTokenSource? loopCts;
                Task? consumeTask;
                IKafkaConsumer<byte[], byte[]>? consumer;
                IKafkaProducer<byte[], byte[]>? producer;
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
                        m_logger.LogDebug(ex, "Kafka consume loop cancellation raised an exception.");
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
                        m_logger.LogDebug(ex, "Kafka consume loop terminated with an exception.");
                    }
                }
                if (consumer is not null)
                {
                    try
                    {
                        await consumer.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogDebug(ex, "Kafka consumer close raised an exception.");
                    }
                    await consumer.DisposeAsync().ConfigureAwait(false);
                }
                if (producer is not null)
                {
                    try
                    {
                        using var flushCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await producer.FlushAsync(flushCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogDebug(ex, "Kafka producer flush raised an exception.");
                    }
                    await producer.DisposeAsync().ConfigureAwait(false);
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
            finally
            {
                m_clientGate.Release();
            }
        }

        private async ValueTask<IKafkaProducer<byte[], byte[]>> EnsureProducerAsync(CancellationToken ct)
        {
            if (m_producer is not null)
            {
                return m_producer;
            }
            ProducerBuilder<byte[], byte[]> builder = Dekaf.Kafka.CreateProducer<byte[], byte[]>();
            ApplyProducerConfig(builder, m_options!);
            m_producer = await builder.BuildAsync(ct).ConfigureAwait(false);
            return m_producer;
        }

        private async ValueTask<IKafkaConsumer<byte[], byte[]>> EnsureConsumerAsync(CancellationToken ct)
        {
            if (m_consumer is not null)
            {
                return m_consumer;
            }
            ConsumerBuilder<byte[], byte[]> builder = Dekaf.Kafka.CreateConsumer<byte[], byte[]>();
            ApplyConsumerConfig(builder, m_options!);
            m_consumer = await builder.BuildAsync(ct).ConfigureAwait(false);
            return m_consumer;
        }

        private async Task ConsumeLoopAsync(
            IKafkaConsumer<byte[], byte[]> consumer,
            bool enableAutoCommit,
            CancellationToken ct)
        {
            try
            {
                await foreach (ConsumeResult<byte[], byte[]> result in consumer.ConsumeAsync(ct).ConfigureAwait(false))
                {
                    if (result.IsPartitionEof)
                    {
                        continue;
                    }
                    try
                    {
                        DispatchRecord(result);
                        if (!enableAutoCommit)
                        {
                            await consumer.CommitAsync(ct).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(ex, "Failed to dispatch inbound Kafka record.");
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Kafka consume loop terminated unexpectedly.");
            }
        }

        private void DispatchRecord(ConsumeResult<byte[], byte[]> result)
        {
            string? contentType = null;
            Dictionary<string, string>? extraHeaders = null;
            IReadOnlyList<Header>? headers = result.Headers;
            if (headers is not null && headers.Count > 0)
            {
                foreach (Header header in headers)
                {
                    string headerValue = header.IsValueNull
                        ? string.Empty
                        : System.Text.Encoding.UTF8.GetString(header.Value.Span);
                    if (string.Equals(header.Key, ContentTypeHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = headerValue;
                        continue;
                    }
                    extraHeaders ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    extraHeaders[header.Key] = headerValue;
                }
            }
            byte[]? keyOrNull = result.Key;
            byte[]? valueOrNull = result.Value;
            byte[] key = keyOrNull ?? [];
            byte[] value = valueOrNull ?? [];
            var message = new KafkaMessage(result.Topic, key, value, contentType, extraHeaders);
            var args = new KafkaIncomingMessageEventArgs(
                message,
                DateTimeUtc.From(m_timeProvider.GetUtcNow()));
            IncomingMessage?.Invoke(this, args);
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(DekafKafkaClientAdapter));
            }
        }

        private static Headers CreateHeaders(KafkaMessage message)
        {
            var headers = Headers.Create();
            if (!string.IsNullOrEmpty(message.ContentType))
            {
                headers.Add(ContentTypeHeader, message.ContentType);
            }
            if (message.Headers is not null)
            {
                foreach (KeyValuePair<string, string> header in message.Headers)
                {
                    headers.Add(header.Key, header.Value);
                }
            }
            return headers;
        }

        private static void ApplyProducerConfig(
            ProducerBuilder<byte[], byte[]> builder,
            KafkaConnectionOptions options)
        {
            ApplyProducerCommonConfig(builder, options);
            KafkaDeliveryGuarantee guarantee = options.DeliveryGuarantee.ToDeliveryGuarantee();
            builder.WithAcks(MapAcks(guarantee.Acks));
            builder.WithIdempotence(guarantee.EnableIdempotence);
            builder.WithDeliveryTimeout(options.MessageTimeout);
        }

        private static void ApplyConsumerConfig(
            ConsumerBuilder<byte[], byte[]> builder,
            KafkaConnectionOptions options)
        {
            ApplyConsumerCommonConfig(builder, options);
            builder.WithGroupId(ResolveGroupId(options));
            builder.WithAutoOffsetReset(MapAutoOffsetReset(options.AutoOffsetReset));
            builder.WithOffsetCommitMode(options.EnableAutoCommit ? OffsetCommitMode.Auto : OffsetCommitMode.Manual);
        }

        private static void ApplyProducerCommonConfig(
            ProducerBuilder<byte[], byte[]> builder,
            KafkaConnectionOptions options)
        {
            builder.WithBootstrapServers(ResolveBootstrapServers(options));
            if (!string.IsNullOrEmpty(options.ClientId))
            {
                builder.WithClientId(options.ClientId);
            }
            ApplyProducerSecurity(builder, options);
        }

        private static void ApplyConsumerCommonConfig(
            ConsumerBuilder<byte[], byte[]> builder,
            KafkaConnectionOptions options)
        {
            builder.WithBootstrapServers(ResolveBootstrapServers(options));
            if (!string.IsNullOrEmpty(options.ClientId))
            {
                builder.WithClientId(options.ClientId);
            }
            ApplyConsumerSecurity(builder, options);
        }

        private static void ApplyProducerSecurity(
            ProducerBuilder<byte[], byte[]> builder,
            KafkaConnectionOptions options)
        {
            if (UseTls(options))
            {
                builder.UseTls(CreateTlsConfig(options));
            }
            switch (options.SaslMechanism)
            {
                case KafkaSaslMechanism.None:
                    break;
                case KafkaSaslMechanism.Plain:
                    builder.WithSaslPlain(RequireUserName(options), ResolvePassword(options));
                    break;
                case KafkaSaslMechanism.ScramSha256:
                    builder.WithSaslScramSha256(RequireUserName(options), ResolvePassword(options));
                    break;
                case KafkaSaslMechanism.ScramSha512:
                    builder.WithSaslScramSha512(RequireUserName(options), ResolvePassword(options));
                    break;
                case KafkaSaslMechanism.OAuthBearer:
                    throw CreateUnsupportedSaslMechanismException(options.SaslMechanism);
                default:
                    throw CreateUnsupportedSaslMechanismException(options.SaslMechanism);
            }
        }

        private static void ApplyConsumerSecurity(
            ConsumerBuilder<byte[], byte[]> builder,
            KafkaConnectionOptions options)
        {
            if (UseTls(options))
            {
                builder.UseTls(CreateTlsConfig(options));
            }
            switch (options.SaslMechanism)
            {
                case KafkaSaslMechanism.None:
                    break;
                case KafkaSaslMechanism.Plain:
                    builder.WithSaslPlain(RequireUserName(options), ResolvePassword(options));
                    break;
                case KafkaSaslMechanism.ScramSha256:
                    builder.WithSaslScramSha256(RequireUserName(options), ResolvePassword(options));
                    break;
                case KafkaSaslMechanism.ScramSha512:
                    builder.WithSaslScramSha512(RequireUserName(options), ResolvePassword(options));
                    break;
                case KafkaSaslMechanism.OAuthBearer:
                    throw CreateUnsupportedSaslMechanismException(options.SaslMechanism);
                default:
                    throw CreateUnsupportedSaslMechanismException(options.SaslMechanism);
            }
        }

        private static TlsConfig CreateTlsConfig(KafkaConnectionOptions options)
        {
            KafkaTlsOptions? tls = options.Tls;
            if (tls is null)
            {
                return new TlsConfig();
            }
            if (string.IsNullOrEmpty(tls.ClientCertificatePath) != string.IsNullOrEmpty(tls.ClientKeyPath))
            {
                throw new NotSupportedException(
                    "Dekaf mutual TLS requires both KafkaTlsOptions.ClientCertificatePath and " +
                    "KafkaTlsOptions.ClientKeyPath.");
            }
            return new TlsConfig
            {
                CaCertificatePath = tls.CaCertificatePath,
                ClientCertificatePath = tls.ClientCertificatePath,
                ClientKeyPath = tls.ClientKeyPath,
                ValidateServerCertificate = tls.ValidateServerCertificate
            };
        }

        private static void ValidateDekafSupport(KafkaConnectionOptions options)
        {
            if (options.SaslMechanism == KafkaSaslMechanism.OAuthBearer)
            {
                throw CreateUnsupportedSaslMechanismException(options.SaslMechanism);
            }
            _ = CreateTlsConfig(options);
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

        private static string ResolveBootstrapServers(KafkaConnectionOptions options)
        {
            string bootstrap = options.BootstrapServers;
            if (string.IsNullOrEmpty(bootstrap) && !string.IsNullOrEmpty(options.Endpoint))
            {
                bootstrap = KafkaEndpointParser.Parse(options.Endpoint).BootstrapServers;
            }
            return bootstrap;
        }

        private static bool UseTls(KafkaConnectionOptions options)
        {
            return options.SecurityProtocol
                    is KafkaSecurityProtocol.Ssl
                    or KafkaSecurityProtocol.SaslSsl ||
                (options.Tls?.UseTls ?? false);
        }

        private static string RequireUserName(KafkaConnectionOptions options)
        {
            if (string.IsNullOrEmpty(options.UserName))
            {
                throw new InvalidOperationException(
                    "Kafka SASL authentication requires KafkaConnectionOptions.UserName.");
            }
            return options.UserName;
        }

        private static string ResolvePassword(KafkaConnectionOptions options)
        {
            if (options.PasswordBytes is not { Length: > 0 } passwordBytes)
            {
                throw new InvalidOperationException(
                    "Kafka SASL authentication requires KafkaConnectionOptions.PasswordBytes.");
            }
            return System.Text.Encoding.UTF8.GetString(passwordBytes);
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

        private static AutoOffsetReset MapAutoOffsetReset(KafkaAutoOffsetReset autoOffsetReset)
        {
            return autoOffsetReset switch
            {
                KafkaAutoOffsetReset.Earliest => AutoOffsetReset.Earliest,
                KafkaAutoOffsetReset.Latest => AutoOffsetReset.Latest,
                _ => AutoOffsetReset.Latest
            };
        }

        private static NotSupportedException CreateUnsupportedSaslMechanismException(KafkaSaslMechanism mechanism)
        {
            return new NotSupportedException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Dekaf Kafka adapter does not support SASL mechanism '{0}'.",
                    mechanism));
        }
    }
}
#endif
