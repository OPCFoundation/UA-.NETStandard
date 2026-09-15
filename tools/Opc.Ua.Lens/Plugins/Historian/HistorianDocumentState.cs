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
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;

namespace UaLens.Plugins.Historian;

/// <summary>
/// A validated, non-secret snapshot of a Historian tab's configuration. It holds
/// the target, read mode, time range and history-update field intent only; it never
/// carries the read results, a live session, or an in-flight operation.
/// </summary>
internal sealed record HistorianStateSnapshot(
    string Title,
    NodeId TargetNodeId,
    string TargetDisplayName,
    HistorianReadMode ReadMode,
    bool ReturnBounds,
    bool ReadModified,
    uint NumValuesPerNode,
    NodeId AggregateNodeId,
    double ProcessingIntervalMs,
    ArrayOf<DateTime> AtTimes,
    DateTime CustomStart,
    DateTime CustomEnd,
    HistorianUpdateOp UpdateOp,
    DateTime UpdateTimestamp,
    string UpdateValueText,
    DateTime UpdateStart,
    DateTime UpdateEnd,
    ArrayOf<DateTime> UpdateAtTimes);

/// <summary>
/// Versioned, source-generated JSON envelope for <see cref="HistorianStateSnapshot"/>.
/// </summary>
internal sealed class HistorianStateDto
{
    public int Version { get; set; } = HistorianStateCodec.CurrentVersion;

    public string Title { get; set; } = string.Empty;

    public string? TargetNodeId { get; set; }

    public string TargetDisplayName { get; set; } = string.Empty;

    public int ReadMode { get; set; }

    public bool ReturnBounds { get; set; }

    public bool ReadModified { get; set; }

    public uint NumValuesPerNode { get; set; } = 1000;

    public string? AggregateNodeId { get; set; }

    public double ProcessingIntervalMs { get; set; } = 1000;

    public List<DateTime> AtTimes { get; set; } = [];

    public DateTime CustomStart { get; set; }

    public DateTime CustomEnd { get; set; }

    public int UpdateOp { get; set; }

    public DateTime UpdateTimestamp { get; set; }

    public string UpdateValueText { get; set; } = "0";

    public DateTime UpdateStart { get; set; }

    public DateTime UpdateEnd { get; set; }

    public List<DateTime> UpdateAtTimes { get; set; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HistorianStateDto))]
internal sealed partial class HistorianStateJsonContext : JsonSerializerContext;

/// <summary>
/// Captures and restores a Historian tab's configuration through the versioned
/// <see cref="HistorianStateDto"/>. Restore validates the read mode, update op and
/// processing interval; unknown versions or invalid enums throw rather than silently
/// dropping data. Restore never reads history or executes an update.
/// </summary>
internal static class HistorianStateCodec
{
    public const int CurrentVersion = 1;

    public static JsonElement Capture(HistorianStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var dto = new HistorianStateDto
        {
            Version = CurrentVersion,
            Title = snapshot.Title,
            TargetNodeId = snapshot.TargetNodeId.IsNull ? null : snapshot.TargetNodeId.ToString(),
            TargetDisplayName = snapshot.TargetDisplayName,
            ReadMode = (int)snapshot.ReadMode,
            ReturnBounds = snapshot.ReturnBounds,
            ReadModified = snapshot.ReadModified,
            NumValuesPerNode = snapshot.NumValuesPerNode,
            AggregateNodeId = snapshot.AggregateNodeId.IsNull ? null : snapshot.AggregateNodeId.ToString(),
            ProcessingIntervalMs = snapshot.ProcessingIntervalMs,
            AtTimes = ToUtcList(snapshot.AtTimes),
            CustomStart = ToUtc(snapshot.CustomStart),
            CustomEnd = ToUtc(snapshot.CustomEnd),
            UpdateOp = (int)snapshot.UpdateOp,
            UpdateTimestamp = ToUtc(snapshot.UpdateTimestamp),
            UpdateValueText = snapshot.UpdateValueText,
            UpdateStart = ToUtc(snapshot.UpdateStart),
            UpdateEnd = ToUtc(snapshot.UpdateEnd),
            UpdateAtTimes = ToUtcList(snapshot.UpdateAtTimes)
        };
        return JsonSerializer.SerializeToElement(dto, HistorianStateJsonContext.Default.HistorianStateDto);
    }

    public static HistorianStateSnapshot Restore(JsonElement state)
    {
        HistorianStateDto dto = state.Deserialize(HistorianStateJsonContext.Default.HistorianStateDto)
            ?? throw new JsonException("Historian configuration cannot be null.");
        if (dto.Version != CurrentVersion)
        {
            throw new JsonException($"Unsupported Historian state version {dto.Version}.");
        }
        if (!Enum.IsDefined((HistorianReadMode)dto.ReadMode))
        {
            throw new JsonException($"Unknown Historian read mode {dto.ReadMode}.");
        }
        if (!Enum.IsDefined((HistorianUpdateOp)dto.UpdateOp))
        {
            throw new JsonException($"Unknown Historian update operation {dto.UpdateOp}.");
        }
        if (!double.IsFinite(dto.ProcessingIntervalMs) || dto.ProcessingIntervalMs <= 0)
        {
            throw new JsonException("Historian processing interval must be a positive number.");
        }
        if (dto.AtTimes is null || dto.UpdateAtTimes is null)
        {
            throw new JsonException("Historian at-time list cannot be null.");
        }

        NodeId target = string.IsNullOrEmpty(dto.TargetNodeId) ? NodeId.Null : NodeId.Parse(dto.TargetNodeId);
        NodeId aggregate = string.IsNullOrEmpty(dto.AggregateNodeId) ? NodeId.Null : NodeId.Parse(dto.AggregateNodeId);
        return new HistorianStateSnapshot(
            dto.Title ?? string.Empty,
            target,
            dto.TargetDisplayName ?? string.Empty,
            (HistorianReadMode)dto.ReadMode,
            dto.ReturnBounds,
            dto.ReadModified,
            dto.NumValuesPerNode,
            aggregate,
            dto.ProcessingIntervalMs,
            ToUtcArray(dto.AtTimes),
            ToUtc(dto.CustomStart),
            ToUtc(dto.CustomEnd),
            (HistorianUpdateOp)dto.UpdateOp,
            ToUtc(dto.UpdateTimestamp),
            dto.UpdateValueText ?? "0",
            ToUtc(dto.UpdateStart),
            ToUtc(dto.UpdateEnd),
            ToUtcArray(dto.UpdateAtTimes));
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static List<DateTime> ToUtcList(ArrayOf<DateTime> values)
    {
        var list = new List<DateTime>(values.Count);
        foreach (DateTime value in values)
        {
            list.Add(ToUtc(value));
        }
        return list;
    }

    private static ArrayOf<DateTime> ToUtcArray(List<DateTime> values)
    {
        var array = new DateTime[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            array[index] = ToUtc(values[index]);
        }
        return array;
    }
}
