/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;
using Opc.Ua;

namespace UaLens.Plugins.Historian;

/// <summary>
/// One row of historical data returned from a HistoryRead.  Pre-formats
/// the value as <see cref="DisplayValue"/> so the DataGrid binding stays
/// trivial and AOT-friendly (no value converters needed).
/// </summary>
internal sealed partial class HistoryRow
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
