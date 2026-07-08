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
using System.Threading;

namespace Opc.Ua.PubSub.Diagnostics
{
    /// <summary>
    /// Default in-memory implementation of <see cref="IPubSubDiagnostics"/>.
    /// One instance per component state machine. Counters use
    /// <see cref="Interlocked"/> to stay lock-free on the hot path; error
    /// history is gated by an internal <see cref="Lock"/> to keep the
    /// ring buffer consistent.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11">
    /// Part 14 §9.1.11 PubSubDiagnosticsType</see>. The error ring buffer
    /// has a fixed capacity of <see cref="ErrorHistoryCapacity"/> entries;
    /// older entries are overwritten in FIFO order so the counter is
    /// bounded regardless of error rate.
    /// </remarks>
    public sealed class PubSubDiagnostics : IPubSubDiagnostics
    {
        /// <summary>
        /// Capacity of the recent-error ring buffer at
        /// <see cref="PubSubDiagnosticsLevel.High"/>.
        /// </summary>
        public const int ErrorHistoryCapacity = 32;

#if NET5_0_OR_GREATER
        private static readonly int s_counterCount =
            Enum.GetValues<PubSubDiagnosticsCounterKind>().Length;
#else
        private static readonly int s_counterCount =
            Enum.GetValues(typeof(PubSubDiagnosticsCounterKind)).Length;
#endif

        private readonly Lock m_lock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly long[] m_counters;
        private readonly PubSubErrorEntry[]? m_errorHistory;
        private int m_errorHistoryHead;
        private int m_errorHistoryCount;
        private PubSubErrorEntry m_lastError;

        /// <summary>
        /// Initializes a new <see cref="PubSubDiagnostics"/> instance at
        /// the requested verbosity tier.
        /// </summary>
        /// <param name="level">
        /// Verbosity tier. Determines whether <see cref="RecordError"/>
        /// retains data and whether a history ring buffer is allocated.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used to stamp error entries. Defaults to
        /// <see cref="TimeProvider.System"/> when <see langword="null"/>.
        /// </param>
        public PubSubDiagnostics(
            PubSubDiagnosticsLevel level,
            TimeProvider? timeProvider = null)
        {
            Level = level;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_counters = new long[s_counterCount];
            m_errorHistory = level == PubSubDiagnosticsLevel.High
                ? new PubSubErrorEntry[ErrorHistoryCapacity]
                : null;
        }

        /// <inheritdoc/>
        public PubSubDiagnosticsLevel Level { get; }

        /// <summary>
        /// Snapshot of the most recent errors recorded at
        /// <see cref="PubSubDiagnosticsLevel.High"/>, newest-first. The
        /// snapshot is independent of subsequent
        /// <see cref="RecordError"/> calls. At lower verbosity tiers the
        /// list is empty.
        /// </summary>
        public ArrayOf<PubSubErrorEntry> RecentErrors
        {
            get
            {
                if (m_errorHistory == null)
                {
                    return ArrayOf<PubSubErrorEntry>.Empty;
                }
                lock (m_lock)
                {
                    int count = m_errorHistoryCount;
                    if (count == 0)
                    {
                        return ArrayOf<PubSubErrorEntry>.Empty;
                    }
                    var snapshot = new PubSubErrorEntry[count];
                    int head = m_errorHistoryHead;
                    for (int i = 0; i < count; i++)
                    {
                        int idx = (head - 1 - i + ErrorHistoryCapacity) % ErrorHistoryCapacity;
                        snapshot[i] = m_errorHistory[idx];
                    }
                    return snapshot;
                }
            }
        }

        /// <inheritdoc/>
        public void Increment(PubSubDiagnosticsCounterKind kind, long delta = 1)
        {
            if (delta < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delta),
                    "Diagnostics counters are monotonic; delta must be non-negative.");
            }
            if (delta == 0)
            {
                return;
            }
            int index = (int)kind;
            if ((uint)index >= (uint)m_counters.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            _ = Interlocked.Add(ref m_counters[index], delta);
        }

        /// <inheritdoc/>
        public long Read(PubSubDiagnosticsCounterKind kind)
        {
            int index = (int)kind;
            if ((uint)index >= (uint)m_counters.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return Interlocked.Read(ref m_counters[index]);
        }

        /// <inheritdoc/>
        public void RecordError(StatusCode statusCode, string message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (Level == PubSubDiagnosticsLevel.Low)
            {
                return;
            }
            var entry = new PubSubErrorEntry(
                new DateTimeUtc(m_timeProvider.GetUtcNow().UtcDateTime),
                statusCode,
                message);
            lock (m_lock)
            {
                m_lastError = entry;
                if (m_errorHistory != null)
                {
                    m_errorHistory[m_errorHistoryHead] = entry;
                    m_errorHistoryHead = (m_errorHistoryHead + 1) % ErrorHistoryCapacity;
                    if (m_errorHistoryCount < ErrorHistoryCapacity)
                    {
                        m_errorHistoryCount++;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            for (int i = 0; i < m_counters.Length; i++)
            {
                Interlocked.Exchange(ref m_counters[i], 0);
            }
            lock (m_lock)
            {
                m_lastError = default;
                if (m_errorHistory != null)
                {
                    Array.Clear(m_errorHistory, 0, m_errorHistory.Length);
                    m_errorHistoryHead = 0;
                    m_errorHistoryCount = 0;
                }
            }
        }

        /// <summary>
        /// The most recent error reported via <see cref="RecordError"/>, or
        /// <see langword="null"/> when none has been recorded at the current
        /// verbosity tier.
        /// </summary>
        public PubSubErrorEntry? LastError
        {
            get
            {
                if (Level == PubSubDiagnosticsLevel.Low)
                {
                    return null;
                }
                lock (m_lock)
                {
                    if (m_lastError.Message == null)
                    {
                        return null;
                    }
                    return m_lastError;
                }
            }
        }
    }
}
