/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Opc.Ua.Server.DataChannels
{
    /// <summary>
    /// Maps exact resource URIs to application policy and optional model identities.
    /// </summary>
    public sealed class DataChannelSourceRegistry
    {
        /// <summary>
        /// Gets a stable snapshot of the registered sources.
        /// </summary>
        public ArrayOf<DataChannelSource> Sources
        {
            get
            {
                lock (m_gate)
                {
                    return [.. m_sources.Values];
                }
            }
        }

        /// <summary>
        /// Registers a unique resource and optional unique source Object identity.
        /// Registration should precede model startup; policy changes can be applied in the provider.
        /// </summary>
        public void Register(DataChannelSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            source.Validate();
            lock (m_gate)
            {
                if (m_sources.ContainsKey(source.ResourceUri))
                {
                    throw new ArgumentException("The exact resource is already registered.", nameof(source));
                }
                foreach (DataChannelSource existing in m_sources.Values)
                {
                    if (!source.SourceNodeId.IsNull && existing.SourceNodeId == source.SourceNodeId)
                    {
                        throw new ArgumentException("The source Object is already registered.", nameof(source));
                    }
                }
                m_sources.Add(source.ResourceUri, source);
            }
        }

        /// <summary>
        /// Resolves an exact resource URI without fetching it or normalizing it.
        /// </summary>
        public bool TryGet(string resourceUri, [NotNullWhen(true)] out DataChannelSource? source)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }
            lock (m_gate)
            {
                return m_sources.TryGetValue(resourceUri, out source);
            }
        }

        /// <summary>
        /// Withdraws one exact resource. Rechecking the provider revokes its dependent flows.
        /// </summary>
        public bool Unregister(string resourceUri)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }
            lock (m_gate)
            {
                return m_sources.Remove(resourceUri);
            }
        }

        /// <summary>
        /// Resolves a source using the current NamespaceArray, refusing ambiguous mappings.
        /// </summary>
        public bool TryGet(
            NodeId sourceNodeId,
            NamespaceTable namespaceUris,
            [NotNullWhen(true)] out DataChannelSource? source)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            source = null;
            if (sourceNodeId.IsNull)
            {
                return false;
            }
            lock (m_gate)
            {
                foreach (DataChannelSource candidate in m_sources.Values)
                {
                    if (candidate.SourceNodeId.IsNull ||
                        ExpandedNodeId.ToNodeId(candidate.SourceNodeId, namespaceUris) != sourceNodeId)
                    {
                        continue;
                    }
                    if (source != null)
                    {
                        source = null;
                        return false;
                    }
                    source = candidate;
                }
            }
            return source != null;
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<string, DataChannelSource> m_sources = new(StringComparer.Ordinal);
    }
}
