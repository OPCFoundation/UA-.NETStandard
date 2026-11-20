// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// Per-GMS storage for immutable results. Implementations must honor cancellation and atomically commit writes.
    /// </summary>
    public interface IGmsResultStore
    {
        /// <summary>
        /// Returns the newest result, or null only when no result is available.
        /// </summary>
        ValueTask<GmsResult?> ReadLatestAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the identified result, or null only when it is absent.
        /// </summary>
        ValueTask<GmsResult?> ReadAsync(string resultId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns newest-first identifiers, never exceeding the positive caller-supplied limit.
        /// </summary>
        ValueTask<ArrayOf<string>> ListIdsAsync(int maximumResults, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically adds or replaces a result, failing without mutation when storage limits are exceeded.
        /// </summary>
        ValueTask WriteAsync(GmsResult result, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns one domain status for each requested identifier: zero on success, nonzero when absent.
        /// Acknowledgment does not delete the result.
        /// </summary>
        ValueTask<ArrayOf<int>> AcknowledgeAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default);
    }
}
