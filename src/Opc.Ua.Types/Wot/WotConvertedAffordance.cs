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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The identity of an authored interaction in a produced NodeSet, separate
    /// from any source address carried by its forms.
    /// </summary>
    public sealed class WotConvertedAffordance
    {
        /// <summary>
        /// Initializes a mapped interaction and an immutable snapshot of its authored schema.
        /// </summary>
        public WotConvertedAffordance(
            WotAffordanceKind kind,
            string name,
            string jsonPointer,
            ExpandedNodeId nodeId,
            ExpandedNodeId ownerNodeId,
            JsonElement affordance)
        {
            Kind = kind;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            JsonPointer = jsonPointer ?? throw new ArgumentNullException(nameof(jsonPointer));
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A converted interaction must identify an existing Node.", nameof(nodeId));
            }
            NodeId = nodeId;
            OwnerNodeId = ownerNodeId;
            Affordance = affordance.Clone();
        }

        /// <summary>
        /// Gets the interaction kind.
        /// </summary>
        public WotAffordanceKind Kind { get; }

        /// <summary>
        /// Gets the authored map key.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the interaction's source JSON Pointer.
        /// </summary>
        public string JsonPointer { get; }

        /// <summary>
        /// Gets the actual converted local Node identity.
        /// </summary>
        public ExpandedNodeId NodeId { get; }

        /// <summary>
        /// Gets the converted owner identity.
        /// </summary>
        public ExpandedNodeId OwnerNodeId { get; }

        /// <summary>
        /// Gets the authored interaction, including its data schemas and local context.
        /// </summary>
        public JsonElement Affordance { get; }
    }
}
