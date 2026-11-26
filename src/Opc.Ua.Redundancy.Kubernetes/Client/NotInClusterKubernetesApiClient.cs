// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Provides a Kubernetes API client that reports the absence of an in-cluster API.
    /// </summary>
    internal sealed class NotInClusterKubernetesApiClient : IKubernetesApiClient
    {
        /// <inheritdoc/>
        public bool IsInCluster => false;

        /// <inheritdoc/>
        public ValueTask<KubernetesLease?> GetLeaseAsync(string namespaceName, string name, CancellationToken ct)
        {
            return new ValueTask<KubernetesLease?>((KubernetesLease?)null);
        }

        /// <inheritdoc/>
        public ValueTask<KubernetesLease> CreateLeaseAsync(
            string namespaceName,
            KubernetesLease lease,
            CancellationToken ct)
        {
            throw new InvalidOperationException("The Kubernetes in-cluster API is not available.");
        }

        /// <inheritdoc/>
        public ValueTask<KubernetesLease> ReplaceLeaseAsync(
            string namespaceName,
            string name,
            KubernetesLease lease,
            CancellationToken ct)
        {
            throw new InvalidOperationException("The Kubernetes in-cluster API is not available.");
        }

        /// <inheritdoc/>
        public ValueTask DeleteLeaseAsync(string namespaceName, string name, CancellationToken ct)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask<KubernetesEndpointSliceList> ListEndpointSlicesAsync(
            string namespaceName,
            string serviceName,
            CancellationToken ct)
        {
            return new ValueTask<KubernetesEndpointSliceList>(new KubernetesEndpointSliceList());
        }
    }
}
