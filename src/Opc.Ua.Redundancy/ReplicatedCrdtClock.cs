// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Crdt;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Keeps local replicated writes newer than accepted remote entries, including tombstones.
    /// </summary>
    internal static class ReplicatedCrdtClock
    {
        /// <summary>
        /// Observes an incoming map before merging it, or rejects state outside the clock tolerance.
        /// </summary>
        public static void Observe(
            HybridLogicalClock clock,
            TimeProvider timeProvider,
            LWWMap<string, ByteString> state)
        {
            if (!state.TryGetLatestTimestamp(out Timestamp latest))
            {
                return;
            }

            long latestAllowed = timeProvider.GetUtcNow().ToUnixTimeMilliseconds() + kMaxClockSkewMilliseconds;
            if (latest.WallClock > latestAllowed)
            {
                throw new FormatException("The replicated state exceeds the allowed clock skew.");
            }
            clock.Witness(latest);
        }

        private const long kMaxClockSkewMilliseconds = 15_000;
    }
}
