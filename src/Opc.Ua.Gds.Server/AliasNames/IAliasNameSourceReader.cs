/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.AliasNames
{
    /// <summary>
    /// Pulls the AliasNames of a registered AliasName Server for the GDS
    /// AliasName Server facet (OPC 10000-17 Annex C.1: "Pull all
    /// AliasNameCategory instances").
    /// </summary>
    public interface IAliasNameSourceReader
    {
        /// <summary>
        /// Reads the AliasNames of <paramref name="application"/>.
        /// </summary>
        /// <param name="application">The registered application record;
        /// its <c>DiscoveryUrls</c> locate the source.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The source's categories and aliases.</returns>
        /// <exception cref="ServiceResultException">The source could not be
        /// reached or read.</exception>
        ValueTask<AliasNameSourceSnapshot> ReadAsync(
            ApplicationRecordDataType application,
            CancellationToken ct = default);
    }
}
