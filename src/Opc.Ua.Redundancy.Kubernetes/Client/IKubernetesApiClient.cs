/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Provides the Kubernetes API operations required by redundancy services.
    /// </summary>
    internal interface IKubernetesApiClient
    {
        /// <summary>
        /// Indicates whether this client is connected to the Kubernetes in-cluster API.
        /// </summary>
        bool IsInCluster { get; }

        /// <summary>
        /// Reads a Kubernetes Lease object by namespace and name.
        /// </summary>
        /// <param name="namespaceName">The Kubernetes namespace that contains the Lease.</param>
        /// <param name="name">The Lease resource name.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        /// <returns>The Lease when it exists; otherwise, <c>null</c>.</returns>
        ValueTask<KubernetesLease?> GetLeaseAsync(string namespaceName, string name, CancellationToken ct);

        /// <summary>
        /// Creates a Kubernetes Lease object in the specified namespace.
        /// </summary>
        /// <param name="namespaceName">The Kubernetes namespace where the Lease is created.</param>
        /// <param name="lease">The Lease payload to create.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        /// <returns>The Lease returned by the Kubernetes API server.</returns>
        ValueTask<KubernetesLease> CreateLeaseAsync(string namespaceName, KubernetesLease lease, CancellationToken ct);

        /// <summary>
        /// Replaces an existing Kubernetes Lease object.
        /// </summary>
        /// <param name="namespaceName">The Kubernetes namespace that contains the Lease.</param>
        /// <param name="name">The Lease resource name.</param>
        /// <param name="lease">The updated Lease payload.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        /// <returns>The Lease returned by the Kubernetes API server.</returns>
        ValueTask<KubernetesLease> ReplaceLeaseAsync(
            string namespaceName,
            string name,
            KubernetesLease lease,
            CancellationToken ct);

        /// <summary>
        /// Deletes a Kubernetes Lease object when it exists.
        /// </summary>
        /// <param name="namespaceName">The Kubernetes namespace that contains the Lease.</param>
        /// <param name="name">The Lease resource name.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        ValueTask DeleteLeaseAsync(string namespaceName, string name, CancellationToken ct);

        /// <summary>
        /// Lists EndpointSlice resources for a Kubernetes Service.
        /// </summary>
        /// <param name="namespaceName">The Kubernetes namespace that contains the Service.</param>
        /// <param name="serviceName">The Service name used by the EndpointSlice label selector.</param>
        /// <param name="ct">The token to monitor for cancellation requests.</param>
        /// <returns>The EndpointSlice list returned by the Kubernetes API server.</returns>
        ValueTask<KubernetesEndpointSliceList> ListEndpointSlicesAsync(
            string namespaceName,
            string serviceName,
            CancellationToken ct);
    }
}
