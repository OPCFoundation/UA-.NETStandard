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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Storage
{
    /// <summary>
    /// A bounded, protected local key/value store with serialized stable-storage commits.
    /// </summary>
    /// <remarks>
    /// The directory has one live owner. Values survive restart, but this is not a distributed
    /// coordination store. The parent directory must already exist. The supplied protector and its
    /// secret-store-managed keys remain owned by the caller. A failed publication makes this instance
    /// unavailable until it is disposed and reopened; no stale in-memory success is returned.
    /// Filesystem and device support for the native durability barriers is required.
    /// </remarks>
    public sealed class FileSharedKeyValueStore :
        ISharedKeyValueStore,
        ISharedKeyValueStoreConsistency,
        ISharedKeyValueStoreDurability,
        ISharedKeyValueStoreCapacity,
        IAsyncDisposable
    {
        /// <summary>
        /// Creates a store without opening it. The size bound includes the complete protected snapshot.
        /// </summary>
        public FileSharedKeyValueStore(
            string directory,
            string logicalStoreId,
            IRecordProtector protector,
            int maximumSnapshotBytes)
            : this(directory, logicalStoreId, protector, maximumSnapshotBytes, null)
        {
        }

        /// <summary>
        /// Creates a store with deterministic publication-boundary fault injection for tests.
        /// </summary>
        internal FileSharedKeyValueStore(
            string directory,
            string logicalStoreId,
            IRecordProtector protector,
            int maximumSnapshotBytes,
            Action<FileStoreCommitPhase>? commitPhase)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            if (string.IsNullOrWhiteSpace(logicalStoreId))
            {
                throw new ArgumentException("A stable logical store identifier is required.", nameof(logicalStoreId));
            }
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            if (protector is NullRecordProtector)
            {
                throw new ArgumentException("Authenticated record protection is required.", nameof(protector));
            }
            if (maximumSnapshotBytes < 256)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSnapshotBytes));
            }
            string fullPath = Path.GetFullPath(directory);
            m_root = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(
                m_root,
                Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A dedicated store directory is required.", nameof(directory));
            }
            m_parent = Path.GetDirectoryName(m_root) ??
                throw new ArgumentException("A dedicated store directory is required.", nameof(directory));
            m_primary = Path.Combine(m_root, "snapshot.bin");
            m_identity = Path.Combine(m_root, "identity.bin");
            m_protector = protector;
            MaximumSnapshotBytes = maximumSnapshotBytes;
            m_snapshotContext = RecordProtectionContext.Create("FileSharedKeyValueStore.Snapshot.1", logicalStoreId);
            m_identityContext = RecordProtectionContext.Create("FileSharedKeyValueStore.Identity.1", logicalStoreId);
            m_commitPhase = commitPhase;
        }

        /// <summary>
        /// Gets the finite bound on a complete protected snapshot, including keys and metadata.
        /// </summary>
        public int MaximumSnapshotBytes { get; }

        /// <inheritdoc/>
        public bool IsLinearizable(string key)
        {
            return key != null;
        }

        /// <inheritdoc/>
        public bool IsProcessLocal(string key)
        {
            return key != null;
        }

        /// <inheritdoc/>
        public async ValueTask VerifyDurabilityAsync(CancellationToken cancellationToken = default)
        {
            using OperationLease lease = await EnterAsync(cancellationToken).ConfigureAwait(false);
            await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
            try
            {
                using (var primary = new FileStream(m_primary, FileMode.Open, FileAccess.Write, FileShare.Read))
                {
                    // FlushAsync has no disk-barrier option. TODO: use an asynchronous disk flush when available.
#pragma warning disable CA1849
                    primary.Flush(flushToDisk: true);
#pragma warning restore CA1849
                }
                DurableFileOperations.SynchronizeDirectory(m_root, "durable key/value store");
            }
            catch (IOException)
            {
                m_faulted = true;
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                m_faulted = true;
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(
            string key, CancellationToken ct = default)
        {
            ValidateKey(key);
            using OperationLease lease = await EnterAsync(ct).ConfigureAwait(false);
            await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
            return m_entries.TryGetValue(key, out ByteString value)
                ? (true, Copy(value))
                : (false, default);
        }

        /// <inheritdoc/>
        public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            ValidateKey(key);
            using OperationLease lease = await EnterAsync(ct).ConfigureAwait(false);
            await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
            var next = new Dictionary<string, ByteString>(m_entries, StringComparer.Ordinal)
            {
                [key] = Copy(value)
            };
            _ = await CommitAsync(next, capacityRejection: false, lease.Token).ConfigureAwait(false);
            Publish(new KeyValueChange { Kind = KeyValueChangeKind.Set, Key = key, Value = Copy(next[key]) });
        }

        /// <inheritdoc/>
        public async ValueTask<bool> CompareAndSwapAsync(
            string key, ByteString expected, ByteString value, CancellationToken ct = default)
        {
            return await CompareAndSwapCoreAsync(key, expected, value, capacityRejection: false, ct)
                .ConfigureAwait(false) == SharedKeyValueStoreWriteResult.Applied;
        }

        /// <inheritdoc/>
        public ValueTask<SharedKeyValueStoreWriteResult> TryCompareAndSwapAsync(
            string key, ByteString expected, ByteString value, CancellationToken cancellationToken = default)
        {
            return CompareAndSwapCoreAsync(key, expected, value, capacityRejection: true, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            ValidateKey(key);
            using OperationLease lease = await EnterAsync(ct).ConfigureAwait(false);
            await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
            if (!m_entries.ContainsKey(key))
            {
                return false;
            }
            var next = new Dictionary<string, ByteString>(m_entries, StringComparer.Ordinal);
            next.Remove(key);
            _ = await CommitAsync(next, capacityRejection: false, lease.Token).ConfigureAwait(false);
            Publish(new KeyValueChange { Kind = KeyValueChangeKind.Delete, Key = key });
            return true;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            keyPrefix ??= string.Empty;
            KeyValuePair<string, ByteString>[] snapshot;
            using (OperationLease lease = await EnterAsync(ct).ConfigureAwait(false))
            {
                await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
                snapshot = [.. m_entries
                    .Where(entry => entry.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new KeyValuePair<string, ByteString>(entry.Key, Copy(entry.Value)))];
            }
            foreach (KeyValuePair<string, ByteString> entry in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                yield return entry;
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValueChange> WatchAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var watcher = new Watcher(keyPrefix ?? string.Empty);
            using (OperationLease lease = await EnterAsync(ct).ConfigureAwait(false))
            {
                await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
                lock (m_watchersLock)
                {
                    m_watchers.Add(watcher);
                }
            }
            try
            {
                await foreach (KeyValueChange change in watcher.Channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    yield return change;
                }
            }
            finally
            {
                lock (m_watchersLock)
                {
                    m_watchers.Remove(watcher);
                    watcher.Channel.Writer.TryComplete();
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposeStarted, 1) != 0)
            {
                await m_disposeCompleted.Task.ConfigureAwait(false);
                return;
            }
            try
            {
                await m_closed.CancelAsync().ConfigureAwait(false);
                await m_gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    lock (m_watchersLock)
                    {
                        foreach (Watcher watcher in m_watchers)
                        {
                            watcher.Channel.Writer.TryComplete();
                        }
                        m_watchers.Clear();
                    }
                    m_directoryLease?.Dispose();
                    m_directoryLease = null;
                    m_entries.Clear();
                }
                finally
                {
                    m_gate.Release();
                    m_gate.Dispose();
                    m_closed.Dispose();
                }
            }
            finally
            {
                m_disposeCompleted.TrySetResult(true);
            }
        }

        /// <summary>
        /// Shares mutation ordering while optionally reporting a known unchanged capacity rejection.
        /// </summary>
        private async ValueTask<SharedKeyValueStoreWriteResult> CompareAndSwapCoreAsync(
            string key, ByteString expected, ByteString value, bool capacityRejection, CancellationToken ct)
        {
            ValidateKey(key);
            using OperationLease lease = await EnterAsync(ct).ConfigureAwait(false);
            await EnsureReadyAsync(lease.Token).ConfigureAwait(false);
            bool present = m_entries.TryGetValue(key, out ByteString current);
            if (expected.IsNull ? present : !present || current != expected)
            {
                return SharedKeyValueStoreWriteResult.ConditionNotMet;
            }
            if (!present && value.IsNull)
            {
                return SharedKeyValueStoreWriteResult.Applied;
            }
            var next = new Dictionary<string, ByteString>(m_entries, StringComparer.Ordinal);
            if (value.IsNull)
            {
                next.Remove(key);
            }
            else
            {
                next[key] = Copy(value);
            }
            if (!await CommitAsync(next, capacityRejection, lease.Token).ConfigureAwait(false))
            {
                return SharedKeyValueStoreWriteResult.CapacityRejected;
            }
            Publish(new KeyValueChange
            {
                Kind = value.IsNull ? KeyValueChangeKind.Delete : KeyValueChangeKind.Set,
                Key = key,
                Value = value.IsNull ? default : Copy(next[key])
            });
            return SharedKeyValueStoreWriteResult.Applied;
        }

        private async ValueTask<OperationLease> EnterAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref m_disposeStarted) != 0)
            {
                throw new ObjectDisposedException(nameof(FileSharedKeyValueStore));
            }
            CancellationTokenSource? linked = null;
            bool acquired = false;
            try
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_closed.Token);
                await m_gate.WaitAsync(linked.Token).ConfigureAwait(false);
                acquired = true;
                var lease = new OperationLease(m_gate, linked);
                linked = null;
                acquired = false;
                return lease;
            }
            finally
            {
                linked?.Dispose();
                if (acquired)
                {
                    m_gate.Release();
                }
            }
        }

        private async ValueTask EnsureReadyAsync(CancellationToken ct)
        {
            if (m_faulted)
            {
                throw new IOException("The durable store requires reopening after an incomplete storage operation.");
            }
            if (!m_loaded)
            {
                await LoadAsync(ct).ConfigureAwait(false);
            }
            try
            {
                ByteString identity = await ReadFileAsync(m_identity, ct).ConfigureAwait(false);
                ByteString snapshot = await ReadFileAsync(m_primary, ct).ConfigureAwait(false);
                if (Digest(identity) != m_identityDigest || Digest(snapshot) != m_snapshotDigest)
                {
                    throw new InvalidDataException("Committed durable store files changed outside their owner.");
                }
            }
            catch (IOException)
            {
                m_faulted = true;
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                m_faulted = true;
                throw;
            }
        }

        private async ValueTask LoadAsync(CancellationToken ct)
        {
            if (!Directory.Exists(m_parent))
            {
                throw new DirectoryNotFoundException("The durable store parent directory must already exist.");
            }
            Directory.CreateDirectory(m_root);
            string ownershipPath = Path.Combine(m_root, "owner.lock");
            bool previouslyOpened = Exists(ownershipPath);
            var directoryLease = new FileStream(
                ownershipPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            m_directoryLease = directoryLease;
            bool loaded = false;
            try
            {
                DurableFileOperations.SynchronizeDirectory(m_parent, "durable key/value parent");
                bool identityExists = Exists(m_identity);
                bool primaryExists = Exists(m_primary);
                if (identityExists != primaryExists)
                {
                    throw new InvalidDataException("Durable store identity or committed snapshot is missing.");
                }
                if (!primaryExists)
                {
                    if (previouslyOpened ||
                        Directory.EnumerateFileSystemEntries(m_root)
                            .Any(path => !string.Equals(
                                Path.GetFileName(path), "owner.lock", StringComparison.Ordinal)))
                    {
                        throw new InvalidDataException(
                            "A missing durable snapshot cannot be treated as an empty store.");
                    }
                    m_epoch = Guid.NewGuid();
                    ByteString identity = m_protector.Protect(m_identityContext, new ByteString(m_epoch.ToByteArray()));
                    ValidateProtectedSize(identity);
                    await DurableFileOperations.WriteNewAsync(
                        m_identity, identity, cancellationToken: ct).ConfigureAwait(false);
                    DurableFileOperations.SynchronizeDirectory(m_root, "durable key/value identity");
                    m_identityDigest = Digest(identity);
                    _ = await CommitAsync(
                        new Dictionary<string, ByteString>(StringComparer.Ordinal), capacityRejection: false, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    ByteString identity = await ReadFileAsync(m_identity, ct).ConfigureAwait(false);
                    if (!m_protector.TryUnprotect(m_identityContext, identity, out ByteString epoch) ||
                        epoch.Length != 16)
                    {
                        throw new InvalidDataException("Durable store identity authentication failed.");
                    }
                    m_epoch = new Guid(epoch.ToArray());
                    if (m_epoch == Guid.Empty)
                    {
                        throw new InvalidDataException("The durable store identity is empty.");
                    }
                    ByteString snapshot = await ReadFileAsync(m_primary, ct).ConfigureAwait(false);
                    if (!m_protector.TryUnprotect(m_snapshotContext, snapshot, out ByteString plaintext))
                    {
                        throw new InvalidDataException("Durable snapshot authentication failed.");
                    }
                    DecodeSnapshot(plaintext);
                    m_identityDigest = Digest(identity);
                    m_snapshotDigest = Digest(snapshot);
                }
                RemoveAbandonedStages();
                DurableFileOperations.SynchronizeDirectory(m_root, "durable key/value store");
                m_loaded = true;
                loaded = true;
            }
            finally
            {
                if (!loaded)
                {
                    m_faulted = true;
#if NET
                    await directoryLease.DisposeAsync().ConfigureAwait(false);
#else
                    directoryLease.Dispose();
#endif
                    m_directoryLease = null;
                }
            }
        }

        private async ValueTask<bool> CommitAsync(
            Dictionary<string, ByteString> next, bool capacityRejection, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ulong revision = checked(m_revision + 1);
            if (!TryEncodeSnapshot(next, revision, out ByteString plaintext))
            {
                if (capacityRejection)
                {
                    return false;
                }
                throw new IOException("The durable store snapshot exceeds its finite storage bound.");
            }
            ByteString protectedSnapshot = m_protector.Protect(m_snapshotContext, plaintext);
            if (capacityRejection && protectedSnapshot.Length > MaximumSnapshotBytes)
            {
                return false;
            }
            ValidateProtectedSize(protectedSnapshot);
            string staged = Path.Combine(m_root, "stage-" + Guid.NewGuid().ToString("N") + ".bin");
            bool committed = false;
            try
            {
                m_commitPhase?.Invoke(FileStoreCommitPhase.BeforeStage);
                await DurableFileOperations.WriteNewAsync(staged, protectedSnapshot, cancellationToken: ct)
                    .ConfigureAwait(false);
                m_commitPhase?.Invoke(FileStoreCommitPhase.AfterStage);
                DurableFileOperations.SynchronizeDirectory(m_root, "durable key/value staging");
                ct.ThrowIfCancellationRequested();
                m_commitPhase?.Invoke(FileStoreCommitPhase.BeforePublish);
                if (Exists(m_primary))
                {
                    File.Replace(staged, m_primary, null);
                }
                else
                {
                    File.Move(staged, m_primary);
                }
                m_commitPhase?.Invoke(FileStoreCommitPhase.AfterPublish);
                DurableFileOperations.SynchronizeDirectory(m_root, "durable key/value publication");
                m_commitPhase?.Invoke(FileStoreCommitPhase.AfterDirectorySync);
                m_snapshotDigest = Digest(protectedSnapshot);
                m_entries = next;
                m_revision = revision;
                committed = true;
                return true;
            }
            finally
            {
                if (!committed)
                {
                    m_faulted = true;
                }
            }
        }

        private bool TryEncodeSnapshot(
            Dictionary<string, ByteString> entries, ulong revision, out ByteString plaintext)
        {
            long length = kHeaderSize;
            foreach (KeyValuePair<string, ByteString> entry in entries)
            {
                length += 8L + s_utf8.GetByteCount(entry.Key) + Math.Max(0, entry.Value.Length);
                if (length > MaximumSnapshotBytes)
                {
                    plaintext = default;
                    return false;
                }
            }
            using var stream = new MemoryStream((int)length);
            using var writer = new BinaryWriter(stream, s_utf8, leaveOpen: true);
            writer.Write(kSchemaVersion);
            writer.Write(m_epoch.ToByteArray());
            writer.Write(revision);
            writer.Write(entries.Count);
            foreach (KeyValuePair<string, ByteString> entry in
                entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                byte[] key = s_utf8.GetBytes(entry.Key);
                writer.Write(key.Length);
                writer.Write(key);
                writer.Write(entry.Value.IsNull ? -1 : entry.Value.Length);
                if (!entry.Value.IsNull)
                {
                    writer.Write(entry.Value.ToArray());
                }
            }
            writer.Flush();
            plaintext = new ByteString(stream.ToArray());
            return true;
        }

        private void DecodeSnapshot(ByteString plaintext)
        {
            if (plaintext.Length < kHeaderSize || plaintext.Length > MaximumSnapshotBytes)
            {
                throw new InvalidDataException("The durable snapshot length is invalid.");
            }
            using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, s_utf8, leaveOpen: true);
            if (reader.ReadInt32() != kSchemaVersion ||
                new Guid(reader.ReadBytes(16)) != m_epoch)
            {
                throw new InvalidDataException("The durable snapshot schema or identity is invalid.");
            }
            ulong revision = reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (revision == 0 || count < 0 || count > (stream.Length - stream.Position) / 8)
            {
                throw new InvalidDataException("The durable snapshot record count or revision is invalid.");
            }
            var entries = new Dictionary<string, ByteString>(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                byte[] keyBytes = ReadBytes(reader, nullable: false).ToArray();
                string key;
                try
                {
                    key = s_utf8.GetString(keyBytes);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException("The durable snapshot contains an invalid key.", ex);
                }
                ByteString value = ReadBytes(reader, nullable: true);
                if (!entries.TryAdd(key, value))
                {
                    throw new InvalidDataException("The durable snapshot contains duplicate keys.");
                }
            }
            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("The durable snapshot contains trailing data.");
            }
            m_entries = entries;
            m_revision = revision;
        }

        private static ByteString ReadBytes(BinaryReader reader, bool nullable)
        {
            int length = reader.ReadInt32();
            if (nullable && length == -1)
            {
                return default;
            }
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            {
                throw new InvalidDataException("A durable snapshot field length is invalid.");
            }
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException("The durable snapshot is truncated.");
            }
            return new ByteString(bytes);
        }

        private async ValueTask<ByteString> ReadFileAsync(string path, CancellationToken ct)
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > MaximumSnapshotBytes)
            {
                throw new InvalidDataException("A durable store file has an invalid length.");
            }
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset != bytes.Length)
            {
#if NET
                int read = await stream.ReadAsync(bytes.AsMemory(offset), ct).ConfigureAwait(false);
#else
                int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, ct).ConfigureAwait(false);
#endif
                if (read == 0)
                {
                    throw new EndOfStreamException("A durable store file is truncated.");
                }
                offset += read;
            }
            byte[] trailing = new byte[1];
#if NET
            int extra = await stream.ReadAsync(trailing.AsMemory(), ct).ConfigureAwait(false);
#else
            int extra = await stream.ReadAsync(trailing, 0, 1, ct).ConfigureAwait(false);
#endif
            if (extra != 0)
            {
                throw new InvalidDataException("A durable store file changed while being read.");
            }
            return new ByteString(bytes);
        }

        private void ValidateProtectedSize(ByteString value)
        {
            if (value.Length <= 0 || value.Length > MaximumSnapshotBytes)
            {
                throw new IOException("A protected durable record exceeds its finite storage bound.");
            }
        }

        private void RemoveAbandonedStages()
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(m_root))
            {
                string name = Path.GetFileName(path);
                if (name is "identity.bin" or "snapshot.bin" or "owner.lock")
                {
                    continue;
                }
                if (name.Length != 42 ||
                    !name.StartsWith("stage-", StringComparison.Ordinal) ||
                    !name.EndsWith(".bin", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(name.Substring(6, 32), "N", out _))
                {
                    throw new InvalidDataException("Unexpected content in the dedicated durable-store directory.");
                }
                File.Delete(path);
            }
        }

        private void Publish(KeyValueChange change)
        {
            lock (m_watchersLock)
            {
                foreach (Watcher watcher in m_watchers)
                {
                    if (change.Key.StartsWith(watcher.Prefix, StringComparison.Ordinal) &&
                        !watcher.Channel.Writer.TryWrite(change))
                    {
                        watcher.Channel.Writer.TryComplete(
                            new IOException("The durable-store watcher fell behind; a new snapshot is required."));
                    }
                }
            }
        }

        private static void ValidateKey(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            _ = s_utf8.GetByteCount(key);
        }

        private static bool Exists(string path)
        {
            try
            {
                _ = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private static ByteString Copy(ByteString value)
        {
            return value.IsNull ? default : new ByteString(value.Span.ToArray());
        }

        private static ByteString Digest(ByteString value)
        {
#if NET
            return new ByteString(SHA256.HashData(value.Span));
#else
            return new ByteString(SHA256.HashData(value.ToArray()));
#endif
        }

        private readonly string m_root;
        private readonly string m_parent;
        private readonly string m_primary;
        private readonly string m_identity;
        private readonly IRecordProtector m_protector;
        private readonly ByteString m_snapshotContext;
        private readonly ByteString m_identityContext;
        private readonly Action<FileStoreCommitPhase>? m_commitPhase;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly CancellationTokenSource m_closed = new();

        private readonly TaskCompletionSource<bool> m_disposeCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Lock m_watchersLock = new();
        private readonly List<Watcher> m_watchers = [];
        private Dictionary<string, ByteString> m_entries = new(StringComparer.Ordinal);
        private FileStream? m_directoryLease;
        private Guid m_epoch;
        private ulong m_revision;
        private ByteString m_identityDigest;
        private ByteString m_snapshotDigest;
        private bool m_loaded;
        private bool m_faulted;
        private int m_disposeStarted;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
        private const int kSchemaVersion = 1;
        private const int kHeaderSize = 32;

        private sealed class OperationLease(SemaphoreSlim gate, CancellationTokenSource cancellation) : IDisposable
        {
            public CancellationToken Token => cancellation.Token;

            public void Dispose()
            {
                cancellation.Dispose();
                gate.Release();
            }
        }

        private sealed class Watcher(string prefix)
        {
            public string Prefix { get; } = prefix;

            public Channel<KeyValueChange> Channel { get; } =
                System.Threading.Channels.Channel.CreateBounded<KeyValueChange>(
                    new BoundedChannelOptions(16)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });
        }
    }

    /// <summary>
    /// Observable local-store publication boundaries for deterministic interruption tests.
    /// </summary>
    internal enum FileStoreCommitPhase
    {
        /// <summary>Before creating the staged snapshot.</summary>
        BeforeStage,

        /// <summary>After flushing staged content.</summary>
        AfterStage,

        /// <summary>Before the atomic primary-file switch.</summary>
        BeforePublish,

        /// <summary>After switching the primary file, before the directory barrier.</summary>
        AfterPublish,

        /// <summary>After the publication directory barrier.</summary>
        AfterDirectorySync
    }
}
