/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
