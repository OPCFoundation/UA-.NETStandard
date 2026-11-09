/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Sample application permissions. Process identities never acquire operator or administrator privileges.
    /// </summary>
    public enum RecyclingAccess
    {
        /// <summary>
        /// Read-only observer.
        /// </summary>
        Observer,
        /// <summary>
        /// Own-role registration, initialization and lease maintenance only.
        /// </summary>
        ProcessView,
        /// <summary>
        /// Equipment enable/disable and explicit product disposition.
        /// </summary>
        Operator,
        /// <summary>
        /// Simulator lifecycle, bounded stepping, reset and checkpoint administration.
        /// </summary>
        SimulatorAdministrator
    }

    /// <summary>
    /// Trusted in-process result of authentication/authorization, never accepted as a method argument.
    /// </summary>
    public sealed record RecyclingPrincipal(
        string Identity,
        RecyclingAccess Access,
        RecyclingProcess Process,
        string ConnectionId,
        string CertificateSha256 = "");

    /// <summary>
    /// Auditable command receipt retained with production state before acknowledgement.
    /// </summary>
    public sealed record RecyclingCommandReceipt(
        string Identity,
        RecyclingAccess Access,
        RecyclingCommand Command,
        RecyclingCommandOutcome Outcome,
        DateTime CommittedUtc);

    /// <summary>
    /// View-local coordination status, distinct from transport, machine and material-disposition state.
    /// </summary>
    public enum RecyclingMembershipState
    {
        /// <summary>
        /// No membership has been established yet.
        /// </summary>
        Uninitialized,
        /// <summary>
        /// The authenticated session's role is initialized and its lease is current.
        /// </summary>
        Ready,
        /// <summary>
        /// A bounded revision-conflict round must retry on the same connection.
        /// </summary>
        RevisionContention,
        /// <summary>
        /// A former session's known owned lease must expire before a replacement can register.
        /// </summary>
        WaitingForPreviousLease,
        /// <summary>
        /// The transport/session or remote coordinator is unavailable and may require reconnection.
        /// </summary>
        TransportUnavailable,
        /// <summary>
        /// Authorization was refused; no retry may reinterpret this as contention.
        /// </summary>
        Rejected,
        /// <summary>
        /// A non-transport protocol/configuration failure requires attention.
        /// </summary>
        Faulted
    }

    /// <summary>
    /// Complete versioned state of one single-writer journal commit.
    /// </summary>
    public sealed record RecyclingCheckpoint
    {
        /// <summary>
        /// Persistence envelope schema.
        /// </summary>
        public int SchemaVersion { get; init; } = 2;

        /// <summary>
        /// Monotonic journal sequence independent of resettable material ticks.
        /// </summary>
        public long Sequence { get; init; }

        /// <summary>
        /// SHA-256 of the preceding committed journal payload.
        /// </summary>
        public string PreviousHash { get; init; } = string.Empty;

        /// <summary>
        /// SHA-256 configuration and compiled sample-model identity.
        /// </summary>
        public string ConfigurationHash { get; init; } = string.Empty;

        /// <summary>
        /// Persisted line identity.
        /// </summary>
        public string LineId { get; init; } = string.Empty;

        /// <summary>
        /// Complete immutable production state.
        /// </summary>
        public CommittedSnapshot Snapshot { get; init; } = new();

        /// <summary>
        /// Bounded durable idempotency and authenticated command audit history.
        /// </summary>
        public ArrayOf<RecyclingCommandReceipt> Receipts { get; init; } = [];
    }
}
