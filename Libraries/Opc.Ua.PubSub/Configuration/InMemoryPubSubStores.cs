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
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// In-memory PubSub configuration store preserving current process-local semantics.
    /// </summary>
    public sealed class InMemoryPubSubConfigurationStore : IPubSubConfigurationStore
    {
        private readonly System.Threading.Lock m_gate = new();
        private PubSubConfigurationDataType m_configuration;

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
        private readonly System.Threading.Lock m_gate = new();
        private uint m_nextReservedId;
        private uint m_nextFileHandle;

        /// <inheritdoc/>
        public ValueTask<ArrayOf<uint>> ReserveIdsAsync(ushort count, CancellationToken cancellationToken = default)
        {
            var ids = new uint[count];
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
        private readonly System.Threading.Lock m_gate = new();
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
    }
}
