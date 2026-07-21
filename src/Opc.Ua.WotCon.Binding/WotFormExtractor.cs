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
using System.Collections.Immutable;
using System.Text.Json;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// Extracts the interaction-affordance forms (property / action / event) from
    /// a WoT Thing Description or Thing Model so binders can classify and compile
    /// them. Extraction is read-only, reflection-free and never performs transport
    /// I/O. Default <c>op</c> values are resolved per the WoT specification when a
    /// form omits them, and per-form security requirements fall back to the
    /// Thing-level default.
    /// </summary>
    public static class WotFormExtractor
    {
        /// <summary>Extracts the affordance forms from a WoT document.</summary>
        public static ImmutableArray<WotAffordanceForm> Extract(
            ReadOnlyMemory<byte> document, int maxJsonDepth = 64)
        {
            var forms = ImmutableArray.CreateBuilder<WotAffordanceForm>();
            try
            {
                var options = new JsonDocumentOptions { MaxDepth = maxJsonDepth <= 0 ? 64 : maxJsonDepth };
                using JsonDocument json = JsonDocument.Parse(document, options);
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return forms.ToImmutable();
                }

                ImmutableArray<string> thingSecurity = ReadSecurity(root);
                Collect(root, "properties", WotAffordanceKind.Property, thingSecurity, forms);
                Collect(root, "actions", WotAffordanceKind.Action, thingSecurity, forms);
                Collect(root, "events", WotAffordanceKind.Event, thingSecurity, forms);
            }
            catch (JsonException)
            {
                // Malformed documents are handled upstream by the converter; the
                // binder layer simply produces no forms.
            }
            return forms.ToImmutable();
        }

        private static void Collect(
            JsonElement root,
            string collection,
            WotAffordanceKind kind,
            ImmutableArray<string> thingSecurity,
            ImmutableArray<WotAffordanceForm>.Builder forms)
        {
            if (!root.TryGetProperty(collection, out JsonElement affordances) ||
                affordances.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty affordance in affordances.EnumerateObject())
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                JsonElement affordanceElement = affordance.Value.Clone();
                string affordanceName = affordance.Name;
                string affordancePointer = "/" + collection + "/" +
                    WotAffordanceForm.EscapePointerToken(affordanceName);

                if (!affordanceElement.TryGetProperty("forms", out JsonElement formsElement) ||
                    formsElement.ValueKind != JsonValueKind.Array)
                {
                    // An affordance without forms still requires a binder; emit a
                    // formless descriptor so a strict closure treats it as
                    // unsupported.
                    forms.Add(new WotAffordanceForm(
                        kind, affordanceName, DefaultOperations(kind, affordanceElement),
                        null, null, null, thingSecurity, affordancePointer + "/forms",
                        default, affordanceElement));
                    continue;
                }

                int index = 0;
                foreach (JsonElement form in formsElement.EnumerateArray())
                {
                    string formPointer = affordancePointer + "/forms/" +
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    index++;
                    if (form.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    JsonElement formElement = form.Clone();
                    ImmutableArray<string> ops = ReadOperations(formElement, kind, affordanceElement);
                    ImmutableArray<string> security = ReadSecurity(formElement);
                    if (security.IsEmpty)
                    {
                        security = thingSecurity;
                    }
                    forms.Add(new WotAffordanceForm(
                        kind,
                        affordanceName,
                        ops,
                        GetString(formElement, "href"),
                        GetString(formElement, "contentType"),
                        GetString(formElement, "subprotocol"),
                        security,
                        formPointer,
                        formElement,
                        affordanceElement));
                }
            }
        }

        private static ImmutableArray<string> ReadOperations(
            JsonElement form, WotAffordanceKind kind, JsonElement affordance)
        {
            if (form.TryGetProperty("op", out JsonElement op))
            {
                if (op.ValueKind == JsonValueKind.String)
                {
                    string? single = op.GetString();
                    return string.IsNullOrEmpty(single)
                        ? DefaultOperations(kind, affordance)
                        : ImmutableArray.Create(single!);
                }
                if (op.ValueKind == JsonValueKind.Array)
                {
                    var builder = ImmutableArray.CreateBuilder<string>();
                    foreach (JsonElement item in op.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            string? value = item.GetString();
                            if (!string.IsNullOrEmpty(value))
                            {
                                builder.Add(value!);
                            }
                        }
                    }
                    return builder.Count == 0 ? DefaultOperations(kind, affordance) : builder.ToImmutable();
                }
            }
            return DefaultOperations(kind, affordance);
        }

        private static ImmutableArray<string> DefaultOperations(
            WotAffordanceKind kind, JsonElement affordance)
        {
            switch (kind)
            {
                case WotAffordanceKind.Action:
                    return ImmutableArray.Create("invokeaction");
                case WotAffordanceKind.Event:
                    return ImmutableArray.Create("subscribeevent", "unsubscribeevent");
                default:
                    bool readOnly = GetBool(affordance, "readOnly");
                    bool writeOnly = GetBool(affordance, "writeOnly");
                    bool observable = GetBool(affordance, "observable");
                    var builder = ImmutableArray.CreateBuilder<string>();
                    if (!writeOnly)
                    {
                        builder.Add("readproperty");
                    }
                    if (!readOnly)
                    {
                        builder.Add("writeproperty");
                    }
                    if (observable)
                    {
                        builder.Add("observeproperty");
                        builder.Add("unobserveproperty");
                    }
                    if (builder.Count == 0)
                    {
                        builder.Add("readproperty");
                    }
                    return builder.ToImmutable();
            }
        }

        private static ImmutableArray<string> ReadSecurity(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("security", out JsonElement security))
            {
                return ImmutableArray<string>.Empty;
            }
            if (security.ValueKind == JsonValueKind.String)
            {
                string? single = security.GetString();
                return string.IsNullOrEmpty(single)
                    ? ImmutableArray<string>.Empty
                    : ImmutableArray.Create(single!);
            }
            if (security.ValueKind == JsonValueKind.Array)
            {
                var builder = ImmutableArray.CreateBuilder<string>();
                foreach (JsonElement item in security.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string? value = item.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            builder.Add(value!);
                        }
                    }
                }
                return builder.ToImmutable();
            }
            return ImmutableArray<string>.Empty;
        }

        private static string? GetString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(property, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool GetBool(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(property, out JsonElement value) &&
                value.ValueKind == JsonValueKind.True;
        }
    }
}
