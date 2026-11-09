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
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua;
using Opc.Ua.Schema;

namespace UaLens.Plugins.Models;

/// <summary>
/// Offline inspection intent only. Values, edits, permissions, sessions and armed
/// mutation controls are deliberately not part of the persisted configuration.
/// </summary>
internal sealed record ModelInspectorState(
    int Version,
    string Title,
    string Target,
    string TargetName,
    UaSchemaFormat SchemaFormat);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ModelInspectorState))]
internal sealed partial class ModelInspectorJsonContext : JsonSerializerContext;

internal static class ModelInspectorStateCodec
{
    public static JsonElement Capture(ModelInspectorState state)
    {
        Validate(state);
        return JsonSerializer.SerializeToElement(state, ModelInspectorJsonContext.Default.ModelInspectorState);
    }

    public static ModelInspectorState Restore(JsonElement state)
    {
        ModelInspectorState restored = state.Deserialize(ModelInspectorJsonContext.Default.ModelInspectorState) ??
            throw new JsonException("Models configuration cannot be null.");
        Validate(restored);
        return restored;
    }

    public static string NormalizeTarget(string text, NamespaceTable? namespaceUris)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        if (text.Length > MaximumIdentifierLength ||
            !ExpandedNodeId.TryParse(text.Trim(), out ExpandedNodeId id) || id.IsNull || id.ServerIndex != 0)
        {
            throw new JsonException("Use a bounded local OPC UA identifier, not a remote server index.");
        }
        if (!string.IsNullOrEmpty(id.NamespaceUri) || id.NamespaceIndex == 0)
        {
            return id.ToString();
        }
        string? uri = namespaceUris?.GetString(id.NamespaceIndex);
        if (string.IsNullOrEmpty(uri))
        {
            throw new JsonException("A nonzero namespace index requires the connected server's namespace table.");
        }
        return NodeId.ToExpandedNodeId(id.InnerNodeId, namespaceUris!).ToString();
    }

    public static NodeId ResolveTarget(string portable, NamespaceTable namespaceUris)
    {
        ArgumentNullException.ThrowIfNull(namespaceUris);
        string normalized = NormalizeTarget(portable, null);
        if (string.IsNullOrEmpty(normalized))
        {
            throw new InvalidOperationException("Choose a Variable, DataType or Method before reading.");
        }
        ExpandedNodeId id = ExpandedNodeId.Parse(normalized);
        NodeId local = ExpandedNodeId.ToNodeId(id, namespaceUris);
        if (local.IsNull)
        {
            throw new ServiceResultException(
                StatusCodes.BadNodeIdUnknown, "The requested namespace is not present in this server.");
        }
        return local;
    }

    public static void Validate(ModelInspectorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != CurrentVersion)
        {
            throw new JsonException($"Unsupported Models configuration version {state.Version}.");
        }
        if (string.IsNullOrWhiteSpace(state.Title) || state.Title.Length > 256 ||
            state.TargetName is null || state.TargetName.Length > 256 || state.Target is null)
        {
            throw new JsonException("Models requires a bounded title, target and display name.");
        }
        if (state.SchemaFormat is not (UaSchemaFormat.JsonCompact or UaSchemaFormat.JsonVerbose or
            UaSchemaFormat.Xsd or UaSchemaFormat.Bsd))
        {
            throw new JsonException("The requested Models schema format is not supported.");
        }
        if (!string.Equals(state.Target, NormalizeTarget(state.Target, null), StringComparison.Ordinal))
        {
            throw new JsonException("Saved Models targets must use canonical namespace-URI identifiers.");
        }
    }

    public const int CurrentVersion = 1;
    public const int MaximumIdentifierLength = 4096;
}
