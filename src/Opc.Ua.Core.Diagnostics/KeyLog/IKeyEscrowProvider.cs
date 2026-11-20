// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Pcap.KeyLog
{
    /// <summary>
    /// Escrow provider for OPC UA secure-channel symmetric key
    /// material that the Pcap binding extracts from live channels.
    /// Replaces the default <see cref="DiskKeyEscrowProvider"/>
    /// disk-based writer with an alternative destination such as a
    /// hardware security module or cloud key vault, satisfying the
    /// "no keys on disk" prescription of the Microsoft SFI / edge
    /// security bar.
    /// </summary>
    /// <remarks>
    /// Implementations are registered via
    /// <c>services.AddSingleton&lt;IKeyEscrowProvider, MyProvider&gt;()</c>
    /// before <c>AddPcap()</c>. When a non-default
    /// provider is registered, the disk-based default is skipped
    /// entirely.
    /// </remarks>
    public interface IKeyEscrowProvider : IAsyncDisposable
    {
        /// <summary>
        /// Begin escrowing keys for a capture session. Returns a
        /// per-session handle that receives subsequent calls.
        /// </summary>
        /// <param name="sessionId">Stable id of the capture session.</param>
        /// <param name="sessionFolder">
        /// The session's per-session folder (used by the disk default
        /// to locate the keylog file; other providers may ignore it).
        /// </param>
        /// <param name="cancellationToken">Cancellation.</param>
        ValueTask<IKeyEscrowSession> BeginSessionAsync(
            string sessionId,
            string sessionFolder,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Per-session escrow handle. Disposed when the capture session
    /// ends.
    /// </summary>
    public interface IKeyEscrowSession : IAsyncDisposable
    {
        /// <summary>
        /// Hand a single key material record to the escrow provider.
        /// </summary>
        ValueTask EscrowAsync(ChannelKeyMaterial material, CancellationToken cancellationToken);

        /// <summary>
        /// Flush pending escrow operations to durable storage.
        /// </summary>
        ValueTask FlushAsync(CancellationToken cancellationToken);
    }
}
