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
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Persists device-side registrar discovery candidates, not registrar approval or installed certificate trust.
    /// One instance must exclusively own its path. This adapter does not provide cross-process coordination.
    /// </summary>
    public sealed class FileSystemProvisionableDeviceRegistrarStore : IProvisionableDeviceRegistrarStore, IDisposable
    {
        /// <summary>
        /// Borrows a file system whose Replace operation publishes complete checkpoints indivisibly.
        /// The host protects the checkpoint path and stops dispatching operations before disposing this instance.
        /// </summary>
        public FileSystemProvisionableDeviceRegistrarStore(
            string path,
            ITelemetryContext telemetry,
            IFileSystem? fileSystem = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A device registrar checkpoint path is required.", nameof(path));
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
        public async ValueTask<ArrayOf<ApplicationDescription>> ReadAsync(
            CancellationToken cancellationToken = default)
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

        /// <inheritdoc/>
        public async ValueTask ReplaceAsync(
            ArrayOf<ApplicationDescription> registrars,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<ApplicationDescription> detached = ProvisionableDeviceRuntime.SnapshotRegistrars(registrars);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Do not conceal damaged state by replacing it with a new candidate set.
                await LoadAsync(cancellationToken).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
                {
                    encoder.WriteUInt32(null, 1);
                    encoder.WriteEncodeableArray(null, detached);
                }
                if (buffer.Length > kMaximumCheckpointBytes)
                {
                    throw new InvalidDataException("The device registrar checkpoint exceeds its size limit.");
                }
                await PublishAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases this instance's serialization gate without disposing the borrowed file system.
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
        /// Uses the standard bounded binary decoder and rejects unknown versions or trailing checkpoint content.
        /// </summary>
        private async ValueTask<ArrayOf<ApplicationDescription>> LoadAsync(CancellationToken cancellationToken)
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
                    throw new InvalidDataException("The device registrar checkpoint exceeds its size limit.");
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
                    throw new InvalidDataException("The device registrar checkpoint version is not supported.");
                }
                ArrayOf<ApplicationDescription> registrars = decoder.ReadEncodeableArray<ApplicationDescription>(null);
                if (decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The device registrar checkpoint contains trailing content.");
                }
                return ProvisionableDeviceRuntime.SnapshotRegistrars(registrars);
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or ArgumentException)
            {
                throw new InvalidDataException("The device registrar checkpoint is invalid.", error);
            }
        }

        /// <summary>
        /// Flushes a staging file before publication; failure preserves the previous candidate checkpoint.
        /// Cancellation after Replace does not report a committed update as uncommitted.
        /// </summary>
        private async ValueTask PublishAsync(MemoryStream buffer, CancellationToken cancellationToken)
        {
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
            try
            {
                if (m_fileSystem.Exists(staging))
                {
                    m_fileSystem.Delete(staging);
                }
            }
            catch (Exception cleanupError) when (publicationError != null)
            {
                throw new AggregateException(
                    "Publishing device registrar candidates and removing their staging file both failed.",
                    publicationError.SourceException, cleanupError);
            }
            publicationError?.Throw();
        }

        /// <summary>
        /// Rejects operations after the host has disposed this store.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileSystemProvisionableDeviceRegistrarStore));
            }
        }

        /// <summary>The maximum encoded checkpoint size, including its version marker.</summary>
        private const int kMaximumCheckpointBytes = 16 * 1024 * 1024;

        /// <summary>The checkpoint path exclusively owned by this instance.</summary>
        private readonly string m_path;

        /// <summary>The borrowed file system.</summary>
        private readonly IFileSystem m_fileSystem;

        /// <summary>The private codec context retaining the standard decoder quotas.</summary>
        private readonly ServiceMessageContext m_context;

        /// <summary>The instance-local operation gate, not a distributed writer claim.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Whether the host has disposed this instance.</summary>
        private int m_disposed;
    }
}
