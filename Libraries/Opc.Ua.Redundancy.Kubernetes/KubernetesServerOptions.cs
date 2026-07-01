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
    /// Extension beyond OPC 10000-4 §6.6: common Kubernetes integration options.
    /// </summary>
    public sealed class KubernetesServerOptions
    {
        /// <summary>
        /// Gets or sets the Kubernetes API host. Defaults to <c>KUBERNETES_SERVICE_HOST</c>.
        /// </summary>
        public string? ApiServerHost { get; set; }

        /// <summary>
        /// Gets or sets the Kubernetes API port. Defaults to <c>KUBERNETES_SERVICE_PORT</c> or 443.
        /// </summary>
        public int? ApiServerPort { get; set; }

        /// <summary>
        /// Gets or sets the service-account bearer token path.
        /// </summary>
        public string TokenPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

        /// <summary>
        /// Gets or sets the service-account CA certificate path.
        /// </summary>
        public string CertificateAuthorityPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

        /// <summary>
        /// Gets or sets the service-account namespace path.
        /// </summary>
        public string NamespacePath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/namespace";

        /// <summary>
        /// Gets or sets the Kubernetes namespace. Defaults to the service-account namespace file.
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Gets or sets this replica's unique identity. Defaults to <see cref="Environment.MachineName"/>.
        /// </summary>
        public string NodeId { get; set; } = Environment.MachineName;
    }
}