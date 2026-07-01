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

using System.Collections.Generic;
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

        private static JsonObject StringOrInteger()
        {
            return new JsonObject { ["type"] = new JsonArray("string", "integer") };
        }

        private static JsonObject Object(JsonObject properties, params string[] required)
        {
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false
            };
            if (required.Length > 0)
            {
                var items = new List<JsonNode?>(required.Length);
                foreach (string name in required)
                {
                    items.Add(name);
                }
                schema["required"] = new JsonArray([.. items]);
            }
            return schema;
        }

        private static JsonObject NodeId()
        {
            return Object(new JsonObject
            {
                ["IdType"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 3 },
                ["Id"] = StringOrInteger(),
                ["Namespace"] = StringOrInteger()
            }, "Id");
        }

        private static JsonObject ExpandedNodeId()
        {
            return Object(new JsonObject
            {
                ["IdType"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 3 },
                ["Id"] = StringOrInteger(),
                ["Namespace"] = StringOrInteger(),
                ["ServerUri"] = StringOrInteger()
            }, "Id");
        }

        private static JsonObject QualifiedName()
        {
            return Object(new JsonObject
            {
                ["Name"] = new JsonObject { ["type"] = "string" },
                ["Uri"] = StringOrInteger()
            }, "Name");
        }

        private static JsonObject LocalizedText()
        {
            return Object(new JsonObject
            {
                ["Locale"] = new JsonObject { ["type"] = "string" },
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
            return Object(new JsonObject
            {
                ["Type"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 29 },
                ["Body"] = true,
                ["Dimensions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "integer" }
                }
            });
        }

        private static JsonObject ExtensionObject()
        {
            return Object(new JsonObject
            {
                ["TypeId"] = NodeId(),
                ["Encoding"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 2 },
                ["Body"] = true
            });
        }

        private static JsonObject DataValue()
        {
            return Object(new JsonObject
            {
                ["Value"] = true,
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
