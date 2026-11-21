/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Persists accepted registrar push work in one atomically replaced checkpoint.
    /// One instance must exclusively own its path; distributed hosts provide a transactional store instead.
    /// </summary>
    public sealed class FileSystemDeviceRegistrarPushWorkStore : IDeviceRegistrarPushWorkStore
    {
        /// <summary>
        /// Creates a durable work store whose file system publishes complete replacements atomically.
        /// </summary>
        public FileSystemDeviceRegistrarPushWorkStore(
            string path,
            ITelemetryContext telemetry,
            IFileSystem? fileSystem = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A push work checkpoint path is required.", nameof(path));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_path = path;
            m_fileSystem = fileSystem ?? LocalFileSystem.Instance;
            m_context = ServiceMessageContext.Create(telemetry);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<DeviceRegistrarPushWorkItem>> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<DeviceRegistrarPushWorkItem> items = await LoadAsync(cancellationToken).ConfigureAwait(false);
                return items.Select(Clone).ToArrayOf();
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask EnqueueAsync(
            Guid id,
            ApplicationDescription application,
            int capacity,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (id == Guid.Empty)
            {
                throw new ArgumentException("A push work identifier is required.", nameof(id));
            }
            if (capacity < 1 || capacity > kMaximumPendingWork)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            ApplicationDescription snapshot = SnapshotApplication(application);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<DeviceRegistrarPushWorkItem> items = await LoadAsync(cancellationToken).ConfigureAwait(false);
                if (items.Any(item => item.Id == id))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                if (items.Count(item => item.State != DeviceRegistrarPushWorkState.Processing) >= capacity)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                if (items.Count >= kMaximumStoredWork)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                items.Add(new DeviceRegistrarPushWorkItem(id, snapshot, DeviceRegistrarPushWorkState.Pending));
                await SaveAsync(items, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<DeviceRegistrarPushWorkState> BeginAttemptAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<DeviceRegistrarPushWorkItem> items = await LoadAsync(cancellationToken).ConfigureAwait(false);
                int index = FindItem(items, id);
                DeviceRegistrarPushWorkState prior = items[index].State;
                items[index] = items[index] with { State = DeviceRegistrarPushWorkState.Processing };
                await SaveAsync(items, cancellationToken).ConfigureAwait(false);
                return prior;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask MarkInterruptedAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<DeviceRegistrarPushWorkItem> items = await LoadAsync(cancellationToken).ConfigureAwait(false);
                int index = FindItem(items, id);
                if (items[index].State != DeviceRegistrarPushWorkState.Interrupted)
                {
                    items[index] = items[index] with { State = DeviceRegistrarPushWorkState.Interrupted };
                    await SaveAsync(items, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask CompleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<DeviceRegistrarPushWorkItem> items = await LoadAsync(cancellationToken).ConfigureAwait(false);
                int index = FindItem(items, id);
                items.RemoveAt(index);
                await SaveAsync(items, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Loads a bounded checkpoint and rejects unsupported versions, invalid states, and trailing data.
        /// </summary>
        private async ValueTask<List<DeviceRegistrarPushWorkItem>> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_fileSystem.Exists(m_path))
            {
                return [];
            }
            using Stream input = m_fileSystem.OpenRead(m_path);
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
                    throw new InvalidDataException("The push work checkpoint exceeds its size limit.");
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
                using var decoder = new BinaryDecoder(buffer, m_context, leaveOpen: true);
                if (decoder.ReadUInt32(null) != 1)
                {
                    throw new InvalidDataException("The push work checkpoint version is not supported.");
                }
                int count = decoder.ReadInt32(null);
                if (count < 0 || count > kMaximumStoredWork)
                {
                    throw new InvalidDataException("The push work checkpoint record count is invalid.");
                }
                var items = new List<DeviceRegistrarPushWorkItem>(count);
                var ids = new HashSet<Guid>();
                for (int index = 0; index < count; index++)
                {
                    string? idText = decoder.ReadString(null);
                    if (!Guid.TryParseExact(idText, "N", out Guid id) || id == Guid.Empty || !ids.Add(id))
                    {
                        throw new InvalidDataException("The push work checkpoint contains an invalid identifier.");
                    }
                    uint rawState = decoder.ReadUInt32(null);
                    if (rawState > (uint)DeviceRegistrarPushWorkState.Interrupted)
                    {
                        throw new InvalidDataException("The push work checkpoint contains an invalid state.");
                    }
                    ApplicationDescription application = decoder.ReadEncodeable<ApplicationDescription>(null);
                    items.Add(new DeviceRegistrarPushWorkItem(
                        id, SnapshotApplication(application), (DeviceRegistrarPushWorkState)rawState));
                }
                if (decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The push work checkpoint contains trailing content.");
                }
                return items;
            }
            catch (Exception error) when (
                error is ServiceResultException or EndOfStreamException or ArgumentException or FormatException)
            {
                throw new InvalidDataException("The push work checkpoint is invalid.", error);
            }
        }

        /// <summary>
        /// Atomically publishes a bounded checkpoint and preserves both publication and cleanup failures.
        /// </summary>
        private async ValueTask SaveAsync(
            List<DeviceRegistrarPushWorkItem> items,
            CancellationToken cancellationToken)
        {
            if (items.Count > kMaximumStoredWork)
            {
                throw new InvalidDataException("The push work checkpoint exceeds its record limit.");
            }
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, 1);
                encoder.WriteInt32(null, items.Count);
                foreach (DeviceRegistrarPushWorkItem item in items.OrderBy(item => item.Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateItem(item);
                    encoder.WriteString(null, item.Id.ToString("N"));
                    encoder.WriteUInt32(null, (uint)item.State);
                    encoder.WriteEncodeable(null, item.Application);
                    if (buffer.Length > kMaximumCheckpointBytes)
                    {
                        throw new InvalidDataException("The push work checkpoint exceeds its size limit.");
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            string staging = m_path + "." + Guid.NewGuid().ToString("N") + ".pending";
            ExceptionDispatchInfo? publicationError = null;
            try
            {
                using (Stream output = m_fileSystem.OpenWrite(staging))
                {
                    buffer.Position = 0;
                    await buffer.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_fileSystem.Replace(staging, m_path);
            }
            catch (Exception error)
            {
                publicationError = ExceptionDispatchInfo.Capture(error);
            }
            if (publicationError != null)
            {
                try
                {
                    if (m_fileSystem.Exists(staging))
                    {
                        m_fileSystem.Delete(staging);
                    }
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(
                        "Publishing push work and removing its staging file both failed.",
                        publicationError.SourceException, cleanupError);
                }
                publicationError.Throw();
            }
        }

        /// <summary>
        /// Detaches and validates application metadata before it enters durable acceptance.
        /// </summary>
        private static ApplicationDescription SnapshotApplication(ApplicationDescription application)
        {
            return DeviceRegistrarPushApplicationSnapshot.Create(application);
        }

        /// <summary>
        /// Clones a public work item so mutable application descriptions are not shared with the store.
        /// </summary>
        private static DeviceRegistrarPushWorkItem Clone(DeviceRegistrarPushWorkItem item)
        {
            return item with { Application = SnapshotApplication(item.Application) };
        }

        /// <summary>
        /// Requires a unique persisted item and reports invalid identifiers as service errors.
        /// </summary>
        private static int FindItem(List<DeviceRegistrarPushWorkItem> items, Guid id)
        {
            int index = items.FindIndex(item => item.Id == id);
            return index >= 0
                ? index
                : throw new ServiceResultException(StatusCodes.BadInvalidArgument);
        }

        /// <summary>
        /// Rejects records that cannot be represented by the durable work contract.
        /// </summary>
        private static void ValidateItem(DeviceRegistrarPushWorkItem item)
        {
            if (item.Id == Guid.Empty ||
                item.State is not (
                    DeviceRegistrarPushWorkState.Pending or
                    DeviceRegistrarPushWorkState.Processing or
                    DeviceRegistrarPushWorkState.Interrupted))
            {
                throw new InvalidDataException("The push work checkpoint contains an invalid item.");
            }
            _ = SnapshotApplication(item.Application);
        }

        /// <summary>
        /// Rejects operations after disposal.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileSystemDeviceRegistrarPushWorkStore));
            }
        }

        /// <summary>The maximum encoded checkpoint length.</summary>
        private const int kMaximumCheckpointBytes = 16 * 1024 * 1024;

        /// <summary>The maximum accepted pending-work capacity.</summary>
        private const int kMaximumPendingWork = 1024;

        /// <summary>The maximum records, allowing one running item beyond pending capacity.</summary>
        private const int kMaximumStoredWork = kMaximumPendingWork + 1;

        /// <summary>The checkpoint path exclusively owned by this store instance.</summary>
        private readonly string m_path;

        /// <summary>The borrowed file system.</summary>
        private readonly IFileSystem m_fileSystem;

        /// <summary>The private codec context retaining the standard decoder quotas.</summary>
        private readonly ServiceMessageContext m_context;

        /// <summary>Serializes operations on this store instance, not across processes.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Whether this store has been disposed.</summary>
        private int m_disposed;
    }
}
