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
    /// Options that control how a bound FileDirectoryType materialises provider entries.
    /// </summary>
    public sealed class FileDirectoryBindingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether CreateFile and CreateDirectory are exposed as writable operations.
        /// </summary>
        public bool AllowCreate { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether DeleteFileSystemObject is exposed as a writable operation.
        /// </summary>
        public bool AllowDelete { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether MoveOrCopy is exposed as a writable operation.
        /// </summary>
        public bool AllowMoveOrCopy { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of entries materialised per directory.
        /// </summary>
        public int MaxEntries { get; set; } = 1024;

        /// <summary>
        /// Gets or sets the maximum directory nesting materialised below the bound root.
        /// </summary>
        public int MaxDepth { get; set; } = 8;
    }

    /// <summary>
    /// Represents an active binding between a FileDirectoryType node and an IFileSystemProvider.
    /// </summary>
    public interface IFileDirectoryBinding : IAsyncDisposable
    {
        /// <summary>
        /// Gets the bound directory node.
        /// </summary>
        FileDirectoryState Directory { get; }

        /// <summary>
        /// Gets the provider that backs the directory node.
        /// </summary>
        IFileSystemProvider Provider { get; }

        /// <summary>
        /// Re-reads the provider and reconciles materialised child nodes.
        /// </summary>
        ValueTask RefreshAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Creates bindings that back existing FileDirectoryType nodes with file-system providers.
    /// </summary>
    public interface IFileDirectoryBinder
    {
        /// <summary>
        /// Binds a FileDirectoryType node to a provider and materialises its current contents.
        /// </summary>
        ValueTask<IFileDirectoryBinding> BindAsync(
            FileDirectoryState directory,
            IFileSystemProvider provider,
            ISystemContext context,
            FileDirectoryBindingOptions? options = null,
            Func<NodeState, CancellationToken, ValueTask>? registerNode = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default FileDirectoryType binder implementation.
    /// </summary>
    public sealed class FileDirectoryBinder : IFileDirectoryBinder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FileDirectoryBinder"/> class.
        /// </summary>
        public FileDirectoryBinder(ITelemetryContext telemetry)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
        }

        /// <inheritdoc/>
        public async ValueTask<IFileDirectoryBinding> BindAsync(
            FileDirectoryState directory,
            IFileSystemProvider provider,
            ISystemContext context,
            FileDirectoryBindingOptions? options = null,
            Func<NodeState, CancellationToken, ValueTask>? registerNode = null,
            CancellationToken cancellationToken = default)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var binding = new FileDirectoryBinding(directory, provider, context,
                options ?? new FileDirectoryBindingOptions(), registerNode);
            await binding.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return binding;
        }

        private sealed class FileDirectoryBinding : IFileDirectoryBinding, IFileSystemHost
        {
            public FileDirectoryBinding(
                FileDirectoryState directory,
                IFileSystemProvider provider,
                ISystemContext context,
                FileDirectoryBindingOptions options,
                Func<NodeState, CancellationToken, ValueTask>? registerNode)
            {
                Directory = directory;
                Provider = provider;
                m_context = context;
                m_options = ValidateOptions(options);
                m_registerNode = registerNode;
                m_nodeIdPrefix = "FileDirectoryBinding:" + directory.NodeId;
            }

            public FileDirectoryState Directory { get; }

            public IFileSystemProvider Provider { get; }

            public bool AllowCreate => m_options.AllowCreate;

            public bool AllowDelete => m_options.AllowDelete;

            public bool AllowMoveOrCopy => m_options.AllowMoveOrCopy;

            public bool UsesVirtualDirectoryBrowsing => false;

            public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
            {
                await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    await ReconcileDirectoryAsync(Directory, string.Empty, 0, seen, cancellationToken)
                        .ConfigureAwait(false);
                    RemoveStaleNodes(seen);
                }
                finally
                {
                    m_gate.Release();
                }
            }

            public ValueTask DisposeAsync()
            {
                if (m_disposed)
                {
                    return default;
                }

                m_disposed = true;
                DetachDirectoryCallbacks(Directory);
                foreach (MaterializedNode entry in m_nodesByPath.Values)
                {
                    DetachCallbacks(entry.Node);
                }
                foreach (FileHandle handle in m_handles.Values)
                {
                    handle.Dispose();
                }
                foreach (MaterializedNode entry in m_nodesByPath.Values)
                {
                    entry.Node.Parent?.RemoveChild(entry.Node);
                }
                m_nodesByPath.Clear();
                m_nodesById.Clear();
                m_handles.Clear();
                m_gate.Dispose();
                return default;
            }

            public NodeId BuildDirectoryNodeId(string providerPath)
            {
                return string.IsNullOrEmpty(providerPath)
                    ? Directory.NodeId
                    : CreateMaterializedNodeId("dir", providerPath);
            }

            public NodeId BuildFileNodeId(string providerPath)
            {
                return CreateMaterializedNodeId("file", providerPath);
            }

            public string CombineProviderPath(string parent, string name)
            {
                if (string.IsNullOrEmpty(parent))
                {
                    return name;
                }
                return parent.TrimEnd('/') + "/" + name;
            }

            public NodeId GetParentNodeId(string providerPath)
            {
                if (string.IsNullOrEmpty(providerPath))
                {
                    return NodeId.Null;
                }
                int slash = providerPath.LastIndexOf('/');
                string parent = slash < 0 ? string.Empty : providerPath[..slash];
                return string.IsNullOrEmpty(parent) ? Directory.NodeId : BuildDirectoryNodeId(parent);
            }

            public FileHandle? GetOrCreateHandle(NodeId nodeId, string providerPath)
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

            public void ForgetHandle(NodeId nodeId)
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

            public async ValueTask OnProviderChangedAsync(CancellationToken cancellationToken)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            public bool TryGetProviderPath(
                NodeId nodeId,
                out string providerPath,
                out bool isDirectory,
                out bool isRoot)
            {
                if (nodeId == Directory.NodeId)
                {
                    providerPath = string.Empty;
                    isDirectory = true;
                    isRoot = true;
                    return true;
                }
                if (m_nodesById.TryGetValue(nodeId, out MaterializedNode? entry))
                {
                    providerPath = entry.ProviderPath;
                    isDirectory = entry.IsDirectory;
                    isRoot = false;
                    return true;
                }

                providerPath = string.Empty;
                isDirectory = false;
                isRoot = false;
                return false;
            }

            public async ValueTask InitializeAsync(CancellationToken cancellationToken)
            {
                WireDirectoryCallbacks(Directory, providerPath: string.Empty);
                m_initializing = true;
                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    m_initializing = false;
                }
            }

            private async ValueTask ReconcileDirectoryAsync(
                FileDirectoryState parent,
                string providerPath,
                int depth,
                HashSet<string> seen,
                CancellationToken cancellationToken)
            {
                List<FileSystemEntry> entries = [];
                await foreach (FileSystemEntry entry in Provider.EnumerateAsync(providerPath, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (entries.Count >= m_options.MaxEntries)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The directory contains more entries than the configured binding limit.");
                    }
                    entries.Add(entry);
                }

                foreach (FileSystemEntry entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        if (depth >= m_options.MaxDepth)
                        {
                            continue;
                        }

                        FileDirectoryState child = await GetOrCreateDirectoryAsync(parent, entry, cancellationToken)
                            .ConfigureAwait(false);
                        seen.Add(entry.Path);
                        if (depth + 1 < m_options.MaxDepth)
                        {
                            await ReconcileDirectoryAsync(child, entry.Path, depth + 1, seen, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await GetOrCreateFileAsync(parent, entry, cancellationToken).ConfigureAwait(false);
                        seen.Add(entry.Path);
                    }
                }
            }

            private async ValueTask<FileDirectoryState> GetOrCreateDirectoryAsync(
                FileDirectoryState parent,
                FileSystemEntry entry,
                CancellationToken cancellationToken)
            {
                if (m_nodesByPath.TryGetValue(entry.Path, out MaterializedNode? existing))
                {
                    if (existing.Node is FileDirectoryState directory)
                    {
                        return directory;
                    }
                    RemoveNode(existing);
                }

                var node = new DirectoryObjectState(
                    m_context,
                    BuildDirectoryNodeId(entry.Path),
                    entry.Path,
                    entry.Name,
                    isRoot: false,
                    this);
                parent.AddChild(node);
                AddMaterializedNode(entry.Path, node, isDirectory: true);
                await RegisterNodeAsync(node, cancellationToken).ConfigureAwait(false);
                return node;
            }

            private async ValueTask<FileState> GetOrCreateFileAsync(
                FileDirectoryState parent,
                FileSystemEntry entry,
                CancellationToken cancellationToken)
            {
                if (m_nodesByPath.TryGetValue(entry.Path, out MaterializedNode? existing))
                {
                    if (existing.Node is FileState file)
                    {
                        return file;
                    }
                    RemoveNode(existing);
                }

                var node = new FileObjectState(m_context, BuildFileNodeId(entry.Path), entry.Path, entry.Name, this);
                parent.AddChild(node);
                AddMaterializedNode(entry.Path, node, isDirectory: false);
                await RegisterNodeAsync(node, cancellationToken).ConfigureAwait(false);
                return node;
            }

            private async ValueTask RegisterNodeAsync(NodeState node, CancellationToken cancellationToken)
            {
                if (!m_initializing && m_registerNode != null)
                {
                    await m_registerNode(node, cancellationToken).ConfigureAwait(false);
                }
            }

            private void RemoveStaleNodes(HashSet<string> seen)
            {
                List<MaterializedNode> stale = [];
                foreach (MaterializedNode entry in m_nodesByPath.Values)
                {
                    if (!seen.Contains(entry.ProviderPath))
                    {
                        stale.Add(entry);
                    }
                }
                stale.Sort(static (left, right) => right.ProviderPath.Length.CompareTo(left.ProviderPath.Length));
                foreach (MaterializedNode entry in stale)
                {
                    RemoveNode(entry);
                }
            }

            private void RemoveNode(MaterializedNode entry)
            {
                List<MaterializedNode> descendants = [];
                string prefix = entry.ProviderPath + "/";
                foreach (MaterializedNode candidate in m_nodesByPath.Values)
                {
                    if (candidate.ProviderPath.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        descendants.Add(candidate);
                    }
                }
                descendants.Sort(static (left, right) => right.ProviderPath.Length.CompareTo(left.ProviderPath.Length));
                foreach (MaterializedNode descendant in descendants)
                {
                    RemoveSingleNode(descendant);
                }
                RemoveSingleNode(entry);
            }

            private void RemoveSingleNode(MaterializedNode entry)
            {
                DetachCallbacks(entry.Node);
                ForgetHandle(entry.Node.NodeId);
                entry.Node.Parent?.RemoveChild(entry.Node);
                m_nodesByPath.Remove(entry.ProviderPath);
                m_nodesById.Remove(entry.Node.NodeId);
            }

            private void AddMaterializedNode(string providerPath, BaseInstanceState node, bool isDirectory)
            {
                var entry = new MaterializedNode(providerPath, node, isDirectory);
                m_nodesByPath[providerPath] = entry;
                m_nodesById[node.NodeId] = entry;
            }

            private void WireDirectoryCallbacks(FileDirectoryState directory, string providerPath)
            {
                EnsureDirectoryMethods(directory);
                directory.DeleteFileSystemObject!.OnCallAsync = (context, method, objectId, objectToDelete, ct) =>
                    FileSystemDirectoryOperations.DeleteAsync(this, objectToDelete, ct);
                directory.CreateFile!.OnCallAsync = (context, method, objectId, fileName, requestFileOpen, ct) =>
                    FileSystemDirectoryOperations.CreateFileAsync(
                        this, context, providerPath, fileName, requestFileOpen, ct);
                directory.CreateDirectory!.OnCallAsync = (context, method, objectId, directoryName, ct) =>
                    FileSystemDirectoryOperations.CreateDirectoryAsync(this, providerPath, directoryName, ct);
                directory.MoveOrCopy!.OnCallAsync = (
                    context,
                    method,
                    objectId,
                    objectToMoveOrCopy,
                    targetDirectory,
                    createCopy,
                    newName,
                    ct) => FileSystemDirectoryOperations.MoveOrCopyAsync(
                        this, objectToMoveOrCopy, targetDirectory, createCopy, newName, ct);
            }

            private void DetachDirectoryCallbacks(FileDirectoryState directory)
            {
                if (directory.DeleteFileSystemObject != null)
                {
                    directory.DeleteFileSystemObject.OnCallAsync = null;
                }
                if (directory.CreateFile != null)
                {
                    directory.CreateFile.OnCallAsync = null;
                }
                if (directory.CreateDirectory != null)
                {
                    directory.CreateDirectory.OnCallAsync = null;
                }
                if (directory.MoveOrCopy != null)
                {
                    directory.MoveOrCopy.OnCallAsync = null;
                }
            }

            private void EnsureDirectoryMethods(FileDirectoryState directory)
            {
                if (directory.DeleteFileSystemObject == null)
                {
                    directory.DeleteFileSystemObject = new DeleteFileMethodState(directory);
                    directory.DeleteFileSystemObject.Create(
                        m_context,
                        MethodIds.FileDirectoryType_DeleteFileSystemObject,
                        new QualifiedName(BrowseNames.DeleteFileSystemObject),
                        new LocalizedText(BrowseNames.DeleteFileSystemObject), false);
                }
                directory.DeleteFileSystemObject.Executable = true;
                directory.DeleteFileSystemObject.UserExecutable = true;

                if (directory.CreateFile == null)
                {
                    directory.CreateFile = new CreateFileMethodState(directory);
                    directory.CreateFile.Create(m_context, MethodIds.FileDirectoryType_CreateFile,
                        new QualifiedName(BrowseNames.CreateFile),
                        new LocalizedText(BrowseNames.CreateFile), false);
                }
                directory.CreateFile.Executable = true;
                directory.CreateFile.UserExecutable = true;

                if (directory.CreateDirectory == null)
                {
                    directory.CreateDirectory = new CreateDirectoryMethodState(directory);
                    directory.CreateDirectory.Create(m_context, MethodIds.FileDirectoryType_CreateDirectory,
                        new QualifiedName(BrowseNames.CreateDirectory),
                        new LocalizedText(BrowseNames.CreateDirectory), false);
                }
                directory.CreateDirectory.Executable = true;
                directory.CreateDirectory.UserExecutable = true;

                if (directory.MoveOrCopy == null)
                {
                    directory.MoveOrCopy = new MoveOrCopyMethodState(directory);
                    directory.MoveOrCopy.Create(m_context, MethodIds.FileDirectoryType_MoveOrCopy,
                        new QualifiedName(BrowseNames.MoveOrCopy),
                        new LocalizedText(BrowseNames.MoveOrCopy), false);
                }
                directory.MoveOrCopy.Executable = true;
                directory.MoveOrCopy.UserExecutable = true;
            }

            private NodeId CreateMaterializedNodeId(string kind, string providerPath)
            {
                string escapedPath = Uri.EscapeDataString(providerPath);
                return new NodeId(m_nodeIdPrefix + "/" + kind + "/" + escapedPath, Directory.NodeId.NamespaceIndex);
            }

            private static void DetachCallbacks(NodeState node)
            {
                if (node is DirectoryObjectState directory)
                {
                    directory.DetachCallbacks();
                }
                else if (node is FileObjectState file)
                {
                    file.DetachCallbacks();
                }
            }

            private static FileDirectoryBindingOptions ValidateOptions(FileDirectoryBindingOptions options)
            {
                if (options.MaxEntries < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(options), "MaxEntries must be non-negative.");
                }
                if (options.MaxDepth < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(options), "MaxDepth must be non-negative.");
                }

                return options;
            }

            private readonly SemaphoreSlim m_gate = new(1, 1);
            private readonly Dictionary<NodeId, FileHandle> m_handles = [];
            private readonly Dictionary<NodeId, MaterializedNode> m_nodesById = [];
            private readonly Dictionary<string, MaterializedNode> m_nodesByPath = new(StringComparer.Ordinal);
            private readonly ISystemContext m_context;
            private readonly FileDirectoryBindingOptions m_options;
            private readonly Func<NodeState, CancellationToken, ValueTask>? m_registerNode;
            private readonly Lock m_lock = new();
            private readonly string m_nodeIdPrefix;
            private bool m_disposed;
            private bool m_initializing;

            private sealed class MaterializedNode
            {
                public MaterializedNode(string providerPath, BaseInstanceState node, bool isDirectory)
                {
                    ProviderPath = providerPath;
                    Node = node;
                    IsDirectory = isDirectory;
                }

                public string ProviderPath { get; }

                public BaseInstanceState Node { get; }

                public bool IsDirectory { get; }
            }
        }
    }
}
