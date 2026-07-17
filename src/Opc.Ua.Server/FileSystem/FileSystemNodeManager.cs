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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Exposes an <see cref="IFileSystemProvider"/> in the server's
    /// address space using the standard OPC UA Part 5 §C / Part 20 §4
    /// <c>FileType</c> / <c>FileDirectoryType</c> companion model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider is mounted as a single
    /// <c>FileDirectoryType</c> instance under the standard
    /// <c>Server.FileSystem</c> object (<c>ObjectIds.FileSystem</c>,
    /// <c>i=16314</c>) via a <c>HasComponent</c> reference. This is
    /// what <c>FileSystemClient.OpenServerFileSystem</c> on the
    /// client side expects to find.
    /// </para>
    /// <para>
    /// To mount several providers side-by-side, register one
    /// <see cref="FileSystemNodeManager"/> per provider — each owns
    /// its own namespace and adds its own <c>HasComponent</c> child
    /// under <c>i=16314</c>.
    /// </para>
    /// </remarks>
    public sealed class FileSystemNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Base URI used to build the namespace URI of a mounted
        /// provider. The full URI is
        /// <c>{NamespaceUriBase}/{MountName}</c>.
        /// </summary>
        public const string NamespaceUriBase = "http://opcfoundation.org/UA/Server/FileSystem";

        /// <summary>
        /// Initialises a new node manager backed by
        /// <paramref name="provider"/>.
        /// </summary>
        public FileSystemNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IFileSystemProvider provider)
            : base(server, configuration, BuildNamespaceUri(provider))
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            SystemContext.SystemHandle = this;
            SystemContext.NodeIdFactory = this;

            NamespaceUris = [BuildNamespaceUri(provider)];
            NamespaceIndex = base.NamespaceIndex;
        }

        /// <summary>
        /// The provider backing this node manager.
        /// </summary>
        public IFileSystemProvider Provider { get; }

        /// <summary>
        /// Namespace index assigned to this provider's nodes.
        /// </summary>
        public new ushort NamespaceIndex { get; }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return FileSystemNodeId.ConstructIdForComponent(node, NamespaceIndex);
        }

        /// <inheritdoc/>
        public override ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                if (!externalReferences.TryGetValue(ObjectIds.FileSystem,
                    out IList<IReference>? references))
                {
                    externalReferences[ObjectIds.FileSystem] = references = [];
                }
                NodeId rootId = FileSystemNodeId.BuildRoot(NamespaceIndex);
                references.Add(new NodeStateReference(
                    ReferenceTypeIds.HasComponent, false, rootId));
            }

            return default;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return new ValueTask<NodeHandle>();
                }

                if (nodeId.IdType != IdType.String &&
                    PredefinedNodes.TryGetValue(nodeId, out NodeState? node))
                {
                    return new ValueTask<NodeHandle>(new NodeHandle
                    {
                        NodeId = nodeId,
                        Node = node,
                        Validated = true
                    });
                }

                if (FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed))
                {
                    return new ValueTask<NodeHandle>(new NodeHandle
                    {
                        NodeId = nodeId,
                        Validated = false,
                        ParsedNodeId = new ParsedFileSystemNodeId(parsed)
                    });
                }

                return new ValueTask<NodeHandle>();
            }
        }

        /// <inheritdoc/>
        public override ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                foreach (FileHandle handle in m_handles.Values)
                {
                    handle.Dispose();
                }
                m_handles.Clear();
            }

            return base.DeleteAddressSpaceAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public override ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                foreach (FileHandle handle in m_handles.Values)
                {
                    handle.CloseSession(sessionId);
                }
            }

            return base.SessionClosingAsync(
                context,
                sessionId,
                deleteSubscriptions,
                cancellationToken);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_lock)
                {
                    foreach (FileHandle handle in m_handles.Values)
                    {
                        handle.Dispose();
                    }
                    m_handles.Clear();
                }
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override async ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            if (handle == null)
            {
                return null!;
            }
            if (handle.Validated)
            {
                return handle.Node;
            }

            NodeState? target = null;
            if (cache != null && cache.TryGetValue(handle.NodeId, out target))
            {
                if (target == null)
                {
                    return null!;
                }
                handle.Node = target;
                handle.Validated = true;
                return target;
            }

            try
            {
                if (handle.ParsedNodeId is not ParsedFileSystemNodeId parsed)
                {
                    return null!;
                }

                FileSystemEntry? entry = await Provider
                    .GetEntryAsync(parsed.Value.ProviderPath, cancellationToken)
                    .ConfigureAwait(false);
                if (parsed.Value.RootType != FileSystemNodeId.Root && entry == null)
                {
                    return null!;
                }

                NodeState? root = parsed.Value.RootType switch
                {
                    FileSystemNodeId.Root => new DirectoryObjectState(
                        context,
                        FileSystemNodeId.BuildRoot(NamespaceIndex),
                        string.Empty,
                        Provider.MountName,
                        isRoot: true),
                    FileSystemNodeId.Directory => new DirectoryObjectState(
                        context,
                        FileSystemNodeId.BuildDirectory(parsed.Value.ProviderPath, NamespaceIndex),
                        parsed.Value.ProviderPath,
                        entry!.Value.Name,
                        isRoot: false),
                    FileSystemNodeId.File => new FileObjectState(
                        context,
                        FileSystemNodeId.BuildFile(parsed.Value.ProviderPath, NamespaceIndex),
                        parsed.Value.ProviderPath,
                        entry!.Value.Name),
                    _ => null
                };
                if (root == null)
                {
                    return null!;
                }

                if (string.IsNullOrEmpty(parsed.Value.ComponentPath))
                {
                    handle.Validated = true;
                    handle.Node = target = root;
                    return target;
                }

                NodeState? component = root.FindChildBySymbolicName(
                    context, parsed.Value.ComponentPath!);
                if (component == null)
                {
                    return null!;
                }
                handle.Validated = true;
                handle.Node = target = component;
                return target;
            }
            finally
            {
                cache?.Add(handle.NodeId, target!);
            }
        }

        /// <summary>
        /// Returns the NodeId of the parent directory of the supplied
        /// provider path, or <see cref="NodeId.Null"/> when the path
        /// is at the root.
        /// </summary>
        internal NodeId GetParentNodeId(string providerPath)
        {
            if (string.IsNullOrEmpty(providerPath))
            {
                return NodeId.Null;
            }
            int slash = providerPath.LastIndexOf('/');
            string parent = slash < 0 ? string.Empty : providerPath[..slash];
            return string.IsNullOrEmpty(parent)
                ? FileSystemNodeId.BuildRoot(NamespaceIndex)
                : FileSystemNodeId.BuildDirectory(parent, NamespaceIndex);
        }

        /// <summary>
        /// Combines a parent provider path with a child name, taking
        /// care of empty parents and trailing slashes.
        /// </summary>
        internal string CombineProviderPath(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent))
            {
                return name;
            }
            return parent.TrimEnd('/') + "/" + name;
        }

        /// <summary>
        /// Looks up or creates the file handle for the given file
        /// NodeId. Returns <c>null</c> if the NodeId is not a file
        /// NodeId in this provider's namespace.
        /// </summary>
        internal FileHandle? GetOrCreateHandle(NodeId nodeId, string providerPath)
        {
            lock (m_lock)
            {
                if (m_handles.TryGetValue(nodeId, out FileHandle? handle))
                {
                    return handle;
                }
                handle = new FileHandle(Provider, providerPath);
                m_handles.Add(nodeId, handle);
                return handle;
            }
        }

        /// <summary>
        /// Drops and disposes the file handle associated with
        /// <paramref name="nodeId"/>. Called after delete / move so
        /// stale handles don't leak.
        /// </summary>
        internal void ForgetHandle(NodeId nodeId)
        {
            lock (m_lock)
            {
                if (m_handles.TryGetValue(nodeId, out FileHandle? handle))
                {
                    handle.Dispose();
                    m_handles.Remove(nodeId);
                }
            }
        }

        private static string BuildNamespaceUri(IFileSystemProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (string.IsNullOrEmpty(provider.MountName))
            {
                throw new ArgumentException(
                    "Provider MountName must be non-empty.",
                    nameof(provider));
            }
            return NamespaceUriBase + "/" + provider.MountName;
        }

        private readonly Dictionary<NodeId, FileHandle> m_handles = [];
        private readonly Lock m_lock = new();

        /// <summary>
        /// Boxes a <see cref="FileSystemNodeId"/> for storage in
        /// <see cref="NodeHandle.ParsedNodeId"/> (which expects a
        /// reference type).
        /// </summary>
        private sealed class ParsedFileSystemNodeId
        {
            public ParsedFileSystemNodeId(FileSystemNodeId value)
            {
                Value = value;
            }

            public FileSystemNodeId Value { get; }
        }
    }
}
