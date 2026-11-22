// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Records server nonces as single-use across the whole replica set so a
    /// captured <c>ActivateSession</c> signature cannot be replayed on another
    /// replica during a mirrored fast-reconnect. A nonce may be consumed
    /// <em>exactly once</em> by exactly one replica; every later attempt (on any
    /// replica) is rejected.
    /// </summary>
    /// <remarks>
    /// OPC UA Part 4 §5.7.3.1 requires the <c>serverNonce</c> to be single-use.
    /// In a shared / mirrored session deployment that guarantee must hold across
    /// replicas, otherwise a Sign-mode <c>ActivateSession</c> captured against
    /// one replica replays against a standby. This
    /// registry provides the cross-replica single-use check.
    /// </remarks>
    public interface ISingleUseNonceRegistry
    {
        /// <summary>
        /// Atomically marks <paramref name="nonce"/> as consumed.
        /// </summary>
        /// <param name="nonce">The server nonce being consumed.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>true</c> when this call was the first to consume the nonce (the
        /// caller may proceed); <c>false</c> when the nonce was already consumed
        /// (the caller must reject the request as a replay).
        /// </returns>
        ValueTask<bool> TryConsumeAsync(ByteString nonce, CancellationToken ct = default);

        /// <summary>
        /// Removes the markers of consumed nonces that are at least
        /// <paramref name="minimumAge"/> old and that no live mirrored session still
        /// carries, so the registry does not grow without bound. A nonce that no
        /// session entry carries any more can no longer be presented for a restore.
        /// </summary>
        /// <param name="retainedNonces">
        /// The nonces of the live mirrored sessions; their markers are kept.
        /// </param>
        /// <param name="minimumAge">
        /// The minimum age of a removed marker, which covers a restore that consumed a
        /// nonce but has not yet mirrored the next one.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The number of markers removed.</returns>
        ValueTask<int> PurgeAsync(
            IEnumerable<ByteString> retainedNonces,
            TimeSpan minimumAge,
            CancellationToken ct = default);
    }
}
