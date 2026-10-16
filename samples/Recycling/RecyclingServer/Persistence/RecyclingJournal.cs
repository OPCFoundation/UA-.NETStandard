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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Single-writer, checksummed, generation-based journal and checkpoints over the existing file provider.
    /// The provider must offer exclusive writer streams and same-directory atomic rename.
    /// </summary>
    public sealed class RecyclingJournal : IAsyncDisposable
    {
        /// <summary>
        /// Creates a journal over an exclusively owned writable provider root.
        /// </summary>
        public RecyclingJournal(IFileSystemProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (!provider.IsWritable)
            {
                throw new ArgumentException("A writable persistence provider is required.", nameof(provider));
            }
        }

        /// <summary>
        /// Explicit recovery notices for discarded uncommitted staging records.
        /// </summary>
        public ArrayOf<string> RecoveryNotices { get; private set; } = [];

        /// <summary>
        /// Most recent fully recovered or committed image.
        /// </summary>
        public RecyclingCheckpoint? Current { get; private set; }

        /// <summary>
        /// Acquires the exclusive writer lease and validates every retained complete record.
        /// An initialized but empty/corrupt root is never treated as a fresh run.
        /// </summary>
        public async ValueTask<RecyclingCheckpoint?> OpenAsync(
            string configurationHash,
            string lineId,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (m_lease != null)
            {
                throw new InvalidOperationException("The journal is already open.");
            }
            await m_provider.CreateDirectoryAsync(string.Empty, cancellationToken).ConfigureAwait(false);
            m_lease = await m_provider.OpenWriteAsync(
                "writer.lock", FileWriteMode.OpenOrCreate, cancellationToken).ConfigureAwait(false);
            try
            {
                bool initialized = await m_provider.GetEntryAsync("initialized", cancellationToken)
                    .ConfigureAwait(false) != null;
                var entries = new List<JournalEntry>();
                var pending = new List<string>();
                await foreach (FileSystemEntry entry in m_provider.EnumerateAsync(string.Empty, cancellationToken)
                    .ConfigureAwait(false))
                {
                    string name = Path.GetFileName(entry.Path);
                    if (name.EndsWith(".pending", StringComparison.Ordinal))
                    {
                        pending.Add(name);
                    }
                    else if (name.EndsWith(".record", StringComparison.Ordinal))
                    {
                        if (entries.Count >= 64)
                        {
                            throw new InvalidDataException("Too many retained journal/checkpoint generations.");
                        }
                        (RecyclingCheckpoint image, string hash) = await ReadAsync(name, cancellationToken)
                            .ConfigureAwait(false);
                        if (image.SchemaVersion != 2 || image.ConfigurationHash != configurationHash ||
                            image.LineId != lineId || image.Sequence < 0)
                        {
                            throw new InvalidDataException(
                                "Persistence schema, configuration/model or line mismatch.");
                        }
                        entries.Add(new JournalEntry(
                            name, image, hash, name.StartsWith("checkpoint-", StringComparison.Ordinal)));
                    }
                }
                if (entries.Count == 0)
                {
                    if (initialized || pending.Count != 0)
                    {
                        throw new InvalidDataException("Initialized state has no complete recoverable commit.");
                    }
                    await m_provider.CreateFileAsync("initialized", cancellationToken).ConfigureAwait(false);
                    return null;
                }
                if (!initialized)
                {
                    throw new InvalidDataException("Committed records exist without their initialization marker.");
                }

                foreach (IGrouping<long, JournalEntry> group in entries.GroupBy(static entry => entry.Image.Sequence))
                {
                    if (group.Select(static entry => entry.Hash).Distinct(StringComparer.Ordinal).Count() != 1)
                    {
                        throw new InvalidDataException(
                            "Conflicting journal/checkpoint copies of a committed sequence.");
                    }
                }
                JournalEntry? checkpoint = entries.Where(static entry => entry.IsCheckpoint)
                    .OrderByDescending(static entry => entry.Image.Sequence).FirstOrDefault();
                long nextSequence = checkpoint?.Image.Sequence + 1 ?? 0;
                string priorHash = checkpoint?.Hash ?? string.Empty;
                RecyclingCheckpoint? latest = checkpoint?.Image;
                foreach (JournalEntry entry in entries
                    .Where(entry => !entry.IsCheckpoint && entry.Image.Sequence >= nextSequence)
                    .OrderBy(static entry => entry.Image.Sequence))
                {
                    if (entry.Image.Sequence != nextSequence || entry.Image.PreviousHash != priorHash)
                    {
                        throw new InvalidDataException("Missing, reordered or unlinked journal generation.");
                    }
                    latest = entry.Image;
                    priorHash = entry.Hash;
                    nextSequence++;
                }
                Current = latest ?? throw new InvalidDataException("No recoverable journal chain.");
                m_hash = priorHash;
                RecoveryNotices = pending.Select(static name =>
                    "Discarded interrupted, uncommitted staging record: " + name).ToArrayOf();
                foreach (string name in pending)
                {
                    await m_provider.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
                }
                return Current;
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Durably writes the complete image and receipt, then atomically publishes the journal generation.
        /// Callers must stop writing after any I/O failure and recover before retrying.
        /// </summary>
        public async ValueTask CommitAsync(
            RecyclingCheckpoint image,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            ThrowIfNotOpen();
            if (image.SchemaVersion != 2 || image.Snapshot.SchemaVersion != 2)
            {
                throw new InvalidDataException("Unsupported recycling checkpoint or snapshot schema.");
            }
            long sequence = Current == null ? 0 : checked(Current.Sequence + 1);
            RecyclingCheckpoint commit = image with { Sequence = sequence, PreviousHash = m_hash };
            string name = RecordName("journal", sequence);
            string hash = await WriteAsync("journal.pending", commit, cancellationToken).ConfigureAwait(false);
            await m_provider.MoveAsync("journal.pending", name, cancellationToken).ConfigureAwait(false);
            Current = commit;
            m_hash = hash;
        }

        /// <summary>
        /// Publishes a self-contained checkpoint before deleting superseded journal records.
        /// A failed compaction must also be recovered before any more writes.
        /// </summary>
        public async ValueTask CheckpointAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfNotOpen();
            RecyclingCheckpoint current = Current ?? throw new InvalidOperationException("No committed state.");
            string name = RecordName("checkpoint", current.Sequence);
            if (await m_provider.GetEntryAsync(name, cancellationToken).ConfigureAwait(false) == null)
            {
                await WriteAsync("checkpoint.pending", current, cancellationToken).ConfigureAwait(false);
                await m_provider.MoveAsync("checkpoint.pending", name, cancellationToken).ConfigureAwait(false);
            }
            var obsolete = new List<string>();
            await foreach (FileSystemEntry entry in m_provider.EnumerateAsync(string.Empty, cancellationToken)
                .ConfigureAwait(false))
            {
                string existing = Path.GetFileName(entry.Path);
                if (existing.EndsWith(".record", StringComparison.Ordinal) && existing != name)
                {
                    obsolete.Add(existing);
                }
            }
            foreach (string path in obsolete)
            {
                await m_provider.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            Stream? lease = Interlocked.Exchange(ref m_lease, null);
            if (lease != null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async ValueTask<string> WriteAsync(
            string path,
            RecyclingCheckpoint image,
            CancellationToken cancellationToken)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                image, RecyclingJsonContext.Default.RecyclingCheckpoint);
            if (payload.Length > kMaximumRecordBytes)
            {
                throw new InvalidDataException("The complete checkpoint exceeds the explicit persistence byte limit.");
            }
            byte[] digest = SHA256.HashData(payload);
            byte[] header = new byte[kHeaderLength];
            "RECYCL02"u8.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), payload.Length);
            digest.CopyTo(header, 12);
            Stream stream = await m_provider.OpenWriteAsync(
                path, FileWriteMode.Truncate, cancellationToken).ConfigureAwait(false);
            await using var streamLifetime = stream.ConfigureAwait(false);
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (stream is FileStream file)
            {
                // FlushAsync alone does not request the OS's durable write barrier; no asynchronous equivalent exists.
                file.Flush(flushToDisk: true);
            }
            return Convert.ToHexString(digest);
        }

        private async ValueTask<(RecyclingCheckpoint Image, string Hash)> ReadAsync(
            string path,
            CancellationToken cancellationToken)
        {
            FileSystemEntry entry = await m_provider.GetEntryAsync(path, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("A journal entry disappeared during recovery.");
            if (entry.Length < kHeaderLength || entry.Length > kMaximumRecordBytes + kHeaderLength)
            {
                throw new InvalidDataException("Truncated or oversized committed record.");
            }
            Stream stream = await m_provider.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            await using var streamLifetime = stream.ConfigureAwait(false);
            byte[] header = new byte[kHeaderLength];
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            if (!header.AsSpan(0, 8).SequenceEqual("RECYCL02"u8) || length < 1 ||
                length > kMaximumRecordBytes || entry.Length != length + kHeaderLength)
            {
                throw new InvalidDataException("Invalid framing or partial committed record.");
            }
            byte[] payload = new byte[length];
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            byte[] hash = SHA256.HashData(payload);
            if (!CryptographicOperations.FixedTimeEquals(hash, header.AsSpan(12, 32)))
            {
                throw new InvalidDataException("Committed record checksum mismatch.");
            }
            RecyclingCheckpoint image;
            try
            {
                image = JsonSerializer.Deserialize(payload, RecyclingJsonContext.Default.RecyclingCheckpoint)
                    ?? throw new InvalidDataException("Null committed record.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Invalid or incompatible committed JSON structure.", exception);
            }
            return (image, Convert.ToHexString(hash));
        }

        private static string RecordName(string prefix, long sequence)
        {
            return prefix + "-" + sequence.ToString("D20", CultureInfo.InvariantCulture) + ".record";
        }

        private void ThrowIfNotOpen()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0 || m_lease == null, this);
        }

        /// <summary>
        /// Fully verified file generation.
        /// </summary>
        private sealed record JournalEntry(string Name, RecyclingCheckpoint Image, string Hash, bool IsCheckpoint);

        private const int kHeaderLength = 44;
        private const int kMaximumRecordBytes = 64 * 1024 * 1024;
        private readonly IFileSystemProvider m_provider;
        private Stream? m_lease;
        private string m_hash = string.Empty;
        private int m_disposed;
    }
}
