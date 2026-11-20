/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
