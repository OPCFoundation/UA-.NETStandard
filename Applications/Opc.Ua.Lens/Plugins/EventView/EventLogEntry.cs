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
using Avalonia.Media;

namespace UaLens.Plugins.EventView;

/// <summary>
/// One row in the details TreeView/pane for the selected event.  Wraps a
/// single SelectClause result so compiled bindings can address
/// <see cref="Path"/> and <see cref="Display"/> directly (tuples are
/// awkward to bind to).
/// </summary>
internal sealed record EventFieldDisplay(string Path, string Display);

/// <summary>
/// One row in the Event View log.  Captures the rendered summary fields
/// (Time / Severity / SourceName / EventType / Message) plus the full
/// list of decoded SelectClause values keyed by BrowsePath so the
/// details pane can render a property tree for the selected row.
/// </summary>
/// <param name="Time">Server-supplied event time (UTC); falls back to receive-time.</param>
/// <param name="Severity">BaseEventType <c>Severity</c> (0-1000, 0 when missing).</param>
/// <param name="SourceName">BaseEventType <c>SourceName</c> ("" when missing).</param>
/// <param name="EventType">Resolved short name for the <c>EventType</c> NodeId.</param>
/// <param name="Message">BaseEventType <c>Message</c> (LocalizedText.Text, "" when missing).</param>
/// <param name="RawFields">
/// All decoded SelectClause results: BrowsePath ("/Severity", "/Time", …) ↔ value
/// (a primitive, <see cref="Opc.Ua.LocalizedText"/>, <see cref="Opc.Ua.NodeId"/>,
/// etc., or <c>null</c> when the clause returned no value).
/// </param>
internal sealed record EventLogEntry(
    DateTime Time,
    ushort Severity,
    string SourceName,
    string EventType,
    string Message,
    IReadOnlyList<(string BrowsePath, object? Value)> RawFields)
{
    /// <summary>
    /// Formatted timestamp ("HH:mm:ss.fff", UTC) used directly by the
    /// compiled binding on the log ListBox so no value converter is
    /// needed (keeps the AXAML AOT-clean).
    /// </summary>
    public string DisplayTime
        => Time.ToUniversalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Severity-to-color mapping per the spec:
    /// &lt;200 gray, &lt;400 blue, &lt;600 amber, &lt;800 red, ≥800 purple.
    /// Exposed as an <see cref="IBrush"/> so the row TextBlock
    /// <c>Foreground</c> can bind directly without a converter.
    /// </summary>
    public IBrush SeverityBrush => SeverityToBrush(Severity);

    /// <summary>
    /// Bind-friendly projection of <see cref="RawFields"/> for the
    /// details TreeView — each item exposes <c>Path</c> and
    /// <c>Display</c> as plain strings.
    /// </summary>
    public IReadOnlyList<EventFieldDisplay> FieldRows
    {
        get
        {
            var list = new List<EventFieldDisplay>(RawFields.Count);
            foreach ((string path, object? value) in RawFields)
            {
                list.Add(new EventFieldDisplay(path, FormatValue(value)));
            }
            return list;
        }
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }
        return value switch
        {
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static IBrush SeverityToBrush(ushort severity)
    {
        if (severity < 200)
        {
            return s_gray;
        }

        if (severity < 400)
        {
            return s_blue;
        }

        if (severity < 600)
        {
            return s_amber;
        }

        if (severity < 800)
        {
            return s_red;
        }

        return s_purple;
    }

    private static readonly IBrush s_gray = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    private static readonly IBrush s_blue = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
    private static readonly IBrush s_amber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly IBrush s_red = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
    private static readonly IBrush s_purple = new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC));
}
