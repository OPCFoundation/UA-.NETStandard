/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Positioning.Server
{
    /// <summary>
    /// Creates standalone <see cref="PositioningNodeManager"/> instances.
    /// </summary>
    public sealed class PositioningNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IPositioningPostSetupRunner? m_runner;

        /// <summary>
        /// Creates a factory without dependency-injection configurators.
        /// </summary>
        public PositioningNodeManagerFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a factory with Positioning post-setup support.
        /// </summary>
        public PositioningNodeManagerFactory(IPositioningPostSetupRunner? runner)
        {
            m_runner = runner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
        [
            Rsl.Namespaces.RSL,
            Gpos.Namespaces.GPOS
        ];

        /// <inheritdoc/>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager manager =
                new PositioningNodeManager(server, configuration, m_runner);
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }
}
