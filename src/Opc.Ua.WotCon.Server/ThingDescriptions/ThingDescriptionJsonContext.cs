// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Opc.Ua.WotCon.Server.ThingDescriptions
{
    /// <summary>
    /// AOT-safe <see cref="JsonSerializerContext"/> for the
    /// <see cref="ThingDescription"/> model.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true)]
    [JsonSerializable(typeof(ThingDescription))]
    [JsonSerializable(typeof(WotProperty))]
    [JsonSerializable(typeof(WotPropertyItems))]
    [JsonSerializable(typeof(WotAction))]
    [JsonSerializable(typeof(WotEvent))]
    [JsonSerializable(typeof(WotActionSchema))]
    [JsonSerializable(typeof(WotActionMember))]
    [JsonSerializable(typeof(Assets.AssetRegistryDeletionIntent))]
    internal sealed partial class ThingDescriptionJsonContext : JsonSerializerContext;
}
