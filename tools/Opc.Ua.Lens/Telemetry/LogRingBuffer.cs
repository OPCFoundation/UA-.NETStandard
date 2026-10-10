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
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Telemetry;

/// <summary>
/// One log line.
/// </summary>
internal readonly record struct LogEntry(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    string Message);

/// <summary>
/// Bounded log storage with atomic publication and consumer cursor advancement.
/// </summary>
internal sealed class LogRingBuffer
{
    public LogRingBuffer(int capacity = 512)
    {
        if (capacity < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        m_buffer = new LogEntry[capacity];
    }

    public int Capacity => m_buffer.Length;

    public long TotalWritten
    {
        get
        {
            lock (m_snapshotLock)
            {
                return m_writeIndex;
            }
        }
    }

    public void Add(in LogEntry entry)
    {
        lock (m_snapshotLock)
        {
            m_buffer[m_writeIndex % m_buffer.Length] = entry;
            m_writeIndex++;
        }
    }

    /// <summary>
    /// Reads retained entries after a committed cursor, reporting overwritten entries
    /// separately. The returned cursor describes exactly this snapshot.
    /// </summary>
    public LogSnapshot ReadSince(long cursor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cursor);
        lock (m_snapshotLock)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cursor, m_writeIndex);
            long firstRetained = Math.Max(0, m_writeIndex - m_buffer.Length);
            long start = Math.Max(cursor, firstRetained);
            var entries = new LogEntry[(int)(m_writeIndex - start)];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = m_buffer[(start + i) % m_buffer.Length];
            }
            return new LogSnapshot(entries, m_writeIndex, Math.Max(0, firstRetained - cursor));
        }
    }

    /// <summary>
    /// Copy the most recent <paramref name="maxCount"/> entries (oldest-first)
    /// into <paramref name="destination"/>. Returns the number of entries written.
    /// </summary>
    public int Snapshot(Span<LogEntry> destination, int maxCount = int.MaxValue)
    {
        lock (m_snapshotLock)
        {
            long total = m_writeIndex;
            int count = (int)Math.Min(total, m_buffer.Length);
            count = Math.Min(count, Math.Min(destination.Length, maxCount));

            long start = Math.Max(0, total - count);
            for (int i = 0; i < count; i++)
            {
                destination[i] = m_buffer[(start + i) % m_buffer.Length];
            }
            return count;
        }
    }

    public List<LogEntry> SnapshotList(int maxCount = int.MaxValue)
    {
        var arr = new LogEntry[Math.Min(m_buffer.Length, maxCount)];
        int n = Snapshot(arr.AsSpan(), maxCount);
        var list = new List<LogEntry>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(arr[i]);
        }
        return list;
    }

    private readonly LogEntry[] m_buffer;
    private long m_writeIndex;
    private readonly Lock m_snapshotLock = new();
}

internal readonly record struct LogSnapshot(ArrayOf<LogEntry> Entries, long Cursor, long Overwritten);
