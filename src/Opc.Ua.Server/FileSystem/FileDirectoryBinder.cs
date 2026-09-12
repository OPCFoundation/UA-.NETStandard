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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
            bool initialized = false;
            try
            {
                await binding.InitializeAsync(cancellationToken).ConfigureAwait(false);
                initialized = true;
                return binding;
            }
            finally
            {
                if (!initialized)
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
            }
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
                m_logger = context.Telemetry.CreateLogger<FileDirectoryBinder>();
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
                if (!TryTrackOperation())
                {
                    return;
                }
                bool entered = false;
                try
                {
                    await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    entered = true;
                    // The binding may have been disposed while this refresh was
                    // queued behind another one.
                    if (m_disposed)
                    {
                        return;
                    }

                    await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CompleteOperation(entered);
                }
            }

            /// <summary>
            /// Tears the binding down under the same gate a refresh takes, so a
            /// refresh that is in flight or queued completes first. Disposing
            /// the gate underneath a refresh would fault its release and let
            /// the teardown race the node materialisation.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                if (!TryTrackOperation())
                {
                    return;
                }
                bool entered = false;
                try
                {
                    await m_gate.WaitAsync().ConfigureAwait(false);
                    entered = true;
                    if (m_disposed)
                    {
                        return;
                    }

                    FileHandle[] handles;
                    lock (m_lock)
                    {
                        m_disposed = true;
                        m_lookupById = [];
                        handles = new FileHandle[m_handles.Count];
                        m_handles.Values.CopyTo(handles, 0);
                        m_handles.Clear();
                    }
                    DetachDirectoryCallbacks(Directory);
                    foreach (MaterializedNode entry in m_nodesByPath.Values)
                    {
                        DetachCallbacks(entry.Node);
                    }
                    foreach (FileHandle handle in handles)
                    {
                        handle.Dispose();
                    }
                    foreach (MaterializedNode entry in m_nodesByPath.Values)
                    {
                        entry.Node.Parent?.RemoveChild(entry.Node);
                    }
                    m_nodesByPath.Clear();
                    m_nodesById.Clear();
                }
                finally
                {
                    CompleteOperation(entered);
                }
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
                    if (m_disposed)
                    {
                        return null;
                    }
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
                FileHandle? retired = null;
                lock (m_lock)
                {
                    if (m_handles.TryGetValue(nodeId, out FileHandle? handle))
                    {
                        m_handles.Remove(nodeId);
                        retired = handle;
                    }
                }
                retired?.Dispose();
            }

            public async ValueTask ApplyMutationAsync(
                FileSystemMutationKind kind,
                string path,
                string targetPath,
                NodeId sourceNodeId,
                CancellationToken cancellationToken)
            {
                if (!TryTrackOperation())
                {
                    throw new ServiceResultException(StatusCodes.BadShutdown);
                }
                bool entered = false;
                try
                {
                    await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    entered = true;
                    if (m_disposed)
                    {
                        throw new ServiceResultException(StatusCodes.BadShutdown);
                    }
                    if (m_refreshRequired)
                    {
                        await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                    }
                    await CheckCapacityAsync(kind, path, targetPath, cancellationToken).ConfigureAwait(false);
                    await FileSystemDirectoryOperations.ApplyProviderMutationAsync(
                        this, kind, path, targetPath, sourceNodeId, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                        NotSupportedException or ServiceResultException or OperationCanceledException or
                        InvalidOperationException)
                    {
                        m_refreshRequired = true;
                        m_logger.FileDirectoryRefreshFailed(ex, Directory.NodeId);
                    }
                }
                finally
                {
                    CompleteOperation(entered);
                }
            }

            public bool TryGetProviderPath(
                NodeId nodeId,
                out string providerPath,
                out bool isDirectory,
                out bool isRoot)
            {
                lock (m_lock)
                {
                    if (!m_disposed)
                    {
                        if (nodeId == Directory.NodeId)
                        {
                            providerPath = string.Empty;
                            isDirectory = true;
                            isRoot = true;
                            return true;
                        }
                        if (m_lookupById.TryGetValue(nodeId, out MaterializedNode? entry))
                        {
                            providerPath = entry.ProviderPath;
                            isDirectory = entry.IsDirectory;
                            isRoot = false;
                            return true;
                        }
                    }
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

            private bool TryTrackOperation()
            {
                lock (m_lock)
                {
                    if (m_disposed)
                    {
                        return false;
                    }
                    m_activeOperations++;
                    return true;
                }
            }

            private void CompleteOperation(bool entered)
            {
                if (entered)
                {
                    m_gate.Release();
                }
                lock (m_lock)
                {
                    if (--m_activeOperations == 0 && m_disposed)
                    {
                        m_gate.Dispose();
                    }
                }
            }

            private async ValueTask CheckCapacityAsync(
                FileSystemMutationKind kind,
                string path,
                string targetPath,
                CancellationToken cancellationToken)
            {
                if (kind == FileSystemMutationKind.Delete)
                {
                    return;
                }
                string destination = kind is FileSystemMutationKind.Move or FileSystemMutationKind.Copy
                    ? targetPath
                    : path;
                string parent = GetParentPath(destination);
                if (kind == FileSystemMutationKind.Move && parent == GetParentPath(path))
                {
                    return;
                }
                if (await Provider.GetEntryAsync(destination, cancellationToken).ConfigureAwait(false) != null)
                {
                    return;
                }

                int count = 0;
                await foreach (FileSystemEntry _ in Provider.EnumerateAsync(parent, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (++count >= m_options.MaxEntries)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The directory cannot admit another entry within the configured binding limit.");
                    }
                }
                if (m_options.MaxEntries == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The directory binding does not admit entries.");
                }
            }

            private async ValueTask RefreshCoreAsync(CancellationToken cancellationToken)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                await ReconcileDirectoryAsync(Directory, string.Empty, 0, seen, cancellationToken)
                    .ConfigureAwait(false);
                RemoveStaleNodes(seen);
                lock (m_lock)
                {
                    m_lookupById = new Dictionary<NodeId, MaterializedNode>(m_nodesById);
                }
                m_refreshRequired = false;
            }

            private static string GetParentPath(string path)
            {
                int slash = path.LastIndexOf('/');
                return slash < 0 ? string.Empty : path[..slash];
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
                        await RegisterNodeAsync(existing, cancellationToken).ConfigureAwait(false);
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
                MaterializedNode materialized = AddMaterializedNode(entry.Path, node, isDirectory: true);
                await RegisterNodeAsync(materialized, cancellationToken).ConfigureAwait(false);
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
                        await RegisterNodeAsync(existing, cancellationToken).ConfigureAwait(false);
                        return file;
                    }
                    RemoveNode(existing);
                }

                var node = new FileObjectState(m_context, BuildFileNodeId(entry.Path), entry.Path, entry.Name, this);
                parent.AddChild(node);
                MaterializedNode materialized = AddMaterializedNode(entry.Path, node, isDirectory: false);
                await RegisterNodeAsync(materialized, cancellationToken).ConfigureAwait(false);
                return node;
            }

            private async ValueTask RegisterNodeAsync(MaterializedNode entry, CancellationToken cancellationToken)
            {
                if (entry.Registered)
                {
                    return;
                }
                if (!m_initializing && m_registerNode != null)
                {
                    await m_registerNode(entry.Node, cancellationToken).ConfigureAwait(false);
                }
                entry.Registered = true;
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

            private MaterializedNode AddMaterializedNode(string providerPath, BaseInstanceState node, bool isDirectory)
            {
                var entry = new MaterializedNode(providerPath, node, isDirectory);
                m_nodesByPath[providerPath] = entry;
                m_nodesById[node.NodeId] = entry;
                return entry;
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
            private readonly ILogger m_logger;
            private readonly FileDirectoryBindingOptions m_options;
            private readonly Func<NodeState, CancellationToken, ValueTask>? m_registerNode;
            private readonly Lock m_lock = new();
            private readonly string m_nodeIdPrefix;
            private Dictionary<NodeId, MaterializedNode> m_lookupById = [];
            private bool m_disposed;
            private bool m_initializing;
            private bool m_refreshRequired;
            private int m_activeOperations;

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

                public bool Registered { get; set; }
            }
        }
    }

    internal static partial class FileDirectoryBinderLog
    {
        [LoggerMessage(EventId = ServerEventIds.FileDirectoryBinder, Level = LogLevel.Error,
            Message = "The provider mutation committed, but file-directory {DirectoryId} refresh failed. " +
                "RefreshAsync or the next mutation will retry reconciliation.")]
        public static partial void FileDirectoryRefreshFailed(this ILogger logger, Exception ex, NodeId directoryId);
    }
}
