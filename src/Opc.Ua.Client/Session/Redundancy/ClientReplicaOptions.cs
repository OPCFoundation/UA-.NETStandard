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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Redundancy
{
    /// <summary>
    /// Options for a client replica that participates in a replica set.
    /// </summary>
    public sealed record class ClientReplicaOptions
    {
        /// <summary>
        /// Stable identity of this replica; used as the lease holder id.
        /// </summary>
        public string NodeId { get; init; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Standby behavior for followers.
        /// </summary>
        public ClientStandbyMode Mode { get; init; } = ClientStandbyMode.Cold;

        /// <summary>
        /// Shared-store key under which the leader publishes its protected session
        /// secrets so a promoted follower can reuse the AuthenticationToken.
        /// </summary>
        public string SessionRecordKey { get; init; } = "client-replica/session";

        /// <summary>
        /// Reuse the leader's mirrored AuthenticationToken on promotion against a
        /// HotAndMirrored server instead of recreating the session. The standby
        /// still performs the full ActivateSession signature validation.
        /// </summary>
        public bool EnableTokenReuse { get; init; } = true;

        /// <summary>
        /// Creates a connected managed session for this replica. The coordinator
        /// connects per <see cref="Mode"/> (immediately for Warm/Hot, on promotion
        /// for Cold). Subscriptions are applied by <see cref="ConfigureLeaderAsync"/>.
        /// </summary>
        public Func<CancellationToken, ValueTask<ManagedSession>>? CreateSessionAsync { get; init; }

        /// <summary>
        /// Applies the leader's subscriptions / publishing to the active session.
        /// Invoked once a replica becomes leader; receives whether the session was
        /// fast-activated by token reuse (subscriptions are mirrored and may only
        /// need publishing enabled) or freshly created.
        /// </summary>
        public Func<ManagedSession, bool, CancellationToken, ValueTask>? ConfigureLeaderAsync { get; init; }
    }
}
