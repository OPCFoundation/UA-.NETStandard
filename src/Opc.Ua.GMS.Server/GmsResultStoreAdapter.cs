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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Results;

namespace Opc.Ua.GMS.Server
{
    /// <summary>
    /// Adapts one bounded provider to Machinery storage without replacing any query, metadata or file protocol.
    /// </summary>
    internal sealed class GmsResultStoreAdapter : IWritableMachineryResultStore
    {
        /// <summary>
        /// Captures the explicitly supplied instance provider.
        /// </summary>
        internal GmsResultStoreAdapter(
            IGmsResultStore store,
            GmsResultLimits limits,
            IServiceMessageContext context,
            CancellationToken lifetime)
        {
            m_store = store;
            m_limits = limits;
            m_context = context;
            m_lifetime = lifetime;
        }

        /// <inheritdoc/>
        public async ValueTask<MachineryResult?> GetLatestResultAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            cancellationToken = linked.Token;
            cancellationToken.ThrowIfCancellationRequested();
            GmsResult? result = await m_store.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result == null ? null : Convert(result);
        }

        /// <inheritdoc/>
        public async ValueTask<MachineryResult?> GetResultByIdAsync(
            string resultId,
            CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            cancellationToken = linked.Token;
            cancellationToken.ThrowIfCancellationRequested();
            GmsResult? result = await m_store.ReadAsync(resultId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result != null && !string.Equals(result.ResultId, resultId, StringComparison.Ordinal))
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "The GMS provider returned another result's identity.");
            }
            return result == null ? null : Convert(result);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<string>> GetResultIdsAsync(
            uint maxResults,
            CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            cancellationToken = linked.Token;
            cancellationToken.ThrowIfCancellationRequested();
            // The extra entry detects an incomplete listing before Machinery evaluates a filter.
            ArrayOf<string> ids = await m_store.ListIdsAsync(
                m_limits.MaximumResults + 1, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (ids.IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "The GMS provider returned no identifier listing.");
            }
            if (ids.Count > m_limits.MaximumResults)
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingLimitsExceeded,
                    "The provider result universe exceeds the configured query bound.");
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "The GMS provider returned an invalid or duplicate result identifier.");
                }
            }
            int count = maxResults == 0 ? ids.Count : (int)Math.Min(maxResults, (uint)ids.Count);
            var selected = new string[count];
            for (int i = 0; i < count; i++)
            {
                selected[i] = ids[i];
            }
            ArrayOf<string> result = selected;
            m_limits.Measure(Variant.From(result), m_context);
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            cancellationToken = linked.Token;
            cancellationToken.ThrowIfCancellationRequested();
            if (resultIds.Count > m_limits.MaximumResults)
            {
                throw ServiceResultException.Create(StatusCodes.BadTooManyOperations, "Too many result identifiers.");
            }
            m_limits.Measure(Variant.From(resultIds), m_context);
            ArrayOf<int> errors = await m_store.AcknowledgeAsync(resultIds, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (errors.IsNull || errors.Count != resultIds.Count)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "The GMS provider did not return one acknowledgment status per identifier.");
            }
            return errors;
        }

        /// <inheritdoc/>
        public async ValueTask AddResultAsync(MachineryResult result, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            cancellationToken = linked.Token;
            cancellationToken.ThrowIfCancellationRequested();
            GmsResult snapshot = Snapshot(result);
            await m_store.WriteAsync(snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Produces a detached standard envelope and associates only its lifetime with its immutable GMS sidecar.
        /// </summary>
        internal MachineryResult Convert(GmsResult result)
        {
            GmsResultBudget.Measure(result, m_limits, m_context);
            ResultDataType data = result.Data;
            m_snapshots.Add(data, result);
            return new MachineryResult(data, result.FileContent, result.MimeType);
        }

        /// <summary>
        /// Recovers only samples owned by this binding, never by identifier from another provider.
        /// </summary>
        internal GmsResult Snapshot(MachineryResult result)
        {
            if (!m_snapshots.TryGetValue(result.Data, out GmsResult? snapshot))
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch,
                    "Publish GMS results through their bound GmsResultPublisher.");
            }
            return snapshot;
        }

        private readonly IGmsResultStore m_store;
        private readonly GmsResultLimits m_limits;
        private readonly IServiceMessageContext m_context;
        private readonly CancellationToken m_lifetime;
        private readonly ConditionalWeakTable<ResultDataType, GmsResult> m_snapshots = new();
    }
}
