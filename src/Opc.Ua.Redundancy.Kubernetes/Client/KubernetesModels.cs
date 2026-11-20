// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Represents the Kubernetes object metadata used by redundancy resources.
    /// </summary>
    internal sealed class KubernetesObjectMetadata
    {
        /// <summary>
        /// Gets or sets the Kubernetes resource name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the Kubernetes namespace that contains the resource.
        /// </summary>
        [JsonPropertyName("namespace")]
        public string? Namespace { get; set; }

        /// <summary>
        /// Gets or sets the Kubernetes resource version used for optimistic updates.
        /// </summary>
        [JsonPropertyName("resourceVersion")]
        public string? ResourceVersion { get; set; }

        /// <summary>
        /// Gets or sets the Kubernetes labels attached to the resource.
        /// </summary>
        [JsonPropertyName("labels")]
        public Dictionary<string, string>? Labels { get; set; }
    }

    /// <summary>
    /// Represents a Kubernetes Lease resource from the coordination API group.
    /// </summary>
    internal sealed class KubernetesLease
    {
        /// <summary>
        /// Gets or sets the Kubernetes API version for the Lease resource.
        /// </summary>
        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = "coordination.k8s.io/v1";

        /// <summary>
        /// Gets or sets the Kubernetes resource kind.
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "Lease";

        /// <summary>
        /// Gets or sets the Lease metadata.
        /// </summary>
        [JsonPropertyName("metadata")]
        public KubernetesObjectMetadata Metadata { get; set; } = new();

        /// <summary>
        /// Gets or sets the Lease specification.
        /// </summary>
        [JsonPropertyName("spec")]
        public KubernetesLeaseSpec Spec { get; set; } = new();
    }

    /// <summary>
    /// Represents the mutable state stored in a Kubernetes Lease specification.
    /// </summary>
    internal sealed class KubernetesLeaseSpec
    {
        /// <summary>
        /// Gets or sets the identity of the current Lease holder.
        /// </summary>
        [JsonPropertyName("holderIdentity")]
        public string? HolderIdentity { get; set; }

        /// <summary>
        /// Gets or sets the number of seconds before the Lease expires.
        /// </summary>
        [JsonPropertyName("leaseDurationSeconds")]
        public int LeaseDurationSeconds { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the current holder acquired the Lease.
        /// </summary>
        [JsonPropertyName("acquireTime")]
        public string? AcquireTime { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the current holder last renewed the Lease.
        /// </summary>
        [JsonPropertyName("renewTime")]
        public string? RenewTime { get; set; }

        /// <summary>
        /// Gets or sets the number of times the Lease changed holders.
        /// </summary>
        [JsonPropertyName("leaseTransitions")]
        public int? LeaseTransitions { get; set; }
    }

    /// <summary>
    /// Represents a Kubernetes EndpointSlice list response.
    /// </summary>
    internal sealed class KubernetesEndpointSliceList
    {
        /// <summary>
        /// Gets or sets the EndpointSlice resources returned by the Kubernetes API server.
        /// </summary>
        [JsonPropertyName("items")]
        public List<KubernetesEndpointSlice> Items { get; set; } = [];
    }

    /// <summary>
    /// Represents a Kubernetes EndpointSlice resource used for peer discovery.
    /// </summary>
    internal sealed class KubernetesEndpointSlice
    {
        /// <summary>
        /// Gets or sets the EndpointSlice metadata.
        /// </summary>
        [JsonPropertyName("metadata")]
        public KubernetesObjectMetadata Metadata { get; set; } = new();

        /// <summary>
        /// Gets or sets the endpoints published in the EndpointSlice.
        /// </summary>
        [JsonPropertyName("endpoints")]
        public List<KubernetesEndpoint> Endpoints { get; set; } = [];

        /// <summary>
        /// Gets or sets the ports published in the EndpointSlice.
        /// </summary>
        [JsonPropertyName("ports")]
        public List<KubernetesEndpointPort> Ports { get; set; } = [];
    }

    /// <summary>
    /// Represents one network endpoint in a Kubernetes EndpointSlice.
    /// </summary>
    internal sealed class KubernetesEndpoint
    {
        /// <summary>
        /// Gets or sets the endpoint IP addresses or DNS names.
        /// </summary>
        [JsonPropertyName("addresses")]
        public List<string> Addresses { get; set; } = [];

        /// <summary>
        /// Gets or sets readiness conditions for the endpoint.
        /// </summary>
        [JsonPropertyName("conditions")]
        public KubernetesEndpointConditions? Conditions { get; set; }
    }

    /// <summary>
    /// Represents readiness conditions for a Kubernetes endpoint.
    /// </summary>
    internal sealed class KubernetesEndpointConditions
    {
        /// <summary>
        /// Gets or sets whether the endpoint is ready to receive traffic.
        /// </summary>
        [JsonPropertyName("ready")]
        public bool? Ready { get; set; }
    }

    /// <summary>
    /// Represents a named port published by a Kubernetes EndpointSlice.
    /// </summary>
    internal sealed class KubernetesEndpointPort
    {
        /// <summary>
        /// Gets or sets the EndpointSlice port name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the TCP port number.
        /// </summary>
        [JsonPropertyName("port")]
        public int? Port { get; set; }
    }
}
