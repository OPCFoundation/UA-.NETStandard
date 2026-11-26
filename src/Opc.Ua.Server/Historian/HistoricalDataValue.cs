// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A raw historical sample returned from an <see cref="IHistorianDataProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Value"/> is the underlying <see cref="DataValue"/> with its
    /// <c>SourceTimestamp</c> set to the storage key (Part 11 §5.2.4 — values
    /// are addressed by <c>SourceTimestamp</c>). The framework reconciles
    /// <c>ServerTimestamp</c>, <c>TimestampsToReturn</c>, index ranges and
    /// data encodings before forwarding the value to the client.
    /// </para>
    /// <para>
    /// <see cref="IsBound"/> indicates that this sample is the closest
    /// matching sample outside the requested time window (the "before" /
    /// "after" bound). The framework only emits a bound when the request
    /// specified <c>ReturnBounds=true</c>; providers should always supply
    /// them when available so the framework can decide whether to keep them.
    /// </para>
    /// </remarks>
    public readonly record struct HistoricalDataValue(DataValue Value, bool IsBound = false);
}
