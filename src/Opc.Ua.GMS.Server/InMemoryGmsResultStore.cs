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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Bounded, single-host reference storage for one GMS. Full stores reject new results without implicit eviction.
    /// </summary>
    public sealed class InMemoryGmsResultStore : IGmsResultStore
    {
        /// <summary>
        /// Creates an independent result store using the supplied codec context and immutable limits.
        /// </summary>
        public InMemoryGmsResultStore(IServiceMessageContext context, GmsResultLimits? limits = null)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_limits = limits ?? new GmsResultLimits();
            m_limits.Validate();
        }

        /// <inheritdoc/>
        public ValueTask<GmsResult?> ReadLatestAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<GmsResult?>(m_order.Count == 0 ? null : m_results[m_order[0]].Result);
            }
        }

        /// <inheritdoc/>
        public ValueTask<GmsResult?> ReadAsync(string resultId, CancellationToken cancellationToken = default)
        {
            RequireIdentifier(resultId);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<GmsResult?>(
                    m_results.TryGetValue(resultId, out Entry? entry) ? entry.Result : null);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<string>> ListIdsAsync(
            int maximumResults,
            CancellationToken cancellationToken = default)
        {
            if (maximumResults < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResults));
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                return new ValueTask<ArrayOf<string>>(
                    m_order.GetRange(0, Math.Min(maximumResults, m_order.Count)).ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask WriteAsync(GmsResult result, CancellationToken cancellationToken = default)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            cancellationToken.ThrowIfCancellationRequested();
            long bytes = GmsResultBudget.Measure(result, m_limits, m_context);
            lock (m_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool exists = m_results.TryGetValue(result.ResultId, out Entry? previous);
                long total = m_bytes - (previous?.Bytes ?? 0) + bytes;
                if ((!exists && m_results.Count >= m_limits.MaximumResults) || total > m_limits.MaximumStoredBytes)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadResourceUnavailable, "The GMS result store's count or byte capacity is exhausted.");
                }
                m_results[result.ResultId] = new Entry(result, bytes);
                m_order.Remove(result.ResultId);
                m_order.Insert(0, result.ResultId);
                m_bytes = total;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<int>> AcknowledgeAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default)
        {
            if (resultIds.Count > m_limits.MaximumResults)
            {
                throw ServiceResultException.Create(StatusCodes.BadTooManyOperations, "Too many result identifiers.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var errors = new int[resultIds.Count];
            lock (m_gate)
            {
                for (int i = 0; i < resultIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(resultIds[i]) &&
                        m_results.TryGetValue(resultIds[i], out Entry? entry))
                    {
                        m_results[resultIds[i]] = entry with { Acknowledged = true };
                    }
                    else
                    {
                        errors[i] = unchecked((int)StatusCodes.BadNotFound.Code);
                    }
                }
            }
            return new ValueTask<ArrayOf<int>>(errors);
        }

        /// <summary>
        /// Explicitly removes an application-owned stored result; this is not an OPC UA machine command.
        /// </summary>
        public ValueTask<bool> RemoveAsync(string resultId, CancellationToken cancellationToken = default)
        {
            RequireIdentifier(resultId);
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!m_results.TryGetValue(resultId, out Entry? entry))
                {
                    return new ValueTask<bool>(false);
                }
                m_results.Remove(resultId);
                m_order.Remove(resultId);
                m_bytes -= entry.Bytes;
            }
            return new ValueTask<bool>(true);
        }

        /// <summary>
        /// Validates storage keys without converting absence into a result.
        /// </summary>
        private static void RequireIdentifier(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId))
            {
                throw new ArgumentException("A result identifier is required.", nameof(resultId));
            }
        }

        private readonly IServiceMessageContext m_context;
        private readonly GmsResultLimits m_limits;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, Entry> m_results = new(StringComparer.Ordinal);
        private readonly List<string> m_order = [];
        private long m_bytes;

        /// <summary>
        /// One immutable stored result and its accounted resource cost.
        /// </summary>
        /// <param name="Result">The acquired result.</param>
        /// <param name="Bytes">Its stored resource cost.</param>
        /// <param name="Acknowledged">Whether an acknowledgment has been received.</param>
        private sealed record Entry(GmsResult Result, long Bytes, bool Acknowledged = false);
    }
}
