// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for Kubernetes Lease-backed leader election.
    /// </summary>
    public sealed class KubernetesLeaderElectionOptions
    {
        /// <summary>
        /// Gets the shared Kubernetes client options.
        /// </summary>
        public KubernetesServerOptions Kubernetes { get; } = new();

        /// <summary>
        /// Gets or sets the Lease name.
        /// </summary>
        public string LeaseName { get; set; } = "opcua-server-leader";

        /// <summary>
        /// Gets or sets how long a leader lease remains valid without renewal.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets how often the background loop renews leadership.
        /// </summary>
        public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the shared-store fallback lease key used outside Kubernetes.
        /// </summary>
        public string FallbackLeaseKey { get; set; } = "addressspace/kubernetes-leader";

        /// <summary>
        /// Gets or sets the explicitly configured replica id used only by the shared-store fallback.
        /// </summary>
        public string FallbackNodeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the same complete set of fallback lease member ids on every replica.
        /// </summary>
        public ArrayOf<string> FallbackLeaseMemberIds { get; set; } = [];

        /// <summary>
        /// Gets or sets whether to explicitly use an independently enrolled freshness store outside Kubernetes.
        /// </summary>
        public bool UseSharedStoreFallback { get; set; }
    }
}
