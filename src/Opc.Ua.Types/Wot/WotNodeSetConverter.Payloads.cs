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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Captures an interaction's payload schemas using the converter's DataType
        /// resolution rules. The affordance must be an original element of the live
        /// document: scoped contexts and inferred identities are keyed by that element.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="affordance"/> is not an original object from <paramref name="document"/>.
        /// </exception>
        public static WotPayloadSchema CapturePayloadSchema(
            WotDocument document, WotAffordanceKind kind, JsonElement affordance)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            if (affordance.ValueKind != JsonValueKind.Object || !document.Owns(affordance))
            {
                throw new ArgumentException(
                    "Payload capture requires an original affordance object from the owning document.",
                    nameof(affordance));
            }
            if (document.TryGetPayloadSchema(kind, affordance, out WotPayloadSchema? captured))
            {
                return captured;
            }
            var nodeSet = new UANodeSet();
            var diagnostics = new List<WotDiagnostic>();
            DataTypeDefinitionContext types = CreateDataTypeDefinitionContext(
                document, nodeSet, [], [], diagnostics);
            WotPayloadSchema result = CapturePayloadSchema(document, kind, affordance, nodeSet, types, diagnostics);
            document.SetPayloadSchema(kind, affordance, result);
            return result;
        }

        private static WotPayloadSchema CapturePayloadSchema(
            WotDocument document,
            WotAffordanceKind kind,
            JsonElement affordance,
            UANodeSet nodeSet,
            DataTypeDefinitionContext types,
            List<WotDiagnostic> diagnostics)
        {
            var bindings = new List<WotPayloadTypeBinding>();
            switch (kind)
            {
                case WotAffordanceKind.Property:
                    Capture(affordance, string.Empty);
                    break;
                case WotAffordanceKind.Action:
                    CaptureMember(InputMember);
                    CaptureMember(OutputMember);
                    break;
                case WotAffordanceKind.Event:
                    CaptureMember("data");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return new WotPayloadSchema(affordance, bindings.ToArrayOf(), diagnostics.ToArrayOf());

            void CaptureMember(string name)
            {
                if (affordance.ValueKind == JsonValueKind.Object &&
                    affordance.TryGetProperty(name, out JsonElement schema))
                {
                    Capture(schema, "/" + name);
                }
            }

            void Capture(JsonElement schema, string pointer)
            {
                if (schema.ValueKind != JsonValueKind.Object)
                {
                    return;
                }
                string local = MapJsonSchemaToDataType(document, schema, nodeSet, diagnostics, types);
                if (NodeSetStandardAliases.TryGetDataTypeNodeId(local, out string standard))
                {
                    local = standard;
                }
                string portable = NormalizeDataTypeValidationIdentity(local, nodeSet);
                ExpandedNodeId identity = ExpandedNodeId.TryParse(portable, out ExpandedNodeId parsed)
                    ? parsed : ExpandedNodeId.Null;
                BuiltInType builtIn = ResolvePayloadBuiltInType(portable, types);
                bindings.Add(new WotPayloadTypeBinding(
                    pointer, identity, TypeInfo.Create(builtIn, ReadValueRank(schema)),
                    ResolvePayloadBrowseName(document, schema, diagnostics)));
                if (schema.TryGetProperty("properties", out JsonElement properties) &&
                    properties.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in properties.EnumerateObject())
                    {
                        Capture(property.Value, pointer + "/properties/" + EscapePointerToken(property.Name));
                    }
                }
                if (schema.TryGetProperty("items", out JsonElement items))
                {
                    Capture(items, pointer + "/items");
                }
                if (schema.TryGetProperty("oneOf", out JsonElement branches) &&
                    branches.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement branch in branches.EnumerateArray())
                    {
                        Capture(branch, pointer +
                            "/oneOf/" +
                            index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        index++;
                    }
                }
            }
        }

        private static void RetainPayloadSchema(
            WotDocument document,
            WotAffordanceKind kind,
            JsonElement affordance,
            UANodeSet nodeSet,
            DataTypeDefinitionContext types)
        {
            document.SetPayloadSchema(kind, affordance,
                CapturePayloadSchema(document, kind, affordance, nodeSet, types, []));
        }

        private static BuiltInType ResolvePayloadBuiltInType(string identity, DataTypeDefinitionContext types)
        {
            // Abstract numeric declarations have no concrete wire BuiltInType.
            // Retain them until the payload codec selects the permitted value type.
            BuiltInType builtIn = identity switch
            {
                WotVocabulary.Number => BuiltInType.Number,
                WotVocabulary.Integer => BuiltInType.Integer,
                "i=28" => BuiltInType.UInteger,
                _ => GetValidationBuiltInType(identity)
            };
            if (builtIn != BuiltInType.Null)
            {
                return builtIn;
            }
            if (types.ValidationTypes.TryGetValue(identity, out DataTypeValidationNode? type))
            {
                if (type.Kind is ValidationDataTypeKind.Structure or ValidationDataTypeKind.Union)
                {
                    return BuiltInType.ExtensionObject;
                }
                if (type.Kind == ValidationDataTypeKind.Enumeration)
                {
                    return BuiltInType.Enumeration;
                }
                if (TryGetSimpleTerminal(identity, types, out string terminal))
                {
                    return GetValidationBuiltInType(terminal);
                }
            }
            return BuiltInType.Null;
        }

        private static string? ResolvePayloadBrowseName(
            WotDocument document, JsonElement schema, List<WotDiagnostic> diagnostics)
        {
            string? name = GetElementString(schema, "uav:browseName");
            if (name is null ||
                name.StartsWith("nsu=", StringComparison.Ordinal) ||
                name.StartsWith('{') ||
                !name.Contains(':', StringComparison.Ordinal))
            {
                return name;
            }
            if (!TrySplitCompactName(document, name, out string uri, out string local, schema))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error, WotDiagnosticCode.EventSelectClauseInvalid,
                    $"The payload BrowseName '{name}' has no resolved namespace."));
                return null;
            }
            return uri == WotVocabulary.OpcUaNamespace ? local : "nsu=" + CoreUtils.EscapeUri(uri) + ";" + local;
        }
    }
}
