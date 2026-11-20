/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for the Kubernetes readiness and liveness HTTP bridge.
    /// </summary>
    public sealed class KubernetesReadinessOptions
    {
        /// <summary>
        /// Gets or sets the listener host. Use <c>+</c> to bind all interfaces.
        /// </summary>
        public string Host { get; set; } = "+";

        /// <summary>
        /// Gets or sets the listener port. The default is <see cref="DefaultPort"/>.
        /// </summary>
        public int Port { get; set; } = DefaultPort;

        /// <summary>
        /// Gets or sets the readiness path.
        /// </summary>
        public string ReadinessPath { get; set; } = "/readyz";

        /// <summary>
        /// Gets or sets the liveness path.
        /// </summary>
        public string LivenessPath { get; set; } = "/livez";

        /// <summary>
        /// Gets or sets the minimum OPC 10000-4 §6.6.2.4.2 <c>ServiceLevel</c> considered ready.
        /// </summary>
        public byte ReadyMinimumServiceLevel { get; set; } = 200;

        /// <summary>
        /// The default listener port, 4852.
        /// </summary>
        /// <remarks>
        /// The probe shares the pod (and, with host networking, the node) with the
        /// application and its sidecars, so the default stays off the ports web
        /// applications and reverse proxies commonly use (8080, 8000, 9090, ...).
        /// 4852 lies next to the OPC UA ports 4840/4843 and is unassigned in the
        /// IANA service name and port number registry (4852-4866).
        /// </remarks>
        public const int DefaultPort = 4852;
    }
}
