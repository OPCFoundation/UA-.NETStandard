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

namespace UaLens.Plugins.PubSub;

internal sealed record PubSubDocumentState(int Version, PubSubConfiguration Configuration);

/// <summary>
/// The only persisted representation. Runtime configuration, keys, observations,
/// Action inputs and authorizations never enter this serializer.
/// </summary>
internal static class PubSubStateCodec
{
    public static JsonElement Capture(PubSubConfiguration configuration)
    {
        PubSubConfigurationValidation.RequireValid(configuration, requireEndpoint: false);
        JsonElement state = JsonSerializer.SerializeToElement(
            new PubSubDocumentState(1, configuration),
            PubSubJsonContext.Default.PubSubDocumentState);
        RequireShape(state);
        return state;
    }

    public static PubSubConfiguration Restore(JsonElement state)
    {
        RequireShape(state);
        PubSubDocumentState saved = state.Deserialize(PubSubJsonContext.Default.PubSubDocumentState)
            ?? throw new JsonException("The PubSub document is missing.");
        if (saved.Version != 1 || saved.Configuration is null)
        {
            throw new JsonException("Unsupported PubSub document version.");
        }
        Validate(saved.Configuration);
        return saved.Configuration;
    }

    public static string Format(PubSubConfiguration configuration)
    {
        PubSubConfigurationValidation.RequireValid(configuration, requireEndpoint: false);
        string json = JsonSerializer.Serialize(configuration, PubSubJsonContext.Default.PubSubConfiguration);
        if (json.Length > MaxConfigurationCharacters)
        {
            throw new JsonException("The PubSub configuration exceeds the document limit.");
        }
        return json;
    }

    public static PubSubConfiguration Parse(string configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Length > MaxConfigurationCharacters)
        {
            throw new JsonException("The PubSub configuration exceeds the document limit.");
        }
        using JsonDocument document = JsonDocument.Parse(configuration, new JsonDocumentOptions { MaxDepth = 16 });
        RequireShape(document.RootElement);
        PubSubConfiguration parsed = document.RootElement.Deserialize(PubSubJsonContext.Default.PubSubConfiguration)
            ?? throw new JsonException("A typed PubSub configuration is required.");
        Validate(parsed);
        return parsed;
    }

    private static void RequireShape(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object || state.GetRawText().Length > MaxConfigurationCharacters)
        {
            throw new JsonException("A bounded PubSub configuration object is required.");
        }
        RejectDuplicateProperties(state, 0);
    }

    private static void RejectDuplicateProperties(JsonElement element, int depth)
    {
        if (depth > 16)
        {
            throw new JsonException("The PubSub configuration exceeds the nesting limit.");
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("Duplicate configuration properties are not allowed.");
                }
                RejectDuplicateProperties(property.Value, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, depth + 1);
            }
        }
    }

    private static void Validate(PubSubConfiguration configuration)
    {
        ArrayOf<PubSubPrerequisite> issues = PubSubConfigurationValidation.Inspect(
            configuration, requireEndpoint: false);
        if (issues.Count > 0)
        {
            throw new JsonException(issues[0].Detail);
        }
    }

    public const int MaxConfigurationCharacters = 65536;
}

/// <summary>
/// Closed converter avoids reflection-based collection activation in NativeAOT.
/// </summary>
internal sealed class PubSubFieldArrayJsonConverter : JsonConverter<ArrayOf<PubSubFieldConfiguration>>
{
    public override ArrayOf<PubSubFieldConfiguration> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Fields must be an array.");
        }
        var fields = new List<PubSubFieldConfiguration>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (fields.Count >= PubSubConfigurationValidation.MaxFields)
            {
                throw new JsonException("At most 32 fields can be configured.");
            }
            fields.Add(JsonSerializer.Deserialize(ref reader, PubSubJsonContext.Default.PubSubFieldConfiguration)
                ?? throw new JsonException("A field cannot be null."));
        }
        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Unterminated field array.");
        }
        return [.. fields];
    }

    public override void Write(
        Utf8JsonWriter writer,
        ArrayOf<PubSubFieldConfiguration> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (PubSubFieldConfiguration field in value)
        {
            JsonSerializer.Serialize(writer, field, PubSubJsonContext.Default.PubSubFieldConfiguration);
        }
        writer.WriteEndArray();
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PubSubDocumentState))]
[JsonSerializable(typeof(PubSubConfiguration))]
[JsonSerializable(typeof(PubSubFieldConfiguration))]
[JsonSerializable(typeof(List<PubSubActionInputDraft>))]
internal sealed partial class PubSubJsonContext : JsonSerializerContext;
