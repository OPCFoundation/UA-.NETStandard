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

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// One declarative field rename; no expression or executable transform is accepted.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubFieldRenameInput
    {
        /// <summary>
        /// Gets the exact source field.
        /// </summary>
        public required string Source { get; init; }

        /// <summary>
        /// Gets the exact output field.
        /// </summary>
        public required string Target { get; init; }
    }

    /// <summary>
    /// A bounded declarative transcoder route owning both explicitly supplied connection plans.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubBridgeInput
    {
        /// <summary>
        /// Creates a route for direct construction with explicitly owned source and target plans.
        /// </summary>
        public PubSubBridgeInput()
        {
        }

        /// <summary>
        /// Preserves metadata-version retention and empty projections during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubBridgeInput(
            string name, PubSubApplicationInput source, PubSubApplicationInput target,
            ArrayOf<PubSubFieldRenameInput> renameFields = default, ArrayOf<string> selectFields = default,
            ArrayOf<string> excludeFields = default, ArrayOf<string> promoteFields = default,
            PubSubPublisherIdInput? publisherId = null, ushort? writerGroupId = null,
            bool preserveMetadataVersion = true, bool dropKeepAlive = false, bool allowSecurityDowngrade = false)
        {
            Name = name;
            Source = source;
            Target = target;
            RenameFields = renameFields.IsNull ? [] : renameFields;
            SelectFields = selectFields.IsNull ? [] : selectFields;
            ExcludeFields = excludeFields.IsNull ? [] : excludeFields;
            PromoteFields = promoteFields.IsNull ? [] : promoteFields;
            PublisherId = publisherId;
            WriterGroupId = writerGroupId;
            PreserveMetadataVersion = preserveMetadataVersion;
            DropKeepAlive = dropKeepAlive;
            AllowSecurityDowngrade = allowSecurityDowngrade;
        }

        /// <summary>
        /// Gets the caller's bridge lease name.
        /// </summary>
        [JsonRequired]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the explicitly owned subscriber application plan.
        /// </summary>
        [JsonRequired]
        public required PubSubApplicationInput Source { get; init; }

        /// <summary>
        /// Gets the explicitly owned egress application plan. Its cyclic DataSet writer is disabled.
        /// </summary>
        [JsonRequired]
        public required PubSubApplicationInput Target { get; init; }

        /// <summary>
        /// Gets declarative field renames.
        /// </summary>
        [JsonConverter(typeof(PubSubInputArrayConverter<PubSubFieldRenameInput>))]
        public ArrayOf<PubSubFieldRenameInput> RenameFields { get; init; } = [];

        /// <summary>
        /// Gets an optional ordered field projection.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> SelectFields { get; init; } = [];

        /// <summary>
        /// Gets optional excluded fields, mutually exclusive with SelectFields.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> ExcludeFields { get; init; } = [];

        /// <summary>
        /// Gets fields promoted into bounded target transport properties.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> PromoteFields { get; init; } = [];

        /// <summary>
        /// Gets an optional discriminator-preserving PublisherId remap.
        /// </summary>
        public PubSubPublisherIdInput? PublisherId { get; init; }

        /// <summary>
        /// Gets an optional WriterGroupId remap.
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// Gets whether the source metadata version is preserved.
        /// </summary>
        public bool PreserveMetadataVersion { get; init; } = true;

        /// <summary>
        /// Gets whether KeepAlive messages are deliberately dropped.
        /// </summary>
        public bool DropKeepAlive { get; init; }

        /// <summary>
        /// Gets explicit caller consent to a security downgrade, still requiring independent host permission.
        /// </summary>
        public bool AllowSecurityDowngrade { get; init; }
    }

    /// <summary>
    /// Exact action-to-method binding. This is data, never an executable callback or arbitrary tool name.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubActionBindingInput
    {
        /// <summary>
        /// Creates a method binding for direct construction with explicit object, method and target identity.
        /// </summary>
        public PubSubActionBindingInput()
        {
        }

        /// <summary>
        /// Preserves an empty output-name list and secure defaults during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubActionBindingInput(
            string objectId, string methodId, ushort actionTargetId, ArrayOf<string> outputFieldNames = default,
            bool allowUnsecured = false)
        {
            ObjectId = objectId;
            MethodId = methodId;
            ActionTargetId = actionTargetId;
            OutputFieldNames = outputFieldNames.IsNull ? [] : outputFieldNames;
            AllowUnsecured = allowUnsecured;
        }

        /// <summary>
        /// Gets the exact method object.
        /// </summary>
        [JsonRequired]
        public required string ObjectId { get; init; }

        /// <summary>
        /// Gets the exact method to invoke.
        /// </summary>
        [JsonRequired]
        public required string MethodId { get; init; }

        /// <summary>
        /// Gets the bound ActionTargetId.
        /// </summary>
        [JsonRequired]
        public required ushort ActionTargetId { get; init; }

        /// <summary>
        /// Gets the exact output field names, in server output order.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> OutputFieldNames { get; init; } = [];

        /// <summary>
        /// Gets explicit consent to serving an unsecured PubSub action, still requiring host permission.
        /// </summary>
        public bool AllowUnsecured { get; init; }
    }
}
