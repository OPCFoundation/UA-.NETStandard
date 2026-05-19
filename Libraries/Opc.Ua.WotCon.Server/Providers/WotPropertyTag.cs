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

using System;
using System.Text.Json;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Describes a single WoT property surfaced as an OPC UA
    /// <c>BaseDataVariableType</c> child of an <c>IWoTAssetType</c> instance.
    /// </summary>
    /// <remarks>
    /// Tags are constructed by the node manager when parsing a WoT Thing
    /// Description and handed to <see cref="IWotAssetProvider"/> for
    /// data-plane operations.
    /// </remarks>
    public sealed class WotPropertyTag
    {
        /// <summary>
        /// Initialises a new <see cref="WotPropertyTag"/>.
        /// </summary>
        public WotPropertyTag(
            string name,
            NodeId nodeId,
            NodeId dataType,
            int valueRank,
            bool readOnly,
            bool observable,
            JsonElement? form)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            NodeId = nodeId;
            DataType = dataType;
            ValueRank = valueRank;
            ReadOnly = readOnly;
            Observable = observable;
            Form = form;
        }

        /// <summary>
        /// The WoT property name (used as <c>BrowseName</c>).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The <c>NodeId</c> of the materialised OPC UA variable.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The mapped OPC UA built-in data type (per OPC 10100-1 §6.3.8 Table 14).
        /// </summary>
        public NodeId DataType { get; }

        /// <summary>
        /// <see cref="ValueRanks.Scalar"/> for scalar properties,
        /// <see cref="ValueRanks.OneDimension"/> for arrays.
        /// </summary>
        public int ValueRank { get; }

        /// <summary>
        /// <c>true</c> when the WoT TD marks the property as <c>readOnly</c>.
        /// </summary>
        public bool ReadOnly { get; }

        /// <summary>
        /// <c>true</c> when the WoT TD marks the property as <c>observable</c>.
        /// </summary>
        public bool Observable { get; }

        /// <summary>
        /// Raw protocol-binding form (as parsed JSON). Providers cast or
        /// re-parse this element to read their specific metadata.
        /// </summary>
        public JsonElement? Form { get; }
    }
}
