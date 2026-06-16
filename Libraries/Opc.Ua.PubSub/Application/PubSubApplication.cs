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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Default sealed <see cref="IPubSubApplication"/> implementation.
    /// Aggregates the runtime <see cref="PubSubConnection"/>s built
    /// from a <see cref="PubSubConfigurationSnapshot"/> and exposes the
    /// shared metadata registry, diagnostics, and state machine.
    /// </summary>
    /// <remarks>
    /// Implements the Application object from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2 PubSub application root</see>. Lifecycle is
    /// cascade-driven via <see cref="PubSubStateMachine"/>: enabling /
    /// disabling the application cascades to every connection.
    /// </remarks>
    public sealed class PubSubApplication : IPubSubApplication
    {
        private readonly PubSubConnection[] m_connections;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger<PubSubApplication> m_logger;
        private readonly System.Threading.Lock m_gate = new();
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="PubSubApplication"/>.
        /// </summary>
        /// <param name="snapshot">Validated configuration snapshot.</param>
        /// <param name="transportFactories">Registered transport factories.</param>
        /// <param name="encoders">Registered network-message encoders.</param>
        /// <param name="decoders">Registered network-message decoders.</param>
        /// <param name="securityPolicies">Registered security policies.</param>
        /// <param name="scheduler">Publish scheduler.</param>
        /// <param name="metaDataRegistry">Shared metadata registry.</param>
        /// <param name="diagnostics">Diagnostics sink.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">Clock.</param>
        /// <param name="publishedDataSetSources">
        /// Optional pre-registered <see cref="IPublishedDataSetSource"/>
        /// instances keyed by published-dataset name. Connections fall
        /// back to an empty source for unregistered datasets.
        /// </param>
        /// <param name="subscribedDataSetSinks">
        /// Optional pre-registered <see cref="ISubscribedDataSetSink"/>
        /// instances keyed by data-set reader name.
        /// </param>
        public PubSubApplication(
            PubSubConfigurationSnapshot snapshot,
            IEnumerable<IPubSubTransportFactory> transportFactories,
            IEnumerable<INetworkMessageEncoder> encoders,
            IEnumerable<INetworkMessageDecoder> decoders,
            IEnumerable<IPubSubSecurityPolicy> securityPolicies,
            IPubSubScheduler scheduler,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            IReadOnlyDictionary<string, IPublishedDataSetSource>? publishedDataSetSources = null,
            IReadOnlyDictionary<string, ISubscribedDataSetSink>? subscribedDataSetSinks = null)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (transportFactories is null)
            {
                throw new ArgumentNullException(nameof(transportFactories));
            }
            if (encoders is null)
            {
                throw new ArgumentNullException(nameof(encoders));
            }
            if (decoders is null)
            {
                throw new ArgumentNullException(nameof(decoders));
            }
            if (securityPolicies is null)
            {
                throw new ArgumentNullException(nameof(securityPolicies));
            }
            if (scheduler is null)
            {
                throw new ArgumentNullException(nameof(scheduler));
            }
            if (metaDataRegistry is null)
            {
                throw new ArgumentNullException(nameof(metaDataRegistry));
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
            Snapshot = snapshot;
            MetaDataRegistry = metaDataRegistry;
            Diagnostics = diagnostics;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<PubSubApplication>();

            IPubSubTransportFactory[] factories = transportFactories.ToArray();
            INetworkMessageEncoder[] encoderArray = encoders.ToArray();
            INetworkMessageDecoder[] decoderArray = decoders.ToArray();

            // Validate against registered factories.
            var validator = new PubSubConfigurationValidator(
                factories.Select(f => f.TransportProfileUri));
            PubSubConfigurationValidationResult result = validator.Validate(snapshot.Configuration);
            result.ThrowIfInvalid();

            ApplicationId = ResolveApplicationId(snapshot);
            State = new PubSubStateMachine(
                "application",
                PubSubComponentKind.Application,
                m_logger);

            var encoderMap = encoderArray.ToDictionary(
                e => e.TransportProfileUri, StringComparer.Ordinal);
            var decoderMap = decoderArray.ToDictionary(
                d => d.TransportProfileUri, StringComparer.Ordinal);
            var factoryMap = factories.ToDictionary(
                f => f.TransportProfileUri, StringComparer.Ordinal);

            // Build runtime PublishedDataSet objects keyed by name.
            var publishedDataSets = new Dictionary<string, IPublishedDataSet>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, PublishedDataSetDataType> kvp
                in snapshot.PublishedDataSetsByName)
            {
                IPublishedDataSetSource source = publishedDataSetSources is not null
                    && publishedDataSetSources.TryGetValue(kvp.Key, out IPublishedDataSetSource? configured)
                    ? configured
                    : EmptyPublishedDataSetSource.Instance;
                publishedDataSets[kvp.Key] = new PublishedDataSet(kvp.Value, source);
            }

            // Build connections.
            var connections = new List<PubSubConnection>(snapshot.ConnectionsByName.Count);
            if (!snapshot.Configuration.Connections.IsNull)
            {
                foreach (PubSubConnectionDataType connectionConfig
                    in snapshot.Configuration.Connections)
                {
                    if (!factoryMap.TryGetValue(connectionConfig.TransportProfileUri ?? string.Empty,
                        out IPubSubTransportFactory? factory))
                    {
                        m_logger.LogWarning(
                            "Skipping connection '{Name}' — no transport factory for {Profile}.",
                            connectionConfig.Name, connectionConfig.TransportProfileUri);
                        continue;
                    }
                    BuildConnection(
                        connectionConfig, factory, encoderMap, decoderMap,
                        publishedDataSets, subscribedDataSetSinks, scheduler,
                        metaDataRegistry, diagnostics, timeProvider, connections);
                }
            }
            m_connections = connections.ToArray();
        }

        private void BuildConnection(
            PubSubConnectionDataType connectionConfig,
            IPubSubTransportFactory factory,
            IReadOnlyDictionary<string, INetworkMessageEncoder> encoderMap,
            IReadOnlyDictionary<string, INetworkMessageDecoder> decoderMap,
            Dictionary<string, IPublishedDataSet> publishedDataSets,
            IReadOnlyDictionary<string, ISubscribedDataSetSink>? subscribedDataSetSinks,
            IPubSubScheduler scheduler,
            IDataSetMetaDataRegistry metaDataRegistry,
            IPubSubDiagnostics diagnostics,
            TimeProvider timeProvider,
            List<PubSubConnection> connections)
        {
            var writerGroups = new List<WriterGroup>();
            if (!connectionConfig.WriterGroups.IsNull)
            {
                foreach (WriterGroupDataType wgConfig in connectionConfig.WriterGroups)
                {
                    var writers = new List<DataSetWriter>();
                    if (!wgConfig.DataSetWriters.IsNull)
                    {
                        foreach (DataSetWriterDataType dswConfig in wgConfig.DataSetWriters)
                        {
                            string pdsName = dswConfig.DataSetName ?? string.Empty;
                            if (!publishedDataSets.TryGetValue(pdsName,
                                out IPublishedDataSet? pds))
                            {
                                m_logger.LogWarning(
                                    "DataSetWriter '{Writer}' references unknown "
                                    + "PublishedDataSet '{Pds}'; skipping.",
                                    dswConfig.Name, pdsName);
                                continue;
                            }
                            writers.Add(new DataSetWriter(dswConfig, pds, m_telemetry));
                        }
                    }
                    double intervalMs = wgConfig.PublishingInterval > 0
                        ? wgConfig.PublishingInterval : 1000;
                    var schedule = new PubSubSchedule(
                        TimeSpan.FromMilliseconds(intervalMs),
                        wgConfig.KeepAliveTime > 0
                            ? TimeSpan.FromMilliseconds(wgConfig.KeepAliveTime)
                            : TimeSpan.FromSeconds(30),
                        TimeSpan.Zero,
                        TimeSpan.Zero);
                    writerGroups.Add(new WriterGroup(
                        wgConfig, writers, schedule, scheduler, m_telemetry, timeProvider));
                }
            }

            var readerGroups = new List<ReaderGroup>();
            if (!connectionConfig.ReaderGroups.IsNull)
            {
                foreach (ReaderGroupDataType rgConfig in connectionConfig.ReaderGroups)
                {
                    var readers = new List<DataSetReader>();
                    if (!rgConfig.DataSetReaders.IsNull)
                    {
                        foreach (DataSetReaderDataType drConfig in rgConfig.DataSetReaders)
                        {
                            ISubscribedDataSetSink sink = subscribedDataSetSinks is not null
                                && subscribedDataSetSinks.TryGetValue(drConfig.Name ?? string.Empty,
                                    out ISubscribedDataSetSink? configured)
                                ? configured
                                : NullSubscribedDataSetSink.Instance;
                            readers.Add(new DataSetReader(drConfig, sink, m_telemetry, timeProvider));
                        }
                    }
                    readerGroups.Add(new ReaderGroup(rgConfig, readers, m_telemetry));
                }
            }

            var connection = new PubSubConnection(
                connectionConfig,
                factory,
                encoderMap,
                decoderMap,
                writerGroups,
                readerGroups,
                metaDataRegistry,
                diagnostics,
                m_telemetry,
                timeProvider);
            State.AttachChild(connection.State);
            connections.Add(connection);
        }

        /// <inheritdoc/>
        public string ApplicationId { get; }

        /// <inheritdoc/>
        public IReadOnlyList<IPubSubConnection> Connections => m_connections;

        /// <inheritdoc/>
        public IDataSetMetaDataRegistry MetaDataRegistry { get; }

        /// <inheritdoc/>
        public PubSubStateMachine State { get; }

        /// <summary>
        /// Diagnostics sink shared by every connection in this
        /// application.
        /// </summary>
        public IPubSubDiagnostics Diagnostics { get; }

        /// <summary>
        /// Configuration snapshot the application was built from.
        /// </summary>
        public PubSubConfigurationSnapshot Snapshot { get; }

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(PubSubApplication));
                }
                if (m_started)
                {
                    return;
                }
                m_started = true;
            }
            _ = State.TryEnable();
            foreach (PubSubConnection connection in m_connections)
            {
                try
                {
                    await connection.EnableAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex,
                        "Failed to enable connection '{Name}'.", connection.Name);
                }
            }
            _ = State.TryMarkOperational();
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!m_started)
                {
                    return;
                }
                m_started = false;
            }
            for (int i = m_connections.Length - 1; i >= 0; i--)
            {
                try
                {
                    await m_connections[i].DisableAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex,
                        "Failed to disable connection '{Name}'.", m_connections[i].Name);
                }
            }
            _ = State.TryDisable();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            foreach (PubSubConnection connection in m_connections)
            {
                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private static string ResolveApplicationId(PubSubConfigurationSnapshot snapshot)
        {
            // Use the first connection's PublisherId as a stable default.
            if (snapshot.ConnectionsByName.Count == 0)
            {
                return "urn:opc:ua:pubsub:application";
            }
            foreach (KeyValuePair<string, PubSubConnectionDataType> kvp
                in snapshot.ConnectionsByName)
            {
                return $"urn:opc:ua:pubsub:{kvp.Key}";
            }
            return "urn:opc:ua:pubsub:application";
        }

        private sealed class EmptyPublishedDataSetSource : IPublishedDataSetSource
        {
            public static EmptyPublishedDataSetSource Instance { get; } = new();

            public DataSetMetaDataType BuildMetaData()
            {
                return new DataSetMetaDataType();
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<PublishedDataSetSnapshot>(
                    new PublishedDataSetSnapshot(
                        new ConfigurationVersionDataType(),
                        [],
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
            }
        }

        private sealed class NullSubscribedDataSetSink : ISubscribedDataSetSink
        {
            public static NullSubscribedDataSetSink Instance { get; } = new();

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }
    }
}
