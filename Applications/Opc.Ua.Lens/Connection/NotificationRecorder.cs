/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
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
using UaLens.Subscriptions;

namespace UaLens.Connection;

/// <summary>
/// Per-tab rolling buffer of <see cref="NotificationEvent"/> records.
/// Wraps a <see cref="ChannelReader{T}"/> that the renderer also drains
/// — the recorder taps the same notifications via a background pump and
/// keeps a bounded history that the user can export to CSV or JSON.
/// </summary>
/// <remarks>
/// The recorder is NOT a second consumer of the channel (that would
/// race the renderer).  Instead the renderer calls <see cref="Record"/>
/// directly from its drain loop.  This keeps a single consumer and
/// guarantees the recording matches what was rendered.
/// </remarks>
internal sealed class NotificationRecorder
{
    private const int kCapacity = 50_000;

    private readonly object m_lock = new();
    private readonly Queue<NotificationEvent> m_buffer = new(kCapacity);

    /// <summary>Append an event to the buffer; drops the oldest at the cap.</summary>
    public void Record(in NotificationEvent ev)
    {
        lock (m_lock)
        {
            if (m_buffer.Count >= kCapacity)
            {
                m_buffer.Dequeue();
            }
            m_buffer.Enqueue(ev);
        }
    }

    /// <summary>Snapshot the buffer for export.  Thread-safe.</summary>
    public NotificationEvent[] Snapshot()
    {
        lock (m_lock)
        {
            return m_buffer.ToArray();
        }
    }

    /// <summary>Discard all recorded events.</summary>
    public void Clear()
    {
        lock (m_lock)
        {
            m_buffer.Clear();
        }
    }

    /// <summary>Write the buffer to <paramref name="path"/> as CSV.</summary>
    public async Task ExportCsvAsync(string path, IReadOnlyDictionary<int, string>? displayNames = null, CancellationToken ct = default)
    {
        NotificationEvent[] snap = Snapshot();
        var sw = new StreamWriter(path, append: false, Encoding.UTF8);
        await using (sw.ConfigureAwait(false))
        {
            await sw.WriteLineAsync("ReceivedAtUtc,Kind,ItemId,DisplayName,SequenceNumber,ValueCount,Value").ConfigureAwait(false);
            foreach (NotificationEvent ev in snap)
            {
                ct.ThrowIfCancellationRequested();
                string name = displayNames is not null && displayNames.TryGetValue(ev.ItemId, out string? n) ? n : "";
                string value = ev.Value.HasValue
                    ? ev.Value.Value.ToString("R", CultureInfo.InvariantCulture)
                    : "";
                await sw.WriteAsync(ev.ReceivedAtUtc.ToString("o", CultureInfo.InvariantCulture)).ConfigureAwait(false);
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

    /// <summary>Write the buffer to <paramref name="path"/> as a JSON array.</summary>
    public async Task ExportJsonAsync(string path, IReadOnlyDictionary<int, string>? displayNames = null, CancellationToken ct = default)
    {
        NotificationEvent[] snap = Snapshot();
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
                    writer.WriteString("receivedAtUtc", ev.ReceivedAtUtc.ToString("o", CultureInfo.InvariantCulture));
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

    private static readonly System.Buffers.SearchValues<char> s_csvQuoteChars =
        System.Buffers.SearchValues.Create(",\"\n\r");

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
}
