// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                // Part 6 5.4.2.15: a null or empty Text is omitted, never written as null.
                ["Text"] = new JsonObject { ["type"] = "string" }
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
