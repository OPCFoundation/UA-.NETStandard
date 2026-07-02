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
        /// Gets or sets whether the DI registration uses <see cref="SharedStoreLeaseElection"/> outside Kubernetes.
        /// </summary>
        public bool UseSharedStoreFallback { get; set; } = true;
    }
}
