// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Bindable options that tune the behaviour of a single
    /// <see cref="PullSecurityKeyProvider"/>. Defaults are chosen to
    /// match the operational guidance in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>: pre-fetch a small
    /// future window so that publish never blocks on the SKS, and
    /// schedule the next pull a few minutes before the active key
    /// expires.
    /// </summary>
    public sealed class PullSecurityKeyProviderOptions
    {
        /// <summary>
        /// Number of future keys to pre-fetch from the SKS in
        /// addition to the currently active key. The total number
        /// of keys requested per pull is
        /// <c>RequestedFutureKeyCount + 1</c>.
        /// </summary>
        public int RequestedFutureKeyCount { get; set; } = 4;

        /// <summary>
        /// Delta subtracted from the current key's expiration to
        /// schedule the next refresh. Should be large enough that
        /// the SKS round-trip cannot push the refresh past the
        /// expiration boundary even under adverse network conditions.
        /// </summary>
        public TimeSpan RefreshLeadTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Delay applied between consecutive failed refresh attempts.
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Minimum interval between two opportunistic refreshes that an
        /// inbound NetworkMessage with an unknown SecurityTokenId may
        /// trigger. The SecurityTokenId is read before the signature is
        /// verified, so forged token ids must not drive SKS calls at
        /// line rate.
        /// </summary>
        public TimeSpan OpportunisticRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum number of consecutive failed refresh attempts
        /// tolerated before the provider stops scheduling retries.
        /// The provider keeps serving the last-known keys until a
        /// caller-driven action restarts it.
        /// </summary>
        public int MaxConsecutiveFailures { get; set; } = 5;
    }
}
