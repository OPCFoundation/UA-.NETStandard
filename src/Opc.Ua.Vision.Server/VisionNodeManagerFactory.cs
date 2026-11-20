/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.Vision.Server.Hosting;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Creates standalone Vision node managers.
    /// </summary>
    public sealed class VisionNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory with default options and the built-in provider.
        /// </summary>
        public VisionNodeManagerFactory()
            : this(
                new IVisionModelProvider[] { new VisionModelProvider() },
                new VisionServerOptions())
        {
        }

        /// <summary>
        /// Creates a factory with explicit providers and options.
        /// </summary>
        public VisionNodeManagerFactory(
            ArrayOf<IVisionModelProvider> providers,
            VisionServerOptions options,
            IVisionPostSetupRunner? runner = null)
        {
            m_providers = VisionNodeManager.NormalizeProviders(providers);
            m_options = options;
            m_options.Validate();
            m_runner = runner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            VisionNodeManager.GetNamespaceUris(m_providers, m_options).ToArrayOf();

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
            IAsyncNodeManager manager = new VisionNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_runner);
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly ArrayOf<IVisionModelProvider> m_providers;
        private readonly VisionServerOptions m_options;
        private readonly IVisionPostSetupRunner? m_runner;
    }
}
