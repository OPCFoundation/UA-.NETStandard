/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that can push aggregate
    /// computation down to the storage engine (Part 11 §5.2.6 ProcessedDetails).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Providers that do not implement this interface get a streaming
    /// framework fallback that uses
    /// <see cref="IHistorianDataProvider.ReadRawAsync"/> and the central
    /// <see cref="AggregateManager"/>'s <see cref="IAggregateCalculator"/>
    /// pipeline.
    /// </para>
    /// </remarks>
    public interface IHistorianProcessedProvider
    {
        /// <summary>
        /// Reads one page of processed (aggregate) values from the archive.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised processed read request.</param>
        /// <param name="resumeToken">Page resume token; empty on first page.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A page of aggregate results and a resume token when further intervals remain.</returns>
        ValueTask<HistorianPage<DataValue>> ReadProcessedAsync(
            HistorianOperationContext context,
            HistorianProcessedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);
    }
}
