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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Publishes <see cref="DataSetMetaDataType"/> announcements for
    /// every <see cref="IDataSetWriter"/> at application startup and
    /// whenever the shared <see cref="IDataSetMetaDataRegistry"/>
    /// raises <see cref="IDataSetMetaDataRegistry.MetaDataChanged"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.7.4">
    /// Part 14 §7.3.4.7.4 MQTT metadata topic</see>,
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
    /// §7.3.4.8 Retained discovery messages</see>,
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
    /// §7.2.4.6.4 UADP DataSetMetaData announcement</see>, and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.5.2">
    /// §7.2.5.5.2 JSON metadata message</see>.
    /// </para>
    /// <para>
    /// On JSON connections the publisher emits a
    /// <see cref="JsonMetaDataMessage"/> on the §7.3.4.7.4 metadata
    /// topic; on MQTT brokers the transport sets the <c>Retain</c>
    /// flag automatically when the resolved topic matches the
    /// <c>/metadata/</c> segment (Part 14 §7.3.4.8).
    /// On UADP connections the publisher emits a
    /// <see cref="UadpDiscoveryResponseMessage"/> with
    /// <see cref="UadpDiscoveryType.DataSetMetaData"/>.
    /// </para>
    /// <para>
    /// Lifetime is owned by <see cref="PubSubApplication"/>: started
    /// after <c>EnableConnectionsAsync</c> returns, disposed before
    /// the connections are torn down.
    /// </para>
    /// </remarks>
    internal sealed class MetaDataPublisher : IAsyncDisposable
    {
        private readonly PubSubApplication m_application;
        private readonly IDataSetMetaDataRegistry m_registry;
        private readonly IReadOnlyDictionary<string, INetworkMessageEncoder> m_encoders;
        private readonly IPubSubDiagnostics m_diagnostics;
        private readonly ITelemetryContext m_telemetry;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<MetaDataPublisher> m_logger;
        private readonly System.Threading.Lock m_gate = new();

        private long m_messageIdSeed;
        private int m_disposed;
        private bool m_subscribed;

        /// <summary>
        /// Initializes a new <see cref="MetaDataPublisher"/>.
        /// </summary>
        /// <param name="application">
        /// Owning <see cref="PubSubApplication"/>; the publisher
        /// enumerates its <see cref="PubSubConnection"/> list to find
        /// the matching transport per writer group.
        /// </param>
        /// <param name="metaDataRegistry">Shared metadata registry.</param>
        /// <param name="encoders">
        /// Network-message encoders keyed by transport profile URI.
        /// </param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock used to stamp MessageIds.</param>
        public MetaDataPublisher(
            PubSubApplication application,
            IDataSetMetaDataRegistry metaDataRegistry,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoders,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }
            if (metaDataRegistry is null)
            {
                throw new ArgumentNullException(nameof(metaDataRegistry));
            }
            if (encoders is null)
            {
                throw new ArgumentNullException(nameof(encoders));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_application = application;
            m_registry = metaDataRegistry;
            m_encoders = encoders;
            m_diagnostics = diagnostics;
            m_telemetry = telemetry;
            m_timeProvider = timeProvider;
            m_logger = telemetry.CreateLogger<MetaDataPublisher>();
        }

        /// <summary>
        /// Subscribes to <see cref="IDataSetMetaDataRegistry.MetaDataChanged"/>
        /// and emits the initial announcement for every writer that
        /// has metadata available. Must be called after the owning
        /// connections have been enabled so a transport is bound.
        /// Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(MetaDataPublisher));
                }
                if (m_subscribed)
                {
                    return;
                }
                m_registry.MetaDataChanged += OnMetaDataChanged;
                m_subscribed = true;
            }
            await PublishInitialAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return default;
            }
            lock (m_gate)
            {
                if (m_subscribed)
                {
                    m_registry.MetaDataChanged -= OnMetaDataChanged;
                    m_subscribed = false;
                }
            }
            return default;
        }

        private async ValueTask PublishInitialAsync(CancellationToken cancellationToken)
        {
            foreach (IPubSubConnection connection in m_application.Connections)
            {
                if (connection is not PubSubConnection runtime)
                {
                    continue;
                }
                foreach (IWriterGroup writerGroup in runtime.WriterGroups)
                {
                    foreach (IDataSetWriter writer in writerGroup.DataSetWriters)
                    {
                        DataSetMetaDataType? meta = ResolveWriterMetaData(writer);
                        if (meta is null)
                        {
                            continue;
                        }
                        try
                        {
                            await PublishMetaDataAsync(
                                runtime,
                                writerGroup,
                                writer,
                                meta,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogWarning(ex,
                                "Failed to publish initial metadata for writer {Writer} in group {Group}.",
                                writer.Name,
                                writerGroup.Name);
                        }
                    }
                }
            }
        }

        private void OnMetaDataChanged(object? sender, DataSetMetaDataChangedEventArgs e)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            // Schedule on the thread pool to avoid running async work
            // on the registry caller's thread; the caller may still be
            // holding the registry write lock.
            _ = Task.Run(async () =>
            {
                try
                {
                    await PublishForKeyAsync(e.Key, e.Current, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex,
                        "Failed to publish metadata change for writer {Writer} in group {Group}.",
                        e.Key.DataSetWriterId,
                        e.Key.WriterGroupId);
                }
            });
        }

        private async ValueTask PublishForKeyAsync(
            DataSetMetaDataKey key,
            DataSetMetaDataType current,
            CancellationToken cancellationToken)
        {
            foreach (IPubSubConnection connection in m_application.Connections)
            {
                if (connection is not PubSubConnection runtime)
                {
                    continue;
                }
                if (!PublisherIdEquals(runtime.PublisherId, key.PublisherId))
                {
                    continue;
                }
                foreach (IWriterGroup writerGroup in runtime.WriterGroups)
                {
                    if (writerGroup.WriterGroupId != key.WriterGroupId)
                    {
                        continue;
                    }
                    foreach (IDataSetWriter writer in writerGroup.DataSetWriters)
                    {
                        if (writer.DataSetWriterId != key.DataSetWriterId)
                        {
                            continue;
                        }
                        await PublishMetaDataAsync(
                            runtime,
                            writerGroup,
                            writer,
                            current,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private async ValueTask PublishMetaDataAsync(
            PubSubConnection connection,
            IWriterGroup writerGroup,
            IDataSetWriter writer,
            DataSetMetaDataType metaData,
            CancellationToken cancellationToken)
        {
            IPubSubTransport? transport = connection.CurrentTransport;
            if (transport is null)
            {
                return;
            }
            string profile = connection.TransportProfileUri;
            string family = TransportProfileFamily(profile);
            Uuid classId = metaData.DataSetClassId == Guid.Empty
                ? Uuid.Empty
                : new Uuid(metaData.DataSetClassId);
            ReadOnlyMemory<byte> payload;
            string? topic = null;
            if (string.Equals(family, "Json", StringComparison.Ordinal))
            {
                if (!TryResolveEncoder(profile, family, out INetworkMessageEncoder? encoder)
                    || encoder is null)
                {
                    m_logger.LogDebug(
                        "No JSON encoder registered for {Profile}; metadata publish skipped.",
                        profile);
                    return;
                }
                var message = new JsonMetaDataMessage
                {
                    MessageId = NewMessageId(),
                    PublisherId = connection.PublisherId,
                    WriterGroupId = writerGroup.WriterGroupId,
                    DataSetWriterId = writer.DataSetWriterId,
                    DataSetClassId = classId,
                    MetaDataPayload = metaData
                };
                var context = new PubSubNetworkMessageContext(
                    ServiceMessageContext.CreateEmpty(m_telemetry),
                    m_registry,
                    m_diagnostics,
                    m_timeProvider);
                payload = await encoder.EncodeAsync(message, context, cancellationToken)
                    .ConfigureAwait(false);
                topic = ResolveMetaDataTopic(
                    transport,
                    connection.PublisherId,
                    writerGroup.WriterGroupId,
                    writer.DataSetWriterId);
            }
            else
            {
                var message = new UadpDiscoveryResponseMessage
                {
                    PublisherId = connection.PublisherId,
                    WriterGroupId = writerGroup.WriterGroupId,
                    DataSetWriterId = writer.DataSetWriterId,
                    DataSetClassId = classId,
                    DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                    DataSetMetaData = metaData,
                    SequenceNumber = NewSequenceNumber(),
                    StatusCode = StatusCodes.Good
                };
                var context = new PubSubNetworkMessageContext(
                    ServiceMessageContext.CreateEmpty(m_telemetry),
                    m_registry,
                    m_diagnostics,
                    m_timeProvider);
                payload = UadpDiscoveryCoder.Encode(message, context);
                topic = ResolveMetaDataTopic(
                    transport,
                    connection.PublisherId,
                    writerGroup.WriterGroupId,
                    writer.DataSetWriterId);
            }

            await transport.SendAsync(payload, topic, cancellationToken).ConfigureAwait(false);
        }

        private bool TryResolveEncoder(
            string profile,
            string family,
            out INetworkMessageEncoder? encoder)
        {
            if (m_encoders.TryGetValue(profile, out encoder))
            {
                return true;
            }
            foreach (KeyValuePair<string, INetworkMessageEncoder> entry in m_encoders)
            {
                if (string.Equals(
                    TransportProfileFamily(entry.Key),
                    family,
                    StringComparison.Ordinal))
                {
                    encoder = entry.Value;
                    return true;
                }
            }
            encoder = null;
            return false;
        }

        private static string? ResolveMetaDataTopic(
            IPubSubTransport transport,
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId)
        {
            if (transport is IPubSubTopicProvider provider)
            {
                return provider.BuildMetaDataTopic(
                    publisherId, writerGroupId, dataSetWriterId);
            }
            return null;
        }

        private static DataSetMetaDataType? ResolveWriterMetaData(IDataSetWriter writer)
        {
            DataSetMetaDataType? meta = writer.PublishedDataSet?.MetaData;
            if (meta is null)
            {
                return null;
            }
            bool hasFields = !meta.Fields.IsNull && meta.Fields.Count > 0;
            bool hasVersion = meta.ConfigurationVersion is not null
                && (meta.ConfigurationVersion.MajorVersion != 0
                    || meta.ConfigurationVersion.MinorVersion != 0);
            return hasFields || hasVersion ? meta : null;
        }

        private static string TransportProfileFamily(string profile)
        {
            if (string.IsNullOrEmpty(profile))
            {
                return "Uadp";
            }
            return profile.Contains("Json", StringComparison.OrdinalIgnoreCase)
                ? "Json"
                : "Uadp";
        }

        private static bool PublisherIdEquals(PublisherId left, PublisherId right)
        {
            if (left.IsNull && right.IsNull)
            {
                return true;
            }
            return left.Equals(right);
        }

        private string NewMessageId()
        {
            long ticks = m_timeProvider.GetUtcNow().UtcTicks;
            long sequence = Interlocked.Increment(ref m_messageIdSeed);
            return string.Format(
                CultureInfo.InvariantCulture,
                "meta-{0:x}-{1:x}",
                ticks,
                sequence);
        }

        private ushort NewSequenceNumber()
        {
            return unchecked((ushort)Interlocked.Increment(ref m_messageIdSeed));
        }
    }
}
