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
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot
{
    public sealed partial class WotProjectionResolver
    {
        private static bool ValidateContexts(WotDocument document, List<WotDiagnostic> diagnostics)
        {
            return Visit(document.RootElement, string.Empty);

            bool Visit(JsonElement value, string pointer, bool indexMap = false)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    bool hasContext = false;
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        string location = pointer + "/" + EscapePointer(member.Name);
                        if (indexMap)
                        {
                            if (!Visit(member.Value, location))
                            {
                                return false;
                            }
                        }
                        else if (member.Name == "@context")
                        {
                            if (hasContext)
                            {
                                return Invalid("A semantic object repeats its context declaration.", location);
                            }
                            hasContext = true;
                            if (!ValidateContext(member.Value, location))
                            {
                                return false;
                            }
                        }
                        else if (!WotDocument.IsSemanticBoundary(member.Name) &&
                            !WotNodeSetConverter.IsLiteralSchemaMember(member.Name) &&
                            !Visit(member.Value, location,
                                WotNodeSetConverter.IsSchemaDeclarationMap(member.Name) ||
                                document.IsContextIndexMap(member.Name, value)))
                        {
                            return false;
                        }
                    }
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement entry in value.EnumerateArray())
                    {
                        if (!Visit(entry, pointer + "/" + index.ToString(CultureInfo.InvariantCulture)))
                        {
                            return false;
                        }
                        index++;
                    }
                }
                return true;
            }

            bool ValidateContext(JsonElement value, string pointer)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        string location = pointer + "/" + EscapePointer(member.Name);
                        if (!names.Add(member.Name))
                        {
                            return Invalid("A semantic context contains a duplicate member.", location);
                        }
                        if ((member.Name == "@context" ||
                                member.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) &&
                            !ValidateContext(member.Value, location))
                        {
                            return false;
                        }
                    }
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement entry in value.EnumerateArray())
                    {
                        if (!ValidateContext(entry, pointer + "/" + index.ToString(CultureInfo.InvariantCulture)))
                        {
                            return false;
                        }
                        index++;
                    }
                }
                else if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    return Invalid("A context must be an object, array, string or null.", pointer);
                }
                return true;
            }

            bool Invalid(string message, string pointer)
            {
                AddError(diagnostics, WotDiagnosticCode.ProjectionContextConflict, message, pointer);
                return false;
            }
        }

        private static JsonObject CloneOwnedObject(WotDocument document, JsonElement original, string origin)
        {
            JsonObject result = CloneObject(original);
            CarryContext(result, document, original, origin);
            return result;
        }

        private static void CarryContext(
            JsonObject target, WotDocument document, JsonElement original, string origin)
        {
            PreserveLiteralValues(target, document, original);
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

        private static string ReadContextBase(WotDocument.ContextBindingScope? scope, string origin)
        {
            string activeBase = origin;
            foreach (JsonElement context in WotDocument.GetContextSequence(scope))
            {
                ApplyContextBase(context, origin, ref activeBase);
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

        private static JsonNode? MergeAnnotationTypes(
            JsonNode? existing, JsonElement additional, JsonElement sourceDefinition,
            ResolvedSource source, Selection selection, JsonElement annotations, List<WotDiagnostic> diagnostics)
        {
            var result = new JsonArray();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in NodeTokens(existing))
            {
                string sourceIdentity = TryExpandSemanticIdentity(
                    value, source.Document, sourceDefinition, source.DocumentHref, true, out string expanded)
                    ? expanded : "\0" + value;
                if (identities.Add(sourceIdentity))
                {
                    result.Add(JsonValue.Create(value));
                }
            }
            foreach (string value in ElementTokens(additional))
            {
                if (!TryPrepareAnnotationIdentity(
                    value, true, source, sourceDefinition, selection, annotations, diagnostics, out string identity))
                {
                    return null;
                }
                if (identities.Add(identity))
                {
                    bool sameMeaning = TryExpandSemanticIdentity(
                        value, source.Document, sourceDefinition, source.DocumentHref, true,
                        out string sourceMeaning) &&
                        sourceMeaning == identity;
                    result.Add(JsonValue.Create(sameMeaning ? value : identity));
                }
            }
            return result.Count == 1 ? CloneNode(result[0]!)! : result;
        }

        private static bool TryPrepareAnnotationIdentity(
            string value, bool vocabulary, ResolvedSource source, JsonElement sourceDefinition,
            Selection selection, JsonElement annotations, List<WotDiagnostic> diagnostics, out string identity)
        {
            if (TryExpandSemanticIdentity(
                value, selection.Document, annotations, selection.DocumentHref, vocabulary, out identity) &&
                TryExpandSemanticIdentity(
                    identity, source.Document, sourceDefinition, source.DocumentHref, vocabulary,
                    out string destination) &&
                identity == destination)
            {
                return true;
            }
            AnnotationIdentityError(diagnostics, value);
            return false;
        }

        private static bool TryExpandSemanticIdentity(
            string value, WotDocument document, JsonElement owner, string origin,
            bool vocabulary, out string identity)
        {
            identity = string.Empty;
            WotDocument.ContextBindingScope? scope = document.GetContextBindings(owner);
            var visited = new HashSet<(string, WotDocument.ContextBindingScope?)>();
            var prefixes = new HashSet<(string, WotDocument.ContextBindingScope?)>();
            string suffix = string.Empty;
            while (!string.IsNullOrEmpty(value) && visited.Add((value, scope)))
            {
                bool unknownTerm = false;
                if (vocabulary &&
                    WotDocument.TryGetKnownContextTerm(
                        scope, value, out JsonElement term, out unknownTerm,
                        out WotDocument.ContextBindingScope? termScope))
                {
                    string? mapped;
                    if (term.ValueKind == JsonValueKind.String)
                    {
                        mapped = term.GetString();
                    }
                    else if (term.ValueKind == JsonValueKind.Object)
                    {
                        mapped = term.TryGetProperty("@id", out JsonElement id)
                            ? id.ValueKind == JsonValueKind.String ? id.GetString() : null
                            : value;
                    }
                    else
                    {
                        return false;
                    }
                    if (mapped is null)
                    {
                        return false;
                    }
                    scope = termScope;
                    if (mapped != value)
                    {
                        value = mapped;
                        continue;
                    }
                }
                int colon = value.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    if (value.AsSpan(colon + 1).StartsWith("//".AsSpan(), StringComparison.Ordinal))
                    {
                        identity = value + suffix;
                        return true;
                    }
                    string prefixName = value[..colon];
                    bool hasPrefix = WotDocument.TryGetKnownContextPrefix(
                        scope, prefixName, out string prefix, out bool uncertain,
                        out WotDocument.ContextBindingScope? prefixScope);
                    if (uncertain)
                    {
                        return false;
                    }
                    if (hasPrefix)
                    {
                        if (!prefixes.Add((prefixName, prefixScope)))
                        {
                            return false;
                        }
                        suffix = value[(colon + 1)..] + suffix;
                        value = prefix;
                        scope = prefixScope;
                        vocabulary = true;
                        continue;
                    }
                    identity = value + suffix;
                    return HasScheme(identity);
                }
                if (unknownTerm)
                {
                    return false;
                }
                if (vocabulary &&
                    WotDocument.TryGetKnownContextTerm(
                        scope, "@vocab", out JsonElement vocab, out _,
                        out WotDocument.ContextBindingScope? vocabScope) &&
                    vocab.ValueKind == JsonValueKind.String)
                {
                    suffix = value + suffix;
                    value = vocab.GetString()!;
                    scope = vocabScope;
                    vocabulary = false;
                    continue;
                }
                _ = WotDocument.TryGetKnownContextTerm(scope, "@base", out _, out bool unknownBase, out _);
                if (unknownBase)
                {
                    return false;
                }
                identity = ResolveContextLocation(ReadContextBase(scope, origin), value) + suffix;
                return HasScheme(identity);
            }
            return false;
        }

        private static bool TryReadKnownContextTerm(
            WotDocument document, JsonElement owner, string term, out JsonElement definition, out bool uncertain)
        {
            return document.TryGetKnownContextTerm(term, out definition, out uncertain, owner);
        }

        private static void AnnotationIdentityError(List<WotDiagnostic> diagnostics, string value)
        {
            AddError(diagnostics, WotDiagnosticCode.ProjectionContextConflict,
                "A projection-owned annotation cannot be resolved in its original context " +
                "without acquiring source meaning.", value);
        }

        private static bool CarryAnnotationLocale(
            JsonObject target, Selection selection, JsonElement annotations, string term,
            ResolvedSource source, JsonElement sourceDefinition, List<WotDiagnostic> diagnostics)
        {
            bool declaredTerm = TryReadKnownContextTerm(
                selection.Document, annotations, term, out JsonElement original, out bool uncertain);
            if (uncertain || (declaredTerm && original.ValueKind == JsonValueKind.Null))
            {
                AnnotationIdentityError(diagnostics, term);
                return false;
            }
            if (!(original.ValueKind == JsonValueKind.Object && original.TryGetProperty("@language", out _)))
            {
                _ = TryReadKnownContextTerm(
                    selection.Document, annotations, "@language", out _, out bool unknownLanguage);
                if (unknownLanguage)
                {
                    AnnotationIdentityError(diagnostics, term);
                    return false;
                }
            }
            JsonObject definition;
            if (original.ValueKind == JsonValueKind.Object)
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
            string identity = declaredTerm
                ? term
                : "https://www.w3.org/2019/wot/td#" + term;
            if (!TryPrepareAnnotationIdentity(
                identity, true, source, sourceDefinition, selection, annotations, diagnostics, out string expanded))
            {
                return false;
            }
            definition["@id"] = expanded;
            definition["@language"] = WotNodeSetConverter.GetDeclaredLocale(selection.Document, annotations, term);
            ((JsonArray)target["@context"]!).Add(new JsonObject { [term] = definition });
            return true;
        }
    }
}
