/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Transport capability for discovery announcements that use a
    /// transport-specific well-known destination instead of the data
    /// message destination.
    /// </summary>
    public interface IPubSubDiscoveryAnnouncementTransport
    {
        /// <summary>
        /// Periodic discovery announcement rate in milliseconds.
        /// A value of zero disables cyclic announcements.
        /// </summary>
        uint DiscoveryAnnounceRate { get; }

        /// <summary>
        /// Sends one already encoded discovery announcement to the
        /// transport-defined discovery destination.
        /// </summary>
        /// <param name="payload">Encoded NetworkMessage payload.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SendDiscoveryAnnouncementAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default);
    }
}
