// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Converts bounded DTO arrays using only the module's source-generated JSON metadata.
    /// </summary>
    /// <typeparam name="T">A concrete ISA-95 input type registered in the generated context.</typeparam>
    public sealed class Isa95InputArrayJsonConverter<T> : McpInputArrayJsonConverter<T>
        where T : class
    {
        /// <inheritdoc/>
        protected override JsonTypeInfo<T> ElementType =>
            Isa95InputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
                ?? throw new NotSupportedException("This type is not an ISA-95 input contract.");
    }

    /// <summary>
    /// Source-generated contracts used by nested input-array converters.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(Isa95TextInput))]
    [JsonSerializable(typeof(Isa95TypedValueInput))]
    [JsonSerializable(typeof(Isa95UnitInput))]
    [JsonSerializable(typeof(Isa95ParameterInput))]
    [JsonSerializable(typeof(Isa95ResourceInput))]
    [JsonSerializable(typeof(Isa95MaterialInput))]
    [JsonSerializable(typeof(Isa95WorkMasterInput))]
    [JsonSerializable(typeof(Isa95StateInput))]
    [JsonSerializable(typeof(Isa95CommentInput))]
    [JsonSerializable(typeof(Isa95JobOrderInput))]
    [JsonSerializable(typeof(Isa95JobResponseInput))]
    [JsonSerializable(typeof(Isa95ResponseQueryInput))]
    internal sealed partial class Isa95InputJsonContext : JsonSerializerContext
    {
    }
}
