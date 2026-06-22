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
    /// Exposes a runtime mutation API per Part 14 §9.1.6.
    /// </remarks>
    public sealed class PubSubApplication : IPubSubApplication
    {
        private readonly List<PubSubConnection> m_connections;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger<PubSubApplication> m_logger;
        private readonly System.Threading.Lock m_gate = new();
        private readonly SemaphoreSlim m_mutationGate = new(1, 1);

        private readonly IPubSubTransportFactory[] m_factories;
        private readonly INetworkMessageEncoder[] m_encoderArray;
        private readonly INetworkMessageDecoder[] m_decoderArray;
        private readonly IPubSubSecurityPolicy[] m_securityPolicies;
        private readonly IPubSubScheduler m_scheduler;
        private readonly TimeProvider m_timeProvider;
        private readonly IReadOnlyDictionary<string, IPublishedDataSetSource>?
            m_publishedDataSetSources;
        private readonly IReadOnlyDictionary<string, ISubscribedDataSetSink>?
            m_subscribedDataSetSinks;
        private readonly IPubSubSecurityWrapperResolver? m_securityWrapperResolver;
        private readonly Func<PubSubConnectionDataType, int>?
            m_maxNetworkMessageSizeResolver;
        private readonly Dictionary<string, IPubSubTransportFactory> m_factoryMap;
        private readonly Dictionary<string, INetworkMessageEncoder> m_encoderMap;
        private readonly Dictionary<string, INetworkMessageDecoder> m_decoderMap;
        private readonly AggregatingPubSubDiagnostics m_aggregatingDiagnostics;

        private readonly Dictionary<string, NodeId> m_connectionNodeIdsByName
            = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeId, string> m_connectionNamesByNodeId = new();
        private readonly Dictionary<NodeId, (string ConnectionName, string GroupName)>
            m_groupRefs = new();
        private readonly Dictionary<NodeId, (string ConnectionName,
            string GroupName, string WriterName)> m_writerRefs = new();
        private readonly Dictionary<NodeId, (string ConnectionName,
            string GroupName, string ReaderName)> m_readerRefs = new();
        private readonly Dictionary<NodeId, string> m_publishedDataSetRefs = new();
        private readonly List<(PubSubActionTarget Target, IPubSubActionHandler Handler)>
            m_actionHandlers = [];

        private bool m_started;
        private bool m_disposed;
        private MetaDataPublisher? m_metaDataPublisher;

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
        /// <param name="securityWrapperResolver">
        /// Optional per-connection resolver that materialises the
        /// <see cref="UadpSecurityWrapper"/> used by every PubSub
        /// connection. Defaults to <see langword="null"/> meaning no
        /// security wrapping is applied.
        /// </param>
        /// <param name="maxNetworkMessageSizeResolver">
        /// Optional per-connection resolver supplying the maximum
        /// outbound UADP NetworkMessage size before chunking. Returning
        /// <c>0</c> disables chunking for that connection.
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
            IReadOnlyDictionary<string, ISubscribedDataSetSink>? subscribedDataSetSinks = null,
            IPubSubSecurityWrapperResolver? securityWrapperResolver = null,
            Func<PubSubConnectionDataType, int>? maxNetworkMessageSizeResolver = null)
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
            m_factories = transportFactories.ToArray();
            m_encoderArray = encoders.ToArray();
            m_decoderArray = decoders.ToArray();
            m_securityPolicies = securityPolicies.ToArray();
            m_scheduler = scheduler;
            m_timeProvider = timeProvider;
            m_publishedDataSetSources = publishedDataSetSources;
            m_subscribedDataSetSinks = subscribedDataSetSinks;
            m_securityWrapperResolver = securityWrapperResolver;
            m_maxNetworkMessageSizeResolver = maxNetworkMessageSizeResolver;
            m_factoryMap = m_factories.ToDictionary(
                factory => factory.TransportProfileUri,
                StringComparer.Ordinal);
            m_encoderMap = m_encoderArray.ToDictionary(
                encoder => encoder.TransportProfileUri,
                StringComparer.Ordinal);
            m_decoderMap = m_decoderArray.ToDictionary(
                decoder => decoder.TransportProfileUri,
                StringComparer.Ordinal);
            m_connections = new List<PubSubConnection>(snapshot.ConnectionsByName.Count);
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<PubSubApplication>();

            Snapshot = snapshot;
            MetaDataRegistry = metaDataRegistry;
            m_aggregatingDiagnostics = new AggregatingPubSubDiagnostics(
                diagnostics,
                EnumerateComponentDiagnostics);
            Diagnostics = m_aggregatingDiagnostics;
            ConfigurationVersion = CreateConfigurationVersion(snapshot.CreatedAt.ToDateTime());

            var validator = new PubSubConfigurationValidator(
                m_factories.Select(factory => factory.TransportProfileUri));
            PubSubConfigurationValidationResult result =
                validator.Validate(snapshot.Configuration);
            result.ThrowIfInvalid();

            ApplicationId = ResolveApplicationId(snapshot);
            State = new PubSubStateMachine(
                "application",
                PubSubComponentKind.Application,
                m_logger);

            Dictionary<string, IPublishedDataSet> publishedDataSets =
                BuildPublishedDataSets(snapshot);
            if (!snapshot.Configuration.Connections.IsNull)
            {
                foreach (PubSubConnectionDataType connectionConfig
                    in snapshot.Configuration.Connections)
                {
                    PubSubConnection? connection = BuildConnection(
                        connectionConfig,
                        publishedDataSets);
                    if (connection is null)
                    {
                        continue;
                    }

                    m_connections.Add(connection);
                    RegisterConnection(connection);
                }
            }

            RegisterPublishedDataSets();
        }

        private PubSubConnection? BuildConnection(
            PubSubConnectionDataType connectionConfig,
            Dictionary<string, IPublishedDataSet> publishedDataSets)
        {
            if (!m_factoryMap.TryGetValue(
                connectionConfig.TransportProfileUri ?? string.Empty,
                out IPubSubTransportFactory? factory))
            {
                m_logger.LogWarning(
                    "Skipping connection '{Name}' — no transport factory for {Profile}.",
                    connectionConfig.Name,
                    connectionConfig.TransportProfileUri);
                return null;
            }

            var writerGroups = new List<WriterGroup>();
            if (!connectionConfig.WriterGroups.IsNull)
            {
                foreach (WriterGroupDataType writerGroupConfig in connectionConfig.WriterGroups)
                {
                    var writers = new List<DataSetWriter>();
                    if (!writerGroupConfig.DataSetWriters.IsNull)
                    {
                        foreach (DataSetWriterDataType writerConfig
                            in writerGroupConfig.DataSetWriters)
                        {
                            string publishedDataSetName =
                                writerConfig.DataSetName ?? string.Empty;
                            if (!publishedDataSets.TryGetValue(
                                publishedDataSetName,
                                out IPublishedDataSet? publishedDataSet))
                            {
                                m_logger.LogWarning(
                                    "DataSetWriter '{Writer}' references unknown "
                                    + "PublishedDataSet '{Pds}'; skipping.",
                                    writerConfig.Name,
                                    publishedDataSetName);
                                continue;
                            }

                            writers.Add(new DataSetWriter(
                                writerConfig,
                                publishedDataSet,
                                m_telemetry));
                        }
                    }

                    double intervalMs = writerGroupConfig.PublishingInterval > 0
                        ? writerGroupConfig.PublishingInterval
                        : 1000;
                    var schedule = new PubSubSchedule(
                        TimeSpan.FromMilliseconds(intervalMs),
                        writerGroupConfig.KeepAliveTime > 0
                            ? TimeSpan.FromMilliseconds(writerGroupConfig.KeepAliveTime)
                            : TimeSpan.FromSeconds(30),
                        TimeSpan.Zero,
                        TimeSpan.Zero);
                    writerGroups.Add(new WriterGroup(
                        writerGroupConfig,
                        writers,
                        schedule,
                        m_scheduler,
                        m_telemetry,
                        m_timeProvider));
                }
            }

            var readerGroups = new List<ReaderGroup>();
            if (!connectionConfig.ReaderGroups.IsNull)
            {
                foreach (ReaderGroupDataType readerGroupConfig in connectionConfig.ReaderGroups)
                {
                    var readers = new List<DataSetReader>();
                    if (!readerGroupConfig.DataSetReaders.IsNull)
                    {
                        foreach (DataSetReaderDataType readerConfig
                            in readerGroupConfig.DataSetReaders)
                        {
                            ISubscribedDataSetSink sink = m_subscribedDataSetSinks is not null
                                && m_subscribedDataSetSinks.TryGetValue(
                                    readerConfig.Name ?? string.Empty,
                                    out ISubscribedDataSetSink? configured)
                                ? configured
                                : NullSubscribedDataSetSink.Instance;
                            readers.Add(new DataSetReader(
                                readerConfig,
                                sink,
                                m_telemetry,
                                m_timeProvider));
                        }
                    }

                    readerGroups.Add(new ReaderGroup(
                        readerGroupConfig,
                        readers,
                        m_telemetry,
                        m_scheduler,
                        Diagnostics));
                }
            }

            PubSubSecurityContext? securityContext =
                m_securityWrapperResolver?.Resolve(connectionConfig);
            bool requiresSecurity = PubSubSecurityWrapperResolver.TryResolveConnectionSecurity(
                connectionConfig,
                out MessageSecurityMode requiredSecurityMode,
                out _);
            if (requiresSecurity && securityContext is null)
            {
                throw new PubSubConfigurationException(
                [
                    new PubSubConfigurationIssue(
                        PubSubConfigurationIssueSeverity.Error,
                        "PSC1401",
                        $"Connection '{connectionConfig.Name}' is configured for "
                        + $"SecurityMode {requiredSecurityMode} but no security wrapper "
                        + "could be resolved (missing key provider, policy or resolver). "
                        + "Refusing to start in the clear.",
                        $"Connections[{connectionConfig.Name}]",
                        "8.3")
                ]);
            }
            int maxMessageSize =
                m_maxNetworkMessageSizeResolver?.Invoke(connectionConfig) ?? 0;
            PubSubConnection connection = new(
                connectionConfig,
                factory,
                m_encoderMap,
                m_decoderMap,
                writerGroups,
                readerGroups,
                MetaDataRegistry,
                Diagnostics,
                m_telemetry,
                m_timeProvider,
                securityContext?.Wrapper,
                securityContext?.WrapOptions ?? UadpSecurityWrapOptions.SignAndEncrypt,
                maxMessageSize,
                requiredSecurityMode,
                m_scheduler);
            lock (m_gate)
            {
                for (int i = 0; i < m_actionHandlers.Count; i++)
                {
                    connection.RegisterActionHandler(
                        m_actionHandlers[i].Target,
                        m_actionHandlers[i].Handler);
                }
            }
            return connection;
        }

        /// <inheritdoc/>
        public string ApplicationId { get; }

        /// <inheritdoc/>
        // Live view over mutable internal list; ArrayOf would copy on every access.
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
        /// Current application configuration version.
        /// </summary>
        public ConfigurationVersionDataType ConfigurationVersion { get; private set; }

        /// <summary>
        /// Raised after the runtime configuration has been replaced.
        /// </summary>
        public event EventHandler<PubSubConfigurationChangedEventArgs>? ConfigurationChanged;

        /// <summary>
        /// Configuration snapshot the application was built from.
        /// </summary>
        public PubSubConfigurationSnapshot Snapshot { get; private set; }

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PubSubConnection[] connections;
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
                connections = [.. m_connections];
            }
            _ = State.TryEnable();
            foreach (PubSubConnection connection in connections)
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
            // Start the metadata publisher AFTER the
            // connections are enabled so a transport is bound for the
            // initial announcement (Part 14 §7.3.4.8 / §7.2.4.6.4).
            var metaDataPublisher = new MetaDataPublisher(
                this,
                MetaDataRegistry,
                m_encoderMap,
                m_aggregatingDiagnostics,
                m_telemetry,
                m_timeProvider);
            try
            {
                await metaDataPublisher.StartAsync(cancellationToken).ConfigureAwait(false);
                lock (m_gate)
                {
                    m_metaDataPublisher = metaDataPublisher;
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to start metadata publisher.");
                await metaDataPublisher.DisposeAsync().ConfigureAwait(false);
            }
            if (State.TryMarkOperational())
            {
                _ = State.TryResumeCascade();
            }
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PubSubConnection[] connections;
            MetaDataPublisher? metaDataPublisher;
            lock (m_gate)
            {
                if (!m_started)
                {
                    return;
                }
                m_started = false;
                connections = [.. m_connections];
                metaDataPublisher = m_metaDataPublisher;
                m_metaDataPublisher = null;
            }
            if (metaDataPublisher is not null)
            {
                try
                {
                    await metaDataPublisher.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to dispose metadata publisher.");
                }
            }
            for (int i = connections.Length - 1; i >= 0; i--)
            {
                try
                {
                    await connections[i].DisableAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogError(ex,
                        "Failed to disable connection '{Name}'.", connections[i].Name);
                }
            }
            _ = State.TryDisable();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            PubSubConnection[] connections;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                connections = [.. m_connections];
            }
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            foreach (PubSubConnection connection in connections)
            {
                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            m_mutationGate.Dispose();
        }

        /// <summary>
        /// Returns a clone of the currently active configuration.
        /// </summary>
        public PubSubConfigurationDataType GetConfiguration()
        {
            return (PubSubConfigurationDataType)Snapshot.Configuration.Clone();
        }

        /// <summary>
        /// Sends a PubSub discovery request on all active runtime connections.
        /// </summary>
        public async ValueTask<PubSubDiscoveryResult> RequestDiscoveryAsync(
            PubSubDiscoveryRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            PubSubConnection[] connections;
            lock (m_gate)
            {
                connections = [.. m_connections];
            }
            if (connections.Length == 0)
            {
                return new PubSubDiscoveryResult();
            }

            var tasks = new Task<PubSubDiscoveryResult>[connections.Length];
            for (int i = 0; i < connections.Length; i++)
            {
                tasks[i] = connections[i]
                    .RequestDiscoveryAsync(request, timeout, cancellationToken)
                    .AsTask();
            }
            PubSubDiscoveryResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            var metaData = new List<PubSubDataSetMetaDataDiscoveryResult>();
            var writerConfigurations =
                new List<PubSubDataSetWriterConfigurationDiscoveryResult>();
            var endpoints = new List<EndpointDescription>();
            for (int i = 0; i < results.Length; i++)
            {
                metaData.AddRange(results[i].DataSetMetaDataEntries);
                writerConfigurations.AddRange(results[i].WriterConfigurations);
                endpoints.AddRange(results[i].PublisherEndpoints);
            }
            return new PubSubDiscoveryResult
            {
                DataSetMetaDataEntries = [.. metaData],
                WriterConfigurations = [.. writerConfigurations],
                PublisherEndpoints = [.. endpoints]
            };
        }

        /// <summary>
        /// Sends a PubSub Action request on the selected runtime connection.
        /// </summary>
        public async ValueTask<PubSubActionResponse> InvokeActionAsync(
            PubSubActionRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            PubSubConnection[] connections;
            lock (m_gate)
            {
                connections = [.. m_connections];
            }
            for (int i = 0; i < connections.Length; i++)
            {
                if (string.IsNullOrEmpty(request.Target.ConnectionName)
                    || string.Equals(
                        connections[i].Name,
                        request.Target.ConnectionName,
                        StringComparison.Ordinal))
                {
                    return await connections[i]
                        .InvokeActionAsync(request, timeout, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException(
                "No PubSub connection is available for the requested Action target.");
        }

        /// <summary>
        /// Registers a responder-side Action handler on matching connections.
        /// </summary>
        public void RegisterActionHandler(
            PubSubActionTarget target,
            IPubSubActionHandler handler)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            PubSubConnection[] connections;
            lock (m_gate)
            {
                m_actionHandlers.Add((target, handler));
                connections = [.. m_connections];
            }
            for (int i = 0; i < connections.Length; i++)
            {
                if (string.IsNullOrEmpty(target.ConnectionName)
                    || string.Equals(connections[i].Name, target.ConnectionName, StringComparison.Ordinal))
                {
                    connections[i].RegisterActionHandler(target, handler);
                }
            }
        }

        /// <summary>
        /// Replaces the entire runtime configuration.
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> ReplaceConfigurationAsync(
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return ApplyMutationAsync(
                _ => (
                    (PubSubConfigurationDataType)configuration.Clone(),
                    (ArrayOf<StatusCode>)[StatusCodes.Good],
                    true),
                cancellationToken);
        }

        /// <summary>
        /// Adds a connection to the running configuration.
        /// </summary>
        public ValueTask<NodeId> AddConnectionAsync(
            PubSubConnectionDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string connectionName = configuration.Name ?? string.Empty;
            if (connectionName.Length == 0)
            {
                throw new ArgumentException(
                    "configuration.Name must not be empty.",
                    nameof(configuration));
            }

            return ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    connections.Add((PubSubConnectionDataType)configuration.Clone());
                    clone.Connections = [.. connections];
                    return (clone, CreateConnectionNodeId(connectionName), true);
                },
                cancellationToken);
        }

        /// <summary>
        /// Removes a connection by runtime node identifier.
        /// </summary>
        public async ValueTask RemoveConnectionAsync(
            NodeId connectionId,
            CancellationToken cancellationToken = default)
        {
            string connectionName = GetConnectionName(connectionId);

            await ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    if (!RemoveByName(
                        connections,
                        connectionName,
                        static connection => connection.Name))
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    clone.Connections = [.. connections];
                    return (clone, false, true);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a WriterGroup to an existing connection.
        /// </summary>
        public ValueTask<NodeId> AddWriterGroupAsync(
            NodeId connectionId,
            WriterGroupDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string connectionName = GetConnectionName(connectionId);
            string writerGroupName = GetRequiredName(
                configuration.Name,
                nameof(configuration),
                $"{nameof(configuration)}.{nameof(WriterGroupDataType.Name)}");

            return ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    List<WriterGroupDataType> writerGroups =
                        CloneWriterGroups(connections[connectionIndex]);
                    writerGroups.Add((WriterGroupDataType)configuration.Clone());
                    connections[connectionIndex].WriterGroups = [.. writerGroups];
                    clone.Connections = [.. connections];
                    return (
                        clone,
                        CreateWriterGroupNodeId(connectionName, writerGroupName),
                        true);
                },
                cancellationToken);
        }

        /// <summary>
        /// Adds a ReaderGroup to an existing connection.
        /// </summary>
        public ValueTask<NodeId> AddReaderGroupAsync(
            NodeId connectionId,
            ReaderGroupDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string connectionName = GetConnectionName(connectionId);
            string readerGroupName = GetRequiredName(
                configuration.Name,
                nameof(configuration),
                $"{nameof(configuration)}.{nameof(ReaderGroupDataType.Name)}");

            return ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    List<ReaderGroupDataType> readerGroups =
                        CloneReaderGroups(connections[connectionIndex]);
                    readerGroups.Add((ReaderGroupDataType)configuration.Clone());
                    connections[connectionIndex].ReaderGroups = [.. readerGroups];
                    clone.Connections = [.. connections];
                    return (
                        clone,
                        CreateReaderGroupNodeId(connectionName, readerGroupName),
                        true);
                },
                cancellationToken);
        }

        /// <summary>
        /// Removes a WriterGroup or ReaderGroup by runtime node identifier.
        /// </summary>
        public async ValueTask RemoveGroupAsync(
            NodeId groupId,
            CancellationToken cancellationToken = default)
        {
            (string connectionName, string groupName) = GetGroupReference(groupId);

            _ = await ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    PubSubConnectionDataType connection = connections[connectionIndex];
                    bool removed = false;

                    List<WriterGroupDataType> writerGroups = CloneWriterGroups(connection);
                    if (RemoveByName(
                        writerGroups,
                        groupName,
                        static writerGroup => writerGroup.Name))
                    {
                        connection.WriterGroups = [.. writerGroups];
                        removed = true;
                    }
                    else
                    {
                        List<ReaderGroupDataType> readerGroups =
                            CloneReaderGroups(connection);
                        if (RemoveByName(
                            readerGroups,
                            groupName,
                            static readerGroup => readerGroup.Name))
                        {
                            connection.ReaderGroups = [.. readerGroups];
                            removed = true;
                        }
                    }

                    if (!removed)
                    {
                        throw new InvalidOperationException(
                            "The referenced group no longer exists in the current configuration.");
                    }

                    clone.Connections = [.. connections];
                    return (clone, false, true);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a DataSetWriter to an existing WriterGroup.
        /// </summary>
        public ValueTask<NodeId> AddDataSetWriterAsync(
            NodeId writerGroupId,
            DataSetWriterDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            (string connectionName, string writerGroupName) =
                GetGroupReference(writerGroupId);
            string writerName = GetRequiredName(
                configuration.Name,
                nameof(configuration),
                $"{nameof(configuration)}.{nameof(DataSetWriterDataType.Name)}");

            return ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    List<WriterGroupDataType> writerGroups =
                        CloneWriterGroups(connections[connectionIndex]);
                    int writerGroupIndex = FindIndexByName(
                        writerGroups,
                        writerGroupName,
                        static writerGroup => writerGroup.Name);
                    if (writerGroupIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced WriterGroup no longer exists in the current configuration.");
                    }

                    List<DataSetWriterDataType> writers =
                        CloneDataSetWriters(writerGroups[writerGroupIndex]);
                    writers.Add((DataSetWriterDataType)configuration.Clone());
                    writerGroups[writerGroupIndex].DataSetWriters = [.. writers];
                    connections[connectionIndex].WriterGroups = [.. writerGroups];
                    clone.Connections = [.. connections];
                    return (
                        clone,
                        CreateWriterNodeId(connectionName, writerGroupName, writerName),
                        true);
                },
                cancellationToken);
        }

        /// <summary>
        /// Removes a DataSetWriter by runtime node identifier.
        /// </summary>
        public async ValueTask RemoveDataSetWriterAsync(
            NodeId writerId,
            CancellationToken cancellationToken = default)
        {
            (string connectionName, string writerGroupName, string writerName) =
                GetWriterReference(writerId);

            _ = await ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    List<WriterGroupDataType> writerGroups =
                        CloneWriterGroups(connections[connectionIndex]);
                    int writerGroupIndex = FindIndexByName(
                        writerGroups,
                        writerGroupName,
                        static writerGroup => writerGroup.Name);
                    if (writerGroupIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced WriterGroup no longer exists in the current configuration.");
                    }

                    List<DataSetWriterDataType> writers =
                        CloneDataSetWriters(writerGroups[writerGroupIndex]);
                    if (!RemoveByName(
                        writers,
                        writerName,
                        static writer => writer.Name))
                    {
                        throw new InvalidOperationException(
                            "The referenced DataSetWriter no longer exists in the current configuration.");
                    }

                    writerGroups[writerGroupIndex].DataSetWriters = [.. writers];
                    connections[connectionIndex].WriterGroups = [.. writerGroups];
                    clone.Connections = [.. connections];
                    return (clone, false, true);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a DataSetReader to an existing ReaderGroup.
        /// </summary>
        public ValueTask<NodeId> AddDataSetReaderAsync(
            NodeId readerGroupId,
            DataSetReaderDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            (string connectionName, string readerGroupName) =
                GetGroupReference(readerGroupId);
            string readerName = GetRequiredName(
                configuration.Name,
                nameof(configuration),
                $"{nameof(configuration)}.{nameof(DataSetReaderDataType.Name)}");

            return ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    List<ReaderGroupDataType> readerGroups =
                        CloneReaderGroups(connections[connectionIndex]);
                    int readerGroupIndex = FindIndexByName(
                        readerGroups,
                        readerGroupName,
                        static readerGroup => readerGroup.Name);
                    if (readerGroupIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced ReaderGroup no longer exists in the current configuration.");
                    }

                    List<DataSetReaderDataType> readers =
                        CloneDataSetReaders(readerGroups[readerGroupIndex]);
                    readers.Add((DataSetReaderDataType)configuration.Clone());
                    readerGroups[readerGroupIndex].DataSetReaders = [.. readers];
                    connections[connectionIndex].ReaderGroups = [.. readerGroups];
                    clone.Connections = [.. connections];
                    return (
                        clone,
                        CreateReaderNodeId(connectionName, readerGroupName, readerName),
                        true);
                },
                cancellationToken);
        }

        /// <summary>
        /// Removes a DataSetReader by runtime node identifier.
        /// </summary>
        public async ValueTask RemoveDataSetReaderAsync(
            NodeId readerId,
            CancellationToken cancellationToken = default)
        {
            (string connectionName, string readerGroupName, string readerName) =
                GetReaderReference(readerId);

            _ = await ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    int connectionIndex = FindIndexByName(
                        connections,
                        connectionName,
                        static connection => connection.Name);
                    if (connectionIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced connection no longer exists in the current configuration.");
                    }

                    List<ReaderGroupDataType> readerGroups =
                        CloneReaderGroups(connections[connectionIndex]);
                    int readerGroupIndex = FindIndexByName(
                        readerGroups,
                        readerGroupName,
                        static readerGroup => readerGroup.Name);
                    if (readerGroupIndex < 0)
                    {
                        throw new InvalidOperationException(
                            "The referenced ReaderGroup no longer exists in the current configuration.");
                    }

                    List<DataSetReaderDataType> readers =
                        CloneDataSetReaders(readerGroups[readerGroupIndex]);
                    if (!RemoveByName(
                        readers,
                        readerName,
                        static reader => reader.Name))
                    {
                        throw new InvalidOperationException(
                            "The referenced DataSetReader no longer exists in the current configuration.");
                    }

                    readerGroups[readerGroupIndex].DataSetReaders = [.. readers];
                    connections[connectionIndex].ReaderGroups = [.. readerGroups];
                    clone.Connections = [.. connections];
                    return (clone, false, true);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a PublishedDataSet to the running configuration.
        /// </summary>
        public ValueTask<NodeId> AddPublishedDataSetAsync(
            PublishedDataSetDataType configuration,
            CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            string publishedDataSetName = GetRequiredName(
                configuration.Name,
                nameof(configuration),
                $"{nameof(configuration)}.{nameof(PublishedDataSetDataType.Name)}");

            return ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PublishedDataSetDataType> publishedDataSets =
                        ClonePublishedDataSets(clone);
                    publishedDataSets.Add((PublishedDataSetDataType)configuration.Clone());
                    clone.PublishedDataSets = [.. publishedDataSets];
                    return (
                        clone,
                        CreatePublishedDataSetNodeId(publishedDataSetName),
                        true);
                },
                cancellationToken);
        }

        /// <summary>
        /// Removes a PublishedDataSet by runtime node identifier.
        /// </summary>
        public async ValueTask RemovePublishedDataSetAsync(
            NodeId publishedDataSetId,
            CancellationToken cancellationToken = default)
        {
            string publishedDataSetName =
                GetPublishedDataSetName(publishedDataSetId);

            _ = await ApplyMutationAsync(
                currentConfiguration =>
                {
                    var clone =
                        (PubSubConfigurationDataType)currentConfiguration.Clone();
                    List<PublishedDataSetDataType> publishedDataSets =
                        ClonePublishedDataSets(clone);
                    if (!RemoveByName(
                        publishedDataSets,
                        publishedDataSetName,
                        static publishedDataSet => publishedDataSet.Name))
                    {
                        throw new InvalidOperationException(
                            "The referenced PublishedDataSet no longer exists in the current configuration.");
                    }

                    clone.PublishedDataSets = [.. publishedDataSets];

                    List<PubSubConnectionDataType> connections =
                        CloneConnections(clone);
                    for (int connectionIndex = 0;
                        connectionIndex < connections.Count;
                        connectionIndex++)
                    {
                        List<WriterGroupDataType> writerGroups =
                            CloneWriterGroups(connections[connectionIndex]);
                        bool writerGroupsChanged = false;
                        for (int writerGroupIndex = 0;
                            writerGroupIndex < writerGroups.Count;
                            writerGroupIndex++)
                        {
                            List<DataSetWriterDataType> writers =
                                CloneDataSetWriters(writerGroups[writerGroupIndex]);
                            int removedCount = writers.RemoveAll(writer =>
                                StringComparer.Ordinal.Equals(
                                    writer.DataSetName,
                                    publishedDataSetName));
                            if (removedCount > 0)
                            {
                                writerGroups[writerGroupIndex].DataSetWriters = [.. writers];
                                writerGroupsChanged = true;
                            }
                        }

                        if (writerGroupsChanged)
                        {
                            connections[connectionIndex].WriterGroups = [.. writerGroups];
                        }
                    }

                    clone.Connections = [.. connections];
                    return (clone, false, true);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private Dictionary<string, IPublishedDataSet> BuildPublishedDataSets(
            PubSubConfigurationSnapshot snapshot)
        {
            var publishedDataSets = new Dictionary<string, IPublishedDataSet>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, PublishedDataSetDataType> kvp
                in snapshot.PublishedDataSetsByName)
            {
                IPublishedDataSetSource source = m_publishedDataSetSources is not null
                    && m_publishedDataSetSources.TryGetValue(
                        kvp.Key,
                        out IPublishedDataSetSource? configured)
                    ? configured
                    : EmptyPublishedDataSetSource.Instance;
                publishedDataSets[kvp.Key] = new PublishedDataSet(kvp.Value, source);
            }

            return publishedDataSets;
        }

        private void RegisterConnection(PubSubConnection connection)
        {
            State.AttachChild(connection.State);

            string connectionName = connection.Name;
            NodeId connectionNodeId = CreateConnectionNodeId(connectionName);
            m_connectionNodeIdsByName[connectionName] = connectionNodeId;
            m_connectionNamesByNodeId[connectionNodeId] = connectionName;

            for (int writerGroupIndex = 0;
                writerGroupIndex < connection.WriterGroups.Count;
                writerGroupIndex++)
            {
                if (connection.WriterGroups[writerGroupIndex] is not WriterGroup writerGroup)
                {
                    continue;
                }
                string writerGroupName = writerGroup.Name;
                NodeId writerGroupNodeId =
                    CreateWriterGroupNodeId(connectionName, writerGroupName);
                m_groupRefs[writerGroupNodeId] =
                    (connectionName, writerGroupName);

                for (int writerIndex = 0;
                    writerIndex < writerGroup.DataSetWriters.Count;
                    writerIndex++)
                {
                    if (writerGroup.DataSetWriters[writerIndex] is not DataSetWriter writer)
                    {
                        continue;
                    }
                    NodeId writerNodeId = CreateWriterNodeId(
                        connectionName,
                        writerGroupName,
                        writer.Name);
                    m_writerRefs[writerNodeId] =
                        (connectionName, writerGroupName, writer.Name);
                }
            }

            for (int readerGroupIndex = 0;
                readerGroupIndex < connection.ReaderGroups.Count;
                readerGroupIndex++)
            {
                if (connection.ReaderGroups[readerGroupIndex] is not ReaderGroup readerGroup)
                {
                    continue;
                }
                string readerGroupName = readerGroup.Name;
                NodeId readerGroupNodeId =
                    CreateReaderGroupNodeId(connectionName, readerGroupName);
                m_groupRefs[readerGroupNodeId] =
                    (connectionName, readerGroupName);

                for (int readerIndex = 0;
                    readerIndex < readerGroup.DataSetReaders.Count;
                    readerIndex++)
                {
                    if (readerGroup.DataSetReaders[readerIndex] is not DataSetReader reader)
                    {
                        continue;
                    }
                    NodeId readerNodeId = CreateReaderNodeId(
                        connectionName,
                        readerGroupName,
                        reader.Name);
                    m_readerRefs[readerNodeId] =
                        (connectionName, readerGroupName, reader.Name);
                }
            }
        }

        private void RegisterPublishedDataSets()
        {
            foreach (DataSetMetaDataKey key in MetaDataRegistry.Keys)
            {
                MetaDataRegistry.Remove(key);
            }

            m_publishedDataSetRefs.Clear();
            foreach (KeyValuePair<string, PublishedDataSetDataType> kvp
                in Snapshot.PublishedDataSetsByName)
            {
                m_publishedDataSetRefs[CreatePublishedDataSetNodeId(kvp.Key)] = kvp.Key;
            }

            foreach (PubSubConnection connection in m_connections)
            {
                for (int writerGroupIndex = 0;
                    writerGroupIndex < connection.WriterGroups.Count;
                    writerGroupIndex++)
                {
                    if (connection.WriterGroups[writerGroupIndex] is not WriterGroup writerGroup)
                    {
                        continue;
                    }
                    for (int writerIndex = 0;
                        writerIndex < writerGroup.DataSetWriters.Count;
                        writerIndex++)
                    {
                        if (writerGroup.DataSetWriters[writerIndex] is not DataSetWriter writer)
                        {
                            continue;
                        }
                        if (writer.PublishedDataSet is not PublishedDataSet publishedDataSet)
                        {
                            continue;
                        }

                        DataSetMetaDataType metaData = publishedDataSet.MetaData;
                        ConfigurationVersionDataType version =
                            metaData.ConfigurationVersion
                            ?? new ConfigurationVersionDataType();
                        var key = new DataSetMetaDataKey(
                            connection.PublisherId,
                            writerGroup.WriterGroupId,
                            writer.DataSetWriterId,
                            publishedDataSet.DataSetClassId,
                            version.MajorVersion);
                        MetaDataRegistry.Register(key, metaData);
                    }
                }
            }
        }

        private async ValueTask<TResult> ApplyMutationAsync<TResult>(
            Func<PubSubConfigurationDataType,
                (PubSubConfigurationDataType Configuration, TResult Result, bool HasChanges)>
                mutator,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mutator is null)
            {
                throw new ArgumentNullException(nameof(mutator));
            }

            await m_mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        throw new ObjectDisposedException(nameof(PubSubApplication));
                    }
                }

                PubSubConfigurationDataType previousConfiguration =
                    GetConfiguration();
                (PubSubConfigurationDataType configuration,
                    TResult result,
                    bool hasChanges) = mutator(previousConfiguration);
                if (!hasChanges)
                {
                    return result;
                }

                MaintainPublishedDataSetConfigurationVersions(previousConfiguration, configuration);
                RebuiltState rebuilt = BuildRebuiltState(configuration);
                bool restartRequired;
                lock (m_gate)
                {
                    restartRequired = m_started;
                }

                if (restartRequired)
                {
                    await StopAsync(cancellationToken).ConfigureAwait(false);
                }

                PubSubConnection[] oldConnections = [.. m_connections];
                foreach (PubSubConnection oldConnection in oldConnections)
                {
                    State.DetachChild(oldConnection.State);
                }

                m_connections.Clear();
                m_connectionNodeIdsByName.Clear();
                m_connectionNamesByNodeId.Clear();
                m_groupRefs.Clear();
                m_writerRefs.Clear();
                m_readerRefs.Clear();
                m_publishedDataSetRefs.Clear();

                Snapshot = rebuilt.Snapshot;
                foreach (PubSubConnection connection in rebuilt.Connections)
                {
                    m_connections.Add(connection);
                    RegisterConnection(connection);
                }

                RegisterPublishedDataSets();
                ConfigurationVersion = CreateConfigurationVersion(
                    m_timeProvider.GetUtcNow().UtcDateTime);

                foreach (PubSubConnection oldConnection in oldConnections)
                {
                    try
                    {
                        await oldConnection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogDebug(
                            ex,
                            "Failed to dispose old connection '{Name}' during configuration replacement.",
                            oldConnection.Name);
                    }
                }

                if (restartRequired)
                {
                    await StartAsync(cancellationToken).ConfigureAwait(false);
                }

                await PublishWriterGroupConfigurationChangesAsync(
                    previousConfiguration,
                    GetConfiguration(),
                    cancellationToken).ConfigureAwait(false);

                try
                {
                    ConfigurationChanged?.Invoke(
                        this,
                        new PubSubConfigurationChangedEventArgs(
                            previousConfiguration,
                            GetConfiguration()));
                }
                catch (Exception ex)
                {
                    m_logger.LogError(
                        ex,
                        "PubSubApplication ConfigurationChanged handler threw.");
                }

                return result;
            }
            finally
            {
                _ = m_mutationGate.Release();
            }
        }

        private async ValueTask PublishWriterGroupConfigurationChangesAsync(
            PubSubConfigurationDataType previousConfiguration,
            PubSubConfigurationDataType currentConfiguration,
            CancellationToken cancellationToken)
        {
            List<PubSubConnectionDataType> previousConnections =
                CloneConnections(previousConfiguration);
            List<PubSubConnectionDataType> currentConnections =
                CloneConnections(currentConfiguration);
            foreach (PubSubConnectionDataType currentConnection in currentConnections)
            {
                string connectionName = currentConnection.Name ?? string.Empty;
                PubSubConnectionDataType? previousConnection = previousConnections.Find(
                    connection => string.Equals(
                        connection.Name,
                        connectionName,
                        StringComparison.Ordinal));
                List<WriterGroupDataType> currentWriterGroups = CloneWriterGroups(currentConnection);
                List<WriterGroupDataType> previousWriterGroups = previousConnection is null
                    ? []
                    : CloneWriterGroups(previousConnection);
                foreach (WriterGroupDataType currentWriterGroup in currentWriterGroups)
                {
                    WriterGroupDataType? previousWriterGroup = previousWriterGroups.Find(
                        writerGroup => string.Equals(
                            writerGroup.Name,
                            currentWriterGroup.Name,
                            StringComparison.Ordinal));
                    if (previousWriterGroup is not null
                        && Utils.IsEqual(previousWriterGroup, currentWriterGroup))
                    {
                        continue;
                    }
                    PubSubConnection? runtime = FindRuntimeConnection(connectionName);
                    if (runtime is null)
                    {
                        continue;
                    }
                    try
                    {
                        await runtime.AnnounceWriterGroupConfigurationAsync(
                            currentWriterGroup.WriterGroupId,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(
                            ex,
                            "Failed to announce WriterGroup configuration change for {Connection}/{WriterGroup}.",
                            connectionName,
                            currentWriterGroup.Name);
                    }
                }
            }
        }

        private PubSubConnection? FindRuntimeConnection(string connectionName)
        {
            lock (m_gate)
            {
                for (int i = 0; i < m_connections.Count; i++)
                {
                    if (string.Equals(
                        m_connections[i].Name,
                        connectionName,
                        StringComparison.Ordinal))
                    {
                        return m_connections[i];
                    }
                }
            }
            return null;
        }

        private IEnumerable<IPubSubDiagnostics> EnumerateComponentDiagnostics()
        {
            yield break;
        }

        private static List<PubSubConnectionDataType> CloneConnections(
            PubSubConfigurationDataType configuration)
        {
            if (configuration.Connections.IsNull)
            {
                return [];
            }

            var connections = new List<PubSubConnectionDataType>(
                configuration.Connections.Count);
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                connections.Add((PubSubConnectionDataType)connection.Clone());
            }

            return connections;
        }

        private static List<WriterGroupDataType> CloneWriterGroups(
            PubSubConnectionDataType connection)
        {
            if (connection.WriterGroups.IsNull)
            {
                return [];
            }

            var writerGroups = new List<WriterGroupDataType>(
                connection.WriterGroups.Count);
            foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
            {
                writerGroups.Add((WriterGroupDataType)writerGroup.Clone());
            }

            return writerGroups;
        }

        private static List<ReaderGroupDataType> CloneReaderGroups(
            PubSubConnectionDataType connection)
        {
            if (connection.ReaderGroups.IsNull)
            {
                return [];
            }

            var readerGroups = new List<ReaderGroupDataType>(
                connection.ReaderGroups.Count);
            foreach (ReaderGroupDataType readerGroup in connection.ReaderGroups)
            {
                readerGroups.Add((ReaderGroupDataType)readerGroup.Clone());
            }

            return readerGroups;
        }

        private static List<DataSetWriterDataType> CloneDataSetWriters(
            WriterGroupDataType writerGroup)
        {
            if (writerGroup.DataSetWriters.IsNull)
            {
                return [];
            }

            var writers = new List<DataSetWriterDataType>(
                writerGroup.DataSetWriters.Count);
            foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
            {
                writers.Add((DataSetWriterDataType)writer.Clone());
            }

            return writers;
        }

        private static List<DataSetReaderDataType> CloneDataSetReaders(
            ReaderGroupDataType readerGroup)
        {
            if (readerGroup.DataSetReaders.IsNull)
            {
                return [];
            }

            var readers = new List<DataSetReaderDataType>(
                readerGroup.DataSetReaders.Count);
            foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
            {
                readers.Add((DataSetReaderDataType)reader.Clone());
            }

            return readers;
        }

        private static List<PublishedDataSetDataType> ClonePublishedDataSets(
            PubSubConfigurationDataType configuration)
        {
            if (configuration.PublishedDataSets.IsNull)
            {
                return [];
            }

            var publishedDataSets = new List<PublishedDataSetDataType>(
                configuration.PublishedDataSets.Count);
            foreach (PublishedDataSetDataType publishedDataSet
                in configuration.PublishedDataSets)
            {
                publishedDataSets.Add(
                    (PublishedDataSetDataType)publishedDataSet.Clone());
            }

            return publishedDataSets;
        }

        private static void MaintainPublishedDataSetConfigurationVersions(
            PubSubConfigurationDataType previousConfiguration,
            PubSubConfigurationDataType newConfiguration)
        {
            if (newConfiguration.PublishedDataSets.IsNull)
            {
                return;
            }

            Dictionary<string, DataSetMetaDataType> previousMetaDataByName = [];
            if (!previousConfiguration.PublishedDataSets.IsNull)
            {
                foreach (PublishedDataSetDataType previous in previousConfiguration.PublishedDataSets)
                {
                    if (!string.IsNullOrEmpty(previous.Name) &&
                        previous.DataSetMetaData is not null)
                    {
                        previousMetaDataByName[previous.Name] = previous.DataSetMetaData;
                    }
                }
            }

            foreach (PublishedDataSetDataType current in newConfiguration.PublishedDataSets)
            {
                if (current.DataSetMetaData is null)
                {
                    continue;
                }

                DataSetMetaDataType? previousMetaData = null;
                if (!string.IsNullOrEmpty(current.Name))
                {
                    _ = previousMetaDataByName.TryGetValue(current.Name, out previousMetaData);
                }

                current.DataSetMetaData.ConfigurationVersion =
                    ConfigurationVersionUtils.CalculateConfigurationVersion(
                        previousMetaData!,
                        current.DataSetMetaData);
            }
        }

        private static int FindIndexByName<T>(
            List<T> items,
            string name,
            Func<T, string?> nameSelector)
        {
            return items.FindIndex(item =>
                StringComparer.Ordinal.Equals(nameSelector(item), name));
        }

        private static bool RemoveByName<T>(
            List<T> items,
            string name,
            Func<T, string?> nameSelector)
        {
            int index = FindIndexByName(items, name, nameSelector);
            if (index < 0)
            {
                return false;
            }

            items.RemoveAt(index);
            return true;
        }

        private static NodeId CreateConnectionNodeId(string connectionName)
        {
            return new($"pubsub:connection:{connectionName}", 0);
        }

        private static NodeId CreateWriterGroupNodeId(
            string connectionName,
            string writerGroupName)
        {
            return new($"pubsub:writer-group:{connectionName}:{writerGroupName}", 0);
        }

        private static NodeId CreateReaderGroupNodeId(
            string connectionName,
            string readerGroupName)
        {
            return new($"pubsub:reader-group:{connectionName}:{readerGroupName}", 0);
        }

        private static NodeId CreateWriterNodeId(
            string connectionName,
            string writerGroupName,
            string writerName)
        {
            return new($"pubsub:writer:{connectionName}:{writerGroupName}:{writerName}", 0);
        }

        private static NodeId CreateReaderNodeId(
            string connectionName,
            string readerGroupName,
            string readerName)
        {
            return new($"pubsub:reader:{connectionName}:{readerGroupName}:{readerName}", 0);
        }

        private static NodeId CreatePublishedDataSetNodeId(string publishedDataSetName)
        {
            return new($"pubsub:published-data-set:{publishedDataSetName}", 0);
        }

        private static string GetRequiredName(
            string? name,
            string argumentName,
            string propertyPath)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"{propertyPath} must not be empty.",
                    argumentName);
            }

            return name;
        }

        private string GetConnectionName(NodeId connectionId)
        {
            if (connectionId.IsNull)
            {
                throw new ArgumentException(
                    "connectionId must not be null.",
                    nameof(connectionId));
            }

            lock (m_gate)
            {
                if (m_connectionNamesByNodeId.TryGetValue(
                    connectionId,
                    out string? connectionName))
                {
                    return connectionName;
                }
            }

            throw new ArgumentException(
                "The specified connectionId does not exist.",
                nameof(connectionId));
        }

        private (string ConnectionName, string GroupName) GetGroupReference(NodeId groupId)
        {
            if (groupId.IsNull)
            {
                throw new ArgumentException(
                    "groupId must not be null.",
                    nameof(groupId));
            }

            lock (m_gate)
            {
                if (m_groupRefs.TryGetValue(
                    groupId,
                    out (string ConnectionName, string GroupName) groupReference))
                {
                    return groupReference;
                }
            }

            throw new ArgumentException(
                "The specified groupId does not exist.",
                nameof(groupId));
        }

        private (string ConnectionName, string GroupName, string WriterName) GetWriterReference(
            NodeId writerId)
        {
            if (writerId.IsNull)
            {
                throw new ArgumentException(
                    "writerId must not be null.",
                    nameof(writerId));
            }

            lock (m_gate)
            {
                if (m_writerRefs.TryGetValue(
                    writerId,
                    out (string ConnectionName, string GroupName, string WriterName) writerReference))
                {
                    return writerReference;
                }
            }

            throw new ArgumentException(
                "The specified writerId does not exist.",
                nameof(writerId));
        }

        private (string ConnectionName, string GroupName, string ReaderName) GetReaderReference(
            NodeId readerId)
        {
            if (readerId.IsNull)
            {
                throw new ArgumentException(
                    "readerId must not be null.",
                    nameof(readerId));
            }

            lock (m_gate)
            {
                if (m_readerRefs.TryGetValue(
                    readerId,
                    out (string ConnectionName, string GroupName, string ReaderName) readerReference))
                {
                    return readerReference;
                }
            }

            throw new ArgumentException(
                "The specified readerId does not exist.",
                nameof(readerId));
        }

        private string GetPublishedDataSetName(NodeId publishedDataSetId)
        {
            if (publishedDataSetId.IsNull)
            {
                throw new ArgumentException(
                    "publishedDataSetId must not be null.",
                    nameof(publishedDataSetId));
            }

            lock (m_gate)
            {
                if (m_publishedDataSetRefs.TryGetValue(
                    publishedDataSetId,
                    out string? publishedDataSetName))
                {
                    return publishedDataSetName;
                }
            }

            throw new ArgumentException(
                "The specified publishedDataSetId does not exist.",
                nameof(publishedDataSetId));
        }

        private RebuiltState BuildRebuiltState(
            PubSubConfigurationDataType configuration)
        {
            PubSubConfigurationSnapshot snapshot =
                PubSubConfigurationSnapshot.Create(configuration, m_timeProvider);
            var validator = new PubSubConfigurationValidator(
                m_factories.Select(factory => factory.TransportProfileUri));
            PubSubConfigurationValidationResult validationResult =
                validator.Validate(snapshot.Configuration);
            validationResult.ThrowIfInvalid();

            Dictionary<string, IPublishedDataSet> publishedDataSets =
                BuildPublishedDataSets(snapshot);
            var connections = new List<PubSubConnection>(
                snapshot.ConnectionsByName.Count);
            if (!snapshot.Configuration.Connections.IsNull)
            {
                foreach (PubSubConnectionDataType connectionConfig
                    in snapshot.Configuration.Connections)
                {
                    PubSubConnection? connection = BuildConnection(
                        connectionConfig,
                        publishedDataSets);
                    if (connection is not null)
                    {
                        connections.Add(connection);
                    }
                }
            }

            return new RebuiltState(snapshot, connections);
        }

        private static ConfigurationVersionDataType CreateConfigurationVersion(
            DateTime timeOfConfiguration)
        {
            uint versionTime =
                ConfigurationVersionUtils.CalculateVersionTime(timeOfConfiguration);
            return new ConfigurationVersionDataType
            {
                MajorVersion = versionTime,
                MinorVersion = versionTime
            };
        }

        private static string ResolveApplicationId(PubSubConfigurationSnapshot snapshot)
        {
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

        private sealed record RebuiltState(
            PubSubConfigurationSnapshot Snapshot,
            List<PubSubConnection> Connections);
    }
}

namespace Opc.Ua.PubSub.Diagnostics
{
    /// <summary>
    /// Aggregates one root diagnostics sink and optional child sinks
    /// into a single application-facing view.
    /// </summary>
    public sealed class AggregatingPubSubDiagnostics : IPubSubDiagnostics
    {
        private readonly IPubSubDiagnostics m_root;
        private readonly Func<IEnumerable<IPubSubDiagnostics>>? m_componentResolver;
        private readonly System.Threading.Lock m_gate = new();
        private PubSubDiagnosticsLevel m_level;

        /// <summary>
        /// Initializes a new <see cref="AggregatingPubSubDiagnostics"/>.
        /// </summary>
        /// <param name="root">Root diagnostics sink.</param>
        /// <param name="componentResolver">
        /// Optional callback returning child diagnostics sinks.
        /// </param>
        public AggregatingPubSubDiagnostics(
            IPubSubDiagnostics root,
            Func<IEnumerable<IPubSubDiagnostics>>? componentResolver = null)
        {
            m_root = root ?? throw new ArgumentNullException(nameof(root));
            m_componentResolver = componentResolver;
            m_level = root.Level;
        }

        /// <inheritdoc/>
        public PubSubDiagnosticsLevel Level
        {
            get
            {
                lock (m_gate)
                {
                    return m_level;
                }
            }
        }

        /// <summary>
        /// Updates the exposed diagnostics level.
        /// </summary>
        /// <param name="level">New level.</param>
        public void SetLevel(PubSubDiagnosticsLevel level)
        {
            lock (m_gate)
            {
                m_level = level;
            }
        }

        /// <inheritdoc/>
        public void Increment(PubSubDiagnosticsCounterKind kind, long delta = 1)
        {
            m_root.Increment(kind, delta);
        }

        /// <inheritdoc/>
        public long Read(PubSubDiagnosticsCounterKind kind)
        {
            long total = m_root.Read(kind);
            foreach (IPubSubDiagnostics diagnostics in ResolveComponents())
            {
                if (!ReferenceEquals(diagnostics, m_root))
                {
                    total += diagnostics.Read(kind);
                }
            }
            return total;
        }

        /// <inheritdoc/>
        public void RecordError(StatusCode statusCode, string message)
        {
            m_root.RecordError(statusCode, message);
        }

        /// <inheritdoc/>
        public void Reset()
        {
            m_root.Reset();
            foreach (IPubSubDiagnostics diagnostics in ResolveComponents())
            {
                if (!ReferenceEquals(diagnostics, m_root))
                {
                    diagnostics.Reset();
                }
            }
        }

        private IEnumerable<IPubSubDiagnostics> ResolveComponents()
        {
            return m_componentResolver?.Invoke()
                ?? Array.Empty<IPubSubDiagnostics>();
        }
    }
}
