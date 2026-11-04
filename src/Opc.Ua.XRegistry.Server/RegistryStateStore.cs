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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// One complete serialized registry generation. Revision zero denotes an uninitialized store.
    /// The document includes all metadata and retained content needed to rebuild its projection.
    /// </summary>
    public readonly record struct RegistryStoredState(ulong Revision, ByteString Document);

    /// <summary>
    /// Atomic store outcome, including the committed state or the current conflicting generation.
    /// </summary>
    public readonly record struct RegistryStateCommit(StatusCode StatusCode, RegistryStoredState State);

    /// <summary>
    /// Atomic compare-and-swap storage for one registry instance.
    /// Implementations do not interpret documents or assign entity epochs.
    /// </summary>
    public interface IRegistryStateStore : IAsyncDisposable
    {
        /// <summary>
        /// Reads the complete committed generation.
        /// </summary>
        ValueTask<RegistryStoredState> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits only when expectedRevision equals the current store revision.
        /// Zero matches an uninitialized store; it is not a wildcard.
        /// Byte-identical state retains its revision. Cancellation is observed before publication.
        /// </summary>
        ValueTask<RegistryStateCommit> CommitAsync(
            ulong expectedRevision,
            ByteString document,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// In-process atomic state adapter for isolated catalogs and deterministic tests.
    /// </summary>
    public sealed class MemoryRegistryStateStore : IRegistryStateStore
    {
        /// <summary>
        /// Creates a bounded in-memory store.
        /// </summary>
        public MemoryRegistryStateStore(int maxStateBytes = 16 * 1024 * 1024)
        {
            if (maxStateBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStateBytes));
            }
            m_maxStateBytes = maxStateBytes;
        }

        /// <inheritdoc/>
        public ValueTask<RegistryStoredState> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ThrowIfDisposed();
                return new ValueTask<RegistryStoredState>(RegistryStateStorage.Copy(m_state));
            }
        }

        /// <inheritdoc/>
        public ValueTask<RegistryStateCommit> CommitAsync(
            ulong expectedRevision,
            ByteString document,
            CancellationToken cancellationToken = default)
        {
            RegistryStateStorage.Validate(document, m_maxStateBytes);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                RegistryStateCommit result = RegistryStateStorage.Next(m_state, expectedRevision, document);
                if (StatusCode.IsGood(result.StatusCode))
                {
                    m_state = result.State;
                }
                return new ValueTask<RegistryStateCommit>(
                    result with { State = RegistryStateStorage.Copy(result.State) });
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposed = true;
                m_state = default;
            }
            return default;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(MemoryRegistryStateStore));
            }
        }

        private readonly Lock m_gate = new();
        private readonly int m_maxStateBytes;
        private RegistryStoredState m_state;
        private bool m_disposed;
    }

    internal static class RegistryStateStorage
    {
        public static void Validate(ByteString document, int maximum)
        {
            if (document.IsNull)
            {
                throw new ArgumentException("A committed registry document cannot be null.", nameof(document));
            }
            if (document.Length > maximum)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        public static RegistryStoredState Copy(RegistryStoredState state)
        {
            return state.Document.IsNull
                ? state
                : new RegistryStoredState(state.Revision, ByteString.From(state.Document.ToArray()));
        }

        public static RegistryStateCommit Next(
            RegistryStoredState current,
            ulong expected,
            ByteString document)
        {
            if (current.Revision != expected)
            {
                return new RegistryStateCommit(StatusCodes.BadInvalidState, current);
            }
            if (current.Revision != 0 && current.Document == document)
            {
                return new RegistryStateCommit(StatusCodes.Good, current);
            }
            if (current.Revision == ulong.MaxValue)
            {
                return new RegistryStateCommit(StatusCodes.BadOutOfRange, current);
            }
            return new RegistryStateCommit(StatusCodes.Good,
                new RegistryStoredState(current.Revision + 1, ByteString.From(document.ToArray())));
        }
    }
}
