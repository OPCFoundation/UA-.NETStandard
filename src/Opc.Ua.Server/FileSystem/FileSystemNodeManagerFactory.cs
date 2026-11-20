/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// <see cref="INodeManagerFactory"/> that wraps an
    /// <see cref="IFileSystemProvider"/> so it can be plugged into a
    /// <c>StandardServer</c> via the standard
    /// <c>NodeManagerFactories</c> collection.
    /// </summary>
    public sealed class FileSystemNodeManagerFactory : INodeManagerFactory, IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a new factory backed by <paramref name="provider"/>.
        /// </summary>
        public FileSystemNodeManagerFactory(IFileSystemProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Whether anonymous sessions may modify the mounted file system.
        /// Defaults to <c>false</c>; see <see cref="FileSystemNodeManager.AllowAnonymousWrite"/>.
        /// </summary>
        public bool AllowAnonymousWrite { get; set; }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
        [
            FileSystemNodeManager.NamespaceUriBase + "/" + m_provider.MountName
        ];

        /// <inheritdoc/>
        public INodeManager Create(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
#pragma warning disable CA2000 // Ownership is transferred to the server via returned node manager instance.
            return new FileSystemNodeManager(server, configuration, m_provider)
            {
                AllowAnonymousWrite = AllowAnonymousWrite
            }.SyncNodeManager;
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // Ownership is transferred to the server via returned node manager instance.
            return new ValueTask<IAsyncNodeManager>(
                new FileSystemNodeManager(server, configuration, m_provider)
                {
                    AllowAnonymousWrite = AllowAnonymousWrite
                });
#pragma warning restore CA2000
        }

        private readonly IFileSystemProvider m_provider;
    }
}
