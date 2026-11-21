// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// In-memory PubSub configuration store preserving current process-local semantics.
    /// </summary>
    public sealed class InMemoryPubSubConfigurationStore : IPubSubConfigurationStore
    {
        private readonly Lock m_gate = new();
        private PubSubConfigurationDataType m_configuration;
        private ConfigurationVersionDataType? m_configurationVersion;

        /// <summary>
        /// Initializes a new store.
        /// </summary>
        /// <param name="configuration">Initial configuration.</param>
        public InMemoryPubSubConfigurationStore(PubSubConfigurationDataType? configuration = null)
        {
            m_configuration = configuration ?? new PubSubConfigurationDataType { Connections = [], PublishedDataSets = [] };
        }

        /// <inheritdoc/>
        public event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public ValueTask<PubSubConfigurationDataType> LoadAsync(CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                return new ValueTask<PubSubConfigurationDataType>((PubSubConfigurationDataType)m_configuration.Clone());
            }
        }

        /// <inheritdoc/>
        public ValueTask SaveAsync(PubSubConfigurationDataType configuration, CancellationToken cancellationToken = default)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            PubSubConfigurationDataType previous;
            lock (m_gate)
            {
                previous = (PubSubConfigurationDataType)m_configuration.Clone();
                m_configuration = (PubSubConfigurationDataType)configuration.Clone();
            }

            Changed?.Invoke(this, new PubSubConfigurationChangedEventArgs(previous, configuration));
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ConfigurationVersionDataType?> GetConfigurationVersionAsync(
            CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                return new ValueTask<ConfigurationVersionDataType?>(
                    m_configurationVersion is null
                        ? null
                        : (ConfigurationVersionDataType)m_configurationVersion.Clone());
            }
        }

        /// <inheritdoc/>
        public ValueTask SetConfigurationVersionAsync(
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default)
        {
            if (configurationVersion is null)
            {
                throw new ArgumentNullException(nameof(configurationVersion));
            }

            lock (m_gate)
            {
                m_configurationVersion = (ConfigurationVersionDataType)configurationVersion.Clone();
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ConfigurationVersionDataType?> GetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                PublishedDataSetDataType? dataSet = FindPublishedDataSet(m_configuration, publishedDataSetName);
                return new ValueTask<ConfigurationVersionDataType?>(
                    dataSet?.DataSetMetaData?.ConfigurationVersion);
            }
        }

        /// <inheritdoc/>
        public ValueTask SetPublishedDataSetConfigurationVersionAsync(
            string publishedDataSetName,
            ConfigurationVersionDataType configurationVersion,
            CancellationToken cancellationToken = default)
        {
            if (configurationVersion is null)
            {
                throw new ArgumentNullException(nameof(configurationVersion));
            }

            lock (m_gate)
            {
                PublishedDataSetDataType? dataSet = FindPublishedDataSet(m_configuration, publishedDataSetName);
                if (dataSet?.DataSetMetaData is not null)
                {
                    dataSet.DataSetMetaData.ConfigurationVersion = configurationVersion;
                }
            }

            return default;
        }

        private static PublishedDataSetDataType? FindPublishedDataSet(
            PubSubConfigurationDataType configuration,
            string publishedDataSetName)
        {
            if (configuration.PublishedDataSets.IsNull)
            {
                return null;
            }

            foreach (PublishedDataSetDataType dataSet in configuration.PublishedDataSets)
            {
                if (StringComparer.Ordinal.Equals(dataSet.Name, publishedDataSetName))
                {
                    return dataSet;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// In-memory id allocator preserving current process-local counters.
    /// </summary>
    public sealed class InMemoryPubSubIdAllocator : IPubSubIdAllocator
    {
        private readonly Lock m_gate = new();
        private uint m_nextReservedId;
        private uint m_nextFileHandle;

        /// <inheritdoc/>
        public ValueTask<ArrayOf<uint>> ReserveIdsAsync(ushort count, CancellationToken cancellationToken = default)
        {
            uint[] ids = new uint[count];
            lock (m_gate)
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    ids[i] = ++m_nextReservedId;
                }
            }

            return new ValueTask<ArrayOf<uint>>(new ArrayOf<uint>(ids));
        }

        /// <inheritdoc/>
        public ValueTask<uint> AllocateFileHandleAsync(CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                return new ValueTask<uint>(++m_nextFileHandle);
            }
        }
    }

    /// <summary>
    /// In-memory runtime-state store preserving current process-local state.
    /// </summary>
    public sealed class InMemoryPubSubRuntimeStateStore : IPubSubRuntimeStateStore
    {
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, PubSubState> m_states = new(StringComparer.Ordinal);

        /// <inheritdoc/>
        public ValueTask<PubSubState?> GetStateAsync(string componentId, CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                return new ValueTask<PubSubState?>(
                    m_states.TryGetValue(componentId, out PubSubState state) ? state : null);
            }
        }

        /// <inheritdoc/>
        public ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId must be non-empty.", nameof(componentId));
            }

            lock (m_gate)
            {
                m_states[componentId] = state;
            }

            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetStateAsync(
            string componentId,
            PubSubState state,
            long fencingToken,
            CancellationToken cancellationToken = default)
        {
            return SetStateAsync(componentId, state, cancellationToken);
        }
    }
}
