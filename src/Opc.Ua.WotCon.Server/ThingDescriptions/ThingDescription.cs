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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opc.Ua.WotCon.Server.ThingDescriptions
{
    /// <summary>
    /// W3C WoT Thing Description root document. Only the fields consumed
    /// by the WoT Connectivity mapping (OPC 10100-1 §6) are modelled;
    /// unknown fields are tolerated and ignored.
    /// </summary>
    public sealed class ThingDescription
    {
        /// <summary>The TD <c>@context</c> array.</summary>
        [JsonPropertyName("@context")]
        public JsonElement? Context { get; set; }

        /// <summary>The TD <c>id</c>.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>The TD <c>@type</c> array.</summary>
        [JsonPropertyName("@type")]
        public JsonElement? Type { get; set; }

        /// <summary>Thing name (used as the OPC UA asset BrowseName).</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Base URI used by relative-href forms.</summary>
        [JsonPropertyName("base")]
        public string? Base { get; set; }

        /// <summary>Human-readable title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Optional description.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>WoT properties keyed by property name.</summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, WotProperty>? Properties { get; set; }

        /// <summary>WoT actions keyed by action name (OPC 10100-1 §6.3.9).</summary>
        [JsonPropertyName("actions")]
        public Dictionary<string, WotAction>? Actions { get; set; }
    }

    /// <summary>
    /// W3C WoT property definition. The <see cref="Type"/> string is
    /// mapped to an OPC UA <c>DataType</c> per OPC 10100-1 §6.3.8 Table 14.
    /// </summary>
    public sealed class WotProperty
    {
        /// <summary>WoT JSON Schema <c>type</c> (e.g. <c>"number"</c>).</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Optional title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Optional description.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary><c>true</c> for read-only properties.</summary>
        [JsonPropertyName("readOnly")]
        public bool ReadOnly { get; set; }

        /// <summary><c>true</c> when the property is observable.</summary>
        [JsonPropertyName("observable")]
        public bool Observable { get; set; }

        /// <summary>Item schema when <see cref="Type"/> is <c>array</c>.</summary>
        [JsonPropertyName("items")]
        public WotPropertyItems? Items { get; set; }

        /// <summary>Optional engineering unit suffix.</summary>
        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        /// <summary>Optional OPC UA NodeId override (when the TD pins one).</summary>
        [JsonPropertyName("opcua:nodeId")]
        public string? OpcUaNodeId { get; set; }

        /// <summary>Forms — protocol-binding specific endpoints.</summary>
        [JsonPropertyName("forms")]
        public List<JsonElement>? Forms { get; set; }
    }

    /// <summary>
    /// Item schema for an array WoT property.
    /// </summary>
    public sealed class WotPropertyItems
    {
        /// <summary>The element JSON-schema <c>type</c>.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    /// <summary>
    /// W3C WoT action definition (OPC 10100-1 §6.3.9). Maps to an OPC UA
    /// <c>MethodNode</c> with input/output arguments derived from the
    /// <see cref="Input"/> / <see cref="Output"/> JSON schemas.
    /// </summary>
    public sealed class WotAction
    {
        /// <summary>Optional title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Optional description.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Input JSON schema. Only <c>type: object</c> with flat
        /// <c>properties</c> is mapped to <see cref="Argument"/>s; other
        /// shapes are mapped to a single <c>BaseDataType</c> argument with
        /// the schema serialised as the description.
        /// </summary>
        [JsonPropertyName("input")]
        public WotActionSchema? Input { get; set; }

        /// <summary>Output JSON schema. Same rules as <see cref="Input"/>.</summary>
        [JsonPropertyName("output")]
        public WotActionSchema? Output { get; set; }

        /// <summary>Forms — protocol-binding specific endpoints.</summary>
        [JsonPropertyName("forms")]
        public List<JsonElement>? Forms { get; set; }
    }

    /// <summary>
    /// JSON-schema fragment describing a WoT action argument set.
    /// </summary>
    public sealed class WotActionSchema
    {
        /// <summary>Schema <c>type</c> (e.g. <c>object</c>).</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Optional title.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Optional description.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Object members. Iteration order is the OPC UA argument order.
        /// </summary>
        [JsonPropertyName("properties")]
        public Dictionary<string, WotActionMember>? Properties { get; set; }
    }

    /// <summary>
    /// One member of a WoT action input/output object.
    /// </summary>
    public sealed class WotActionMember
    {
        /// <summary>JSON-schema <c>type</c>.</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Optional description.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Optional engineering unit suffix.</summary>
        [JsonPropertyName("unit")]
        public string? Unit { get; set; }

        /// <summary>Optional minimum value (numeric schemas).</summary>
        [JsonPropertyName("minimum")]
        public double? Minimum { get; set; }

        /// <summary>Optional maximum value (numeric schemas).</summary>
        [JsonPropertyName("maximum")]
        public double? Maximum { get; set; }

        /// <summary>Element schema for array members.</summary>
        [JsonPropertyName("items")]
        public WotPropertyItems? Items { get; set; }
    }
}
