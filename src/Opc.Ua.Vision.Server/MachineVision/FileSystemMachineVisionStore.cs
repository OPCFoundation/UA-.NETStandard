/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Single-host, persistent Machine Vision storage over the existing file-system provider.
    /// Each document is committed as a complete OPC UA binary record with its namespace tables.
    /// </summary>
    /// <remarks>
    /// Give each store an exclusive provider-relative root. A backup makes an interrupted replacement
    /// recoverable; providers must implement rename without exposing partially moved file content.
    /// Use a transactional IMachineVisionStore implementation for multi-host writers.
    /// The caller or dependency-injection container owns this store's disposal.
    /// </remarks>
    public sealed class FileSystemMachineVisionStore : IMachineVisionStore, IAsyncDisposable
    {
        /// <summary>
        /// Creates a store over a provider without accessing the operating-system filesystem directly.
        /// </summary>
        public FileSystemMachineVisionStore(
            IFileSystemProvider provider,
            IServiceMessageContext context,
            string rootPath = "MachineVision",
            int maximumDocumentBytes = 32 * 1024 * 1024)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (maximumDocumentBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDocumentBytes));
            }
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("A provider-relative store root is required.", nameof(rootPath));
            }
            string root = rootPath.Replace('\\', '/').Trim('/');
            foreach (string segment in root.Split('/'))
            {
                if (segment is "" or "." or "..")
                {
                    throw new ArgumentException(
                        "The store root must not contain traversal or empty segments.", nameof(rootPath));
                }
            }
            m_root = root;
            m_maximumBytes = maximumDocumentBytes;
            m_context = new ServiceMessageContext(context, context.Telemetry)
            {
                MaxByteStringLength = maximumDocumentBytes,
                MaxMessageSize = maximumDocumentBytes
            };
            m_logger = context.Telemetry.CreateLogger<FileSystemMachineVisionStore>();
        }

        /// <inheritdoc/>
        public async ValueTask<MachineVisionDocument?> ReadAsync(
            MachineVisionDocumentKind kind, string id, CancellationToken cancellationToken = default)
        {
            InMemoryMachineVisionStore.ValidateKey(kind, id);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = PathOf(kind, id);
                await RecoverAsync(path, cancellationToken).ConfigureAwait(false);
                if (await m_provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false) == null)
                {
                    return null;
                }
                MachineVisionDocument document = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
                if (document.Kind != kind || document.Id != id)
                {
                    throw new InvalidDataException("The stored document identity does not match its storage key.");
                }
                return document;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<MachineVisionDocument>> ListAsync(
            MachineVisionDocumentKind kind, CancellationToken cancellationToken = default)
        {
            InMemoryMachineVisionStore.ValidateKey(kind, nameof(kind));
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string folder = FolderOf(kind);
                if (await m_provider.GetEntryAsync(folder, cancellationToken).ConfigureAwait(false) == null)
                {
                    return [];
                }
                var paths = new HashSet<string>(StringComparer.Ordinal);
                var pendingFiles = new List<string>();
                await foreach (FileSystemEntry entry in m_provider.EnumerateAsync(folder, cancellationToken)
                    .WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (!entry.IsDirectory)
                    {
                        if (IsPendingFile(entry.Path))
                        {
                            pendingFiles.Add(entry.Path);
                            continue;
                        }
                        if (entry.Path.EndsWith(".bin", StringComparison.Ordinal))
                        {
                            paths.Add(entry.Path);
                        }
                        else if (entry.Path.EndsWith(".bin.previous", StringComparison.Ordinal))
                        {
                            paths.Add(entry.Path[..^".previous".Length]);
                        }
                    }
                }
                foreach (string pending in pendingFiles)
                {
                    await m_provider.DeleteAsync(pending, cancellationToken).ConfigureAwait(false);
                }
                var documents = new List<MachineVisionDocument>();
                foreach (string path in paths)
                {
                    await RecoverAsync(path, cancellationToken).ConfigureAwait(false);
                    MachineVisionDocument document = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
                    if (document.Kind != kind || path != PathOf(kind, document.Id))
                    {
                        throw new InvalidDataException("A stored document is in the wrong collection or path.");
                    }
                    documents.Add(document);
                }
                documents.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
                return documents.ToArrayOf();
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask WriteAsync(MachineVisionDocument document, CancellationToken cancellationToken = default)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            InMemoryMachineVisionStore.ValidateKey(document.Kind, document.Id);
            byte[] content = Encode(document);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = PathOf(document.Kind, document.Id);
                string pending = path + ".pending";
                string backup = path + ".previous";
                await m_provider.CreateDirectoryAsync(FolderOf(document.Kind), cancellationToken).ConfigureAwait(false);
                await RecoverAsync(path, cancellationToken).ConfigureAwait(false);
                bool previous = await m_provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false) != null;
                try
                {
                    using (Stream stream = await m_provider.OpenWriteAsync(
                        pending, FileWriteMode.Truncate, cancellationToken).ConfigureAwait(false))
                    {
#if NET5_0_OR_GREATER
                        await stream.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
                        await stream.WriteAsync(content, 0, content.Length, cancellationToken).ConfigureAwait(false);
#endif
                        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    if (previous)
                    {
                        await m_provider.MoveAsync(path, backup, cancellationToken).ConfigureAwait(false);
                    }
                    try
                    {
                        await m_provider.MoveAsync(pending, path, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        if (previous)
                        {
                            try
                            {
                                await m_provider.MoveAsync(backup, path, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception recovery)
                            {
                                throw new AggregateException("Document commit and rollback failed.", failure, recovery);
                            }
                        }
                        throw;
                    }
                    if (previous)
                    {
                        await CleanupAsync(backup).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await CleanupAsync(pending).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(
            MachineVisionDocumentKind kind, string id, CancellationToken cancellationToken = default)
        {
            InMemoryMachineVisionStore.ValidateKey(kind, id);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string path = PathOf(kind, id);
                await RecoverAsync(path, cancellationToken).ConfigureAwait(false);
                if (await m_provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false) == null)
                {
                    return false;
                }
                await m_provider.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            await m_gate.WaitAsync().ConfigureAwait(false);
            m_gate.Release();
            m_gate.Dispose();
            m_lifetime.Dispose();
        }

        private async ValueTask EnterAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileSystemMachineVisionStore));
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
            await m_gate.WaitAsync(linked.Token).ConfigureAwait(false);
        }

        private async ValueTask RecoverAsync(string path, CancellationToken ct)
        {
            string folder = path[..path.LastIndexOf('/')];
            if (await m_provider.GetEntryAsync(folder, ct).ConfigureAwait(false) != null)
            {
                var stale = new List<string>();
                await foreach (FileSystemEntry entry in m_provider.EnumerateAsync(folder, ct)
                    .WithCancellation(ct).ConfigureAwait(false))
                {
                    if (!entry.IsDirectory &&
                        IsPendingFile(entry.Path) &&
                        (entry.Path == path + ".pending" ||
                            entry.Path.StartsWith(path + ".pending-", StringComparison.Ordinal)))
                    {
                        stale.Add(entry.Path);
                    }
                }
                foreach (string pending in stale)
                {
                    await m_provider.DeleteAsync(pending, ct).ConfigureAwait(false);
                }
            }
            string backup = path + ".previous";
            if (await m_provider.GetEntryAsync(backup, ct).ConfigureAwait(false) == null)
            {
                return;
            }
            if (await m_provider.GetEntryAsync(path, ct).ConfigureAwait(false) == null)
            {
                await m_provider.MoveAsync(backup, path, ct).ConfigureAwait(false);
            }
            else
            {
                await m_provider.DeleteAsync(backup, ct).ConfigureAwait(false);
            }
        }

        private static bool IsPendingFile(string path)
        {
            int marker = path.LastIndexOf(".bin.pending", StringComparison.Ordinal);
            if (marker < 0)
            {
                return false;
            }
            string suffix = path[(marker + ".bin.pending".Length)..];
            return suffix.Length == 0 ||
                (suffix.Length == 33 && suffix[0] == '-' && Guid.TryParseExact(suffix[1..], "N", out _));
        }

        private async ValueTask CleanupAsync(string path)
        {
            try
            {
                if (await m_provider.GetEntryAsync(path, CancellationToken.None).ConfigureAwait(false) != null)
                {
                    await m_provider.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (IOException exception)
            {
                m_logger.CleanupFailed(path, exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                m_logger.CleanupFailed(path, exception);
            }
        }

        private byte[] Encode(MachineVisionDocument document)
        {
            using var encoder = new BinaryEncoder(m_context);
            encoder.WriteUInt32(null, kFormat);
            encoder.WriteStringArray(null, m_context.NamespaceUris.ToArrayOf());
            encoder.WriteStringArray(null, m_context.ServerUris.ToArrayOf());
            encoder.WriteInt32(null, (int)document.Kind);
            encoder.WriteString(null, document.Id);
            encoder.WriteVariant(null, document.Metadata);
            encoder.WriteByteString(null, document.Content);
            byte[] data = encoder.CloseAndReturnBuffer() ??
                throw new InvalidDataException("The document encoder returned no data.");
            if (data.Length > m_maximumBytes)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded, "The document is too large.");
            }
            return data;
        }

        private async ValueTask<MachineVisionDocument> ReadFileAsync(string path, CancellationToken ct)
        {
            using Stream stream = await m_provider.OpenReadAsync(path, ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            while (true)
            {
#if NET5_0_OR_GREATER
                int read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    break;
                }
                if (buffer.Length + read > m_maximumBytes)
                {
                    throw new InvalidDataException("The stored document exceeds its size limit.");
                }
#if NET5_0_OR_GREATER
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
#else
                await buffer.WriteAsync(chunk, 0, read, ct).ConfigureAwait(false);
#endif
            }
            using var decoder = new BinaryDecoder(buffer.ToArray(), m_context);
            if (decoder.ReadUInt32(null) != kFormat)
            {
                throw new InvalidDataException("Unsupported Machine Vision document format.");
            }
            ArrayOf<string?> namespaces = decoder.ReadStringArray(null);
            ArrayOf<string?> servers = decoder.ReadStringArray(null);
            string[] namespaceEntries = MappingEntries(namespaces);
            string[] serverEntries = MappingEntries(servers);
            var namespaceTable = new NamespaceTable(namespaceEntries);
            var serverTable = new StringTable(serverEntries);
            if (namespaceTable.Count != namespaceEntries.Length || serverTable.Count != serverEntries.Length)
            {
                throw new InvalidDataException("Invalid stored namespace or server mapping table.");
            }
            decoder.SetMappingTables(namespaceTable, serverTable);
            var kind = (MachineVisionDocumentKind)decoder.ReadInt32(null);
            string id = decoder.ReadString(null) ?? throw new InvalidDataException("The stored identifier is missing.");
            InMemoryMachineVisionStore.ValidateKey(kind, id);
            return new MachineVisionDocument(kind, id, decoder.ReadVariant(null), decoder.ReadByteString(null));
        }

        private string FolderOf(MachineVisionDocumentKind kind)
        {
            return m_root + "/" + kind;
        }

        private static string[] MappingEntries(ArrayOf<string?> values)
        {
            string[] entries = new string[values.Count];
            for (int ii = 0; ii < values.Count; ii++)
            {
                entries[ii] = values[ii] ?? throw new InvalidDataException("A stored mapping table contains null.");
            }
            return entries;
        }

        private string PathOf(MachineVisionDocumentKind kind, string id)
        {
            byte[] data = Encoding.UTF8.GetBytes(id);
#if NET5_0_OR_GREATER
            byte[] digest = SHA256.HashData(data);
#else
            using SHA256 hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(data);
#endif
            return FolderOf(kind) + "/" + CoreUtils.ToHexString(digest) + ".bin";
        }

        private const uint kFormat = 0x4d565331;
        private readonly IFileSystemProvider m_provider;
        private readonly ServiceMessageContext m_context;
        private readonly string m_root;
        private readonly int m_maximumBytes;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly CancellationTokenSource m_lifetime = new();
        private int m_disposed;
    }

    /// <summary>
    /// Reports cleanup failures without hiding the committed state of a document transaction.
    /// </summary>
    internal static partial class FileSystemMachineVisionStoreLog
    {
        [LoggerMessage(
            EventId = VisionServerEventIds.MachineVisionRuntime + 2,
            Level = LogLevel.Warning,
            Message = "Machine Vision document cleanup failed for {Path}; committed content is unchanged.")]
        public static partial void CleanupFailed(this ILogger logger, [FilePath] string path, Exception exception);
    }
}
