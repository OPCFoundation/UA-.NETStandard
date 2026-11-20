// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: bridges a local node graph (<see cref="ILocalAddressSpace"/>) to a
    /// shared <see cref="INodeStateStore"/> so address-space topology and
    /// variable values replicate across server replicas.
    /// </summary>
    /// <remarks>
    /// A synchronizer runs in one of two roles, selected by the
    /// leader-election predicate supplied at construction:
    /// <list type="bullet">
    /// <item>
    /// <b>Writer (leader):</b> captures committed local changes and writes
    /// them through to the store (shared read, master write).
    /// </item>
    /// <item>
    /// <b>Reader (standby):</b> applies topology and value changes from the
    /// store change-feed to its local graph and never writes.
    /// </item>
    /// </list>
    /// Active/active multi-writer with conflict resolution is layered on top
    /// later (CRDT); this single-writer model is the active/passive default.
    /// </remarks>
    public interface IAddressSpaceSynchronizer : IAsyncDisposable
    {
        /// <summary>
        /// <c>true</c> when this synchronizer currently acts as the writer
        /// (leader).
        /// </summary>
        bool IsWriter { get; }

        /// <summary>
        /// Seeds the store from the local graph when the store is empty and
        /// this replica is the writer; otherwise hydrates the local graph
        /// from the store. Call once before <see cref="Start"/>.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SeedOrHydrateAsync(CancellationToken ct = default);

        /// <summary>
        /// Starts background replication: outbound capture for a writer, or
        /// the inbound apply loop for a reader.
        /// </summary>
        void Start();
    }
}
