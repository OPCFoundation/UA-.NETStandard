/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A process-local registry store that keeps its committed generation in
    /// memory only. It implements the same transactional commit semantics as the
    /// durable <see cref="FileWotRegistryStore"/> — a commit atomically switches
    /// the single committed snapshot reference, so a failed commit leaves the
    /// previously committed generation intact — but it persists nothing to disk,
    /// so a fresh store instance (a process restart) starts from an empty
    /// registry. Useful for tests and for servers whose documents are
    /// re-populated programmatically at start-up.
    /// </summary>
    public sealed class InMemoryWotRegistryStore : IWotRegistryStore, IWotRegistryResourceStoreProvider
    {
        /// <inheritdoc/>
        public IXRegistryResourceStore ResourceStore { get; } = new InMemoryResourceStore();

        /// <inheritdoc/>
        public ValueTask<WotRegistrySnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<WotRegistrySnapshot>(Volatile.Read(ref m_committed));
        }

        /// <inheritdoc/>
        public ValueTask CommitAsync(
            WotRegistrySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            // The immutable snapshot is committed by a single atomic reference
            // switch, so a reader (LoadAsync) never observes a partial generation.
            Volatile.Write(ref m_committed, snapshot ?? WotRegistrySnapshot.Empty);
            return default;
        }

        private WotRegistrySnapshot m_committed = WotRegistrySnapshot.Empty;
    }
}
