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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Publisher
{
    /// <summary>
    /// Builds and owns the client Subscriptions that back the
    /// <see cref="ReadMode.Subscription"/> publisher read strategy. Each
    /// affinity group (a WriterGroup by default, or a single DataSetWriter) gets
    /// one <see cref="IDataChangeSubscription"/> whose monitored items
    /// keep a <see cref="SubscriptionReadStrategy"/> latest-value cache current.
    /// On start the coordinator creates the subscriptions, adds a monitored item
    /// per published variable, applies the changes server-side, then primes the
    /// caches with a one-shot Read so the first publish cycle is not empty.
    /// </summary>
    public sealed class SubscriptionCoordinator : IAsyncDisposable
    {
        /// <summary>
        /// Creates a coordinator for the supplied PubSub configuration, external
        /// server session and subscription affinity.
        /// </summary>
        /// <param name="configuration">
        /// The PubSub configuration describing the WriterGroups, DataSetWriters
        /// and PublishedDataSets to subscribe to.
        /// </param>
        /// <param name="session">
        /// The session used to create subscriptions and prime initial values.
        /// </param>
        /// <param name="affinity">
        /// Selects whether one subscription is created per WriterGroup (default)
        /// or per DataSetWriter.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used to create loggers.
        /// </param>
        public SubscriptionCoordinator(
            PubSubConfigurationDataType configuration,
            IServerSession session,
            SubscriptionAffinity affinity,
            ITelemetryContext telemetry)
        {
            m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_affinity = affinity;
            m_logger = telemetry.CreateLogger<SubscriptionCoordinator>();

            m_dataSetsByName = BuildDataSetMap(configuration);
            BuildGroups();
        }

        /// <summary>
        /// Connects the session, builds the configured subscriptions, applies
        /// the monitored items server-side and primes the latest-value caches.
        /// The call is idempotent: invoking it again once started is a no-op.
        /// </summary>
        /// <param name="ct">
        /// A token used to cancel the start.
        /// </param>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            await m_startLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_started)
                {
                    return;
                }

                await m_session.ConnectAsync(ct).ConfigureAwait(false);
                foreach (SubscriptionGroup group in m_groups)
                {
                    await BuildGroupSubscriptionAsync(group, ct).ConfigureAwait(false);
                }
                m_started = true;
            }
            finally
            {
                m_startLock.Release();
            }
        }

        /// <summary>
        /// Returns the read strategy whose cache backs the supplied
        /// PublishedDataSet. The same strategy may be shared by several datasets
        /// that belong to the same affinity group.
        /// </summary>
        /// <param name="publishedDataSetName">
        /// The name of the PublishedDataSet to resolve.
        /// </param>
        /// <returns>
        /// The subscription-backed read strategy for the dataset.
        /// </returns>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when no subscription is configured for the dataset.
        /// </exception>
        /// <exception cref="ArgumentNullException"></exception>
        public IReadStrategy GetReadStrategy(string publishedDataSetName)
        {
            if (publishedDataSetName is null)
            {
                throw new ArgumentNullException(nameof(publishedDataSetName));
            }
            ThrowIfDisposed();

            if (m_strategiesByDataSet.TryGetValue(publishedDataSetName, out SubscriptionReadStrategy? strategy))
            {
                return strategy;
            }
            throw new KeyNotFoundException(
                "No external subscription read strategy is configured for " +
                $"PublishedDataSet '{publishedDataSetName}'.");
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;

            foreach (SubscriptionGroup group in m_groups)
            {
                group.Strategy.Dispose();
                if (group.Subscription is not null)
                {
                    await group.Subscription.DisposeAsync().ConfigureAwait(false);
                    group.Subscription = null;
                }
            }
            m_startLock.Dispose();
        }

        private void BuildGroups()
        {
            if (m_configuration.Connections.IsNull)
            {
                return;
            }

            foreach (PubSubConnectionDataType connection in m_configuration.Connections)
            {
                if (connection?.WriterGroups is null || connection.WriterGroups.IsNull)
                {
                    continue;
                }

                foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
                {
                    if (writerGroup is null)
                    {
                        continue;
                    }

                    double intervalMs = writerGroup.PublishingInterval > 0
                        ? writerGroup.PublishingInterval
                        : DefaultPublishingIntervalMs;

                    if (m_affinity == SubscriptionAffinity.DataSetWriter)
                    {
                        BuildWriterGroups(writerGroup, intervalMs);
                    }
                    else
                    {
                        BuildWriterGroupGroup(writerGroup, intervalMs);
                    }
                }
            }
        }

        private void BuildWriterGroupGroup(WriterGroupDataType writerGroup, double intervalMs)
        {
            var strategy = new SubscriptionReadStrategy(m_telemetry);
            var group = new SubscriptionGroup(
                $"WriterGroup '{writerGroup.Name}' ({writerGroup.WriterGroupId})",
                intervalMs,
                strategy);

            if (!writerGroup.DataSetWriters.IsNull)
            {
                foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
                {
                    AddDataSet(group, strategy, writer?.DataSetName);
                }
            }

            if (group.DataSetNames.Count > 0)
            {
                m_groups.Add(group);
            }
        }

        private void BuildWriterGroups(WriterGroupDataType writerGroup, double intervalMs)
        {
            if (writerGroup.DataSetWriters.IsNull)
            {
                return;
            }

            foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
            {
                if (writer is null)
                {
                    continue;
                }

                var strategy = new SubscriptionReadStrategy(m_telemetry);
                var group = new SubscriptionGroup(
                    $"DataSetWriter '{writer.Name}' ({writer.DataSetWriterId})",
                    intervalMs,
                    strategy);

                AddDataSet(group, strategy, writer.DataSetName);
                if (group.DataSetNames.Count > 0)
                {
                    m_groups.Add(group);
                }
            }
        }

        private void AddDataSet(
            SubscriptionGroup group,
            SubscriptionReadStrategy strategy,
            string? dataSetName)
        {
            if (string.IsNullOrEmpty(dataSetName))
            {
                return;
            }
            if (!m_dataSetsByName.ContainsKey(dataSetName!))
            {
                m_logger.LogWarning(
                    "DataSetWriter references unknown PublishedDataSet '{Pds}'; " +
                    "it will produce no monitored items.",
                    dataSetName);
                return;
            }
            if (!group.DataSetNames.Contains(dataSetName!))
            {
                group.DataSetNames.Add(dataSetName!);
            }
            m_strategiesByDataSet[dataSetName!] = strategy;
        }

        private async ValueTask BuildGroupSubscriptionAsync(
            SubscriptionGroup group,
            CancellationToken ct)
        {
            IDataChangeSubscription subscription =
                await m_session.CreateDataChangeSubscriptionAsync(
                    group.PublishingIntervalMs, ct).ConfigureAwait(false);
            group.Subscription = subscription;
            group.Strategy.Attach(subscription);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var primeNodes = new List<ReadValueId>();
            var primeKeys = new List<MonitoredItemKey>();

            foreach (string dataSetName in group.DataSetNames)
            {
                if (!m_dataSetsByName.TryGetValue(dataSetName, out PublishedDataSetDataType? dataSet) ||
                    dataSet is null)
                {
                    continue;
                }

                foreach (PublishedVariableDataType variable in GetPublishedVariables(dataSet))
                {
                    NodeId nodeId;
                    try
                    {
                        nodeId = await m_session
                            .ResolveNodeIdAsync(variable.PublishedVariable, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        m_logger.LogWarning(
                            ex,
                            "Could not resolve published variable {NodeId} for {Group}; " +
                            "it will not be monitored.",
                            variable.PublishedVariable,
                            group.Label);
                        continue;
                    }
                    if (nodeId.IsNull)
                    {
                        continue;
                    }

                    uint attributeId = variable.AttributeId != 0
                        ? variable.AttributeId
                        : Attributes.Value;
                    string dedupe = string.Concat(
                        nodeId.ToString(),
                        "|",
                        attributeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (!seen.Add(dedupe))
                    {
                        continue;
                    }

                    double samplingMs = variable.SamplingIntervalHint > 0
                        ? variable.SamplingIntervalHint
                        : group.PublishingIntervalMs;

                    uint clientHandle = await subscription.AddMonitoredItemAsync(
                        nodeId, attributeId, samplingMs, ct).ConfigureAwait(false);
                    group.Strategy.RegisterMonitoredItem(clientHandle, nodeId, attributeId);

                    primeNodes.Add(new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    });
                    primeKeys.Add(new MonitoredItemKey(nodeId, attributeId));
                }
            }

            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);

            if (primeNodes.Count == 0)
            {
                m_logger.LogDebug(
                    "No monitored items created for {Group}; nothing to prime.",
                    group.Label);
                return;
            }

            ArrayOf<DataValue> values = await m_session.ReadAsync(
                primeNodes.ToArrayOf(), ct).ConfigureAwait(false);
            int primeCount = values.IsNull ? 0 : values.Count;
            for (int i = 0; i < primeKeys.Count && i < primeCount; i++)
            {
                group.Strategy.Seed(primeKeys[i].NodeId, primeKeys[i].AttributeId, values[i]);
            }

            m_logger.LogDebug(
                "Built {Group}: {Count} monitored item(s) primed at {Interval} ms.",
                group.Label,
                primeNodes.Count,
                group.PublishingIntervalMs);
        }

        private static Dictionary<string, PublishedDataSetDataType> BuildDataSetMap(
            PubSubConfigurationDataType configuration)
        {
            var map = new Dictionary<string, PublishedDataSetDataType>(StringComparer.Ordinal);
            if (configuration.PublishedDataSets.IsNull)
            {
                return map;
            }

            foreach (PublishedDataSetDataType dataSet in configuration.PublishedDataSets)
            {
                if (dataSet?.Name is { Length: > 0 } name)
                {
                    map[name] = dataSet;
                }
            }
            return map;
        }

        private static List<PublishedVariableDataType> GetPublishedVariables(
            PublishedDataSetDataType dataSet)
        {
            var variables = new List<PublishedVariableDataType>();
            ExtensionObject source = dataSet.DataSetSource;
            if (source.IsNull ||
                !source.TryGetValue(out PublishedDataItemsDataType? items) ||
                items is null ||
                items.PublishedData.IsNull)
            {
                return variables;
            }

            ArrayOf<PublishedVariableDataType> published = items.PublishedData;
            for (int i = 0; i < published.Count; i++)
            {
                PublishedVariableDataType variable = published[i];
                if (variable is not null)
                {
                    variables.Add(variable);
                }
            }
            return variables;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(SubscriptionCoordinator));
            }
        }

        /// <summary>
        /// One affinity group and its backing subscription.
        /// </summary>
        private sealed class SubscriptionGroup
        {
            public SubscriptionGroup(
                string label,
                double publishingIntervalMs,
                SubscriptionReadStrategy strategy)
            {
                Label = label;
                PublishingIntervalMs = publishingIntervalMs;
                Strategy = strategy;
            }

            public string Label { get; }

            public double PublishingIntervalMs { get; }

            public SubscriptionReadStrategy Strategy { get; }

            public List<string> DataSetNames { get; } = [];

            public IDataChangeSubscription? Subscription { get; set; }
        }

        /// <summary>
        /// Node/attribute pair recorded for priming a monitored item.
        /// </summary>
        private readonly struct MonitoredItemKey
        {
            public MonitoredItemKey(NodeId nodeId, uint attributeId)
            {
                NodeId = nodeId;
                AttributeId = attributeId;
            }

            public NodeId NodeId { get; }

            public uint AttributeId { get; }
        }

        private const double DefaultPublishingIntervalMs = 1000;

        private readonly PubSubConfigurationDataType m_configuration;
        private readonly IServerSession m_session;
        private readonly SubscriptionAffinity m_affinity;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_startLock = new(1, 1);
        private readonly List<SubscriptionGroup> m_groups = [];
        private readonly Dictionary<string, SubscriptionReadStrategy> m_strategiesByDataSet =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, PublishedDataSetDataType> m_dataSetsByName;
        private bool m_started;
        private bool m_disposed;
    }
}
