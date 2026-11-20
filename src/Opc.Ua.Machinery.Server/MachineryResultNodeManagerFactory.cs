// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Creates stand-alone <see cref="MachineryResultNodeManager"/> instances.
    /// </summary>
    public sealed class MachineryResultNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory.
        /// </summary>
        /// <param name="options">The result-server options.</param>
        /// <param name="store">The store the methods read from.</param>
        public MachineryResultNodeManagerFactory(
            MachineryResultServerOptions options,
            IMachineryResultStore store)
        {
            m_options = options ?? throw new System.ArgumentNullException(nameof(options));
            m_options.Validate();
            m_store = store ?? throw new System.ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Gets the created managers, so a hosted service can push results into
        /// them once the server is running.
        /// </summary>
        public MachineryResultNodeManager? Manager { get; private set; }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            new string[]
            {
                m_options.InstanceNamespaceUri,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult
            };

        /// <inheritdoc/>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the node manager is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var nodeManager = new MachineryResultNodeManager(
                server,
                configuration,
                m_options,
                m_store);
            Manager = nodeManager;
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly MachineryResultServerOptions m_options;
        private readonly IMachineryResultStore m_store;
    }
}
