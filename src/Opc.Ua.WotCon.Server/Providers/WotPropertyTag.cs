// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
