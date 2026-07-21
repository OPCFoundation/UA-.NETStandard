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
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>The kind of interaction affordance a form belongs to.</summary>
    public enum WotAffordanceKind
    {
        /// <summary>A property affordance.</summary>
        Property,

        /// <summary>An action affordance.</summary>
        Action,

        /// <summary>An event affordance.</summary>
        Event
    }

    /// <summary>
    /// An immutable description of a single WoT interaction-affordance form. It
    /// carries the affordance metadata and a reflection-free snapshot of the form
    /// and affordance JSON (cloned, so it is safe to retain on the immutable
    /// registry snapshot) together with the RFC 6901 JSON Pointer that locates the
    /// form in the originating document. Binders read protocol vocabulary terms
    /// from <see cref="FormElement"/>; the object performs no transport I/O.
    /// </summary>
    public sealed class WotAffordanceForm
    {
        /// <summary>Initializes a new immutable affordance form.</summary>
        public WotAffordanceForm(
            WotAffordanceKind kind,
            string affordanceName,
            ImmutableArray<string> operations,
            string? href,
            string? contentType,
            string? subprotocol,
            ImmutableArray<string> securitySchemes,
            string jsonPointer,
            JsonElement formElement,
            JsonElement affordanceElement)
        {
            Kind = kind;
            AffordanceName = affordanceName ?? string.Empty;
            Operations = operations.IsDefault ? ImmutableArray<string>.Empty : operations;
            Href = href;
            ContentType = contentType;
            Subprotocol = subprotocol;
            SecuritySchemes = securitySchemes.IsDefault ? ImmutableArray<string>.Empty : securitySchemes;
            JsonPointer = jsonPointer ?? string.Empty;
            FormElement = formElement;
            AffordanceElement = affordanceElement;
        }

        /// <summary>Gets the affordance kind (property / action / event).</summary>
        public WotAffordanceKind Kind { get; }

        /// <summary>Gets the affordance (property / action / event) name.</summary>
        public string AffordanceName { get; }

        /// <summary>
        /// Gets the resolved interaction operations (<c>op</c>) for the form.
        /// Defaults per the WoT specification are applied when the form omits
        /// <c>op</c>: read/write for properties, invoke for actions and
        /// subscribe/unsubscribe for events.
        /// </summary>
        public ImmutableArray<string> Operations { get; }

        /// <summary>Gets the form target <c>href</c>, if any.</summary>
        public string? Href { get; }

        /// <summary>Gets the form <c>contentType</c>, if any.</summary>
        public string? ContentType { get; }

        /// <summary>Gets the form <c>subprotocol</c>, if any.</summary>
        public string? Subprotocol { get; }

        /// <summary>Gets the security scheme names required by the form (no secrets).</summary>
        public ImmutableArray<string> SecuritySchemes { get; }

        /// <summary>Gets the RFC 6901 JSON Pointer that locates the form.</summary>
        public string JsonPointer { get; }

        /// <summary>Gets the reflection-free JSON snapshot of the form object.</summary>
        public JsonElement FormElement { get; }

        /// <summary>Gets the reflection-free JSON snapshot of the owning affordance object.</summary>
        public JsonElement AffordanceElement { get; }

        /// <summary>Gets whether the form declares the supplied case-insensitive <c>op</c>.</summary>
        public bool HasOperation(string op)
        {
            foreach (string value in Operations)
            {
                if (string.Equals(value, op, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Builds a child JSON Pointer under the form (for example <c>href</c>).</summary>
        public string Pointer(string childToken)
        {
            if (string.IsNullOrEmpty(childToken))
            {
                return JsonPointer;
            }
            return JsonPointer + "/" + EscapePointerToken(childToken);
        }

        /// <summary>
        /// Reads a string-valued term from the form object, honouring both the
        /// plain and a colon-prefixed vocabulary form (for example <c>href</c> or
        /// <c>modv:function</c>).
        /// </summary>
        public bool TryGetString(string term, out string value)
        {
            if (FormElement.ValueKind == JsonValueKind.Object &&
                FormElement.TryGetProperty(term, out JsonElement element) &&
                element.ValueKind == JsonValueKind.String)
            {
                value = element.GetString() ?? string.Empty;
                return true;
            }
            value = string.Empty;
            return false;
        }

        /// <summary>Reads a boolean-valued term from the form object.</summary>
        public bool TryGetBoolean(string term, out bool value)
        {
            if (FormElement.ValueKind == JsonValueKind.Object &&
                FormElement.TryGetProperty(term, out JsonElement element) &&
                (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                value = element.GetBoolean();
                return true;
            }
            value = false;
            return false;
        }

        /// <summary>Reads an integer-valued term from the form object.</summary>
        public bool TryGetInt32(string term, out int value)
        {
            if (FormElement.ValueKind == JsonValueKind.Object &&
                FormElement.TryGetProperty(term, out JsonElement element))
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
                {
                    return true;
                }
                if (element.ValueKind == JsonValueKind.String &&
                    int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }
            value = 0;
            return false;
        }

        /// <summary>
        /// Reads a string-array-valued term from the form object (for example
        /// <c>uav:eventFields</c>). Non-string / empty array entries are
        /// skipped.
        /// </summary>
        public bool TryGetStringArray(string term, out ImmutableArray<string> values)
        {
            if (FormElement.ValueKind == JsonValueKind.Object &&
                FormElement.TryGetProperty(term, out JsonElement element) &&
                element.ValueKind == JsonValueKind.Array)
            {
                ImmutableArray<string>.Builder builder =
                    ImmutableArray.CreateBuilder<string>(element.GetArrayLength());
                foreach (JsonElement item in element.EnumerateArray())
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
                values = builder.ToImmutable();
                return values.Length > 0;
            }
            values = ImmutableArray<string>.Empty;
            return false;
        }

        /// <summary>Escapes a single RFC 6901 JSON Pointer reference token.</summary>
        public static string EscapePointerToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return token;
            }
            bool needsEscape = false;
            foreach (char c in token)
            {
                if (c == '~' || c == '/')
                {
                    needsEscape = true;
                    break;
                }
            }
            if (!needsEscape)
            {
                return token;
            }
            var builder = new StringBuilder(token.Length + 4);
            foreach (char c in token)
            {
                switch (c)
                {
                    case '~':
                        builder.Append("~0");
                        break;
                    case '/':
                        builder.Append("~1");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }
    }
}
