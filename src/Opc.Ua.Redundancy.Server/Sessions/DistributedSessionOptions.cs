/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Options controlling the <see cref="DistributedSessionManager"/>.
    /// </summary>
    public sealed class DistributedSessionOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether a standby replica may
        /// <em>restore</em> a mirrored session and let a client reconnect by
        /// re-running <c>ActivateSession</c> on a new SecureChannel (the
        /// OPC UA HotAndMirrored fast reconnect).
        /// </summary>
        /// <remarks>
        /// Defaults to <c>false</c> — the safe, spec-compliant default is
        /// re-authentication on failover (a fresh <c>CreateSession</c> +
        /// <c>ActivateSession</c>), which needs no shared session state. Even
        /// when this is <c>false</c> the manager still mirrors session metadata
        /// for cross-replica visibility, but it will not admit a session from
        /// the shared store. When set to <c>true</c>, a reconnect still performs
        /// the full <c>ActivateSession</c> client-signature validation against
        /// the mirrored, single-use <c>serverNonce</c>; the token is never an
        /// authenticator on its own.
        /// </remarks>
        public bool EnableFastReconnect { get; set; }

        /// <summary>
        /// Gets or sets how often the replica that owns a session checks it for client
        /// activity to mirror. A session's liveness heartbeat is written at most once
        /// per third of its timeout, and only while the client uses the session, so a
        /// standby can tell a session in normal use from one that timed out
        /// (OPC 10000-4 5.7.2.1). Defaults to one second; zero or a negative value
        /// disables the heartbeat, after which a mirrored session expires its timeout
        /// after its last activation.
        /// </summary>
        public TimeSpan LivenessInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets how often the shared store is swept for the entries of timed-out
        /// sessions and for orphaned consumed-nonce markers. Sessions of a replica that
        /// crashed or was shut down are never closed there, so without the sweep their
        /// records (with their nonces and client certificates) would stay in the store.
        /// Defaults to five minutes; zero or a negative value disables the sweep.
        /// </summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the minimum age of a consumed-nonce marker that no live session
        /// entry references before the sweep removes it. Defaults to one hour.
        /// </summary>
        public TimeSpan NonceMarkerRetention { get; set; } = TimeSpan.FromHours(1);
    }
}
