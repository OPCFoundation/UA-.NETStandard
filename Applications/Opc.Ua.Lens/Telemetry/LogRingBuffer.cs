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
/// Lock-free ring buffer of log entries. Producers (any thread) call <see cref="Add"/>;
/// the UI thread snapshots via <see cref="Snapshot"/> at frame boundaries.
/// </summary>
internal sealed class LogRingBuffer
{
    private readonly LogEntry[] m_buffer;
    private long m_writeIndex;
    private readonly Lock m_snapshotLock = new();

    public LogRingBuffer(int capacity = 512)
    {
        if (capacity < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        m_buffer = new LogEntry[capacity];
    }

    public int Capacity => m_buffer.Length;

    public long TotalWritten => Interlocked.Read(ref m_writeIndex);

    public void Add(in LogEntry entry)
    {
        long idx = Interlocked.Increment(ref m_writeIndex) - 1;
        m_buffer[idx % m_buffer.Length] = entry;
    }

    /// <summary>
    /// Copy the most recent <paramref name="maxCount"/> entries (oldest-first)
    /// into <paramref name="destination"/>. Returns the number of entries written.
    /// </summary>
    public int Snapshot(Span<LogEntry> destination, int maxCount = int.MaxValue)
    {
        lock (m_snapshotLock)
        {
            long total = Interlocked.Read(ref m_writeIndex);
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
}
