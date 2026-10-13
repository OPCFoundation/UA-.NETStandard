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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// A single explicitly typed scalar or one-dimensional built-in array.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowValueInput
    {
        /// <summary>
        /// Gets or sets the UA built-in type. Structured extension objects are not accepted by this bounded contract.
        /// </summary>
        public required BuiltInType Type { get; set; }

        /// <summary>
        /// Gets or sets a JSON scalar or array of that scalar. ByteString uses base64; times require a UTC offset.
        /// </summary>
        public required JsonElement Value { get; set; }
    }

    /// <summary>
    /// A historical data value with explicit source time and preserved quality.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowDataValueInput
    {
        /// <summary>
        /// Gets or sets the explicit UA scalar or array-element type.
        /// </summary>
        public required BuiltInType Type { get; set; }

        /// <summary>
        /// Gets or sets an actual JSON scalar. Array values instead supply ArrayLength and root arrayElements.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement Value { get; set; }

        /// <summary>
        /// Gets or sets a one-dimensional array length; zero is an explicit typed empty array.
        /// </summary>
        public int? ArrayLength { get; set; }

        /// <summary>
        /// Gets or sets the explicit ISO 8601 source timestamp.
        /// </summary>
        public required string SourceTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the optional explicit server timestamp.
        /// </summary>
        public string? ServerTimestamp { get; set; }

        /// <summary>
        /// Gets or sets the numeric UA status code; zero means Good.
        /// </summary>
        public uint StatusCode { get; set; }
    }

    /// <summary>
    /// One URI-qualified path segment of an indexed event select clause.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowEventPathInput
    {
        /// <summary>
        /// Gets or sets the zero-based index in the root select array.
        /// </summary>
        public required int SelectIndex { get; set; }

        /// <summary>
        /// Gets or sets this segment's zero-based position in that browse path.
        /// </summary>
        public required int SegmentIndex { get; set; }

        /// <summary>
        /// Gets or sets the exact advertised namespace URI.
        /// </summary>
        public string NamespaceUri { get; set; } = Namespaces.OpcUa;

        /// <summary>
        /// Gets or sets the browse name.
        /// </summary>
        public required string Name { get; set; }
    }

    /// <summary>
    /// One flat event select clause; an absent path with NodeId attribute selects a condition identity.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowEventFieldInput
    {
        /// <summary>
        /// Gets or sets the event type that declares the field.
        /// </summary>
        public string TypeDefinitionId { get; set; } = ObjectTypeIds.BaseEventType.ToString();

        /// <summary>
        /// Gets or sets a single browse name. Omit when supplying indexed root paths or selecting NodeId.
        /// </summary>
        public string? BrowseName { get; set; }

        /// <summary>
        /// Gets or sets the single browse name's exact URI; omission uses the standard OPC UA namespace.
        /// </summary>
        public string? NamespaceUri { get; set; }

        /// <summary>
        /// Gets or sets the selected attribute; defaults to Value.
        /// </summary>
        public uint AttributeId { get; set; } = Attributes.Value;
    }

    /// <summary>
    /// One flat typed field of a historical event, indexed by event and select-clause position.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowHistoryEventInput
    {
        /// <summary>
        /// Gets or sets the zero-based event index; events must form a contiguous sequence starting at zero.
        /// </summary>
        public int EventIndex { get; set; }

        /// <summary>
        /// Gets or sets the zero-based field index in the root select array.
        /// </summary>
        public required int FieldIndex { get; set; }

        /// <summary>
        /// Gets or sets the explicit UA scalar or array-element type.
        /// </summary>
        public required BuiltInType Type { get; set; }

        /// <summary>
        /// Gets or sets an actual JSON scalar; arrays instead use ArrayLength and root arrayElements.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement Value { get; set; }

        /// <summary>
        /// Gets or sets the one-dimensional array length, including an explicit typed empty array.
        /// </summary>
        public int? ArrayLength { get; set; }
    }

    /// <summary>
    /// One scalar element of a typed array declared by a root values or events entry.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowArrayElementInput
    {
        /// <summary>
        /// Gets or sets the zero-based index of the declaring entry in root values or events.
        /// </summary>
        public required int ValueIndex { get; set; }

        /// <summary>
        /// Gets or sets the zero-based element index within that one-dimensional array.
        /// </summary>
        public required int ElementIndex { get; set; }

        /// <summary>
        /// Gets or sets an actual JSON scalar, interpreted using the declaring entry's UA type.
        /// </summary>
        public required JsonElement Value { get; set; }
    }

    /// <summary>
    /// An annotation whose timestamp identifies the annotation, not a sample offset.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowAnnotationInput
    {
        /// <summary>
        /// Gets or sets an explicit ISO 8601 annotation timestamp.
        /// </summary>
        public required string AnnotationTime { get; set; }

        /// <summary>
        /// Gets or sets the annotation text, including an explicit empty string.
        /// </summary>
        public required string Message { get; set; }

        /// <summary>
        /// Gets or sets historical annotation attribution; this is data, not MCP authorization.
        /// </summary>
        public string? UserName { get; set; }
    }

    /// <summary>
    /// One declared role-permission entry.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowRolePermissionInput
    {
        /// <summary>
        /// Gets or sets the exact role node id.
        /// </summary>
        public required string RoleId { get; set; }

        /// <summary>
        /// Gets or sets the numeric Part 3 PermissionType mask.
        /// </summary>
        public uint Permissions { get; set; }
    }

    /// <summary>
    /// Bounded array of data values for one explicit historical update.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowDataValuesInput
    {
        /// <summary>
        /// Gets or sets the values; at least one is required.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowDataValueInput>))]
        public ArrayOf<WorkflowDataValueInput> Values { get; set; } = [];

        /// <summary>
        /// Gets or sets flat elements for entries with an explicit array length.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowArrayElementInput>))]
        public ArrayOf<WorkflowArrayElementInput> ArrayElements { get; set; } = [];
    }

    /// <summary>
    /// Event select clauses and optional update rows, without executable filters or method dispatch.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowHistoryEventsInput
    {
        /// <summary>
        /// Gets or sets the select clauses; at most 32 are accepted.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowEventFieldInput>))]
        public ArrayOf<WorkflowEventFieldInput> Select { get; set; } = [];

        /// <summary>
        /// Gets or sets indexed, URI-qualified path segments for multi-segment selectors.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowEventPathInput>))]
        public ArrayOf<WorkflowEventPathInput> Paths { get; set; } = [];

        /// <summary>
        /// Gets or sets the historical events for update; reads require this array to be empty.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowHistoryEventInput>))]
        public ArrayOf<WorkflowHistoryEventInput> Events { get; set; } = [];

        /// <summary>
        /// Gets or sets flat array elements indexed to entries in Events.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowArrayElementInput>))]
        public ArrayOf<WorkflowArrayElementInput> ArrayElements { get; set; } = [];
    }

    /// <summary>
    /// Explicit annotation batch; remove identifies annotations by their timestamp.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowAnnotationsInput
    {
        /// <summary>
        /// Gets or sets the annotation entries.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowAnnotationInput>))]
        public ArrayOf<WorkflowAnnotationInput> Annotations { get; set; } = [];
    }

    /// <summary>
    /// Replacement declared permission set, including the deliberately empty set.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowPermissionsInput
    {
        /// <summary>
        /// Gets or sets exact role permission entries; no implicit merge is performed.
        /// </summary>
        [JsonConverter(typeof(WorkflowInputArrayConverter<WorkflowRolePermissionInput>))]
        public required ArrayOf<WorkflowRolePermissionInput> Permissions { get; set; }
    }

    /// <summary>
    /// Bounded strings used as UTC timestamps or base64 occurrence identifiers.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class WorkflowStringsInput
    {
        /// <summary>
        /// Gets or sets the explicit ordered entries.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> Values { get; set; } = [];
    }

    /// <summary>
    /// Reflection-free conversion for the closed set of workflow request elements.
    /// </summary>
    /// <typeparam name="T">A registered request element.</typeparam>
    public sealed class WorkflowInputArrayConverter<T> : McpInputArrayJsonConverter<T>
        where T : class
    {
        /// <inheritdoc/>
        protected override JsonTypeInfo<T> ElementType =>
            WorkflowInputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ??
            throw new NotSupportedException("This is not a registered workflow input element.");
    }

    /// <summary>
    /// Source-generated input metadata, also used for the published MCP schemas.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(WorkflowValueInput))]
    [JsonSerializable(typeof(WorkflowDataValueInput))]
    [JsonSerializable(typeof(WorkflowEventPathInput))]
    [JsonSerializable(typeof(WorkflowEventFieldInput))]
    [JsonSerializable(typeof(WorkflowHistoryEventInput))]
    [JsonSerializable(typeof(WorkflowArrayElementInput))]
    [JsonSerializable(typeof(WorkflowAnnotationInput))]
    [JsonSerializable(typeof(WorkflowRolePermissionInput))]
    [JsonSerializable(typeof(WorkflowDataValuesInput))]
    [JsonSerializable(typeof(WorkflowHistoryEventsInput))]
    [JsonSerializable(typeof(WorkflowAnnotationsInput))]
    [JsonSerializable(typeof(WorkflowPermissionsInput))]
    [JsonSerializable(typeof(WorkflowStringsInput))]
    [JsonSerializable(typeof(IdentityCriteriaType))]
    [JsonSerializable(typeof(MessageSecurityMode))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(uint?))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(bool?))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonElement?))]
    internal sealed partial class WorkflowInputJsonContext : JsonSerializerContext;
}
