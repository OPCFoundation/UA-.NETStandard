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
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Bounded simulated storage for one peripheral dataset, not a connection to physical equipment.
    /// Reports copy rather than consume records. Capacity never silently evicts a retained acquisition.
    /// Optional retention uses receipt time, never rewrites acquisition time, and is disabled by default.
    /// </summary>
    public sealed class InMemoryWmtpRecordStore : IWmtpRecordStore, IAsyncDisposable
    {
        /// <summary>
        /// Creates an isolated record store with explicit capacity, report budget and optional receipt-time retention.
        /// </summary>
        public InMemoryWmtpRecordStore(
            ITelemetryContext telemetry,
            int capacity = 1024,
            int maximumReportBytes = 1024 * 1024,
            TimeProvider? timeProvider = null,
            TimeSpan? retention = null)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (capacity is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            if (maximumReportBytes is < 256 or > 64 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumReportBytes));
            }
            if (retention.HasValue && retention.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(retention));
            }
            m_capacity = capacity;
            m_maximumReportBytes = maximumReportBytes;
            m_clock = timeProvider ?? TimeProvider.System;
            m_retention = retention;
            m_messageContext = ServiceMessageContext.Create(telemetry);
            m_messageContext.Factory.Builder.AddMachineToolTypeSystem(MachineToolParts.Wmtp).Commit();
            m_messageContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.MachineTool.WMTP.Namespaces.WMTP);
        }

        /// <summary>
        /// Retains an independent copy of an actual record, or refuses the addition without changing the dataset.
        /// </summary>
        public ValueTask AppendAsync(WMTPOutputDataType record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WmtpValidation.Record(record);
            var copy = (WMTPOutputDataType)record.Clone();
            lock (m_gate)
            {
                EnsureActive();
                cancellationToken.ThrowIfCancellationRequested();
                Expire();
                if (m_records.Count == m_capacity)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable, "The record store is full.");
                }
                m_records.Add(new Entry(copy, m_clock.GetUtcNow()));
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<uint> CountAsync(WmtpRecordQuery query, CancellationToken cancellationToken = default)
        {
            WmtpValidation.Query(query);
            lock (m_gate)
            {
                EnsureActive();
                cancellationToken.ThrowIfCancellationRequested();
                Expire();
                uint count = 0;
                for (int index = 0; index < m_records.Count; index++)
                {
                    if (Matches(query, index))
                    {
                        count++;
                    }
                }
                return new ValueTask<uint>(count);
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WMTPOutputDataType>> ReadAsync(
            WmtpRecordQuery query, CancellationToken cancellationToken = default)
        {
            WmtpValidation.Query(query);
            var records = new List<WMTPOutputDataType>();
            lock (m_gate)
            {
                EnsureActive();
                cancellationToken.ThrowIfCancellationRequested();
                Expire();
                for (int index = 0; index < m_records.Count; index++)
                {
                    if (Matches(query, index))
                    {
                        records.Add((WMTPOutputDataType)m_records[index].Record.Clone());
                    }
                }
            }
            using (var encoder = new BinaryEncoder(m_messageContext))
            {
                encoder.WriteInt32(null, records.Count);
                foreach (WMTPOutputDataType record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    encoder.WriteExtensionObject(null, new ExtensionObject(record));
                    if (encoder.Position > m_maximumReportBytes)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The complete report exceeds the configured budget; use an explicit index or time range.");
                    }
                }
            }
            return new ValueTask<ArrayOf<WMTPOutputDataType>>(records.ToArrayOf());
        }

        /// <inheritdoc/>
        public ValueTask DeleteAsync(WmtpRecordQuery query, CancellationToken cancellationToken = default)
        {
            WmtpValidation.Query(query);
            lock (m_gate)
            {
                EnsureActive();
                cancellationToken.ThrowIfCancellationRequested();
                Expire();
                var retained = new List<Entry>(m_records.Count);
                for (int index = 0; index < m_records.Count; index++)
                {
                    if (!Matches(query, index))
                    {
                        retained.Add(m_records[index]);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                m_records = retained;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_disposed = true;
                m_records.Clear();
            }
            return default;
        }

        /// <summary>
        /// Applies inclusive bounds to storage-order records.
        /// </summary>
        private bool Matches(WmtpRecordQuery query, int index)
        {
            WMTPOutputDataType record = m_records[index].Record;
            return query.Selection switch
            {
                WmtpRecordSelection.Index => record.Index >= query.FromIndex && record.Index <= query.ToIndex,
                WmtpRecordSelection.Time => record.Timestamp >= query.FromTime && record.Timestamp <= query.ToTime,
                WmtpRecordSelection.First => index == 0,
                WmtpRecordSelection.Last => index == m_records.Count - 1,
                _ => true
            };
        }

        /// <summary>
        /// Prunes only records whose explicitly configured receipt-time retention has expired.
        /// </summary>
        private void Expire()
        {
            if (m_retention.HasValue)
            {
                DateTimeOffset boundary = m_clock.GetUtcNow() - m_retention.Value;
                m_records.RemoveAll(entry => entry.Received <= boundary);
            }
        }

        /// <summary>
        /// Prevents use after asynchronous lifetime cleanup.
        /// </summary>
        private void EnsureActive()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(InMemoryWmtpRecordStore));
            }
        }

        /// <summary>
        /// Keeps retention time separate from the immutable acquisition snapshot.
        /// </summary>
        private sealed record Entry(WMTPOutputDataType Record, DateTimeOffset Received);

        /// <summary>The short storage mutation gate; no callbacks are invoked while held.</summary>
        private readonly Lock m_gate = new();

        /// <summary>The bounded number of retained records.</summary>
        private readonly int m_capacity;

        /// <summary>The complete report encoding budget.</summary>
        private readonly int m_maximumReportBytes;

        /// <summary>The injected receipt clock.</summary>
        private readonly TimeProvider m_clock;

        /// <summary>The explicitly selected retention interval.</summary>
        private readonly TimeSpan? m_retention;

        /// <summary>The registered encoding context.</summary>
        private readonly ServiceMessageContext m_messageContext;

        /// <summary>The per-dataset records in storage order.</summary>
        private List<Entry> m_records = [];

        /// <summary>Whether storage is closed.</summary>
        private bool m_disposed;
    }
}
