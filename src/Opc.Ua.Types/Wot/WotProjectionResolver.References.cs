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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Opc.Ua.Wot
{
    public sealed partial class WotProjectionResolver
    {
        private static bool ValidateReusableSchemaDefinitions(WotDocument document, List<WotDiagnostic> diagnostics)
        {
            var definitions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            JsonElement previous = default;
            foreach (JsonProperty rootMember in document.RootElement.EnumerateObject())
            {
                if (rootMember.Name != "schemaDefinitions")
                {
                    continue;
                }
                if (rootMember.Value.ValueKind != JsonValueKind.Object)
                {
                    return Invalid("schemaDefinitions must be an object.");
                }
                if (previous.ValueKind != JsonValueKind.Undefined &&
                    !Equivalent(previous, rootMember.Value))
                {
                    return Invalid("Repeated schemaDefinitions containers must agree.");
                }
                previous = rootMember.Value;
                foreach (JsonProperty definition in rootMember.Value.EnumerateObject())
                {
                    if (definition.Value.ValueKind != JsonValueKind.Object)
                    {
                        return Invalid($"Reusable schema '{definition.Name}' must be an object.");
                    }
                    if (definitions.TryGetValue(definition.Name, out JsonElement existing) &&
                        !Equivalent(existing, definition.Value))
                    {
                        return Invalid($"Reusable schema '{definition.Name}' has contradictory duplicate definitions.");
                    }
                    definitions[definition.Name] = definition.Value;
                }
            }
            return true;

            static bool Equivalent(JsonElement left, JsonElement right)
            {
                return WotJsonCanonicalizer.TryCanonicalize(left, out string first, out _) &&
                    WotJsonCanonicalizer.TryCanonicalize(right, out string second, out _) &&
                    string.Equals(first, second, StringComparison.Ordinal);
            }

            bool Invalid(string message)
            {
                AddError(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved, message, "/schemaDefinitions");
                return false;
            }
        }

        private sealed partial class SchemaReferenceClosure
        {
            public SchemaReferenceClosure(
                JsonObject root,
                WotDocument projectionDocument,
                string? documentLocation,
                Selection selection,
                WotNodeSetConverterOptions options,
                List<WotDiagnostic> diagnostics)
            {
                m_root = root;
                m_selection = selection;
                m_options = options;
                m_diagnostics = diagnostics;
                m_host = new ReferenceOwner(
                    projectionDocument, documentLocation ?? projectionDocument.Id ?? string.Empty, null);
            }

            public void Close(CancellationToken cancellationToken)
            {
                InitializeDataTypeCarriage();
                RegisterRootUriVariables();
                foreach (ResolvedAffordance member in m_selection.Members)
                {
                    var owner = new ReferenceOwner(
                        member.Source.Document, member.Source.DocumentHref, member.Source.Source.SourceName);
                    string destination = "/" + MapName(member.Kind) + "/" + EscapePointer(member.Name);
                    m_locations.TryAdd((false, owner.Href, member.Pointer), destination);
                    RegisterAffordanceUriVariables(member, owner, destination);
                    m_pending.Enqueue(new ReferenceCarriage(member.Value, owner, member.Pointer, destination));
                    if (member.Kind == WotAffordanceKind.Event &&
                        member.Value[WotEventSelectClauses.Term] is JsonArray clauses)
                    {
                        for (int index = 0; index < clauses.Count; index++)
                        {
                            if (clauses[index] is JsonObject clause)
                            {
                                string suffix = "/" + WotEventSelectClauses.Term +
                                    "/" + index.ToString(CultureInfo.InvariantCulture);
                                m_pending.Enqueue(new ReferenceCarriage(
                                    clause, owner, member.Pointer + suffix, destination + suffix)
                                {
                                    EventSelectClause = true
                                });
                            }
                        }
                    }
                    ReferenceOwner formOwner = member.Source.Source.Routing == WotProjectionRouting.Projection
                        ? member.GeneratedFormOwner ?? m_host
                        : owner;
                    string formPointer = member.Source.Source.Routing == WotProjectionRouting.Projection
                        ? destination
                        : member.Pointer;
                    RegisterForms(member.Value, formOwner, formPointer, destination);
                }

                if (m_root.TryGetPropertyValue("schemaDefinitions", out JsonNode? definitions))
                {
                    if (definitions is not JsonObject map)
                    {
                        Fail("The projection's schemaDefinitions must be an object.", "/schemaDefinitions");
                        return;
                    }
                    m_definitions = map;
                    foreach (KeyValuePair<string, JsonNode?> definition in Ordered(map))
                    {
                        string pointer = "/schemaDefinitions/" + EscapePointer(definition.Key);
                        if (definition.Value is not JsonObject schema)
                        {
                            Fail("A reusable schema definition must be an object.", pointer);
                            continue;
                        }
                        m_locations.TryAdd((true, m_host.Href, pointer), pointer);
                        m_pending.Enqueue(new ReferenceCarriage(schema, m_host, pointer, pointer));
                    }
                }

                if (!WithinBudget())
                {
                    return;
                }
                while (m_pending.Count != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReferenceCarriage carriage = m_pending.Dequeue();
                    RewriteReferences(carriage);
                    if (!carriage.EventSelectClause)
                    {
                        RegisterChildren(carriage);
                    }
                }
            }

            private void RewriteReferences(ReferenceCarriage carriage)
            {
                if (carriage.EventSelectClause)
                {
                    RewriteLocationReference(carriage, WotEventSelectClauses.TypeDefinitionReferenceTerm);
                    return;
                }
                RewriteDataTypeReferences(carriage);
                foreach (string term in s_locationReferences)
                {
                    RewriteLocationReference(carriage, term);
                }
                if (carriage.NamedResponse && carriage.Value.TryGetPropertyValue("schema", out JsonNode? schemaName))
                {
                    string? name = ReadString(schemaName);
                    if (string.IsNullOrEmpty(name))
                    {
                        Fail("A response schema must name a reusable schema definition.", carriage.SourcePointer);
                        return;
                    }
                    string? pointer = CarrySchema(carriage.Owner, "/schemaDefinitions/" + EscapePointer(name!));
                    if (pointer is not null)
                    {
                        carriage.Value["schema"] = UnescapeAffordanceName(pointer["/schemaDefinitions/".Length..]);
                    }
                }
            }

            private void RewriteLocationReference(ReferenceCarriage carriage, string term)
            {
                if (!carriage.Value.TryGetPropertyValue(term, out JsonNode? reference))
                {
                    return;
                }
                string? text = ReadString(reference);
                if (string.IsNullOrEmpty(text))
                {
                    Fail($"A carried {term} must be a non-empty reference.", carriage.SourcePointer + "/" + term);
                    return;
                }
                string? relocated = Relocate(carriage, term, text!);
                if (relocated is not null)
                {
                    carriage.Value[term] = relocated;
                }
            }

            private string? Relocate(ReferenceCarriage carriage, string term, string reference)
            {
                ReferenceOwner owner = carriage.Owner;
                if (term != "$ref" && HasScheme(reference))
                {
                    JsonElement original = OriginalElement(carriage);
                    if (original.ValueKind != JsonValueKind.Object ||
                        !TryExpandSemanticIdentity(
                            reference, owner.Document, original, owner.Href, false, out string identity))
                    {
                        Fail("A carried Binding reference cannot be expanded in its original owner context.",
                            owner.Href + "#" + carriage.SourcePointer + "/" + term);
                        return null;
                    }
                    reference = identity;
                }
                string expanded = ResolveHref(owner.Href, reference);
                string location = SplitDocumentPart(expanded);
                string pointer = SplitPointer(expanded);
                if (((reference.Length > 0 && reference[0] == '#') ||
                    string.Equals(location, owner.Href, StringComparison.Ordinal)) &&
                    pointer.Length > 0 &&
                    pointer[0] == '/')
                {
                    string? destination = CarrySchema(owner, pointer);
                    return destination is null ? null : "#" + destination;
                }
                return expanded;
            }

            private string? CarrySchema(ReferenceOwner owner, string pointer)
            {
                if (!IsCanonicalPointer(pointer) ||
                    !WotDocument.TryEvaluatePointer(owner.Document.RootElement, pointer, out JsonElement target) ||
                    target.ValueKind != JsonValueKind.Object)
                {
                    Fail("A local schema reference must identify an existing schema object.", owner.Href + "#" + pointer);
                    return null;
                }
                string? replacedScope = ReplacedUriVariableScope(owner, pointer);
                const string prefix = "/schemaDefinitions/";
                bool reusable = pointer.StartsWith(prefix, StringComparison.Ordinal);
                bool replacedVariable = replacedScope is not null && pointer.Length > replacedScope.Length;
                string? localRoot = reusable || replacedVariable ? null : FindLocalSchemaRoot(owner, pointer);
                string? mapped = MappedPointer(owner, pointer, replacedScope?.Length ?? 0);
                if (mapped is not null)
                {
                    if (!reusable && !replacedVariable && localRoot is null &&
                        mapped.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        Fail("A local schema reference cannot enter a literal or non-schema member.",
                            owner.Href + "#" + pointer);
                        return null;
                    }
                    return mapped;
                }

                if (!reusable && !replacedVariable && localRoot is null)
                {
                    Fail("The local reference does not identify a known DataSchema location.",
                        owner.Href + "#" + pointer);
                    return null;
                }
                int nameStart;
                string rootPointer;
                if (localRoot is not null)
                {
                    rootPointer = localRoot;
                    nameStart = rootPointer.LastIndexOf('/') + 1;
                }
                else
                {
                    nameStart = replacedVariable ? replacedScope!.Length + 1 : prefix.Length;
                    int next = pointer.IndexOf('/', nameStart);
                    rootPointer = next < 0 ? pointer : pointer[..next];
                }
                if (!WotDocument.TryEvaluatePointer(owner.Document.RootElement, rootPointer, out JsonElement original) ||
                    original.ValueKind != JsonValueKind.Object)
                {
                    Fail("A reusable schema definition must be an object.", owner.Href + "#" + rootPointer);
                    return null;
                }
                m_definitions ??= [];
                if ((long)m_selection.Members.Count + m_definitions.Count + m_uriVariableCount + m_dataTypeCount >=
                    m_options.MaxNodeCount)
                {
                    BudgetError();
                    return null;
                }
                string name = UnescapeAffordanceName(rootPointer[nameStart..]);
                if (m_definitions.ContainsKey(name))
                {
                    string stem = owner.SourceName is null
                        ? "q:p:" + EncodeSecurityName(rootPointer)
                        : "q:d:" + EncodeSecurityName(owner.SourceName) + ":" + EncodeSecurityName(rootPointer);
                    name = stem;
                    for (int suffix = 1; m_definitions.ContainsKey(name); suffix++)
                    {
                        name = stem + ":" + suffix.ToString(CultureInfo.InvariantCulture);
                    }
                }
                string destination = prefix + EscapePointer(name);
                JsonObject value = CloneOwnedObject(owner.Document, original, owner.Href);
                m_definitions[name] = value;
                if (!m_root.ContainsKey("schemaDefinitions"))
                {
                    m_root["schemaDefinitions"] = m_definitions;
                }
                m_locations.Add((owner.SourceName is null, owner.Href, rootPointer), destination);
                m_pending.Enqueue(new ReferenceCarriage(value, owner, rootPointer, destination));
                return destination + pointer[rootPointer.Length..];
            }

            private static string? FindLocalSchemaRoot(ReferenceOwner owner, string pointer)
            {
                string[] tokens = pointer[1..].Split('/');
                if (tokens.Length < 2)
                {
                    return null;
                }
                int rootLength;
                if (tokens[0] is "properties" or "uriVariables" or "$defs" or "definitions")
                {
                    rootLength = 2;
                }
                else if (tokens.Length >= 3 &&
                    ((tokens[0] == "actions" && tokens[2] is "input" or "output") ||
                        (tokens[0] == "events" && tokens[2] is "data" or "subscription" or
                            "cancellation" or "dataResponse")))
                {
                    rootLength = 3;
                }
                else if (tokens.Length >= 4 && tokens[0] is "actions" or "events" && tokens[2] == "uriVariables")
                {
                    rootLength = 4;
                }
                else
                {
                    return null;
                }

                string rootPointer = "/" + string.Join("/", tokens, 0, rootLength);
                if (!WotDocument.TryEvaluatePointer(owner.Document.RootElement, rootPointer, out JsonElement schema) ||
                    schema.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }
                int index = rootLength;
                while (index < tokens.Length)
                {
                    string member = UnescapeAffordanceName(tokens[index++]);
                    if (!schema.TryGetProperty(member, out JsonElement child))
                    {
                        return null;
                    }
                    if (Array.IndexOf(s_schemaMaps, member) >= 0)
                    {
                        if (child.ValueKind != JsonValueKind.Object || index == tokens.Length ||
                            !child.TryGetProperty(UnescapeAffordanceName(tokens[index++]), out child))
                        {
                            return null;
                        }
                    }
                    else if (Array.IndexOf(s_schemaChildren, member) >= 0)
                    {
                        if (child.ValueKind == JsonValueKind.Array)
                        {
                            if (index == tokens.Length ||
                                !int.TryParse(tokens[index], NumberStyles.None, CultureInfo.InvariantCulture,
                                    out int item) ||
                                !string.Equals(tokens[index], item.ToString(CultureInfo.InvariantCulture),
                                    StringComparison.Ordinal) ||
                                item >= child.GetArrayLength())
                            {
                                return null;
                            }
                            child = child[item];
                            index++;
                        }
                    }
                    else
                    {
                        return null;
                    }
                    if (child.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }
                    schema = child;
                }
                return rootPointer;
            }

            private string? MappedPointer(ReferenceOwner owner, string pointer, int minimumAncestorLength)
            {
                bool host = owner.SourceName is null;
                if (m_locations.TryGetValue((host, owner.Href, pointer), out string? destination))
                {
                    return destination;
                }
                int longest = -1;
                string? mapped = null;
                foreach (KeyValuePair<(bool Host, string Href, string Pointer), string> location in m_locations)
                {
                    if (location.Key.Host == host &&
                        string.Equals(location.Key.Href, owner.Href, StringComparison.Ordinal) &&
                        location.Key.Pointer.Length >= minimumAncestorLength &&
                        location.Key.Pointer.Length > longest &&
                        pointer.StartsWith(location.Key.Pointer + "/", StringComparison.Ordinal))
                    {
                        longest = location.Key.Pointer.Length;
                        mapped = location.Value + pointer[longest..];
                    }
                }
                return mapped;
            }

            private void RegisterChildren(ReferenceCarriage carriage)
            {
                foreach (KeyValuePair<string, JsonNode?> child in Ordered(carriage.Value))
                {
                    string sourcePointer = carriage.SourcePointer + "/" + EscapePointer(child.Key);
                    string destination = carriage.Destination + "/" + EscapePointer(child.Key);
                    if (Array.IndexOf(s_schemaMaps, child.Key) >= 0 && child.Value is JsonObject map)
                    {
                        foreach (KeyValuePair<string, JsonNode?> entry in Ordered(map))
                        {
                            if (entry.Value is JsonObject schema)
                            {
                                m_pending.Enqueue(m_uriVariableCarriages.TryGetValue(schema, out ReferenceCarriage? owned)
                                    ? owned
                                    : new ReferenceCarriage(schema, carriage.Owner,
                                        sourcePointer + "/" + EscapePointer(entry.Key),
                                        destination + "/" + EscapePointer(entry.Key)));
                            }
                        }
                    }
                    else if (Array.IndexOf(s_schemaChildren, child.Key) >= 0)
                    {
                        if (child.Value is JsonObject schema)
                        {
                            m_pending.Enqueue(new ReferenceCarriage(schema, carriage.Owner, sourcePointer, destination));
                        }
                        else if (child.Value is JsonArray alternatives)
                        {
                            for (int index = 0; index < alternatives.Count; index++)
                            {
                                if (alternatives[index] is JsonObject alternative)
                                {
                                    string suffix = "/" + index.ToString(CultureInfo.InvariantCulture);
                                    m_pending.Enqueue(new ReferenceCarriage(
                                        alternative, carriage.Owner, sourcePointer + suffix, destination + suffix));
                                }
                            }
                        }
                    }
                }
            }

            private void RegisterForms(
                JsonObject affordance, ReferenceOwner owner, string sourcePointer, string destination)
            {
                if (!affordance.TryGetPropertyValue("forms", out JsonNode? value) || value is not JsonArray forms)
                {
                    return;
                }
                for (int index = 0; index < forms.Count; index++)
                {
                    if (forms[index] is not JsonObject form)
                    {
                        continue;
                    }
                    string suffix = "/forms/" + index.ToString(CultureInfo.InvariantCulture);
                    if (!WotDocument.TryEvaluatePointer(owner.Document.RootElement, sourcePointer + suffix,
                        out JsonElement original))
                    {
                        Fail("A carried form lost its original context owner.", sourcePointer + suffix);
                        continue;
                    }
                    CarryContext(form, owner.Document, original, owner.Href);
                    if (form.TryGetPropertyValue("additionalResponses", out JsonNode? responsesValue) &&
                        responsesValue is JsonArray responses)
                    {
                        for (int responseIndex = 0; responseIndex < responses.Count; responseIndex++)
                        {
                            if (responses[responseIndex] is JsonObject response)
                            {
                                string responseSuffix = suffix +
                                    "/additionalResponses/" +
                                    responseIndex.ToString(CultureInfo.InvariantCulture);
                                m_pending.Enqueue(new ReferenceCarriage(
                                    response, owner, sourcePointer + responseSuffix, destination + responseSuffix)
                                {
                                    NamedResponse = true
                                });
                            }
                        }
                    }
                }
            }

            private bool WithinBudget()
            {
                if ((long)m_selection.Members.Count + (m_definitions?.Count ?? 0) + m_uriVariableCount + m_dataTypeCount <=
                    m_options.MaxNodeCount)
                {
                    return true;
                }
                BudgetError();
                return false;
            }

            private void BudgetError()
            {
                AddError(m_diagnostics, WotDiagnosticCode.TraversalBudgetExhausted,
                    $"Projection definitions exceed the configured maximum of {m_options.MaxNodeCount}.");
            }

            private void Fail(string message, string pointer)
            {
                AddError(m_diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved, message, pointer);
            }

            private static string? ReadString(JsonNode? node)
            {
                return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
            }

            private static bool IsCanonicalPointer(string pointer)
            {
                if (pointer.Length == 0 || pointer[0] != '/')
                {
                    return false;
                }
                foreach (string token in pointer[1..].Split('/'))
                {
                    if (!string.Equals(token, EscapePointer(UnescapeAffordanceName(token)), StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            }

            private static IEnumerable<KeyValuePair<string, JsonNode?>> Ordered(JsonObject value)
            {
                return value.OrderBy(member => member.Key, WotCodePointComparer.Instance);
            }

            private readonly JsonObject m_root;
            private readonly Selection m_selection;
            private readonly WotNodeSetConverterOptions m_options;
            private readonly List<WotDiagnostic> m_diagnostics;
            private readonly ReferenceOwner m_host;
            private readonly Queue<ReferenceCarriage> m_pending = new();
            private readonly Dictionary<(bool Host, string Href, string Pointer), string> m_locations = [];
            private JsonObject? m_definitions;
            private static readonly string[] s_locationReferences = ["$ref", "tm:ref", "uav:externalSchema"];
            private static readonly string[] s_schemaMaps = ["properties", "uriVariables", "$defs", "definitions"];

            private static readonly string[] s_schemaChildren =
            [
                "input", "output", "data", "subscription", "cancellation", "dataResponse",
                "items", "oneOf", "allOf", "anyOf", "not", "if", "then", "else",
                "additionalProperties", "contains", "propertyNames"
            ];
        }

        private sealed record ReferenceOwner(WotDocument Document, string Href, string? SourceName);

        private sealed record ReferenceCarriage(
            JsonObject Value, ReferenceOwner Owner, string SourcePointer, string Destination)
        {
            public bool NamedResponse { get; init; }

            public bool EventSelectClause { get; init; }
        }
    }
}
