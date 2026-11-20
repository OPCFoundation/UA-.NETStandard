/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for Kubernetes EndpointSlice peer discovery.
    /// </summary>
    public sealed class KubernetesPeerDiscoveryOptions
    {
        /// <summary>
        /// Gets the shared Kubernetes client options.
        /// </summary>
        public KubernetesServerOptions Kubernetes { get; } = new();

        /// <summary>
        /// Gets or sets the headless Service name whose EndpointSlices contain OPC UA peers.
        /// </summary>
        public string ServiceName { get; set; } = "opcua-ha-headless";

        /// <summary>
        /// Gets or sets the EndpointSlice port name to prefer.
        /// </summary>
        public string? PortName { get; set; } = "opcua-tcp";

        /// <summary>
        /// Gets or sets the OPC UA URI scheme used for discovered peers.
        /// </summary>
        public string UriScheme { get; set; } = "opc.tcp";

        /// <summary>
        /// Gets or sets the fallback OPC UA port when EndpointSlices do not name one.
        /// </summary>
        public int Port { get; set; } = 4840;

        /// <summary>
        /// Gets or sets this pod DNS name or address to exclude from the discovered peer list.
        /// </summary>
        public string? LocalAddress { get; set; }

        /// <summary>
        /// Gets or sets how often the optional background refresh loop polls EndpointSlices.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(15);
    }
}
