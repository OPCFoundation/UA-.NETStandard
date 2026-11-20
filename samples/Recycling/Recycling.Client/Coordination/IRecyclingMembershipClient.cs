// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Authenticated connection borrowed by bounded membership operations.
    /// The connection owner, not membership retry logic, controls its lifetime.
    /// </summary>
    public interface IRecyclingMembershipClient : IAsyncDisposable
    {
        /// <summary>
        /// Actual server-side session identity, preserved while retrying revision contention.
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// Whether the managed connection is currently usable.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Actual authenticated user-certificate fingerprint, not a caller-supplied actor claim.
        /// </summary>
        string CertificateSha256 { get; }

        /// <summary>
        /// Reads a committed image for optimistic membership commands.
        /// </summary>
        ValueTask<CommittedSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies a typed command through the authenticated connection.
        /// </summary>
        ValueTask<RecyclingCommandOutcome> ExecuteAsync(
            RecyclingCommand command,
            CancellationToken cancellationToken = default);
    }
}
