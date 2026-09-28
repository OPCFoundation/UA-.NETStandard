/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Text.Json.Nodes;

namespace Opc.Ua.Schema.Json
{
    /// <summary>
    /// Builds the JSON Schema definitions for the standard OPC UA built-in
    /// object types (NodeId, Variant, ExtensionObject, ...) as described by the
    /// OPC UA Part 6 JSON encoding. These definitions are emitted into the
    /// <c>$defs</c> section of a document and referenced from fields so the
    /// standard types are described once per document.
    /// </summary>
    internal static class StandardJsonDefinitions
    {
        /// <summary>
        /// Creates the JSON Schema definition for the supplied standard type.
        /// </summary>
        /// <param name="builtInType">The built-in type to describe.</param>
        /// <returns>The JSON Schema object for the type.</returns>
        public static JsonObject Create(BuiltInType builtInType)
        {
            return builtInType switch
            {
                BuiltInType.NodeId => NodeId(),
                BuiltInType.ExpandedNodeId => ExpandedNodeId(),
                BuiltInType.QualifiedName => QualifiedName(),
                BuiltInType.LocalizedText => LocalizedText(),
                BuiltInType.StatusCode => StatusCode(),
                BuiltInType.Variant => Variant(),
                BuiltInType.ExtensionObject => ExtensionObject(),
                BuiltInType.DataValue => DataValue(),
                BuiltInType.DiagnosticInfo => DiagnosticInfo(),
                _ => new JsonObject { ["type"] = "object" }
            };
        }

        private static JsonObject Object(JsonObject properties, bool additionalProperties = false)
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = additionalProperties
            };
        }

        private static JsonObject NodeId()
        {
            // Part 6 5.4.2.10: NodeIds are encoded as a JSON string (e.g. "nsu=...;i=5").
            return new JsonObject { ["type"] = "string" };
        }

        private static JsonObject ExpandedNodeId()
        {
            // Part 6 5.4.2.11: ExpandedNodeIds are encoded as a JSON string.
            return new JsonObject { ["type"] = "string" };
        }

        private static JsonObject QualifiedName()
        {
            // Part 6 5.4.2.14: QualifiedNames are encoded as a JSON string.
            return new JsonObject { ["type"] = "string" };
        }

        private static JsonObject UaType()
        {
            return new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 31 };
        }

        private static JsonObject Dimensions()
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "integer" }
            };
        }

        private static JsonObject LocalizedText()
        {
            return Object(new JsonObject
            {
                ["Locale"] = new JsonObject { ["type"] = "string" },
                // the verbose encoding writes "Text": null for a LocalizedText that only has a locale.
                ["Text"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
            });
        }

        private static JsonObject StatusCode()
        {
            return Object(new JsonObject
            {
                ["Code"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 4294967295 },
                ["Symbol"] = new JsonObject { ["type"] = "string" }
            });
        }

        private static JsonObject Variant()
        {
            // Part 6 5.4.2.17: {"UaType", "Value", "Dimensions"}.
            return Object(new JsonObject
            {
                ["UaType"] = UaType(),
                ["Value"] = true,
                ["Dimensions"] = Dimensions()
            });
        }

        private static JsonObject ExtensionObject()
        {
            // Part 6 5.4.2.16: {"UaTypeId", "UaEncoding", "UaBody"}; a JSON encoded body
            // is written inline, so the structure fields appear next to UaTypeId.
            return Object(new JsonObject
            {
                ["UaTypeId"] = NodeId(),
                ["UaEncoding"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 2 },
                ["UaBody"] = true
            }, additionalProperties: true);
        }

        private static JsonObject DataValue()
        {
            // Part 6 5.4.2.18: a Variant with the additional DataValue fields.
            return Object(new JsonObject
            {
                ["UaType"] = UaType(),
                ["Value"] = true,
                ["Dimensions"] = Dimensions(),
                ["Status"] = StatusCode(),
                ["SourceTimestamp"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["SourcePicoseconds"] = new JsonObject { ["type"] = "integer" },
                ["ServerTimestamp"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["ServerPicoseconds"] = new JsonObject { ["type"] = "integer" }
            });
        }

        private static JsonObject DiagnosticInfo()
        {
            return new JsonObject { ["type"] = "object" };
        }
    }
}
