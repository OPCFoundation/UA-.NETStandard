// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Defers certificate activation calls to the standard server instance created by the hosting service.
    /// </summary>
    internal sealed class ServerApplicationCertificateActivator : IServerApplicationCertificateActivator
    {
        /// <summary>
        /// Attaches the one server instance owned by this host.
        /// </summary>
        /// <param name="server">The host-owned standard server.</param>
        internal void Attach(StandardServer server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            StandardServer? existing = Interlocked.CompareExchange(ref m_server, server, null);
            if (existing != null && !ReferenceEquals(existing, server))
            {
                throw new InvalidOperationException("The certificate activator is already bound to another server.");
            }
        }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(
            NodeId certificateType,
            string expectedThumbprint,
            CancellationToken cancellationToken = default)
        {
            StandardServer? server = Volatile.Read(ref m_server);
            if (server == null)
            {
                throw new InvalidOperationException("The server certificate activator has not been attached.");
            }

            return server.ActivateAsync(certificateType, expectedThumbprint, cancellationToken);
        }

        /// <summary>
        /// The standard server created by the host.
        /// </summary>
        private StandardServer? m_server;
    }
}
