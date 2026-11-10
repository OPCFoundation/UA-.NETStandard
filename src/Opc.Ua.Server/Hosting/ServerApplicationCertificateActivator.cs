/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

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
