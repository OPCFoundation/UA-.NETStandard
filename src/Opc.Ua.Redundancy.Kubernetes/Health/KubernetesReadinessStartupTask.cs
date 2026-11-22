// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Kubernetes
{
    /// <summary>
    /// Starts the Kubernetes readiness endpoint when the OPC UA server has started.
    /// </summary>
    internal sealed class KubernetesReadinessStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates a startup task for a Kubernetes readiness server.
        /// </summary>
        /// <param name="server">The readiness server to start.</param>
        public KubernetesReadinessStartupTask(KubernetesReadinessServer server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            m_server.Start();
            return default;
        }

        private readonly KubernetesReadinessServer m_server;
    }
}
