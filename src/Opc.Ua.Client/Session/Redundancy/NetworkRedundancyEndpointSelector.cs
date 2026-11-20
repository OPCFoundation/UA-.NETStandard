// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Selects alternate endpoints for non-transparent network redundancy.
    /// </summary>
    public sealed class NetworkRedundancyEndpointSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkRedundancyEndpointSelector"/> class.
        /// </summary>
        public NetworkRedundancyEndpointSelector(
            ConfiguredEndpoint primaryEndpoint,
            ArrayOf<ConfiguredEndpoint> alternateEndpoints)
        {
            if (primaryEndpoint is null)
            {
                throw new ArgumentNullException(nameof(primaryEndpoint));
            }

            m_endpoints = CreateEndpointList(primaryEndpoint, alternateEndpoints);
        }

        /// <summary>
        /// Gets a value indicating whether alternates are available.
        /// </summary>
        public bool HasAlternates => m_endpoints.Count > 1;

        /// <summary>
        /// Returns the next endpoint for the same logical server.
        /// </summary>
        public ConfiguredEndpoint? SelectNext(ConfiguredEndpoint currentEndpoint)
        {
            if (!HasAlternates)
            {
                return null;
            }

            int currentIndex = FindIndex(currentEndpoint);
            int nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + 1) % m_endpoints.Count;
            return m_endpoints[nextIndex];
        }

        private int FindIndex(ConfiguredEndpoint endpoint)
        {
            string? endpointUrl = endpoint.Description.EndpointUrl;
            for (int ii = 0; ii < m_endpoints.Count; ii++)
            {
                if (string.Equals(
                    m_endpoints[ii].Description.EndpointUrl,
                    endpointUrl,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return ii;
                }
            }

            return -1;
        }

        private static List<ConfiguredEndpoint> CreateEndpointList(
            ConfiguredEndpoint primaryEndpoint,
            ArrayOf<ConfiguredEndpoint> alternateEndpoints)
        {
            var endpoints = new List<ConfiguredEndpoint> { primaryEndpoint };
            for (int ii = 0; ii < alternateEndpoints.Count; ii++)
            {
                ConfiguredEndpoint endpoint = alternateEndpoints[ii];
                if (IsSameLogicalServer(primaryEndpoint, endpoint) &&
                    !ContainsEndpoint(endpoints, endpoint))
                {
                    endpoints.Add(endpoint);
                }
            }

            return endpoints;
        }

        private static bool ContainsEndpoint(
            List<ConfiguredEndpoint> endpoints,
            ConfiguredEndpoint endpoint)
        {
            foreach (ConfiguredEndpoint item in endpoints)
            {
                if (string.Equals(
                    item.Description.EndpointUrl,
                    endpoint.Description.EndpointUrl,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSameLogicalServer(
            ConfiguredEndpoint left,
            ConfiguredEndpoint right)
        {
            string? leftUri = left.Description.Server?.ApplicationUri;
            string? rightUri = right.Description.Server?.ApplicationUri;
            return string.IsNullOrEmpty(leftUri) ||
                string.IsNullOrEmpty(rightUri) ||
                string.Equals(leftUri, rightUri, StringComparison.Ordinal);
        }

        private readonly List<ConfiguredEndpoint> m_endpoints;
    }
}
