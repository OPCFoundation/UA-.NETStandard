/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.Transfer
{
    /// <summary>
    /// Default in-memory <see cref="ITransferService"/> implementation.
    /// Holds active transfers in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// keyed by an auto-incrementing transfer ID and exposes a
    /// pluggable export/import callback model for application logic
    /// to wire device-specific behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire device-specific export/import via
    /// <see cref="RegisterExporter"/> /
    /// <see cref="RegisterImporter"/>. If no exporter is registered
    /// for an element, <see cref="ITransferService.TransferFromDeviceAsync"/>
    /// returns a transfer ID that will surface
    /// <see cref="Opc.Ua.StatusCodes.BadNotSupported"/> on the first
    /// <see cref="ITransferService.FetchAsync"/>. Same for importer.
    /// </para>
    /// </remarks>
    public sealed class DefaultTransferService : ITransferService
    {
        /// <summary>
        /// Creates a new service.
        /// </summary>
        /// <param name="timeProvider">
        /// Optional time source for testability (defaults to
        /// <see cref="TimeProvider.System"/>).
        /// </param>
        /// <param name="transferTimeout">
        /// Maximum age of an idle transfer before it is auto-discarded
        /// on the next <see cref="ITransferService.FetchAsync"/>.
        /// Defaults to 5 minutes.
        /// </param>
        public DefaultTransferService(
            TimeProvider? timeProvider = null,
            TimeSpan? transferTimeout = null)
        {
            m_time = timeProvider ?? TimeProvider.System;
            m_timeout = transferTimeout ?? TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Registers a callback that produces the current parameter
        /// set for <paramref name="elementId"/>. Invoked once per
        /// <see cref="ITransferService.TransferFromDeviceAsync"/>
        /// call.
        /// </summary>
        public void RegisterExporter(
            NodeId elementId,
            Func<ISystemContext, CancellationToken, ValueTask<ParameterSet>> exporter)
        {
            if (elementId.IsNull) { throw new ArgumentNullException(nameof(elementId)); }
            if (exporter == null) { throw new ArgumentNullException(nameof(exporter)); }
            m_exporters[elementId] = exporter;
        }

        /// <summary>
        /// Registers a callback that applies a parameter set to
        /// <paramref name="elementId"/>. Receives the
        /// <see cref="ParameterSet"/> built by
        /// <see cref="ITransferService.TransferToDeviceAsync"/> and
        /// returns the per-entry status codes (must match the input
        /// <see cref="ParameterSet.Entries"/> count and order).
        /// </summary>
        public void RegisterImporter(
            NodeId elementId,
            Func<ISystemContext, ParameterSet, CancellationToken, ValueTask<StatusCode[]>> importer)
        {
            if (elementId.IsNull) { throw new ArgumentNullException(nameof(elementId)); }
            if (importer == null) { throw new ArgumentNullException(nameof(importer)); }
            m_importers[elementId] = importer;
        }

        /// <inheritdoc/>
        public async ValueTask<int> TransferToDeviceAsync(
            ISystemContext context,
            NodeId elementId,
            ParameterSet parameters,
            CancellationToken ct = default)
        {
            if (context == null) { throw new ArgumentNullException(nameof(context)); }
            if (elementId.IsNull) { throw new ArgumentNullException(nameof(elementId)); }
            if (parameters == null) { throw new ArgumentNullException(nameof(parameters)); }

            int transferId = Interlocked.Increment(ref m_nextTransferId);

            ParameterEntry[] resultEntries;
            if (m_importers.TryGetValue(elementId, out var importer))
            {
                StatusCode[] statuses;
                try
                {
                    statuses = await importer(context, parameters, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_transfers[transferId] = new TransferState(
                        elementId, Array.Empty<ParameterEntry>(),
                        new ServiceResult(ex).StatusCode,
                        m_time.GetUtcNow().UtcDateTime);
                    return transferId;
                }

                resultEntries = new ParameterEntry[parameters.Entries.Count];
                for (int i = 0; i < parameters.Entries.Count; i++)
                {
                    StatusCode status = i < statuses.Length
                        ? statuses[i]
                        : StatusCodes.Bad;
                    resultEntries[i] = parameters.Entries[i] with { StatusCode = status };
                }
            }
            else
            {
                resultEntries = Array.Empty<ParameterEntry>();
                m_transfers[transferId] = new TransferState(
                    elementId, resultEntries,
                    StatusCodes.BadNotSupported,
                    m_time.GetUtcNow().UtcDateTime);
                return transferId;
            }

            m_transfers[transferId] = new TransferState(
                elementId, resultEntries,
                StatusCodes.Good,
                m_time.GetUtcNow().UtcDateTime);
            return transferId;
        }

        /// <inheritdoc/>
        public async ValueTask<int> TransferFromDeviceAsync(
            ISystemContext context,
            NodeId elementId,
            CancellationToken ct = default)
        {
            if (context == null) { throw new ArgumentNullException(nameof(context)); }
            if (elementId.IsNull) { throw new ArgumentNullException(nameof(elementId)); }

            int transferId = Interlocked.Increment(ref m_nextTransferId);
            ParameterEntry[] entries;
            StatusCode error = StatusCodes.Good;

            if (m_exporters.TryGetValue(elementId, out var exporter))
            {
                try
                {
                    ParameterSet set = await exporter(context, ct).ConfigureAwait(false);
                    entries = new ParameterEntry[set.Entries.Count];
                    for (int i = 0; i < set.Entries.Count; i++)
                    {
                        entries[i] = set.Entries[i];
                    }
                }
                catch (Exception ex)
                {
                    entries = Array.Empty<ParameterEntry>();
                    error = new ServiceResult(ex).StatusCode;
                }
            }
            else
            {
                entries = Array.Empty<ParameterEntry>();
                error = StatusCodes.BadNotSupported;
            }

            m_transfers[transferId] = new TransferState(
                elementId, entries, error,
                m_time.GetUtcNow().UtcDateTime);
            return transferId;
        }

        /// <inheritdoc/>
        public ValueTask<FetchResult> FetchAsync(
            ISystemContext context,
            int transferId,
            int sequenceNumber,
            int maxResults,
            bool omitGoodResults,
            CancellationToken ct = default)
        {
            if (context == null) { throw new ArgumentNullException(nameof(context)); }
            if (sequenceNumber < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequenceNumber), sequenceNumber,
                    "Sequence number must be non-negative.");
            }
            if (maxResults < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxResults), maxResults,
                    "MaxResults must be non-negative (0 = unlimited).");
            }

            DiscardExpired();

            if (!m_transfers.TryGetValue(transferId, out TransferState? state))
            {
                return new ValueTask<FetchResult>(new FetchResult(
                    sequenceNumber,
                    EndOfResults: true,
                    Entries: Array.Empty<ParameterEntry>(),
                    TransferError: StatusCodes.BadNotFound));
            }

            if (StatusCode.IsBad(state.TransferError))
            {
                m_transfers.TryRemove(transferId, out _);
                return new ValueTask<FetchResult>(new FetchResult(
                    sequenceNumber,
                    EndOfResults: true,
                    Entries: Array.Empty<ParameterEntry>(),
                    TransferError: state.TransferError));
            }

            int offset = sequenceNumber;
            if (offset >= state.Entries.Length)
            {
                m_transfers.TryRemove(transferId, out _);
                return new ValueTask<FetchResult>(new FetchResult(
                    sequenceNumber,
                    EndOfResults: true,
                    Entries: Array.Empty<ParameterEntry>(),
                    TransferError: StatusCodes.Good));
            }

            int take = maxResults == 0
                ? state.Entries.Length - offset
                : Math.Min(maxResults, state.Entries.Length - offset);

            var chunk = new List<ParameterEntry>(take);
            for (int i = 0; i < take; i++)
            {
                ParameterEntry entry = state.Entries[offset + i];
                if (omitGoodResults && StatusCode.IsGood(entry.StatusCode))
                {
                    continue;
                }
                chunk.Add(entry);
            }

            int nextOffset = offset + take;
            bool endOfResults = nextOffset >= state.Entries.Length;
            if (endOfResults)
            {
                m_transfers.TryRemove(transferId, out _);
            }

            return new ValueTask<FetchResult>(new FetchResult(
                nextOffset,
                endOfResults,
                chunk.ToArray(),
                StatusCodes.Good));
        }

        private void DiscardExpired()
        {
            DateTime cutoff = m_time.GetUtcNow().UtcDateTime - m_timeout;
            foreach (KeyValuePair<int, TransferState> kvp in m_transfers)
            {
                if (kvp.Value.CreatedAt < cutoff)
                {
                    m_transfers.TryRemove(kvp.Key, out _);
                }
            }
        }

        private readonly TimeProvider m_time;
        private readonly TimeSpan m_timeout;
        private readonly ConcurrentDictionary<NodeId,
            Func<ISystemContext, CancellationToken, ValueTask<ParameterSet>>> m_exporters
            = new();
        private readonly ConcurrentDictionary<NodeId,
            Func<ISystemContext, ParameterSet, CancellationToken, ValueTask<StatusCode[]>>>
            m_importers = new();
        private readonly ConcurrentDictionary<int, TransferState> m_transfers = new();
        private int m_nextTransferId;

        private sealed record TransferState(
            NodeId ElementId,
            ParameterEntry[] Entries,
            StatusCode TransferError,
            DateTime CreatedAt);
    }
}
