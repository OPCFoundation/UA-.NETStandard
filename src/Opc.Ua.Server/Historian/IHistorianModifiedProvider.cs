// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that support modified-history reads
    /// (Part 11 §5.2.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modified history returns explicit INSERT records and the prior versions
    /// of replaced or deleted values, together with the <see cref="ModificationInfo"/>.
    /// An INSERT record contains the inserted value, which may still be the current
    /// raw value. Its existence alone does not imply <see cref="AggregateBits.ExtraData"/>.
    /// Raw capture via <see cref="IHistorianBulkInsertProvider"/> need not log INSERT records.
    /// </para>
    /// </remarks>
    public interface IHistorianModifiedProvider
    {
        /// <summary>
        /// Reads one page of modified-history values from the archive.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <param name="request">Normalised modified read request.</param>
        /// <param name="resumeToken">Page resume token; empty on first page.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A page of retained values and modification metadata, with a token when more entries remain.
        /// </returns>
        ValueTask<HistorianPage<ModifiedDataValue>> ReadModifiedAsync(
            HistorianOperationContext context,
            HistorianModifiedReadRequest request,
            HistorianResumeToken resumeToken,
            CancellationToken ct);
    }
}
