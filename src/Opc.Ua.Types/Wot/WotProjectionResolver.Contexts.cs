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
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot
{
    public sealed partial class WotProjectionResolver
    {
        private static JsonObject CloneOwnedObject(WotDocument document, JsonElement original, string origin)
        {
            JsonObject result = CloneObject(original);
            CarryContext(result, document, original, origin);
            return result;
        }

        private static void CarryContext(
            JsonObject target, WotDocument document, JsonElement original, string origin)
        {
            var context = new JsonArray
            {
                null,
                new JsonObject
                {
                    ["ua"] = WotVocabulary.OpcUaNamespace,
                    ["uav"] = WotVocabulary.VocabularyNamespace,
                    ["@base"] = string.IsNullOrEmpty(origin) ? null : JsonValue.Create(origin)
                }
            };
            string activeBase = origin;
            foreach (JsonElement entry in document.GetContextSequence(original))
            {
                AppendOwnedContext(context, entry, origin, ref activeBase);
            }
            target["@context"] = context;
            RelocateNestedContexts(target, document, original, origin, root: true);
        }

        private static void AppendContext(JsonArray target, JsonElement context)
        {
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    AppendContext(target, entry);
                }
                return;
            }
            target.Add(CloneNode(context));
        }

        private static void AppendOwnedContext(
            JsonArray target, JsonElement context, string origin, ref string activeBase)
        {
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    AppendOwnedContext(target, entry, origin, ref activeBase);
                }
                return;
            }
            target.Add(CloneContext(context, origin, ref activeBase));
        }

        private static JsonNode? CloneContext(JsonElement context, string origin)
        {
            string activeBase = origin;
            return CloneContext(context, origin, ref activeBase);
        }

        private static JsonNode? CloneContext(JsonElement context, string origin, ref string activeBase)
        {
            if (context.ValueKind == JsonValueKind.String)
            {
                return JsonValue.Create(ResolveContextLocation(origin, context.GetString()!));
            }
            if (context.ValueKind == JsonValueKind.Array)
            {
                var entries = new JsonArray();
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    entries.Add(CloneContext(entry, origin, ref activeBase));
                }
                return entries;
            }
            if (context.ValueKind != JsonValueKind.Object)
            {
                if (context.ValueKind == JsonValueKind.Null)
                {
                    activeBase = origin;
                }
                return CloneNode(context);
            }
            JsonObject result = CloneObject(context);
            ApplyContextBase(context, origin, ref activeBase);
            foreach (JsonProperty entry in context.EnumerateObject())
            {
                if (entry.Name == "@base" && entry.Value.ValueKind == JsonValueKind.String)
                {
                    result[entry.Name] = activeBase;
                }
                else if (entry.Name == "@import" && entry.Value.ValueKind == JsonValueKind.String)
                {
                    result[entry.Name] = ResolveContextLocation(origin, entry.Value.GetString()!);
                }
                else if (entry.Value.ValueKind == JsonValueKind.Object &&
                    entry.Value.TryGetProperty("@context", out JsonElement scoped))
                {
                    string scopedBase = activeBase;
                    result[entry.Name]!["@context"] = CloneContext(scoped, origin, ref scopedBase);
                }
            }
            return result;
        }

        private static string ResolveContextLocation(string origin, string reference)
        {
            return TrySplitBase(origin, out _, out string? authority, out string path, out _) &&
                (authority is not null || path.StartsWith('/'))
                ? ResolveHref(origin, reference)
                : reference;
        }

        private static void ApplyContextBase(JsonElement context, string origin, ref string activeBase)
        {
            if (context.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in context.EnumerateArray())
                {
                    ApplyContextBase(entry, origin, ref activeBase);
                }
            }
            else if (context.ValueKind == JsonValueKind.Null)
            {
                activeBase = origin;
            }
            else if (context.ValueKind == JsonValueKind.Object &&
                context.TryGetProperty("@base", out JsonElement baseUri))
            {
                if (baseUri.ValueKind == JsonValueKind.String)
                {
                    activeBase = ResolveContextLocation(activeBase, baseUri.GetString()!);
                }
                else if (baseUri.ValueKind == JsonValueKind.Null)
                {
                    activeBase = string.Empty;
                }
            }
        }

        private static string ReadContextBase(
            WotDocument document, JsonElement owner, string origin, JsonElement exclude = default)
        {
            string activeBase = origin;
            foreach (JsonElement context in document.GetContextSequence(owner))
            {
                if (!context.Equals(exclude))
                {
                    ApplyContextBase(context, origin, ref activeBase);
                }
            }
            return activeBase;
        }

        private static void RelocateNestedContexts(
            JsonNode? target, WotDocument document, JsonElement original, string origin,
            bool root = false, bool indexMap = false)
        {
            if (original.ValueKind == JsonValueKind.Object && target is JsonObject value)
            {
                foreach (JsonProperty member in original.EnumerateObject())
                {
                    if (indexMap)
                    {
                        RelocateNestedContexts(value[member.Name], document, member.Value, origin);
                    }
                    else if (member.Name == "@context")
                    {
                        if (!root)
                        {
                            string activeBase = ReadContextBase(document, original, origin, member.Value);
                            value[member.Name] = CloneContext(member.Value, origin, ref activeBase);
                        }
                    }
                    else if (!WotDocument.IsSemanticBoundary(member.Name) &&
                        !WotNodeSetConverter.IsLiteralSchemaMember(member.Name))
                    {
                        RelocateNestedContexts(value[member.Name], document, member.Value, origin,
                            indexMap: WotNodeSetConverter.IsSchemaDeclarationMap(member.Name) ||
                                document.IsContextIndexMap(member.Name, original));
                    }
                }
            }
            else if (original.ValueKind == JsonValueKind.Array && target is JsonArray array)
            {
                int index = 0;
                foreach (JsonElement entry in original.EnumerateArray())
                {
                    RelocateNestedContexts(array[index++], document, entry, origin);
                }
            }
        }

        private static JsonNode MergeAnnotationTypes(
            JsonNode? existing, JsonElement additional, JsonElement sourceDefinition,
            ResolvedSource source, Selection selection, JsonElement annotations)
        {
            var result = new JsonArray();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in NodeTokens(existing))
            {
                if (identities.Add(ExpandSemanticIdentity(
                    value, source.Document, sourceDefinition, source.DocumentHref, vocabulary: true)))
                {
                    result.Add(JsonValue.Create(value));
                }
            }
            foreach (string value in ElementTokens(additional))
            {
                string identity = ExpandSemanticIdentity(
                    value, selection.Document, annotations, selection.DocumentHref, vocabulary: true);
                if (identities.Add(identity))
                {
                    string sourceMeaning = ExpandSemanticIdentity(
                        value, source.Document, sourceDefinition, source.DocumentHref, vocabulary: true);
                    result.Add(JsonValue.Create(sourceMeaning == identity ? value : identity));
                }
            }
            return result.Count == 1 ? CloneNode(result[0]!)! : result;
        }

        private static string ExpandSemanticIdentity(
            string value, WotDocument document, JsonElement owner, string origin, bool vocabulary)
        {
            if (vocabulary && document.TryGetContextTerm(value, out JsonElement term, owner))
            {
                string? mapped = term.ValueKind == JsonValueKind.String ? term.GetString() :
                    term.ValueKind == JsonValueKind.Object &&
                    term.TryGetProperty("@id", out JsonElement id) &&
                    id.ValueKind == JsonValueKind.String
                    ? id.GetString() : null;
                if (mapped is not null)
                {
                    value = mapped;
                }
            }
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && document.TryGetContextPrefix(value[..colon], out string prefix, owner))
            {
                return prefix + value[(colon + 1)..];
            }
            if (colon < 0 &&
                vocabulary &&
                document.TryGetContextTerm("@vocab", out JsonElement vocab, owner) &&
                vocab.ValueKind == JsonValueKind.String)
            {
                return ResolveContextLocation(ReadContextBase(document, owner, origin), vocab.GetString()!) + value;
            }
            return ResolveContextLocation(ReadContextBase(document, owner, origin), value);
        }

        private static void CarryAnnotationLocale(
            JsonObject target, Selection selection, JsonElement annotations, string term)
        {
            JsonObject definition;
            if (selection.Document.TryGetContextTerm(term, out JsonElement original, annotations) &&
                original.ValueKind == JsonValueKind.Object)
            {
                definition = CloneObject(original);
            }
            else
            {
                definition = [];
                if (original.ValueKind == JsonValueKind.String)
                {
                    definition["@id"] = original.GetString();
                }
            }
            string identity = definition["@id"] is JsonValue id && id.TryGetValue(out string? declared)
                ? declared
                : "https://www.w3.org/2019/wot/td#" + term;
            definition["@id"] = ExpandSemanticIdentity(
                identity, selection.Document, annotations, selection.DocumentHref, vocabulary: true);
            definition["@language"] = WotNodeSetConverter.GetDeclaredLocale(selection.Document, annotations, term);
            ((JsonArray)target["@context"]!).Add(new JsonObject { [term] = definition });
        }
    }
}
