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
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Opc.Ua;

namespace UaLens.Plugins.Historian;

/// <summary>
/// One row of historical data returned from a HistoryRead.  Pre-formats
/// the value as <see cref="DisplayValue"/> so the DataGrid binding stays
/// trivial and AOT-friendly (no value converters needed).
/// </summary>
internal sealed partial class HistoryRow : ObservableObject
{
    public DateTime SourceTimestamp { get; }
    public DateTime ServerTimestamp { get; }
    public Variant Value { get; }
    public StatusCode StatusCode { get; }
    public string DisplayValue { get; }

    /// <summary>
    /// Formatted timestamp string (UTC, ISO-8601 round-trip, millisecond
    /// precision) — used by the DataGrid binding and the CSV export.
    /// </summary>
    public string DisplayTimestamp { get; }

    /// <summary>Formatted status code (e.g. "Good", "BadOutOfRange").</summary>
    public string DisplayStatus { get; }

    /// <summary>
    /// True when <see cref="Value"/> coerces to a finite IEEE 754 double
    /// — those rows are charted by <see cref="HistorianPlugin"/>.
    /// </summary>
    public bool IsNumeric { get; }

    /// <summary>Best-effort numeric coercion of <see cref="Value"/> (NaN otherwise).</summary>
    public double Numeric { get; }

    /// <summary>
    /// Optional <see cref="Opc.Ua.Annotation"/> attached to this row's
    /// <see cref="SourceTimestamp"/> — populated post-read by
    /// <see cref="HistoryReader.AttachAnnotationsAsync"/> when the
    /// historizing variable exposes the standard <c>Annotations</c>
    /// property (Part 11 §5.4.5).  <c>null</c> if the server does not
    /// support annotations on this node.
    /// </summary>
    public Annotation? Annotation
    {
        get => m_annotation;
        set
        {
            if (!ReferenceEquals(m_annotation, value))
            {
                m_annotation = value;
                OnPropertyChanged(nameof(Annotation));
                OnPropertyChanged(nameof(DisplayAnnotation));
            }
        }
    }
    private Annotation? m_annotation;

    /// <summary>
    /// Single-line projection of <see cref="Annotation"/> for the
    /// Annotation column in the results grid — empty when no annotation
    /// has been attached.
    /// </summary>
    public string DisplayAnnotation
    {
        get
        {
            if (m_annotation is null)
            {
                return string.Empty;
            }
            string msg = m_annotation.Message ?? string.Empty;
            string user = m_annotation.UserName ?? string.Empty;
            return user.Length == 0 ? msg : $"{msg}  —  {user}";
        }
    }

    public HistoryRow(DateTime sourceTimestamp,
                      DateTime serverTimestamp,
                      Variant value,
                      StatusCode statusCode)
    {
        SourceTimestamp = sourceTimestamp;
        ServerTimestamp = serverTimestamp;
        Value = value;
        StatusCode = statusCode;
        DisplayValue = FormatValue(value);
        DisplayTimestamp = sourceTimestamp
            .ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        DisplayStatus = StatusCode.LookupSymbolicId(statusCode.Code) is { Length: > 0 } sym
            ? sym
            : $"0x{statusCode.Code:X8}";
        (IsNumeric, Numeric) = TryGetDouble(value);
    }

    private static string FormatValue(Variant v)
    {
        if (v.IsNull)
        {
            return string.Empty;
        }
        object? boxed = v.Value;
        return boxed switch
        {
            null => string.Empty,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => boxed.ToString() ?? string.Empty
        };
    }

    private static (bool, double) TryGetDouble(Variant v)
    {
        if (v.IsNull)
        {
            return (false, double.NaN);
        }
        try
        {
            double d = Convert.ToDouble(v.Value, CultureInfo.InvariantCulture);
            return double.IsFinite(d) ? (true, d) : (false, double.NaN);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return (false, double.NaN);
        }
    }
}
