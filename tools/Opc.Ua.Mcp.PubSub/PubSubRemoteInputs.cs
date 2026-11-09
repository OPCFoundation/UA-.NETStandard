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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// One namespace-qualified published variable and its explicit metadata alias.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubVariableInput
    {
        /// <summary>
        /// Creates a mapping for direct construction with explicit field and node identity.
        /// </summary>
        public PubSubVariableInput()
        {
        }

        /// <summary>
        /// Preserves the Value attribute default during source-generated JSON construction.
        /// </summary>
        [JsonConstructor]
        [SetsRequiredMembers]
        public PubSubVariableInput(
            string name, string nodeId, uint attributeId = Attributes.Value, bool promoted = false)
        {
            Name = name;
            NodeId = nodeId;
            AttributeId = attributeId;
            Promoted = promoted;
        }

        /// <summary>
        /// Gets the exact field alias.
        /// </summary>
        [JsonRequired]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the local or namespace-URI-qualified source variable NodeId.
        /// </summary>
        [JsonRequired]
        public required string NodeId { get; init; }

        /// <summary>
        /// Gets the attribute sampled from the source.
        /// </summary>
        public uint AttributeId { get; init; } = Attributes.Value;

        /// <summary>
        /// Gets whether the field is promoted into NetworkMessage metadata.
        /// </summary>
        public bool Promoted { get; init; }
    }

    /// <summary>
    /// Bounded variable changes in the order expected by the standard method.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubVariablesInput
    {
        /// <summary>
        /// Gets the exact ordered field additions.
        /// </summary>
        [JsonConverter(typeof(PubSubInputArrayConverter<PubSubVariableInput>))]
        public required ArrayOf<PubSubVariableInput> Variables { get; init; }
    }

    /// <summary>
    /// A real server-returned configuration version, not an invented client revision.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubVersionInput
    {
        /// <summary>
        /// Gets the major configuration version.
        /// </summary>
        public required uint MajorVersion { get; init; }

        /// <summary>
        /// Gets the minor configuration version.
        /// </summary>
        public required uint MinorVersion { get; init; }
    }

    /// <summary>
    /// One server field index returned by the standard PubSub model.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubFieldIndexInput
    {
        /// <summary>
        /// Gets the exact field index to remove.
        /// </summary>
        public required uint Index { get; init; }
    }

    /// <summary>
    /// A bounded ordered field removal request.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubFieldIndicesInput
    {
        /// <summary>
        /// Gets the field indexes without replaying a previous metadata snapshot.
        /// </summary>
        [JsonConverter(typeof(PubSubInputArrayConverter<PubSubFieldIndexInput>))]
        public required ArrayOf<PubSubFieldIndexInput> Fields { get; init; }
    }

    /// <summary>
    /// One exact object/node selection used for bounded security-group target changes.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubNodeInput
    {
        /// <summary>
        /// Gets the namespace-qualified node identity.
        /// </summary>
        public required string NodeId { get; init; }
    }

    /// <summary>
    /// A bounded set of security groups explicitly attached to one push target.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PubSubNodesInput
    {
        /// <summary>
        /// Gets the exact ordered target identities.
        /// </summary>
        [JsonConverter(typeof(PubSubInputArrayConverter<PubSubNodeInput>))]
        public required ArrayOf<PubSubNodeInput> Nodes { get; init; }
    }
}
