/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Persists ticket envelopes and metadata without changing their validation or authority policy.
    /// One instance exclusively owns its path; the host protects the file from unauthorized writes.
    /// This store provides no cross-process writer coordination.
    /// </summary>
    public sealed class FileSystemTicketStore : ITicketStore, IDisposable
    {
        /// <summary>
        /// Borrows a file system whose Replace operation publishes complete files indivisibly.
        /// Missing files denote an empty store; damaged or unreadable files are never replaced implicitly.
        /// </summary>
        public FileSystemTicketStore(string path, IFileSystem? fileSystem = null, TimeProvider? timeProvider = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A ticket checkpoint path is required.", nameof(path));
            }
            m_path = path;
            m_files = fileSystem ?? LocalFileSystem.Instance;
            m_time = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public async ValueTask AddAsync(
            string ticketId,
            byte[] encodedTicket,
            TicketMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(ticketId))
            {
                throw new ArgumentException("Ticket id must be non-empty.", nameof(ticketId));
            }
            if (encodedTicket == null)
            {
                throw new ArgumentNullException(nameof(encodedTicket));
            }
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }
            ValidateMetadata(metadata);
            if (encodedTicket.Length > kMaximumCheckpointBytes)
            {
                throw new ArgumentException("The ticket exceeds the checkpoint size limit.", nameof(encodedTicket));
            }
            byte[] detached = (byte[])encodedTicket.Clone();
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Dictionary<string, TicketRecord> records = await LoadAsync(cancellationToken).ConfigureAwait(false);
                records[ticketId] = new TicketRecord(ticketId, detached, metadata, m_time.GetUtcNow());
                await SaveAsync(records, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveAsync(string ticketId, CancellationToken cancellationToken = default)
        {
            if (ticketId == null)
            {
                throw new ArgumentNullException(nameof(ticketId));
            }
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Dictionary<string, TicketRecord> records = await LoadAsync(cancellationToken).ConfigureAwait(false);
                if (!records.Remove(ticketId))
                {
                    return false;
                }
                await SaveAsync(records, cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TicketRecord?> GetAsync(string ticketId, CancellationToken cancellationToken = default)
        {
            if (ticketId == null)
            {
                throw new ArgumentNullException(nameof(ticketId));
            }
            Dictionary<string, TicketRecord> records = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return records.TryGetValue(ticketId, out TicketRecord? record) ? record : null;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<TicketRecord> ListAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Dictionary<string, TicketRecord> records = await ReadAsync(cancellationToken).ConfigureAwait(false);
            foreach (TicketRecord record in records.Values.OrderBy(
                record => record.TicketId, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<TicketRecord?> FindByProductInstanceUriAsync(
            string productInstanceUri,
            CancellationToken cancellationToken = default)
        {
            if (productInstanceUri == null)
            {
                throw new ArgumentNullException(nameof(productInstanceUri));
            }
            await foreach (TicketRecord record in ListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(record.Metadata.ProductInstanceUri, productInstanceUri, StringComparison.Ordinal))
                {
                    return record;
                }
            }
            return null;
        }

        /// <summary>
        /// Releases the local gate after the host has drained operations; the borrowed file system remains open.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Serializes file reads with publication and returns a detached snapshot.
        /// </summary>
        private async ValueTask<Dictionary<string, TicketRecord>> ReadAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Bounds the entire file before decoding records; duplicates and incomplete records are rejected.
        /// </summary>
        private async ValueTask<Dictionary<string, TicketRecord>> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = new Dictionary<string, TicketRecord>(StringComparer.Ordinal);
            if (!m_files.Exists(m_path))
            {
                return records;
            }
            using Stream input = m_files.OpenRead(m_path);
            using var buffer = new MemoryStream();
            byte[] block = new byte[8192];
            int read;
#if NETSTANDARD2_0 || NETFRAMEWORK
            while ((read = await input.ReadAsync(block, 0, block.Length, cancellationToken).ConfigureAwait(false)) > 0)
#else
            while ((read = await input.ReadAsync(block.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
#endif
            {
                if (buffer.Length + read > kMaximumCheckpointBytes)
                {
                    throw new InvalidDataException("The ticket checkpoint exceeds its size limit.");
                }
#if NETSTANDARD2_0 || NETFRAMEWORK
                await buffer.WriteAsync(block, 0, read, cancellationToken).ConfigureAwait(false);
#else
                await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
#endif
            }
            buffer.Position = 0;
            try
            {
                using var reader = new BinaryReader(buffer, s_utf8, leaveOpen: true);
                if (reader.ReadUInt32() != 1)
                {
                    throw new InvalidDataException("The ticket checkpoint version is not supported.");
                }
                int count = reader.ReadInt32();
                if (count < 0 || count > kMaximumTickets)
                {
                    throw new InvalidDataException("The ticket checkpoint record count is invalid.");
                }
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string id = ReadText(reader, buffer);
                    int length = reader.ReadInt32();
                    if (id.Length == 0 || records.ContainsKey(id) || length < 0 ||
                        length > buffer.Length - buffer.Position)
                    {
                        throw new InvalidDataException("The ticket checkpoint record is invalid.");
                    }
                    byte[] encoded = reader.ReadBytes(length);
                    var metadata = new TicketMetadata(
                        (TicketKind)reader.ReadInt32(), ReadText(reader, buffer), ReadText(reader, buffer),
                        ReadText(reader, buffer), ReadText(reader, buffer));
                    ValidateMetadata(metadata);
                    var createdAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
                    records.Add(id, new TicketRecord(id, encoded, metadata, createdAt));
                }
                if (buffer.Position != buffer.Length)
                {
                    throw new InvalidDataException("The ticket checkpoint contains trailing content.");
                }
                return records;
            }
            catch (Exception error) when (error is EndOfStreamException or ArgumentException or FormatException)
            {
                throw new InvalidDataException("The ticket checkpoint is invalid.", error);
            }
        }

        /// <summary>
        /// Stages complete state before replacing the previous checkpoint and preserves publication/cleanup errors.
        /// </summary>
        private async ValueTask SaveAsync(
            Dictionary<string, TicketRecord> records, CancellationToken cancellationToken)
        {
            if (records.Count > kMaximumTickets)
            {
                throw new InvalidDataException("The ticket checkpoint exceeds its record limit.");
            }
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, s_utf8, leaveOpen: true))
            {
                writer.Write(1u);
                writer.Write(records.Count);
                foreach (TicketRecord record in records.Values.OrderBy(
                    record => record.TicketId, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteText(writer, record.TicketId);
                    writer.Write(record.EncodedTicket.Length);
                    writer.Write(record.EncodedTicket);
                    writer.Write((int)record.Metadata.Kind);
                    WriteText(writer, record.Metadata.ManufacturerName);
                    WriteText(writer, record.Metadata.ModelName);
                    WriteText(writer, record.Metadata.SerialNumber);
                    WriteText(writer, record.Metadata.ProductInstanceUri);
                    writer.Write(record.CreatedAt.UtcDateTime.Ticks);
                    if (buffer.Length > kMaximumCheckpointBytes)
                    {
                        throw new InvalidDataException("The ticket checkpoint exceeds its size limit.");
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            string staging = m_path + "." + Guid.NewGuid().ToString("N") + ".pending";
            ExceptionDispatchInfo? publicationError = null;
            try
            {
                using (Stream output = m_files.OpenWrite(staging))
                {
                    buffer.Position = 0;
                    await buffer.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_files.Replace(staging, m_path);
            }
            catch (Exception error)
            {
                publicationError = ExceptionDispatchInfo.Capture(error);
            }
            try
            {
                if (m_files.Exists(staging))
                {
                    m_files.Delete(staging);
                }
            }
            catch (Exception cleanupError) when (publicationError != null)
            {
                throw new AggregateException(
                    "Publishing tickets and removing the staging file both failed.",
                    publicationError.SourceException, cleanupError);
            }
            publicationError?.Throw();
        }

        /// <summary>
        /// Checks encoded string lengths against the remaining bounded input before allocating a buffer.
        /// </summary>
        private static string ReadText(BinaryReader reader, MemoryStream buffer)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > buffer.Length - buffer.Position)
            {
                throw new InvalidDataException("A ticket checkpoint string length is invalid.");
            }
            return s_utf8.GetString(reader.ReadBytes(length));
        }

        /// <summary>
        /// Bounds UTF-8 output before allocating a buffer or appending it to the checkpoint.
        /// </summary>
        private static void WriteText(BinaryWriter writer, string value)
        {
            int length = s_utf8.GetByteCount(value);
            if (writer.BaseStream.Position + sizeof(int) + length > kMaximumCheckpointBytes)
            {
                throw new InvalidDataException("The ticket checkpoint exceeds its size limit.");
            }
            writer.Write(length);
            writer.Write(s_utf8.GetBytes(value));
        }

        /// <summary>
        /// Requires representable metadata without making a ticket-authority or identity decision.
        /// </summary>
        private static void ValidateMetadata(TicketMetadata metadata)
        {
            if (metadata.Kind is not (
                TicketKind.Unspecified or TicketKind.DeviceIdentity or TicketKind.CompositeIdentity) ||
                metadata.ManufacturerName == null || metadata.ModelName == null ||
                metadata.SerialNumber == null || metadata.ProductInstanceUri == null)
            {
                throw new ArgumentException("Ticket metadata is invalid.", nameof(metadata));
            }
        }

        /// <summary>
        /// Rejects calls after disposal before accessing the borrowed file system.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileSystemTicketStore));
            }
        }

        /// <summary>The maximum complete checkpoint length.</summary>
        private const int kMaximumCheckpointBytes = 16 * 1024 * 1024;

        /// <summary>The maximum number of tickets in one checkpoint.</summary>
        private const int kMaximumTickets = 10000;

        /// <summary>The codec rejects malformed UTF-8 rather than changing ticket identifiers or metadata.</summary>
        private static readonly UTF8Encoding s_utf8 = new(false, true);

        /// <summary>The checkpoint path exclusively owned by this instance.</summary>
        private readonly string m_path;

        /// <summary>The borrowed file system.</summary>
        private readonly IFileSystem m_files;

        /// <summary>The host's clock for accepted ticket timestamps.</summary>
        private readonly TimeProvider m_time;

        /// <summary>The instance-local read/publication gate.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Whether this instance has been disposed.</summary>
        private int m_disposed;
    }
}
