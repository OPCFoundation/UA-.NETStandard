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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Internal bridge used by file-system node states to share FileType and
    /// FileDirectoryType method semantics across lazy and materialised hosts.
    /// </summary>
    internal interface IFileSystemHost
    {
        /// <summary>
        /// The provider that backs the hosted file system.
        /// </summary>
        IFileSystemProvider Provider { get; }

        /// <summary>
        /// Whether the host exposes the CreateFile and CreateDirectory methods.
        /// </summary>
        bool AllowCreate { get; }

        /// <summary>
        /// Whether the host exposes the Delete method.
        /// </summary>
        bool AllowDelete { get; }

        /// <summary>
        /// Whether the host exposes the MoveOrCopy method.
        /// </summary>
        bool AllowMoveOrCopy { get; }

        /// <summary>
        /// Whether directory children are resolved on demand rather than
        /// materialised as node states up front.
        /// </summary>
        bool UsesVirtualDirectoryBrowsing { get; }

        /// <summary>
        /// Returns the NodeId that represents a directory at the given
        /// provider path.
        /// </summary>
        /// <param name="providerPath">The provider-relative directory path.</param>
        /// <returns>The directory NodeId.</returns>
        NodeId BuildDirectoryNodeId(string providerPath);

        /// <summary>
        /// Returns the NodeId that represents a file at the given provider
        /// path.
        /// </summary>
        /// <param name="providerPath">The provider-relative file path.</param>
        /// <returns>The file NodeId.</returns>
        NodeId BuildFileNodeId(string providerPath);

        /// <summary>
        /// Joins a child name onto a parent provider path.
        /// </summary>
        /// <param name="parent">The parent provider path.</param>
        /// <param name="name">The child name.</param>
        /// <returns>The combined provider path.</returns>
        string CombineProviderPath(string parent, string name);

        /// <summary>
        /// Returns the NodeId of the directory containing the given provider
        /// path.
        /// </summary>
        /// <param name="providerPath">The provider-relative path.</param>
        /// <returns>The parent directory NodeId.</returns>
        NodeId GetParentNodeId(string providerPath);

        /// <summary>
        /// Returns the open-file handle tracked for a node, creating it when
        /// the node is not tracked yet.
        /// </summary>
        /// <param name="nodeId">The file NodeId.</param>
        /// <param name="providerPath">The provider-relative file path.</param>
        /// <returns>The handle, or <c>null</c> when one cannot be tracked.</returns>
        FileHandle? GetOrCreateHandle(NodeId nodeId, string providerPath);

        /// <summary>
        /// Drops the handle tracked for a node.
        /// </summary>
        /// <param name="nodeId">The file NodeId.</param>
        void ForgetHandle(NodeId nodeId);

        /// <summary>
        /// Notifies the host that the provider's contents changed so it can
        /// refresh whatever it has materialised.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes once the host caught up.</returns>
        ValueTask OnProviderChangedAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Resolves a NodeId back to the provider path it represents.
        /// </summary>
        /// <param name="nodeId">The NodeId to resolve.</param>
        /// <param name="providerPath">The resolved provider-relative path.</param>
        /// <param name="isDirectory">Whether the path is a directory.</param>
        /// <param name="isRoot">Whether the path is the file system root.</param>
        /// <returns>
        /// <c>true</c> when the NodeId belongs to this host.
        /// </returns>
        bool TryGetProviderPath(NodeId nodeId, out string providerPath, out bool isDirectory, out bool isRoot);
    }
}
