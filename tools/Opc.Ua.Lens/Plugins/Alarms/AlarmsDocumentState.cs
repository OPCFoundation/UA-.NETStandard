// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;

namespace UaLens.Plugins.Alarms;

/// <summary>
/// Portable selection and display intent only. Neither captured conditions nor
/// command inputs, subscription handles or an observing flag are persisted.
/// </summary>
internal sealed record AlarmsDocumentState(
    int Version,
    string Title,
    string SourceId,
    string SourceName,
    double PublishingInterval,
    bool RetainedOnly);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AlarmsDocumentState))]
internal sealed partial class AlarmsJsonContext : JsonSerializerContext;

internal static class AlarmsStateCodec
{
    public const int CurrentVersion = 1;

    public static JsonElement Capture(AlarmsDocumentState state)
    {
        Validate(state);
        return JsonSerializer.SerializeToElement(state, AlarmsJsonContext.Default.AlarmsDocumentState);
    }

    public static AlarmsDocumentState Restore(JsonElement state)
    {
        AlarmsDocumentState restored = state.Deserialize(AlarmsJsonContext.Default.AlarmsDocumentState)
            ?? throw new JsonException("The Alarms configuration is missing.");
        Validate(restored);
        return restored;
    }

    public static void Validate(AlarmsDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != CurrentVersion)
        {
            throw new JsonException($"Unsupported Alarms configuration version {state.Version}.");
        }
        if (state.Title is null || state.Title.Length > 256 ||
            state.SourceName is null || state.SourceName.Length > 256 ||
            string.IsNullOrWhiteSpace(state.SourceId) || state.SourceId.Length > AlarmLimits.IdentifierLength ||
            !ExpandedNodeId.TryParse(state.SourceId, out ExpandedNodeId source) || source.IsNull ||
            source.ServerIndex != 0 || (source.NamespaceIndex != 0 && string.IsNullOrEmpty(source.NamespaceUri)))
        {
            throw new JsonException(
                "Alarms requires a bounded local source using a namespace URI (or namespace zero).");
        }
        if (!double.IsFinite(state.PublishingInterval) || state.PublishingInterval is < 50 or > 60000)
        {
            throw new JsonException("The requested alarm publishing interval must be between 50 and 60000 ms.");
        }
    }
}
