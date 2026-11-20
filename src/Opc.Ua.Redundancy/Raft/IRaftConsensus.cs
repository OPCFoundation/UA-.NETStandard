/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the strongly-consistent (CP) consensus seam used to build a linearizable
    /// <see cref="ISharedKeyValueStore"/> and a native <see cref="ILeaderElection"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape mirrors a replicated-state-machine consensus node: an opaque application command is
    /// <see cref="ProposeAsync"/>d, replicated, and — once committed — surfaced in log order on
    /// <see cref="Committed"/> so every replica applies the same sequence of commands. This is exactly the contract
    /// of the <c>RaftNode</c> facade in the external <c>RaftCs</c> package (<c>marcschier/raft-cs</c>), so the
    /// production adapter that wraps a real Raft replica is a thin shim over this interface.
    /// </para>
    /// <para>
    /// <see cref="InProcessRaftConsensus"/> provides a deterministic in-memory backend (a single shared committed log)
    /// for single-process deployments, in-process replica sets, and tests; a multi-node Raft engine is a drop-in
    /// replacement of the same contract.
    /// </para>
    /// </remarks>
    public interface IRaftConsensus : IAsyncDisposable
    {
        /// <summary>
        /// <c>true</c> when this replica currently believes itself to be the leader (the only replica that may
        /// originate proposals in a real Raft cluster).
        /// </summary>
        bool IsLeader { get; }

        /// <summary>
        /// Raised when leadership is gained (<c>true</c>) or lost (<c>false</c>).
        /// </summary>
        event Action<bool>? LeadershipChanged;

        /// <summary>
        /// A reader over committed application command payloads, in log order. Every replica observes the identical
        /// sequence, which is what makes a state machine built on top deterministic and linearizable.
        /// </summary>
        ChannelReader<ReadOnlyMemory<byte>> Committed { get; }

        /// <summary>
        /// Starts the consensus replica (joins the cluster / begins the driver loop).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Proposes an opaque application command to be replicated and committed. The command is surfaced on
        /// <see cref="Committed"/> once it commits.
        /// </summary>
        /// <param name="command">The opaque command payload.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default);

        /// <summary>
        /// Requests that this replica (try to) become the leader. Best-effort: a backend with deterministic
        /// leadership may treat this as a no-op and simply report the current state.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask CampaignAsync(CancellationToken ct = default);
    }
}
