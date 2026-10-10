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
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.Machinery.Server.Results
{
    /// <summary>
    /// One OPC 40001-101 result: its metadata, its inline content and, when the
    /// result is too large to return inline, the payload a client downloads
    /// through <c>ResultTransfer.GenerateFileForRead</c>.
    /// </summary>
    public sealed class MachineryResult
    {
        /// <summary>
        /// Creates a result.
        /// </summary>
        /// <param name="data">The result data, including its metadata.</param>
        /// <param name="fileContent">
        /// The transferable payload. When non-empty, the result's
        /// <c>HasTransferableDataOnFile</c> flag is set and the payload becomes
        /// downloadable.
        /// </param>
        /// <param name="mimeType">
        /// The MIME type reported on the transient file object.
        /// </param>
        public MachineryResult(
            ResultDataType data,
            ByteString fileContent = default,
            string mimeType = "application/octet-stream")
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.ResultMetaData == null ||
                string.IsNullOrEmpty(data.ResultMetaData.ResultId))
            {
                throw new ArgumentException(
                    "A Machinery result must carry a ResultMetaData.ResultId.",
                    nameof(data));
            }
            FileContent = fileContent;
            MimeType = mimeType;
            if (!fileContent.IsNull && fileContent.Span.Length > 0)
            {
                data.ResultMetaData.HasTransferableDataOnFile = true;
                data.ResultMetaData.EncodingMask |=
                    (uint)ResultMetaDataTypeFields.HasTransferableDataOnFile;
            }
        }

        /// <summary>
        /// Gets the result identifier, taken from the metadata.
        /// </summary>
        public string ResultId => Data.ResultMetaData!.ResultId!;

        /// <summary>
        /// Gets the result data.
        /// </summary>
        public ResultDataType Data { get; }

        /// <summary>
        /// Gets the transferable payload; empty when the result carries none.
        /// </summary>
        public ByteString FileContent { get; }

        /// <summary>
        /// Gets the MIME type of <see cref="FileContent"/>.
        /// </summary>
        public string MimeType { get; }

        /// <summary>
        /// Gets or sets whether the result has been acknowledged by a client.
        /// </summary>
        public bool IsAcknowledged { get; set; }
    }

    /// <summary>
    /// Supplies OPC 40001-101 results to a <c>ResultManagement</c> object.
    /// </summary>
    /// <remarks>
    /// The five methods of <c>ResultManagementType</c> are all optional; the
    /// builder wires exactly the ones the store can answer, so a read-only
    /// store that cannot acknowledge simply does not publish
    /// <c>AcknowledgeResults</c>.
    /// </remarks>
    public interface IMachineryResultStore
    {
        /// <summary>
        /// Returns the most recent result, or <see langword="null"/> when the
        /// store is empty.
        /// </summary>
        ValueTask<MachineryResult?> GetLatestResultAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns one result by identifier, or <see langword="null"/> when it
        /// is unknown.
        /// </summary>
        ValueTask<MachineryResult?> GetResultByIdAsync(
            string resultId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the identifiers of the stored results, newest first.
        /// </summary>
        /// <remarks>
        /// This order is what <c>GetResultIdListFiltered</c> returns when the
        /// client asks for no ordering. A filter or an <c>orderedBy</c> is
        /// evaluated by the library over this listing (with
        /// <paramref name="maxResults"/> zero) and
        /// <see cref="GetResultByIdAsync"/>, so a store does not have to
        /// understand <c>ContentFilter</c> itself.
        /// </remarks>
        /// <param name="maxResults">
        /// The maximum number of identifiers to return; zero means no limit.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask<ArrayOf<string>> GetResultIdsAsync(
            uint maxResults,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Acknowledges results. Returns one status per requested identifier:
        /// zero when acknowledged, non-zero when the identifier was unknown.
        /// Returns an empty array when the store does not support
        /// acknowledgement, in which case the method is not published.
        /// </summary>
        ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A result store the library's publisher can write to.
    /// </summary>
    /// <remarks>
    /// Implement it next to <see cref="IMachineryResultStore"/> when the store
    /// should take results from
    /// <see cref="IMachineryResultPublisher.PublishAsync"/>. A store that
    /// ingests results through a path of its own — a database a PLC gateway
    /// fills, say — does not implement it; publishing then only announces a
    /// result the store already holds.
    /// </remarks>
    public interface IWritableMachineryResultStore : IMachineryResultStore
    {
        /// <summary>
        /// Adds a result to the store, replacing any result with the same
        /// identifier.
        /// </summary>
        /// <param name="result">The result to add.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask AddResultAsync(
            MachineryResult result,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Publishes new results into a running <c>ResultManagement</c> object.
    /// </summary>
    public interface IMachineryResultPublisher
    {
        /// <summary>
        /// Makes <paramref name="result"/> available and raises the
        /// OPC 40001-101 <c>ResultReadyEventType</c> for it; a machine with a
        /// <c>Results</c> folder also shows it in its result variables.
        /// </summary>
        /// <remarks>
        /// A store implementing <see cref="IWritableMachineryResultStore"/>
        /// receives the result first. Any other store has to return it from
        /// <see cref="IMachineryResultStore.GetResultByIdAsync"/> already —
        /// otherwise the publish is refused with <c>Bad_InvalidState</c>,
        /// because an event announcing a result the methods cannot hand out
        /// would promise the client something the server cannot keep.
        /// </remarks>
        /// <param name="result">The result to publish.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask PublishAsync(
            MachineryResult result,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The one path both publishers take to make a result available before
    /// they announce it.
    /// </summary>
    internal static class MachineryResultIngestion
    {
        /// <summary>
        /// Adds <paramref name="result"/> to a writable store, or checks that
        /// a store with its own ingestion path already holds it.
        /// </summary>
        /// <param name="store">
        /// The store the application bound, before any wrapper the library
        /// puts around it.
        /// </param>
        /// <param name="result">The result being published.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask StoreAsync(
            IMachineryResultStore store,
            MachineryResult result,
            CancellationToken cancellationToken)
        {
            if (store is IWritableMachineryResultStore writable)
            {
                await writable.AddResultAsync(result, cancellationToken).ConfigureAwait(false);
                return;
            }

            MachineryResult? stored = await store
                .GetResultByIdAsync(result.ResultId, cancellationToken)
                .ConfigureAwait(false);
            if (stored == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Result '{0}' cannot be published: the store '{1}' does not implement " +
                    "IWritableMachineryResultStore and does not hold the result either. " +
                    "Add it through the store's own ingestion path first.",
                    result.ResultId,
                    store.GetType().Name);
            }
        }
    }

    /// <summary>
    /// A bounded in-memory result store. Good enough for a machine that keeps
    /// the last few results in RAM, and the reference implementation of
    /// <see cref="IMachineryResultStore"/>.
    /// </summary>
    public sealed class InMemoryMachineryResultStore : IWritableMachineryResultStore
    {
        /// <summary>
        /// Creates a store.
        /// </summary>
        /// <param name="capacity">
        /// The number of results kept; the oldest is dropped when the limit is
        /// reached.
        /// </param>
        public InMemoryMachineryResultStore(int capacity = 64)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "The result store capacity must be positive.");
            }
            m_capacity = capacity;
        }

        /// <summary>
        /// Adds a result, dropping the oldest when the store is full.
        /// </summary>
        public void Add(MachineryResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            lock (m_lock)
            {
                m_results.RemoveAll(
                    existing => string.Equals(
                        existing.ResultId,
                        result.ResultId,
                        StringComparison.Ordinal));
                m_results.Add(result);
                while (m_results.Count > m_capacity)
                {
                    m_results.RemoveAt(0);
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask AddResultAsync(
            MachineryResult result,
            CancellationToken cancellationToken = default)
        {
            Add(result);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<MachineryResult?> GetLatestResultAsync(
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                return new ValueTask<MachineryResult?>(
                    m_results.Count == 0 ? null : m_results[^1]);
            }
        }

        /// <inheritdoc/>
        public ValueTask<MachineryResult?> GetResultByIdAsync(
            string resultId,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                for (int ii = m_results.Count - 1; ii >= 0; ii--)
                {
                    if (string.Equals(m_results[ii].ResultId, resultId, StringComparison.Ordinal))
                    {
                        return new ValueTask<MachineryResult?>(m_results[ii]);
                    }
                }
            }
            return new ValueTask<MachineryResult?>((MachineryResult?)null);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<string>> GetResultIdsAsync(
            uint maxResults,
            CancellationToken cancellationToken = default)
        {
            lock (m_lock)
            {
                int count = m_results.Count;
                if (maxResults > 0 && maxResults < (uint)count)
                {
                    count = (int)maxResults;
                }
                var ids = new string[count];
                for (int ii = 0; ii < count; ii++)
                {
                    ids[ii] = m_results[m_results.Count - 1 - ii].ResultId;
                }
                return new ValueTask<ArrayOf<string>>(ids.ToArrayOf());
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<int>> AcknowledgeResultsAsync(
            ArrayOf<string> resultIds,
            CancellationToken cancellationToken = default)
        {
            if (resultIds.IsNull || resultIds.Count == 0)
            {
                return new ValueTask<ArrayOf<int>>(ArrayOf<int>.Empty);
            }
            var errors = new int[resultIds.Count];
            lock (m_lock)
            {
                for (int ii = 0; ii < resultIds.Count; ii++)
                {
                    errors[ii] = unchecked((int)StatusCodes.BadNotFound.Code);
                    for (int jj = 0; jj < m_results.Count; jj++)
                    {
                        if (string.Equals(
                                m_results[jj].ResultId,
                                resultIds[ii],
                                StringComparison.Ordinal))
                        {
                            m_results[jj].IsAcknowledged = true;
                            errors[ii] = 0;
                            break;
                        }
                    }
                }
            }
            return new ValueTask<ArrayOf<int>>(errors.ToArrayOf());
        }

        private readonly List<MachineryResult> m_results = [];
        private readonly Lock m_lock = new();
        private readonly int m_capacity;
    }
}
