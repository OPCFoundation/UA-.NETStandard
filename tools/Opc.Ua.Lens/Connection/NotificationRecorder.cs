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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens.Connection
{
    /// <summary>
    /// Bounded document history with independent playback cursors for charts and exports.
    /// </summary>
    /// <remarks>
    /// The document owns one <see cref="CaptureAsync"/> task for its adapter.
    /// Renderers read independent cursors, never the adapter's competing-consumer channel.
    /// </remarks>
    internal sealed class NotificationRecorder
    {
        public NotificationRecorder(int capacity = 50_000)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            m_buffer = new NotificationEvent[capacity];
        }

        public long TotalWritten
        {
            get
            {
                lock (m_lock)
                {
                    return m_written;
                }
            }
        }

        public long HistoryDiscarded
        {
            get
            {
                lock (m_lock)
                {
                    return m_discarded;
                }
            }
        }

        /// <summary>
        /// Captures the source independently of whether any document view is attached.
        /// The caller owns cancellation and must await this task before replacing the source.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task CaptureAsync(
            ChannelReader<NotificationEvent> source,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (Interlocked.CompareExchange(ref m_capturing, 1, 0) != 0)
            {
                throw new InvalidOperationException("A notification recorder already has a source.");
            }
            try
            {
                await foreach (NotificationEvent notification in source.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    Record(in notification);
                }
            }
            finally
            {
                Volatile.Write(ref m_capturing, 0);
            }
        }

        /// <summary>
        /// Creates an independent cursor starting at the oldest retained notification.
        /// Recreating a chart therefore replays history instead of resetting collected data.
        /// </summary>
        public ChannelReader<NotificationEvent> CreateReader()
        {
            lock (m_lock)
            {
                return new HistoryReader(this, m_written - m_count);
            }
        }

        /// <summary>
        /// Appends a notification, retaining only the configured history window.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Record(in NotificationEvent ev)
        {
            TaskCompletionSource<bool>? available;
            lock (m_lock)
            {
                if (m_completed)
                {
                    throw new InvalidOperationException("The notification recorder is closed.");
                }
                if (m_count == m_buffer.Length)
                {
                    m_discarded++;
                }
                else
                {
                    m_count++;
                }
                m_buffer[(int)(m_written % m_buffer.Length)] = ev;
                m_written++;
                available = m_available;
                m_available = null;
            }
            available?.TrySetResult(true);
        }

        /// <summary>
        /// Takes an ordered snapshot for export.
        /// </summary>
        public ArrayOf<NotificationEvent> Snapshot()
        {
            lock (m_lock)
            {
                var result = new NotificationEvent[m_count];
                long first = m_written - m_count;
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = m_buffer[(int)((first + i) % m_buffer.Length)];
                }
                return result;
            }
        }

        /// <summary>
        /// Explicitly clears retained history without rewinding cursor positions or delivery totals.
        /// </summary>
        public void Clear()
        {
            lock (m_lock)
            {
                m_count = 0;
                Array.Clear(m_buffer);
            }
        }

        /// <summary>
        /// Completes playback after the document has stopped and awaited its capture task.
        /// </summary>
        public void Complete()
        {
            TaskCompletionSource<bool>? available;
            lock (m_lock)
            {
                m_completed = true;
                available = m_available;
                m_available = null;
            }
            available?.TrySetResult(false);
            m_completion.TrySetResult();
        }

        /// <summary>
        /// Writes the retained buffer to <paramref name="path"/> as CSV.
        /// </summary>
        public async Task ExportCsvAsync(
            string path,
            IReadOnlyDictionary<int, string>? displayNames = null,
            CancellationToken ct = default)
        {
            ArrayOf<NotificationEvent> snap = Snapshot();
            var sw = new StreamWriter(path, append: false, Encoding.UTF8);
            await using (sw.ConfigureAwait(false))
            {
                await sw.WriteLineAsync("ReceivedAtUtc,Kind,ItemId,DisplayName,SequenceNumber,ValueCount,Value")
                    .ConfigureAwait(false);
                for (int i = 0; i < snap.Count; i++)
                {
                    NotificationEvent ev = snap[i];
                    ct.ThrowIfCancellationRequested();
                    string name = displayNames is not null && displayNames.TryGetValue(ev.ItemId, out string? n)
                        ? n : string.Empty;
                    string value = ev.Value.HasValue
                        ? ev.Value.Value.ToString("R", CultureInfo.InvariantCulture)
                        : string.Empty;
                    await sw.WriteAsync(ev.ReceivedAtUtc.ToString("o", CultureInfo.InvariantCulture))
                        .ConfigureAwait(false);
                    await sw.WriteAsync(",").ConfigureAwait(false);
                    await sw.WriteAsync(ev.Kind.ToString()).ConfigureAwait(false);
                    await sw.WriteAsync(",").ConfigureAwait(false);
                    await sw.WriteAsync(ev.ItemId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    await sw.WriteAsync(",").ConfigureAwait(false);
                    await sw.WriteAsync(CsvEscape(name)).ConfigureAwait(false);
                    await sw.WriteAsync(",").ConfigureAwait(false);
                    await sw.WriteAsync(ev.SequenceNumber.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    await sw.WriteAsync(",").ConfigureAwait(false);
                    await sw.WriteAsync(ev.ValueCount.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
                    await sw.WriteAsync(",").ConfigureAwait(false);
                    await sw.WriteLineAsync(value).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Writes the retained buffer to <paramref name="path"/> as JSON.
        /// </summary>
        public async Task ExportJsonAsync(
            string path,
            IReadOnlyDictionary<int, string>? displayNames = null,
            CancellationToken ct = default)
        {
            ArrayOf<NotificationEvent> snap = Snapshot();
            var fs = new FileStream(path, FileMode.Create);
            await using (fs.ConfigureAwait(false))
            {
                var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
                await using (writer.ConfigureAwait(false))
                {
                    writer.WriteStartArray();
                    foreach (NotificationEvent ev in snap)
                    {
                        ct.ThrowIfCancellationRequested();
                        writer.WriteStartObject();
                        writer.WriteString(
                            "receivedAtUtc", ev.ReceivedAtUtc.ToString("o", CultureInfo.InvariantCulture));
                        writer.WriteString("kind", ev.Kind.ToString());
                        writer.WriteNumber("itemId", ev.ItemId);
                        if (displayNames is not null && displayNames.TryGetValue(ev.ItemId, out string? n))
                        {
                            writer.WriteString("displayName", n);
                        }
                        writer.WriteNumber("sequenceNumber", ev.SequenceNumber);
                        writer.WriteNumber("valueCount", ev.ValueCount);
                        if (ev.Value.HasValue)
                        {
                            writer.WriteNumber("value", ev.Value.Value);
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    await writer.FlushAsync(ct).ConfigureAwait(false);
                }
            }
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            if (s.AsSpan().IndexOfAny(s_csvQuoteChars) < 0)
            {
                return s;
            }

            return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        private bool TryRead(ref long position, out NotificationEvent notification)
        {
            lock (m_lock)
            {
                position = Math.Max(position, m_written - m_count);
                if (position >= m_written)
                {
                    notification = default;
                    return false;
                }
                notification = m_buffer[(int)(position % m_buffer.Length)];
                position++;
                return true;
            }
        }

        private ValueTask<bool> WaitToReadAsync(long position, CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                if (position < m_written && m_count > 0)
                {
                    return ValueTask.FromResult(true);
                }
                if (m_completed)
                {
                    return ValueTask.FromResult(false);
                }
                m_available ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return new ValueTask<bool>(m_available.Task.WaitAsync(cancellationToken));
            }
        }

        private bool IsDrained(long position)
        {
            lock (m_lock)
            {
                return m_completed && Math.Max(position, m_written - m_count) >= m_written;
            }
        }

        private sealed class HistoryReader(NotificationRecorder owner, long position) : ChannelReader<NotificationEvent>
        {
            public override Task Completion => m_completion ??= WaitForCompletionAsync();

            public override bool TryRead(out NotificationEvent item)
            {
                bool read = owner.TryRead(ref m_position, out item);
                if (owner.IsDrained(m_position))
                {
                    m_drained.TrySetResult();
                }
                return read;
            }

            public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            {
                return owner.WaitToReadAsync(m_position, cancellationToken);
            }

            private async Task WaitForCompletionAsync()
            {
                await owner.m_completion.Task.ConfigureAwait(false);
                if (!owner.IsDrained(m_position))
                {
                    await m_drained.Task.ConfigureAwait(false);
                }
            }

            private readonly TaskCompletionSource m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private Task? m_completion;
            private long m_position = position;
        }

        private static readonly System.Buffers.SearchValues<char> s_csvQuoteChars =
            System.Buffers.SearchValues.Create(",\"\n\r");

        private readonly Lock m_lock = new();
        private readonly NotificationEvent[] m_buffer;
        private readonly TaskCompletionSource m_completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool>? m_available;
        private long m_written;
        private long m_discarded;
        private int m_count;
        private int m_capturing;
        private bool m_completed;
    }
}
