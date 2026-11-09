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

namespace UaLens.Plugins.EventView;

/// <summary>
/// A validated, non-secret snapshot of an Event View tab's configuration. It
/// holds only the selection and filter intent; it carries no live subscription,
/// monitored item, credential or captured event history.
/// </summary>
internal sealed record EventViewStateSnapshot(
    string Title,
    EventFilterConfig Filter,
    bool DisplayPaused,
    ArrayOf<EventSourceSelection> Sources);

/// <summary>
/// One persisted event source: the NodeId to subscribe to plus the display name
/// shown in the sources panel. Bound to a live monitored item only after connect.
/// </summary>
internal sealed record EventSourceSelection(NodeId NodeId, string Name);

/// <summary>
/// Versioned, source-generated JSON envelope for <see cref="EventViewStateSnapshot"/>.
/// Collections use <see cref="List{T}"/> so the trim-safe generator can round-trip them.
/// </summary>
internal sealed class EventViewStateDto
{
    public int Version { get; set; } = EventViewStateCodec.CurrentVersion;

    public string Title { get; set; } = string.Empty;

    public ushort SeverityThreshold { get; set; }

    public List<string> Fields { get; set; } = [];

    public string? EventTypeNodeId { get; set; }

    /// <summary>
    /// Base64 of the OPC UA binary-encoded WhereClause <see cref="ContentFilter"/>,
    /// or null when no advanced filter is configured.
    /// </summary>
    public string? WhereClause { get; set; }

    public bool DisplayPaused { get; set; }

    public List<EventSourceDto> Sources { get; set; } = [];
}

/// <summary>
/// Persisted event source row.
/// </summary>
internal sealed class EventSourceDto
{
    public string NodeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EventViewStateDto))]
internal sealed partial class EventViewStateJsonContext : JsonSerializerContext;

/// <summary>
/// Captures and restores an Event View tab's configuration through the versioned
/// <see cref="EventViewStateDto"/>. The WhereClause is round-tripped with the stack's
/// own binary encoder (AOT-safe generated encode/decode) rather than an ad-hoc codec.
/// Unknown versions and malformed sources throw instead of silently dropping data.
/// </summary>
internal static class EventViewStateCodec
{
    public const int CurrentVersion = 1;

    private const string WhereClauseField = "WhereClause";

    public static JsonElement Capture(EventViewStateSnapshot snapshot, IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        var fields = new List<string>(snapshot.Filter.Fields.Count);
        foreach (string field in snapshot.Filter.Fields)
        {
            fields.Add(field);
        }
        var sources = new List<EventSourceDto>(snapshot.Sources.Count);
        foreach (EventSourceSelection source in snapshot.Sources)
        {
            sources.Add(new EventSourceDto
            {
                NodeId = source.NodeId.ToString() ?? string.Empty,
                Name = source.Name
            });
        }
        var dto = new EventViewStateDto
        {
            Version = CurrentVersion,
            Title = snapshot.Title,
            SeverityThreshold = snapshot.Filter.SeverityThreshold,
            Fields = fields,
            EventTypeNodeId = snapshot.Filter.EventTypeNodeId is { IsNull: false } typeId
                ? typeId.ToString()
                : null,
            WhereClause = EncodeWhereClause(snapshot.Filter.WhereClause, context),
            DisplayPaused = snapshot.DisplayPaused,
            Sources = sources
        };
        return JsonSerializer.SerializeToElement(dto, EventViewStateJsonContext.Default.EventViewStateDto);
    }

    public static EventViewStateSnapshot Restore(JsonElement state, IServiceMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EventViewStateDto dto = state.Deserialize(EventViewStateJsonContext.Default.EventViewStateDto)
            ?? throw new JsonException("Event View configuration cannot be null.");
        if (dto.Version != CurrentVersion)
        {
            throw new JsonException($"Unsupported Event View state version {dto.Version}.");
        }
        if (dto.Fields is null || dto.Sources is null)
        {
            throw new JsonException("Event View state is missing its fields or sources.");
        }

        NodeId? eventType = string.IsNullOrEmpty(dto.EventTypeNodeId)
            ? null
            : NodeId.Parse(dto.EventTypeNodeId);
        ContentFilter? whereClause = DecodeWhereClause(dto.WhereClause, context);
        var filter = new EventFilterConfig(
            (ushort)Math.Clamp((int)dto.SeverityThreshold, 0, ushort.MaxValue),
            new List<string>(dto.Fields),
            eventType,
            whereClause);

        var sources = new List<EventSourceSelection>(dto.Sources.Count);
        foreach (EventSourceDto source in dto.Sources)
        {
            if (string.IsNullOrEmpty(source.NodeId))
            {
                throw new JsonException("An Event View source is missing its NodeId.");
            }
            sources.Add(new EventSourceSelection(NodeId.Parse(source.NodeId), source.Name ?? string.Empty));
        }

        return new EventViewStateSnapshot(dto.Title ?? string.Empty, filter, dto.DisplayPaused, [.. sources]);
    }

    private static string? EncodeWhereClause(ContentFilter? whereClause, IServiceMessageContext context)
    {
        if (whereClause is null || whereClause.Elements.Count == 0)
        {
            return null;
        }
        using var encoder = new BinaryEncoder(context);
        encoder.WriteEncodeable(WhereClauseField, whereClause);
        byte[]? buffer = encoder.CloseAndReturnBuffer();
        return buffer is null ? null : Convert.ToBase64String(buffer);
    }

    private static ContentFilter? DecodeWhereClause(string? base64, IServiceMessageContext context)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return null;
        }
        byte[] bytes = Convert.FromBase64String(base64);
        using var decoder = new BinaryDecoder(bytes, context);
        ContentFilter whereClause = decoder.ReadEncodeable<ContentFilter>(WhereClauseField);
        return whereClause.Elements.Count == 0 ? null : whereClause;
    }
}
