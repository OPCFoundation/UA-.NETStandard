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

namespace Opc.Ua.Wot
{
    public sealed partial class WotProjectionResolver
    {
        private static JsonObject? CloneAffordance(
            ResolvedSource source, JsonElement definition, string pointer, List<WotDiagnostic> diagnostics)
        {
            if (!definition.TryGetProperty("uriVariables", out _))
            {
                return CloneOwnedObject(source.Document, definition, source.DocumentHref);
            }
            int errors = CountErrors(diagnostics);
            Dictionary<string, JsonElement> variables = ReadUriVariables(definition, pointer, diagnostics);
            if (CountErrors(diagnostics) != errors)
            {
                return null;
            }
            var target = new JsonObject();
            foreach (JsonProperty member in definition.EnumerateObject())
            {
                if (member.Name != "uriVariables")
                {
                    target.Add(member.Name, CloneNode(member.Value));
                    continue;
                }
                if (target.ContainsKey("uriVariables"))
                {
                    continue;
                }
                var local = new JsonObject();
                foreach (KeyValuePair<string, JsonElement> variable in variables
                    .OrderBy(entry => entry.Key, WotCodePointComparer.Instance))
                {
                    local.Add(variable.Key, CloneObject(variable.Value));
                }
                target.Add("uriVariables", local);
            }
            CarryContext(target, source.Document, definition, source.DocumentHref);
            return target;
        }

        private static Dictionary<string, JsonElement> ReadUriVariables(
            JsonElement scope, string pointer, List<WotDiagnostic> diagnostics)
        {
            var variables = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (scope.ValueKind != JsonValueKind.Object)
            {
                return variables;
            }
            JsonElement previous = default;
            foreach (JsonProperty property in scope.EnumerateObject())
            {
                if (property.Name != "uriVariables")
                {
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    Fail("uriVariables must be an object.", pointer + "/uriVariables");
                    return variables;
                }
                if (previous.ValueKind != JsonValueKind.Undefined && !EquivalentVariables(previous, property.Value))
                {
                    Fail("Repeated uriVariables containers must agree.", pointer + "/uriVariables");
                    return variables;
                }
                previous = property.Value;
                foreach (JsonProperty variable in property.Value.EnumerateObject())
                {
                    string variablePointer = pointer + "/uriVariables/" + EscapePointer(variable.Name);
                    if (variable.Value.ValueKind != JsonValueKind.Object)
                    {
                        Fail($"URI variable '{variable.Name}' must be a DataSchema object.", variablePointer);
                        continue;
                    }
                    if (variables.TryGetValue(variable.Name, out JsonElement existing) &&
                        !EquivalentVariables(existing, variable.Value))
                    {
                        Fail($"URI variable '{variable.Name}' has contradictory duplicate definitions.",
                            variablePointer);
                        continue;
                    }
                    variables[variable.Name] = variable.Value;
                }
            }
            return variables;

            void Fail(string message, string location) =>
                AddError(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved, message, location);
        }

        private static bool EquivalentVariables(JsonElement left, JsonElement right)
        {
            return WotJsonCanonicalizer.TryCanonicalize(left, out string first, out _) &&
                WotJsonCanonicalizer.TryCanonicalize(right, out string second, out _) &&
                string.Equals(first, second, StringComparison.Ordinal);
        }

        private sealed partial class SchemaReferenceClosure
        {
            private void RegisterRootUriVariables()
            {
                Dictionary<string, JsonElement> definitions = ReadUriVariables(
                    m_host.Document.RootElement, string.Empty, m_diagnostics);
                foreach ((string name, string pointer, bool security) in
                    EnumerateFormUriVariables(m_root, m_host, string.Empty))
                {
                    if (security)
                    {
                        if (definitions.ContainsKey(name))
                        {
                            Fail($"The URI variable '{name}' is declared by both a data schema " +
                                "and an active URI security scheme.", pointer);
                        }
                    }
                    else if (!definitions.ContainsKey(name))
                    {
                        Fail($"The form owner does not declare the required URI variable '{name}'.", pointer);
                    }
                }
                if (!m_root.ContainsKey("uriVariables"))
                {
                    return;
                }
                var variables = new JsonObject();
                m_root["uriVariables"] = variables;
                foreach (KeyValuePair<string, JsonElement> definition in definitions
                    .OrderBy(entry => entry.Key, WotCodePointComparer.Instance))
                {
                    if ((long)m_selection.Members.Count +
                        (m_definitions?.Count ?? 0) +
                        m_uriVariableCount +
                        m_dataTypeCount >=
                        m_options.MaxNodeCount)
                    {
                        BudgetError();
                        return;
                    }
                    JsonObject schema = CloneOwnedObject(m_host.Document, definition.Value, m_host.Href);
                    variables[definition.Key] = schema;
                    string pointer = "/uriVariables/" + EscapePointer(definition.Key);
                    RegisterVariable(schema, m_host, pointer, pointer);
                    m_pending.Enqueue(m_uriVariableCarriages[schema]);
                }
            }

            private void RegisterAffordanceUriVariables(
                ResolvedAffordance member, ReferenceOwner sourceOwner, string destination)
            {
                bool hostForms = member.Source.Source.Routing == WotProjectionRouting.Projection;
                ReferenceOwner owner = hostForms ? member.GeneratedFormOwner ?? m_host : sourceOwner;
                string originalPointer = hostForms ? destination : member.Pointer;
                JsonElement original = member.Definition;
                if (hostForms)
                {
                    m_replacedUriVariableScopes.Add((sourceOwner.Href, member.Pointer + "/uriVariables"));
                    if (!WotDocument.TryEvaluatePointer(owner.Document.RootElement, originalPointer, out original))
                    {
                        original = default;
                    }
                }
                Dictionary<string, JsonElement> local = ReadUriVariables(original, originalPointer, m_diagnostics);
                Dictionary<string, JsonElement> root = ReadUriVariables(
                    owner.Document.RootElement, string.Empty, m_diagnostics);
                var variables = new JsonObject();
                foreach (KeyValuePair<string, JsonElement> declaration in local
                    .OrderBy(entry => entry.Key, WotCodePointComparer.Instance))
                {
                    AddVariable(declaration.Key, declaration.Value, originalPointer +
                        "/uriVariables/" +
                        EscapePointer(declaration.Key));
                }
                foreach ((string name, string pointer, bool security) in
                    EnumerateFormUriVariables(member.Value, owner, destination))
                {
                    if (security)
                    {
                        if (local.ContainsKey(name) || root.ContainsKey(name))
                        {
                            Fail($"The URI variable '{name}' is declared by both a data schema " +
                                "and an active URI security scheme.", pointer);
                        }
                        continue;
                    }
                    if (variables.ContainsKey(name))
                    {
                        continue;
                    }
                    if (!root.TryGetValue(name, out JsonElement definition))
                    {
                        Fail($"The form owner does not declare the required URI variable '{name}'.", pointer);
                        continue;
                    }
                    AddVariable(name, definition, "/uriVariables/" + EscapePointer(name));
                }
                if (variables.Count != 0)
                {
                    var ordered = new JsonObject();
                    foreach (KeyValuePair<string, JsonNode?> variable in Ordered(variables).ToArray())
                    {
                        variables.Remove(variable.Key);
                        ordered[variable.Key] = variable.Value;
                    }
                    member.Value["uriVariables"] = ordered;
                }
                else
                {
                    member.Value.Remove("uriVariables");
                }

                void AddVariable(string name, JsonElement definition, string pointer)
                {
                    if ((long)m_selection.Members.Count +
                        (m_definitions?.Count ?? 0) +
                        m_uriVariableCount +
                        m_dataTypeCount >=
                        m_options.MaxNodeCount)
                    {
                        BudgetError();
                        return;
                    }
                    JsonObject schema = CloneOwnedObject(owner.Document, definition, owner.Href);
                    variables[name] = schema;
                    RegisterVariable(schema, owner, pointer, destination + "/uriVariables/" + EscapePointer(name));
                }
            }

            private IEnumerable<(string Name, string Pointer, bool Security)> EnumerateFormUriVariables(
                JsonObject value, ReferenceOwner owner, string destination)
            {
                if (value["forms"] is not JsonArray forms)
                {
                    yield break;
                }
                for (int index = 0; index < forms.Count; index++)
                {
                    if (forms[index] is not JsonObject form || ReadString(form["href"]) is not string href)
                    {
                        continue;
                    }
                    string pointer = destination + "/forms/" + index.ToString(CultureInfo.InvariantCulture) + "/href";
                    if (!TryReadTemplateVariables(href, out List<string> names, out string error))
                    {
                        Fail(error, pointer);
                        continue;
                    }
                    if (names.Count == 0)
                    {
                        continue;
                    }
                    HashSet<string> securityVariables = ReadSecurityUriVariables(form, owner, pointer);
                    foreach (string name in names)
                    {
                        yield return (name, pointer, securityVariables.Contains(name));
                    }
                }
            }

            private HashSet<string> ReadSecurityUriVariables(JsonObject form, ReferenceOwner owner, string pointer)
            {
                var variables = new HashSet<string>(StringComparer.Ordinal);
                var pending = new Stack<string>();
                if (form["security"] is JsonNode requirement)
                {
                    foreach (string name in NodeTokens(requirement))
                    {
                        pending.Push(name);
                    }
                }
                else if (owner.Document.RootElement.TryGetProperty("security", out JsonElement inherited))
                {
                    foreach (string name in ElementTokens(inherited))
                    {
                        pending.Push(Qualify(owner.SourceName, name));
                    }
                }
                var visited = new HashSet<string>(StringComparer.Ordinal);
                JsonObject? definitions = m_root["securityDefinitions"] as JsonObject;
                while (pending.Count != 0)
                {
                    string name = pending.Pop();
                    if (!visited.Add(name))
                    {
                        continue;
                    }
                    if (definitions?[name] is not JsonObject definition)
                    {
                        Fail($"The required security definition '{name}' is not available in the resolved view.",
                            pointer);
                        continue;
                    }
                    if (ReadString(definition["scheme"]) == "combo")
                    {
                        foreach (string key in s_comboKeys)
                        {
                            foreach (string child in NodeTokens(definition[key]))
                            {
                                pending.Push(child);
                            }
                        }
                    }
                    else if (ReadString(definition["scheme"]) == "apikey" && ReadString(definition["in"]) == "uri")
                    {
                        string? variable = ReadString(definition["name"]);
                        if (string.IsNullOrEmpty(variable))
                        {
                            Fail("An active URI API key security scheme must declare a non-empty variable name.",
                                pointer);
                        }
                        else
                        {
                            variables.Add(variable);
                        }
                    }
                }
                return variables;
            }

            private void RegisterVariable(JsonObject schema, ReferenceOwner owner, string pointer, string destination)
            {
                m_uriVariableCarriages.Add(schema, new ReferenceCarriage(schema, owner, pointer, destination));
                m_locations.TryAdd((owner.SourceName is null, owner.Href, pointer), destination);
                m_uriVariableCount++;
            }

            private string? ReplacedUriVariableScope(ReferenceOwner owner, string pointer)
            {
                if (owner.SourceName is null)
                {
                    return null;
                }
                foreach ((string href, string scope) in m_replacedUriVariableScopes)
                {
                    if (string.Equals(href, owner.Href, StringComparison.Ordinal) &&
                        (string.Equals(pointer, scope, StringComparison.Ordinal) ||
                            pointer.StartsWith(scope + "/", StringComparison.Ordinal)))
                    {
                        return scope;
                    }
                }
                return null;
            }

            private bool TryReadTemplateVariables(string href, out List<string> names, out string error)
            {
                names = [];
                error = "The form href contains an invalid URI-template expression.";
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int offset = 0; offset < href.Length; offset++)
                {
                    if (href[offset] == '}')
                    {
                        return false;
                    }
                    if (href[offset] != '{')
                    {
                        continue;
                    }
                    int close = href.IndexOf('}', offset + 1);
                    if (close < 0)
                    {
                        return false;
                    }
                    int start = offset + 1;
                    if (start < close && href[start] is '+' or '#' or '.' or '/' or ';' or '?' or '&')
                    {
                        start++;
                    }
                    while (start < close)
                    {
                        int comma = href.IndexOf(',', start, close - start);
                        int end = comma < 0 ? close : comma;
                        string spec = href[start..end];
                        int modifier = spec.IndexOfAny(s_variableModifiers);
                        string name = modifier < 0 ? spec : spec[..modifier];
                        if (!IsVariableName(name) || !IsVariableModifier(spec, modifier))
                        {
                            return false;
                        }
                        if (seen.Add(name))
                        {
                            if (names.Count >= m_options.MaxNodeCount)
                            {
                                BudgetError();
                                return false;
                            }
                            names.Add(name);
                        }
                        if (comma < 0)
                        {
                            start = close;
                            break;
                        }
                        start = comma + 1;
                        if (start == close)
                        {
                            return false;
                        }
                    }
                    if (start != close ||
                        start == offset + 1 ||
                        (close == offset + 2 && href[offset + 1] is '+' or '#' or '.' or '/' or ';' or '?' or '&'))
                    {
                        return false;
                    }
                    offset = close;
                }
                error = string.Empty;
                return true;
            }

            private static bool IsVariableName(string name)
            {
                bool segment = false;
                for (int index = 0; index < name.Length; index++)
                {
                    char value = name[index];
                    if (value == '.')
                    {
                        if (!segment)
                        {
                            return false;
                        }
                        segment = false;
                        continue;
                    }
                    if (value == '%')
                    {
                        if (index + 2 >= name.Length ||
                            !Uri.IsHexDigit(name[index + 1]) ||
                            !Uri.IsHexDigit(name[index + 2]))
                        {
                            return false;
                        }
                        index += 2;
                    }
                    else if (value is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and
                        not (>= '0' and <= '9') and not '_')
                    {
                        return false;
                    }
                    segment = true;
                }
                return segment;
            }

            private static bool IsVariableModifier(string spec, int modifier)
            {
                if (modifier < 0)
                {
                    return true;
                }
                if (spec[modifier] == '*')
                {
                    return modifier == spec.Length - 1;
                }
                int length = spec.Length - modifier - 1;
                if (length is < 1 or > 4 || spec[modifier + 1] is < '1' or > '9')
                {
                    return false;
                }
                for (int index = modifier + 2; index < spec.Length; index++)
                {
                    if (spec[index] is < '0' or > '9')
                    {
                        return false;
                    }
                }
                return true;
            }

            private readonly Dictionary<JsonObject, ReferenceCarriage> m_uriVariableCarriages = [];
            private readonly HashSet<(string Href, string Pointer)> m_replacedUriVariableScopes = [];
            private int m_uriVariableCount;
            private static readonly char[] s_variableModifiers = [':', '*'];
        }
    }
}
