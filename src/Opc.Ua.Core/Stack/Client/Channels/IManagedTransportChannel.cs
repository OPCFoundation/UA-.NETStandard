/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A transport channel managed by an <see cref="IClientChannelManager"/>.
    /// The channel is reference-counted; disposing a managed channel
    /// releases the caller's lease on the underlying channel, which the
    /// manager keeps open as long as at least one participant lease
    /// remains active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Service calls via <see cref="ITransportChannel.SendRequestAsync"/>
    /// are gated by the manager: requests block until
    /// <see cref="State"/> is <see cref="ChannelState.Ready"/>. The
    /// manager supplies a separate, non-owning recovery view for a participant's
    /// <see cref="IReconnectParticipant.OnReconnectAsync"/> call and any immediately following recreation.
    /// Only that generation-bound view bypasses the gate; ordinary calls on the owning lease remain gated.
    /// Disposing a recovery view does not release the owning lease.
    /// </para>
    /// <para>
    /// The <see cref="StateChanged"/> event is best-effort and may be
    /// raised on the manager's worker fiber. Handlers must not block,
    /// take locks held elsewhere, or call back into the manager;
    /// instead post work to a separate worker or set flags consumed
    /// elsewhere.
    /// </para>
    /// </remarks>
    public interface IManagedTransportChannel : ITransportChannel
    {
        /// <summary>
        /// Composite identity of the underlying channel. All
        /// participants on the same key share one transport channel.
        /// </summary>
        ManagedChannelKey Key { get; }

        /// <summary>
        /// Current lifecycle state of the channel.
        /// </summary>
        ChannelState State { get; }

        /// <summary>
        /// Owner manager instance. Use this to trigger an explicit
        /// reconnect or to fetch diagnostics; do not retain references
        /// across <see cref="IDisposable.Dispose"/>.
        /// </summary>
        IClientChannelManager Manager { get; }

        /// <summary>
        /// Best-effort notification when the channel state changes.
        /// </summary>
        /// <remarks>
        /// Handlers run on the manager's reconnect fiber. They must
        /// not block, acquire long-held locks, or re-enter the manager.
        /// </remarks>
        event Action<IManagedTransportChannel, ChannelStateChange>? StateChanged;
    }
}
