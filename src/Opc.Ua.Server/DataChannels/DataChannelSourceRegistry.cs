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
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
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
                    var sources = new DataChannelSource[m_sources.Count];
                    int index = 0;
                    foreach (Registration registration in m_sources.Values)
                    {
                        sources[index++] = registration.Source;
                    }
                    return sources;
                }
            }
        }

        /// <summary>
        /// Registers a unique resource and optional unique source Object identity.
        /// Registration should precede model startup; revoke active flows after changing local policy.
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
                foreach (Registration registration in m_sources.Values)
                {
                    DataChannelSource existing = registration.Source;
                    if (!source.SourceNodeId.IsNull && existing.SourceNodeId == source.SourceNodeId)
                    {
                        throw new ArgumentException("The source Object is already registered.", nameof(source));
                    }
                }
                m_sources.Add(source.ResourceUri, new Registration(source));
            }
        }

        /// <summary>
        /// Resolves an exact resource URI without fetching it or normalizing it.
        /// </summary>
        public bool TryGet(string resourceUri, [NotNullWhen(true)] out DataChannelSource? source)
        {
            return TryGetWithRevocation(resourceUri, out source, out _);
        }

        /// <summary>
        /// Resolves a source and its registration-scoped revocation signal.
        /// </summary>
        internal bool TryGetWithRevocation(
            string resourceUri,
            [NotNullWhen(true)] out DataChannelSource? source,
            out CancellationToken revocationToken)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }
            lock (m_gate)
            {
                if (m_sources.TryGetValue(resourceUri, out Registration? registration))
                {
                    source = registration.Source;
                    revocationToken = registration.Revocation.Token;
                    return true;
                }
                source = null;
                revocationToken = default;
                return false;
            }
        }

        /// <summary>
        /// Withdraws one exact resource and immediately revokes its configured flows.
        /// </summary>
        public bool Unregister(string resourceUri)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }
            Registration? registration;
            lock (m_gate)
            {
                if (!m_sources.TryGetValue(resourceUri, out Registration? found) || found is null)
                {
                    return false;
                }
                registration = found;
                m_sources.Remove(resourceUri);
            }
            Revoke(registration.Revocation);
            return true;
        }

        /// <summary>
        /// Revokes active configured flows for one resource while keeping the registration available.
        /// </summary>
        public bool RevokeConfiguredFlows(string resourceUri)
        {
            if (resourceUri == null)
            {
                throw new ArgumentNullException(nameof(resourceUri));
            }
            CancellationTokenSource previous;
            lock (m_gate)
            {
                if (!m_sources.TryGetValue(resourceUri, out Registration? registration) ||
                    registration is null)
                {
                    return false;
                }
                previous = registration.Revocation;
                registration.Revocation = new CancellationTokenSource();
            }
            Revoke(previous);
            return true;
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
                foreach (Registration registration in m_sources.Values)
                {
                    DataChannelSource candidate = registration.Source;
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

        private sealed class Registration(DataChannelSource source)
        {
            public DataChannelSource Source { get; } = source;
            public CancellationTokenSource Revocation { get; set; } = new();
        }

        private static void Revoke(CancellationTokenSource revocation)
        {
            try
            {
                revocation.Cancel();
            }
            finally
            {
                revocation.Dispose();
            }
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<string, Registration> m_sources = new(StringComparer.Ordinal);
    }
}
