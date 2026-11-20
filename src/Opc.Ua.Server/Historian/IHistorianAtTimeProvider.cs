/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that can answer read-at-time
    /// (Part 11 §5.2.6.6) requests natively. Providers that do not
    /// implement this interface get a streaming framework fallback that
    /// uses <see cref="IHistorianDataProvider.ReadRawAsync"/> plus
    /// stepped/sloped interpolation.
    /// </summary>
    public interface IHistorianAtTimeProvider
    {
        /// <summary>
        /// Returns one value per requested timestamp, in the same order
        /// the client supplied. Exact values preserve their quality and use
        /// the Raw information bits. Values without an exact match are
        /// calculated from simple or interpolated bounds according to
        /// <see cref="HistorianAtTimeReadRequest.UseSimpleBounds"/> and the
        /// node's stepped setting, and use the Interpolated information bits.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised at-time read request.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>One exact or calculated value per requested timestamp, preserving the request order.</returns>
        ValueTask<ArrayOf<DataValue>> ReadAtTimeAsync(
            HistorianOperationContext context,
            HistorianAtTimeReadRequest request,
            CancellationToken ct);
    }
}
