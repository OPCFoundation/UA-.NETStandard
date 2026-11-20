// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Defines methods used to encode and decode objects.
    /// </summary>
    public interface IEncodeable : ICloneable
    {
        /// <summary>
        /// Returns the NodeId for the encodeable type.
        /// </summary>
        /// <value>The NodeId.</value>
        ExpandedNodeId TypeId { get; }

        /// <summary>
        /// Returns the NodeId for the default binary encoding for the type.
        /// </summary>
        /// <value>The NodeId for binary encoding.</value>
        ExpandedNodeId BinaryEncodingId { get; }

        /// <summary>
        /// Returns the NodeId for the default XML encoding for the type.
        /// </summary>
        /// <value>The NodeId for the  XML encoding id.</value>
        ExpandedNodeId XmlEncodingId { get; }

        /// <summary>
        /// Encodes the object in a stream.
        /// </summary>
        /// <param name="encoder">The encoder to be used for encoding the current value.</param>
        void Encode(IEncoder encoder);

        /// <summary>
        /// Decodes the object from a stream.
        /// </summary>
        /// <param name="decoder">The decoder to be used for decoding the current value.</param>
        void Decode(IDecoder decoder);

        /// <summary>
        /// Checks if the value is equal to the another encodeable object.
        /// </summary>
        /// <param name="encodeable">The encodeable.</param>
        /// <returns>
        /// 	<c>true</c> if the specified instance of the <see cref="IEncodeable"/> type is equal; otherwise <c>false</c>.
        /// </returns>
        bool IsEqual(IEncodeable? encodeable);
    }
}
