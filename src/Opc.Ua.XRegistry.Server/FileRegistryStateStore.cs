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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Single-writer local-file registry state with staged atomic publication.
    /// A process interruption preserves a complete old or new generation, not partial state.
    /// Shared-folder clustering and arbitrary storage/power-loss durability are not claimed.
    /// </summary>
    public sealed class FileRegistryStateStore : IRegistryStateStore
    {
        /// <summary>
        /// Opens an exclusive writer lease for one local directory.
        /// Existing committed state is validated when first read or changed.
        /// </summary>
        public FileRegistryStateStore(string directory, int maxStateBytes = 16 * 1024 * 1024)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A registry state directory is required.", nameof(directory));
            }
            if (maxStateBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStateBytes));
            }
            string root = Path.GetFullPath(directory);
            m_statePath = Path.Combine(root, "state.bin");
            m_stagedPath = Path.Combine(root, "state.pending");
            m_maxStateBytes = maxStateBytes;
            m_writerLease = m_files.OpenWrite(Path.Combine(root, "writer.lock"));
        }

        /// <inheritdoc/>
        public async ValueTask<RegistryStoredState> ReadAsync(CancellationToken cancellationToken = default)
        {
            using Operation operation = Enter();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadAsync(cancellationToken).ConfigureAwait(false);
                return RegistryStateStorage.Copy(m_state);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<RegistryStateCommit> CommitAsync(
            ulong expectedRevision,
            ByteString document,
            CancellationToken cancellationToken = default)
        {
            RegistryStateStorage.Validate(document, m_maxStateBytes);
            using Operation operation = Enter();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadAsync(cancellationToken).ConfigureAwait(false);
                RegistryStateCommit result = RegistryStateStorage.Next(m_state, expectedRevision, document);
                if (StatusCode.IsGood(result.StatusCode) && result.State.Revision != m_state.Revision)
                {
                    byte[] bytes = Encode(result.State);
                    using (Stream output = m_files.OpenWrite(m_stagedPath))
                    {
#if NETFRAMEWORK
                        await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#else
                        await output.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
#endif
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    m_files.Replace(m_stagedPath, m_statePath);
                    m_state = result.State;
                }
                return result with { State = RegistryStateStorage.Copy(result.State) };
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            bool ownsDisposal;
            lock (m_lifetime)
            {
                ownsDisposal = !m_closing;
                m_closing = true;
                if (m_operations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            if (!ownsDisposal)
            {
                await m_disposed.Task.ConfigureAwait(false);
                return;
            }
            await m_drained.Task.ConfigureAwait(false);
            try
            {
                m_writerLease.Dispose();
                m_gate.Dispose();
                m_disposed.TrySetResult(true);
            }
            catch (IOException error)
            {
                m_disposed.TrySetException(error);
                throw;
            }
        }

        private async ValueTask LoadAsync(CancellationToken cancellationToken)
        {
            if (m_loaded)
            {
                return;
            }
            if (m_files.Exists(m_statePath, isDirectory: false))
            {
                using Stream input = m_files.OpenRead(m_statePath);
                if (input.Length < 52 || input.Length > (long)m_maxStateBytes + 52)
                {
                    throw new InvalidDataException("Registry state length exceeds its bounds or is truncated.");
                }
                using var content = new MemoryStream();
                await input.CopyToAsync(content, 81920, cancellationToken).ConfigureAwait(false);
                content.Position = 0;
                using var reader = new BinaryReader(content, Encoding.UTF8, leaveOpen: true);
                if (reader.ReadUInt64() != kMagic)
                {
                    throw new InvalidDataException("The registry state format is unknown.");
                }
                ulong revision = reader.ReadUInt64();
                int length = reader.ReadInt32();
                byte[] digest = reader.ReadBytes(32);
                if (revision == 0 || length < 0 || length > m_maxStateBytes || content.Length != length + 52L)
                {
                    throw new InvalidDataException("The registry state header is invalid.");
                }
                byte[] document = reader.ReadBytes(length);
                if (!Hash(revision, document).AsSpan().SequenceEqual(digest))
                {
                    throw new InvalidDataException("The committed registry state checksum is invalid.");
                }
                m_state = new RegistryStoredState(revision, ByteString.From(document));
            }
            if (m_files.Exists(m_stagedPath, isDirectory: false))
            {
                m_files.Delete(m_stagedPath, isDirectory: false);
            }
            m_loaded = true;
        }

        private static byte[] Encode(RegistryStoredState state)
        {
            byte[] document = state.Document.ToArray();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(kMagic);
                writer.Write(state.Revision);
                writer.Write(document.Length);
                writer.Write(Hash(state.Revision, document));
                writer.Write(document);
            }
            return stream.ToArray();
        }

        private static byte[] Hash(ulong revision, byte[] bytes)
        {
            byte[] header = new byte[12];
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(), revision);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), bytes.Length);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(header);
            hash.AppendData(bytes);
            return hash.GetHashAndReset();
        }

        private Operation Enter()
        {
            lock (m_lifetime)
            {
                if (m_closing)
                {
                    throw new ObjectDisposedException(nameof(FileRegistryStateStore));
                }
                m_operations++;
                return new Operation(this);
            }
        }

        private void Leave()
        {
            lock (m_lifetime)
            {
                if (--m_operations == 0 && m_closing)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        private readonly struct Operation(FileRegistryStateStore owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Leave();
            }
        }

        private const ulong kMagic = 0x0031545347455258;
        private readonly LocalFileSystem m_files = LocalFileSystem.Instance;
        private readonly string m_statePath;
        private readonly string m_stagedPath;
        private readonly int m_maxStateBytes;
        private readonly Stream m_writerLease;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Lock m_lifetime = new();
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> m_disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private RegistryStoredState m_state;
        private int m_operations;
        private bool m_closing;
        private bool m_loaded;
    }
}
