/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Optional owner capability for protecting an exact Version from retention eviction.
    /// Acquisition is atomic with the owner's allocation and retention decisions.
    /// </summary>
    public interface IWotRegistryVersionLeaseProvider
    {
        /// <summary>
        /// Acquires a lease on the current incarnation represented by a provider-issued Version
        /// snapshot. A deleted or replaced incarnation is rejected, rather than leased by id alone.
        /// The caller owns the returned lease and must dispose it when access ends.
        /// </summary>
        /// <param name="groupId">The group ID.</param>
        /// <param name="resourceId">The resource ID.</param>
        /// <param name="version">The WoT resource version.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<IWotRegistryVersionLease> AcquireVersionLeaseAsync(
            string groupId,
            string resourceId,
            WotResourceVersion version,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Protects one exact Version incarnation from retention until disposed.
    /// This is not a content-write reservation or a durable cross-process lease.
    /// </summary>
    public interface IWotRegistryVersionLease : IDisposable
    {
        /// <summary>
        /// Gets the canonical Version snapshot captured at acquisition.
        /// </summary>
        WotResourceVersion Version { get; }
    }
}
