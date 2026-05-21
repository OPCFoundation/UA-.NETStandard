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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// A <c>System.IO</c>-style asynchronous client over the OPC UA
    /// file-system primitives defined in Part 5 §C and Part 20 §4 (the
    /// <c>FileType</c>, <c>FileDirectoryType</c> and
    /// <c>TemporaryFileTransferType</c> object types).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="FileSystemClient"/> is rooted at any object of
    /// type <c>FileDirectoryType</c> (or a subtype thereof). The
    /// helper <see cref="OpenServerFileSystem(ISession, FileSystemClientOptions?)"/>
    /// roots at the standard <c>Server.FileSystem</c> object
    /// (<c>NodeId i=16314</c>). Other directories — for example a
    /// vendor-specific location reached via Browse — can be passed to
    /// the regular constructor.
    /// </para>
    /// <para>
    /// Path syntax is documented on <see cref="UaPath"/>. Briefly:
    /// segments are <see cref="QualifiedName"/>s, separated by
    /// forward slashes (<c>'/'</c>), with optional
    /// <c>"&lt;ns&gt;:"</c> namespace prefix per segment. The empty
    /// string and <c>"/"</c> both mean "the root".
    /// </para>
    /// <para>
    /// All operations are async-only. When a server returns a
    /// well-known file-system status code
    /// (<c>BadNoMatch</c>, <c>BadNotFound</c>, <c>BadNodeIdUnknown</c>,
    /// <c>BadBrowseNameDuplicated</c>, <c>BadUserAccessDenied</c>, …)
    /// the failure is translated into the familiar <c>System.IO</c>
    /// equivalent (<see cref="FileNotFoundException"/>,
    /// <see cref="DirectoryNotFoundException"/>,
    /// <see cref="UnauthorizedAccessException"/>,
    /// <see cref="IOException"/>); other Bad codes propagate as
    /// <see cref="ServiceResultException"/> per the existing OPC UA
    /// convention.
    /// </para>
    /// </remarks>
    public sealed class FileSystemClient
    {
        /// <summary>
        /// Creates a new <see cref="FileSystemClient"/> rooted at the
        /// supplied <paramref name="rootDirectoryId"/>.
        /// </summary>
        /// <param name="session">The OPC UA session used for all
        /// service calls.</param>
        /// <param name="rootDirectoryId">The NodeId of the
        /// <c>FileDirectoryType</c> instance to use as the root. Must
        /// not be null.</param>
        /// <param name="options">Optional configuration; defaults are
        /// applied when <c>null</c>.</param>
        public FileSystemClient(
            ISession session,
            NodeId rootDirectoryId,
            FileSystemClientOptions? options = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (rootDirectoryId.IsNull)
            {
                throw new ArgumentNullException(nameof(rootDirectoryId));
            }
            Options = (options ?? new FileSystemClientOptions()).Clone();
            Options.Validate();
            m_pathCache = new PathCache(Options.PathCacheSize);
            Root = new UaDirectoryInfo(this, parent: null, rootDirectoryId, kRootBrowseName, []);
        }

        /// <summary>
        /// Returns a <see cref="FileSystemClient"/> rooted at the
        /// standard OPC UA <c>Server.FileSystem</c> object
        /// (<c>NodeId i=16314</c>). Servers that do not expose this
        /// object will fail on the first operation with
        /// <see cref="DirectoryNotFoundException"/>.
        /// </summary>
        public static FileSystemClient OpenServerFileSystem(
            ISession session,
            FileSystemClientOptions? options = null)
        {
            return new FileSystemClient(
                session,
                ObjectIds.FileSystem,
                options);
        }

        /// <summary>The session used for all service calls.</summary>
        public ISession Session { get; }

        /// <summary>The (cloned, immutable) configuration.</summary>
        public FileSystemClientOptions Options { get; }

        /// <summary>The root directory.</summary>
        public UaDirectoryInfo Root { get; }

        // ----------------------------------------------------------------
        // Public path-based API
        // ----------------------------------------------------------------

        /// <summary>
        /// Resolves <paramref name="path"/> as a directory; throws
        /// <see cref="DirectoryNotFoundException"/> if missing or if
        /// the resolved object is not a directory.
        /// </summary>
        public async ValueTask<UaDirectoryInfo> GetDirectoryAsync(
            string path,
            CancellationToken ct = default)
        {
            UaFileSystemInfo? info = await GetInfoAsync(path, ct).ConfigureAwait(false);
            if (info is UaDirectoryInfo dir)
            {
                return dir;
            }
            throw (Exception)FileSystemErrors.NotFound(path, targetIsDirectory: true);
        }

        /// <summary>
        /// Resolves <paramref name="path"/> as a file; throws
        /// <see cref="FileNotFoundException"/> if missing or if the
        /// resolved object is not a file.
        /// </summary>
        public async ValueTask<UaFileInfo> GetFileAsync(
            string path,
            CancellationToken ct = default)
        {
            UaFileSystemInfo? info = await GetInfoAsync(path, ct).ConfigureAwait(false);
            if (info is UaFileInfo file)
            {
                return file;
            }
            throw (Exception)FileSystemErrors.NotFound(path, targetIsDirectory: false);
        }

        /// <summary>
        /// Resolves <paramref name="path"/> and returns the matching
        /// info object, or <c>null</c> when nothing exists.
        /// </summary>
        public async ValueTask<UaFileSystemInfo?> GetInfoAsync(
            string path,
            CancellationToken ct = default)
        {
            QualifiedName[] segments = UaPath.Parse(path);
            if (segments.Length == 0)
            {
                return Root;
            }
            ResolvedNode? resolved = await ResolveSegmentsAsync(segments, throwOnMissing: false, ct)
                .ConfigureAwait(false);
            if (resolved == null)
            {
                return null;
            }
            return await BuildInfoAsync(resolved.Value, segments, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns <c>true</c> when something (file or directory)
        /// exists at <paramref name="path"/>.
        /// </summary>
        public async ValueTask<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            return await GetInfoAsync(path, ct).ConfigureAwait(false) != null;
        }

        /// <summary>
        /// Returns <c>true</c> when a file exists at
        /// <paramref name="path"/>.
        /// </summary>
        public async ValueTask<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            UaFileSystemInfo? info = await GetInfoAsync(path, ct).ConfigureAwait(false);
            return info is UaFileInfo;
        }

        /// <summary>
        /// Returns <c>true</c> when a directory exists at
        /// <paramref name="path"/>.
        /// </summary>
        public async ValueTask<bool> DirectoryExistsAsync(string path, CancellationToken ct = default)
        {
            UaFileSystemInfo? info = await GetInfoAsync(path, ct).ConfigureAwait(false);
            return info is UaDirectoryInfo;
        }

        /// <summary>
        /// Creates the directory at <paramref name="path"/>, including
        /// any missing intermediate directories when
        /// <paramref name="createIntermediate"/> is <c>true</c>.
        /// </summary>
        /// <param name="path">The target path.</param>
        /// <param name="createIntermediate">When <c>true</c>, missing
        /// parent directories are created in turn.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="IOException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask<UaDirectoryInfo> CreateDirectoryAsync(
            string path,
            bool createIntermediate = true,
            CancellationToken ct = default)
        {
            QualifiedName[] segments = UaPath.Parse(path);
            if (segments.Length == 0)
            {
                return Root;
            }
            UaDirectoryInfo current = Root;
            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                QualifiedName segment = segments[i];
                NodeId? childId = await TryResolveSingleAsync(current.NodeId, segment, ct)
                    .ConfigureAwait(false);
                if (childId != null)
                {
                    UaFileSystemInfo info = await BuildInfoAsync(
                        new ResolvedNode(childId.Value, segment),
                        segments.Take(i + 1).ToArray(),
                        ct).ConfigureAwait(false);
                    if (info is UaDirectoryInfo dir)
                    {
                        current = dir;
                        continue;
                    }
                    throw new IOException(
                        $"Cannot create directory '{path}': '{string.Join("/", segments.Take(i + 1).Select(UaPath.FormatSegment))}' is not a directory.");
                }
                if (!createIntermediate && !isLast)
                {
                    throw (Exception)FileSystemErrors.NotFound(
                        UaPath.Format(segments.Take(i + 1).ToArray()),
                        targetIsDirectory: true);
                }
                if (segment.NamespaceIndex != 0)
                {
                    throw new ArgumentException(
                        $"Cannot create '{UaPath.FormatSegment(segment)}': leaf segments must not include a namespace prefix; the server picks the BrowseName namespace.",
                        nameof(path));
                }
                current = await CreateDirectoryInAsync(current, segment.Name!, ct)
                    .ConfigureAwait(false);
            }
            return current;
        }

        /// <summary>
        /// Creates the file at <paramref name="path"/>, optionally
        /// creating any missing intermediate directories. The server
        /// is asked NOT to immediately open the file
        /// (<c>requestFileOpen: false</c>).
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask<UaFileInfo> CreateFileAsync(
            string path,
            bool createIntermediate = true,
            CancellationToken ct = default)
        {
            QualifiedName[] segments = UaPath.Parse(path);
            if (segments.Length == 0)
            {
                throw new ArgumentException("Cannot create a file at the root path.", nameof(path));
            }
            UaDirectoryInfo parent;
            if (segments.Length == 1)
            {
                parent = Root;
            }
            else
            {
                string parentPath = UaPath.Format(segments.Take(segments.Length - 1).ToArray());
                if (createIntermediate)
                {
                    parent = await CreateDirectoryAsync(parentPath, true, ct).ConfigureAwait(false);
                }
                else
                {
                    parent = await GetDirectoryAsync(parentPath, ct).ConfigureAwait(false);
                }
            }
            QualifiedName leaf = segments[^1];
            if (leaf.NamespaceIndex != 0)
            {
                throw new ArgumentException(
                    $"Cannot create file '{UaPath.FormatSegment(leaf)}': leaf segments must not include a namespace prefix; the server picks the BrowseName namespace.",
                    nameof(path));
            }
            return await CreateFileInAsync(parent, leaf.Name!, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the file or directory at <paramref name="path"/>.
        /// When <paramref name="recursive"/> is <c>true</c> and the
        /// target is a directory, the server's recursive
        /// <c>Delete</c> primitive is invoked (no client-side
        /// traversal).
        /// </summary>
        public async ValueTask DeleteAsync(
            string path,
            bool recursive = false,
            CancellationToken ct = default)
        {
            UaFileSystemInfo? info = await GetInfoAsync(path, ct).ConfigureAwait(false);
            if (info == null)
            {
                throw (Exception)FileSystemErrors.NotFound(path, targetIsDirectory: false);
            }
            await DeleteCoreAsync(info, recursive, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves a file or directory from <paramref name="srcPath"/>
        /// to <paramref name="destPath"/>.
        /// </summary>
        public async ValueTask<UaFileSystemInfo> MoveAsync(
            string srcPath,
            string destPath,
            CancellationToken ct = default)
        {
            UaFileSystemInfo? src = await GetInfoAsync(srcPath, ct).ConfigureAwait(false);
            if (src == null)
            {
                throw (Exception)FileSystemErrors.NotFound(srcPath, targetIsDirectory: false);
            }
            return await MoveOrCopyAsync(src, destPath, copy: false, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a file or directory from <paramref name="srcPath"/>
        /// to <paramref name="destPath"/>.
        /// </summary>
        public async ValueTask<UaFileSystemInfo> CopyAsync(
            string srcPath,
            string destPath,
            CancellationToken ct = default)
        {
            UaFileSystemInfo? src = await GetInfoAsync(srcPath, ct).ConfigureAwait(false);
            if (src == null)
            {
                throw (Exception)FileSystemErrors.NotFound(srcPath, targetIsDirectory: false);
            }
            return await MoveOrCopyAsync(src, destPath, copy: true, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the file at <paramref name="path"/> for reading.
        /// </summary>
        public async ValueTask<UaFileStream> OpenReadAsync(
            string path,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetFileAsync(path, ct).ConfigureAwait(false);
            await file.RefreshAsync(ct).ConfigureAwait(false);
            return await file.OpenReadAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the file at <paramref name="path"/> for writing
        /// (truncates).
        /// </summary>
        public async ValueTask<UaFileStream> OpenWriteAsync(
            string path,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetFileAsync(path, ct).ConfigureAwait(false);
            await file.RefreshAsync(ct).ConfigureAwait(false);
            return await file.OpenWriteAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the file at <paramref name="path"/> for appending.
        /// </summary>
        public async ValueTask<UaFileStream> OpenAppendAsync(
            string path,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetFileAsync(path, ct).ConfigureAwait(false);
            await file.RefreshAsync(ct).ConfigureAwait(false);
            return await file.OpenAppendAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the file at <paramref name="path"/> with the supplied
        /// <paramref name="mode"/>.
        /// </summary>
        public async ValueTask<UaFileStream> OpenAsync(
            string path,
            UaFileMode mode,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetFileAsync(path, ct).ConfigureAwait(false);
            await file.RefreshAsync(ct).ConfigureAwait(false);
            return await file.OpenAsync(mode, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the entire file at <paramref name="path"/> into
        /// memory.
        /// </summary>
        public async ValueTask<byte[]> ReadAllBytesAsync(
            string path,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetFileAsync(path, ct).ConfigureAwait(false);
            await file.RefreshAsync(ct).ConfigureAwait(false);
            return await file.ReadAllBytesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the entire file at <paramref name="path"/> as text.
        /// </summary>
        public async ValueTask<string> ReadAllTextAsync(
            string path,
            Encoding? encoding = null,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetFileAsync(path, ct).ConfigureAwait(false);
            await file.RefreshAsync(ct).ConfigureAwait(false);
            return await file.ReadAllTextAsync(encoding, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Truncates and overwrites the file at <paramref name="path"/>
        /// with <paramref name="bytes"/>. Creates the file (and any
        /// missing intermediate directories) when it does not exist.
        /// </summary>
        public async ValueTask WriteAllBytesAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetOrCreateFileAsync(path, ct).ConfigureAwait(false);
            await file.WriteAllBytesAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Truncates and overwrites the file at <paramref name="path"/>
        /// with <paramref name="contents"/>. Creates the file (and any
        /// missing intermediate directories) when it does not exist.
        /// </summary>
        public async ValueTask WriteAllTextAsync(
            string path,
            string contents,
            Encoding? encoding = null,
            CancellationToken ct = default)
        {
            UaFileInfo file = await GetOrCreateFileAsync(path, ct).ConfigureAwait(false);
            await file.WriteAllTextAsync(contents, encoding, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerates the immediate children of the directory at
        /// <paramref name="path"/>.
        /// </summary>
        public async IAsyncEnumerable<UaFileSystemInfo> EnumerateAsync(
            string path = UaPath.Root,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            UaDirectoryInfo dir = await GetDirectoryAsync(path, ct).ConfigureAwait(false);
            await foreach (UaFileSystemInfo child in dir.EnumerateAsync(ct).ConfigureAwait(false))
            {
                yield return child;
            }
        }

        /// <summary>
        /// Enumerates the immediate file children of the directory at
        /// <paramref name="path"/>.
        /// </summary>
        public async IAsyncEnumerable<UaFileInfo> EnumerateFilesAsync(
            string path = UaPath.Root,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            UaDirectoryInfo dir = await GetDirectoryAsync(path, ct).ConfigureAwait(false);
            await foreach (UaFileInfo child in dir.EnumerateFilesAsync(ct).ConfigureAwait(false))
            {
                yield return child;
            }
        }

        /// <summary>
        /// Enumerates the immediate directory children of the
        /// directory at <paramref name="path"/>.
        /// </summary>
        public async IAsyncEnumerable<UaDirectoryInfo> EnumerateDirectoriesAsync(
            string path = UaPath.Root,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            UaDirectoryInfo dir = await GetDirectoryAsync(path, ct).ConfigureAwait(false);
            await foreach (UaDirectoryInfo child in dir.EnumerateDirectoriesAsync(ct)
                .ConfigureAwait(false))
            {
                yield return child;
            }
        }

        // ----------------------------------------------------------------
        // Internal helpers (called from UaFileSystemInfo / Ua*Info)
        // ----------------------------------------------------------------

        internal async IAsyncEnumerable<UaFileSystemInfo> EnumerateChildrenAsync(
            UaDirectoryInfo directory,
            bool includeFiles,
            bool includeDirectories,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await EnsureTypeTreeFetchedAsync(ct).ConfigureAwait(false);
            ITypeTable typeTree = Session.TypeTree;

            ByteString continuation;
            ArrayOf<ReferenceDescription> references;
            (_, continuation, references) = await Session.BrowseAsync(
                requestHeader: null,
                view: null,
                directory.NodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                (uint)NodeClass.Object,
                ct).ConfigureAwait(false);

            while (true)
            {
                // Materialise to an array first — ReadOnlySpan<T>.Enumerator
                // (returned by ArrayOf<T>.GetEnumerator) cannot cross an
                // async iterator's `yield return` boundary.
                var snapshot = new ReferenceDescription[references.Count];
                for (int i = 0; i < references.Count; i++)
                {
                    snapshot[i] = references[i];
                }
                foreach (ReferenceDescription reference in snapshot)
                {
                    UaFileSystemInfo? info = TryClassifyChild(
                        directory,
                        reference,
                        typeTree,
                        includeFiles,
                        includeDirectories);
                    if (info != null)
                    {
                        yield return info;
                    }
                }
                if (continuation.IsNull || continuation.Length == 0)
                {
                    yield break;
                }
                (_, continuation, references) = await Session.BrowseNextAsync(
                    requestHeader: null,
                    releaseContinuationPoint: false,
                    continuation,
                    ct).ConfigureAwait(false);
            }
        }

        internal async ValueTask<UaDirectoryInfo> CreateDirectoryInAsync(
            UaDirectoryInfo parent,
            string name,
            CancellationToken ct)
        {
            ValidateLeafName(name);
            NodeId newId;
            try
            {
                newId = await parent.Proxy.CreateDirectoryAsync(name, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                throw FileSystemErrors.Translate(ex, parent.FullPath + "/" + name, targetIsDirectory: true);
            }
            QualifiedName actualName = await ReadBrowseNameAsync(newId, ct).ConfigureAwait(false);
            m_pathCache.InvalidateChildrenOf(parent.NodeId);
            m_pathCache.Put(parent.NodeId, actualName, newId);
            return new UaDirectoryInfo(
                this,
                parent,
                newId,
                actualName,
                AppendSegment(parent.Segments, actualName));
        }

        internal async ValueTask<UaFileInfo> CreateFileInAsync(
            UaDirectoryInfo parent,
            string name,
            CancellationToken ct)
        {
            ValidateLeafName(name);
            NodeId newId;
            try
            {
                (newId, _) = await parent.Proxy
                    .CreateFileAsync(name, requestFileOpen: false, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                throw FileSystemErrors.Translate(ex, parent.FullPath + "/" + name, targetIsDirectory: false);
            }
            QualifiedName actualName = await ReadBrowseNameAsync(newId, ct).ConfigureAwait(false);
            m_pathCache.InvalidateChildrenOf(parent.NodeId);
            m_pathCache.Put(parent.NodeId, actualName, newId);
            return new UaFileInfo(
                this,
                parent,
                newId,
                actualName,
                AppendSegment(parent.Segments, actualName));
        }

        internal async ValueTask DeleteCoreAsync(
            UaFileSystemInfo target,
            bool recursive,
            CancellationToken ct)
        {
            if (target.Parent == null)
            {
                throw new IOException("Cannot delete the root directory.");
            }

            if (target is UaDirectoryInfo dir && !recursive)
            {
                // Empty-check before delegating to server.
                await foreach (UaFileSystemInfo _ in dir.EnumerateAsync(ct).ConfigureAwait(false))
                {
                    throw new IOException(
                        $"Directory '{target.FullPath}' is not empty; pass recursive: true to delete recursively.");
                }
            }

            try
            {
                await target.Parent.Proxy
                    .DeleteFileSystemObjectAsync(target.NodeId, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                throw FileSystemErrors.Translate(ex, target.FullPath, target.IsDirectory);
            }

            m_pathCache.InvalidateChildrenOf(target.Parent.NodeId);
            if (target is UaDirectoryInfo)
            {
                m_pathCache.InvalidateChildrenOf(target.NodeId);
            }
        }

        internal async ValueTask<UaFileSystemInfo> MoveOrCopyAsync(
            UaFileSystemInfo source,
            string destPath,
            bool copy,
            CancellationToken ct)
        {
            QualifiedName[] segments = UaPath.Parse(destPath);
            if (segments.Length == 0)
            {
                throw new ArgumentException(
                    "Destination path must include at least one segment.",
                    nameof(destPath));
            }
            UaDirectoryInfo destDir;
            if (segments.Length == 1)
            {
                destDir = Root;
            }
            else
            {
                string parentPath = UaPath.Format(segments.Take(segments.Length - 1).ToArray());
                destDir = await GetDirectoryAsync(parentPath, ct).ConfigureAwait(false);
            }
            return await MoveOrCopyAsync(source, destDir, segments[^1].Name!, copy, ct)
                .ConfigureAwait(false);
        }

        internal async ValueTask<UaFileSystemInfo> MoveOrCopyAsync(
            UaFileSystemInfo source,
            UaDirectoryInfo destinationDirectory,
            string newName,
            bool copy,
            CancellationToken ct)
        {
            ValidateLeafName(newName);
            if (source.Parent == null)
            {
                throw new IOException("Cannot move or copy the root directory.");
            }

            NodeId newId;
            try
            {
                newId = await source.Parent.Proxy.MoveOrCopyAsync(
                    source.NodeId,
                    destinationDirectory.NodeId,
                    createCopy: copy,
                    newName,
                    ct).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                throw FileSystemErrors.Translate(ex, source.FullPath, source.IsDirectory);
            }

            // Invalidate caches for both ends.
            m_pathCache.InvalidateChildrenOf(source.Parent.NodeId);
            m_pathCache.InvalidateChildrenOf(destinationDirectory.NodeId);

            QualifiedName actualName = await ReadBrowseNameAsync(newId, ct).ConfigureAwait(false);
            IReadOnlyList<QualifiedName> destSegments = AppendSegment(
                destinationDirectory.Segments, actualName);

            if (source is UaDirectoryInfo)
            {
                return new UaDirectoryInfo(
                    this, destinationDirectory, newId, actualName, destSegments);
            }
            return new UaFileInfo(
                this, destinationDirectory, newId, actualName, destSegments);
        }

        internal async ValueTask<FileMetadata> ReadFileMetadataAsync(
            NodeId fileNodeId,
            string? path,
            CancellationToken ct)
        {
            // Build a single TranslateBrowsePathsToNodeIds call for the
            // seven well-known property browse names.
            var browsePathsList = new List<BrowsePath>(kFileTypePropertyNames.Length);
            for (int i = 0; i < kFileTypePropertyNames.Length; i++)
            {
                var element = new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(kFileTypePropertyNames[i])
                };
                browsePathsList.Add(new BrowsePath
                {
                    StartingNode = fileNodeId,
                    RelativePath = new RelativePath { Elements = [element] }
                });
            }
            var browsePaths = browsePathsList.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, browsePaths);

            // Collect resolved property NodeIds.
            var nodesToReadList = new List<ReadValueId>(kFileTypePropertyNames.Length);
            var indexToProperty = new int[kFileTypePropertyNames.Length];
            for (int i = 0; i < kFileTypePropertyNames.Length; i++)
            {
                BrowsePathResult result = response.Results[i];
                bool optional = i >= kMandatoryPropertyCount;

                bool empty = result.Targets.Count == 0;
                if (StatusCode.IsBad(result.StatusCode) || empty)
                {
                    uint code = result.StatusCode.Code;
                    if (optional ||
                        code == StatusCodes.BadNoMatch ||
                        code == StatusCodes.BadNodeIdUnknown)
                    {
                        continue;
                    }
                    throw FileSystemErrors.Translate(
                        new ServiceResultException(result.StatusCode),
                        path,
                        targetIsDirectory: false);
                }

                var nodeId = ExpandedNodeId.ToNodeId(
                    result.Targets[0].TargetId,
                    Session.MessageContext.NamespaceUris);
                indexToProperty[nodesToReadList.Count] = i;
                nodesToReadList.Add(new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                });
            }

            if (nodesToReadList.Count == 0)
            {
                return default;
            }

            var nodesToRead = nodesToReadList.ToArrayOf();
            ReadResponse readResponse = await Session.ReadAsync(
                null,
                0.0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(readResponse.Results, nodesToRead);
            ClientBase.ValidateDiagnosticInfos(readResponse.DiagnosticInfos, nodesToRead);

            ulong? size = null;
            bool? writable = null;
            bool? userWritable = null;
            ushort? openCount = null;
            string? mimeType = null;
            uint? maxByteStringLength = null;
            DateTime? lastModifiedTime = null;

            for (int j = 0; j < nodesToRead.Count; j++)
            {
                int property = indexToProperty[j];
                DataValue dv = readResponse.Results[j];
                if (StatusCode.IsBad(dv.StatusCode))
                {
                    bool optional = property >= kMandatoryPropertyCount;
                    uint code = dv.StatusCode.Code;
                    if (optional ||
                        code == StatusCodes.BadNoMatch ||
                        code == StatusCodes.BadNodeIdUnknown)
                    {
                        continue;
                    }
                    throw FileSystemErrors.Translate(
                        new ServiceResultException(dv.StatusCode),
                        path,
                        targetIsDirectory: false);
                }

                Variant value = dv.WrappedValue;
                switch (property)
                {
                    case 0:
                        size = TryGetUInt64(value);
                        break;
                    case 1:
                        writable = TryGetBool(value);
                        break;
                    case 2:
                        userWritable = TryGetBool(value);
                        break;
                    case 3:
                        openCount = TryGetUInt16(value);
                        break;
                    case 4:
                        mimeType = TryGetString(value);
                        break;
                    case 5:
                        maxByteStringLength = TryGetUInt32(value);
                        break;
                    case 6:
                        lastModifiedTime = TryGetDateTime(value);
                        break;
                }
            }

            return new FileMetadata
            {
                Size = size,
                Writable = writable,
                UserWritable = userWritable,
                OpenCount = openCount,
                MimeType = mimeType,
                MaxByteStringLength = maxByteStringLength,
                LastModifiedTime = lastModifiedTime
            };
        }

        // ----------------------------------------------------------------
        // Internals
        // ----------------------------------------------------------------

        private async ValueTask<UaFileInfo> GetOrCreateFileAsync(string path, CancellationToken ct)
        {
            UaFileSystemInfo? existing = await GetInfoAsync(path, ct).ConfigureAwait(false);
            if (existing is UaFileInfo file)
            {
                return file;
            }
            if (existing is UaDirectoryInfo)
            {
                throw new IOException(
                    $"Cannot write to '{path}': path refers to a directory.");
            }
            return await CreateFileAsync(path, true, ct).ConfigureAwait(false);
        }

        private async ValueTask<ResolvedNode?> ResolveSegmentsAsync(
            QualifiedName[] segments,
            bool throwOnMissing,
            CancellationToken ct)
        {
            // Walk segment-by-segment, leveraging the path cache when
            // possible. A single TranslateBrowsePathsToNodeIds call would
            // be more efficient on a cache miss, but per-segment walking
            // is simpler, lets us populate the cache, and gives precise
            // error reporting (we know which segment failed). For most
            // workloads the cache covers the prefix so we still avoid
            // round-trips.
            NodeId currentParent = Root.NodeId;
            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                QualifiedName segment = segments[i];
                NodeId? cached = m_pathCache.TryGet(currentParent, segment);
                if (cached != null)
                {
                    if (isLast)
                    {
                        return new ResolvedNode(cached.Value, segment);
                    }
                    currentParent = cached.Value;
                    continue;
                }
                NodeId? resolved = await TryResolveSingleAsync(currentParent, segment, ct)
                    .ConfigureAwait(false);
                if (resolved == null)
                {
                    if (throwOnMissing)
                    {
                        throw (Exception)FileSystemErrors.NotFound(
                            UaPath.Format(segments.Take(i + 1).ToArray()),
                            targetIsDirectory: !isLast);
                    }
                    return null;
                }
                m_pathCache.Put(currentParent, segment, resolved.Value);
                if (isLast)
                {
                    return new ResolvedNode(resolved.Value, segment);
                }
                currentParent = resolved.Value;
            }
            // segments.Length == 0 case is handled by callers.
            return null;
        }

        private async ValueTask<NodeId?> TryResolveSingleAsync(
            NodeId parent,
            QualifiedName segment,
            CancellationToken ct)
        {
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = segment
            };
            ArrayOf<BrowsePath> browsePaths = new[]
            {
                new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath { Elements = [element] }
                }
            }.ToArrayOf();

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, browsePaths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, browsePaths);

            BrowsePathResult result = response.Results[0];
            uint code = result.StatusCode.Code;
            if (code == StatusCodes.BadNoMatch ||
                code == StatusCodes.BadNodeIdUnknown)
            {
                return null;
            }
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            ArrayOf<BrowsePathTarget> targets = result.Targets;
            if (targets.Count == 0)
            {
                return null;
            }
            if (targets.Count > 1)
            {
                throw FileSystemErrors.Ambiguous(UaPath.FormatSegment(segment), targets.Count);
            }
            return ExpandedNodeId.ToNodeId(
                targets[0].TargetId,
                Session.MessageContext.NamespaceUris);
        }

        private async ValueTask<UaFileSystemInfo> BuildInfoAsync(
            ResolvedNode resolved,
            QualifiedName[] segments,
            CancellationToken ct)
        {
            NodeId typeDef = await ReadTypeDefinitionAsync(resolved.NodeId, ct).ConfigureAwait(false);
            UaDirectoryInfo? parentInfo = null;
            if (segments.Length > 1)
            {
                ResolvedNode? parent = await ResolveSegmentsAsync(
                    segments.Take(segments.Length - 1).ToArray(),
                    throwOnMissing: true,
                    ct).ConfigureAwait(false);
                if (parent != null)
                {
                    parentInfo = new UaDirectoryInfo(
                        this,
                        parent: null, // grandparent reference omitted for the synthesized parent stub
                        parent.Value.NodeId,
                        parent.Value.BrowseName,
                        segments.Take(segments.Length - 1).ToArray());
                }
            }
            else
            {
                parentInfo = Root;
            }

            await EnsureTypeTreeFetchedAsync(ct).ConfigureAwait(false);
            ITypeTable typeTree = Session.TypeTree;

            bool isFile = IsFileType(typeDef, typeTree);
            bool isDir = IsDirectoryType(typeDef, typeTree);

            if (isDir)
            {
                return new UaDirectoryInfo(this, parentInfo, resolved.NodeId, resolved.BrowseName, segments);
            }
            if (isFile)
            {
                return new UaFileInfo(this, parentInfo, resolved.NodeId, resolved.BrowseName, segments);
            }
            throw new IOException(
                $"Path '/{string.Join("/", segments.Select(UaPath.FormatSegment))}' resolves to a NodeId whose TypeDefinition is neither FileType nor FileDirectoryType.");
        }

        private UaFileSystemInfo? TryClassifyChild(
            UaDirectoryInfo parent,
            ReferenceDescription reference,
            ITypeTable typeTree,
            bool includeFiles,
            bool includeDirectories)
        {
            var childId = ExpandedNodeId.ToNodeId(
                reference.NodeId,
                Session.MessageContext.NamespaceUris);
            var typeDef = ExpandedNodeId.ToNodeId(
                reference.TypeDefinition,
                Session.MessageContext.NamespaceUris);
            QualifiedName name = reference.BrowseName;

            bool isDir = IsDirectoryType(typeDef, typeTree);
            bool isFile = IsFileType(typeDef, typeTree);

            if (isDir && includeDirectories)
            {
                m_pathCache.Put(parent.NodeId, name, childId);
                return new UaDirectoryInfo(
                    this, parent, childId, name, AppendSegment(parent.Segments, name));
            }
            if (isFile && includeFiles)
            {
                m_pathCache.Put(parent.NodeId, name, childId);
                return new UaFileInfo(
                    this, parent, childId, name, AppendSegment(parent.Segments, name));
            }
            return null;
        }

        private bool IsFileType(NodeId typeDef, ITypeTable typeTree)
        {
            if (typeDef.IsNull)
            {
                return false;
            }
            if (typeDef.Equals(kFileTypeId))
            {
                return true;
            }
            return Options.IncludeFileTypeSubtypes && typeTree.IsTypeOf(typeDef, kFileTypeId);
        }

        private bool IsDirectoryType(NodeId typeDef, ITypeTable typeTree)
        {
            if (typeDef.IsNull)
            {
                return false;
            }
            if (typeDef.Equals(kFileDirectoryTypeId))
            {
                return true;
            }
            return Options.IncludeFileDirectoryTypeSubtypes &&
                typeTree.IsTypeOf(typeDef, kFileDirectoryTypeId);
        }

        private async ValueTask EnsureTypeTreeFetchedAsync(CancellationToken ct)
        {
            if (m_typeTreeFetched)
            {
                return;
            }
            await Session.FetchTypeTreeAsync(kFileTypeId, ct).ConfigureAwait(false);
            await Session.FetchTypeTreeAsync(kFileDirectoryTypeId, ct).ConfigureAwait(false);
            m_typeTreeFetched = true;
        }

        private async ValueTask<NodeId> ReadTypeDefinitionAsync(NodeId nodeId, CancellationToken ct)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await Session.BrowseAsync(
                requestHeader: null,
                view: null,
                nodeId,
                maxResultsToReturn: 1,
                BrowseDirection.Forward,
                ReferenceTypeIds.HasTypeDefinition,
                includeSubtypes: false,
                (uint)NodeClass.ObjectType,
                ct).ConfigureAwait(false);

            if (references.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                references[0].NodeId,
                Session.MessageContext.NamespaceUris);
        }

        private async ValueTask<QualifiedName> ReadBrowseNameAsync(NodeId nodeId, CancellationToken ct)
        {
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.BrowseName
                }
            }.ToArrayOf();
            ReadResponse response = await Session.ReadAsync(
                null,
                0.0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, nodesToRead);
            DataValue dv = response.Results[0];
            if (StatusCode.IsBad(dv.StatusCode))
            {
                throw new ServiceResultException(dv.StatusCode);
            }
            return TryGetQualifiedName(dv.WrappedValue) ?? QualifiedName.Null;
        }

        private static QualifiedName[] AppendSegment(
            IReadOnlyList<QualifiedName> parent,
            QualifiedName name)
        {
            var combined = new QualifiedName[parent.Count + 1];
            for (int i = 0; i < parent.Count; i++)
            {
                combined[i] = parent[i];
            }
            combined[^1] = name;
            return combined;
        }

        private static void ValidateLeafName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Name must be non-empty.", nameof(name));
            }
            if (name.Contains(UaPath.Separator, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Leaf name '{name}' must not contain a path separator.",
                    nameof(name));
            }
            if (name.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Leaf name '{name}' must not include a namespace prefix; the server picks the BrowseName namespace.",
                    nameof(name));
            }
        }

        private static ulong? TryGetUInt64(Variant value)
        {
            if (value.TryGetValue(out ulong u64))
            {
                return u64;
            }
            if (value.TryGetValue(out long i64) && i64 >= 0)
            {
                return (ulong)i64;
            }
            if (value.TryGetValue(out uint u32))
            {
                return u32;
            }
            if (value.TryGetValue(out int i32) && i32 >= 0)
            {
                return (ulong)i32;
            }
            return null;
        }

        private static uint? TryGetUInt32(Variant value)
        {
            if (value.TryGetValue(out uint u32))
            {
                return u32;
            }
            if (value.TryGetValue(out int i32) && i32 >= 0)
            {
                return (uint)i32;
            }
            if (value.TryGetValue(out ulong u64) && u64 <= uint.MaxValue)
            {
                return (uint)u64;
            }
            return null;
        }

        private static ushort? TryGetUInt16(Variant value)
        {
            if (value.TryGetValue(out ushort u16))
            {
                return u16;
            }
            if (value.TryGetValue(out short i16) && i16 >= 0)
            {
                return (ushort)i16;
            }
            if (value.TryGetValue(out uint u32) && u32 <= ushort.MaxValue)
            {
                return (ushort)u32;
            }
            if (value.TryGetValue(out int i32) && i32 >= 0 && i32 <= ushort.MaxValue)
            {
                return (ushort)i32;
            }
            return null;
        }

        private static bool? TryGetBool(Variant value)
        {
            if (value.TryGetValue(out bool b))
            {
                return b;
            }
            return null;
        }

        private static string? TryGetString(Variant value)
        {
            if (value.TryGetValue(out string s))
            {
                return s;
            }
            return null;
        }

        private static DateTime? TryGetDateTime(Variant value)
        {
            if (value.TryGetValue(out DateTimeUtc dtu))
            {
                return dtu.ToDateTime();
            }
            return null;
        }

        private static QualifiedName? TryGetQualifiedName(Variant value)
        {
            // QualifiedName is a struct; cast through Raw which preserves
            // boxed value types unchanged.
            object? raw = value.AsBoxedObject();
            if (raw is QualifiedName qn)
            {
                return qn;
            }
            return null;
        }

        private readonly struct ResolvedNode
        {
            public ResolvedNode(NodeId nodeId, QualifiedName browseName)
            {
                NodeId = nodeId;
                BrowseName = browseName;
            }

            public NodeId NodeId { get; }
            public QualifiedName BrowseName { get; }
        }

        /// <summary>
        /// Indices 0..3 are mandatory in the FileType definition; 4..6 are optional.
        /// </summary>
        private const int kMandatoryPropertyCount = 4;

        private static readonly string[] kFileTypePropertyNames =
        [
            "Size",
            "Writable",
            "UserWritable",
            "OpenCount",
            "MimeType",
            "MaxByteStringLength",
            "LastModifiedTime"
        ];

        private static readonly NodeId kFileTypeId = ObjectTypeIds.FileType;
        private static readonly NodeId kFileDirectoryTypeId = ObjectTypeIds.FileDirectoryType;
        private static readonly QualifiedName kRootBrowseName = new("FileSystem");

        private readonly PathCache m_pathCache;
        private bool m_typeTreeFetched;
    }
}
