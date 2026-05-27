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
            return new FileSystemNodeManager(server, configuration, m_provider).SyncNodeManager;
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
                new FileSystemNodeManager(server, configuration, m_provider));
#pragma warning restore CA2000
        }

        private readonly IFileSystemProvider m_provider;
    }
}
