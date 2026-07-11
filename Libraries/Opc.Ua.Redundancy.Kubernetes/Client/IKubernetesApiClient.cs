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
