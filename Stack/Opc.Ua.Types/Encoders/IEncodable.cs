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
using System.Collections.Generic;
using System.Runtime.Serialization;

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
        bool IsEqual(IEncodeable encodeable);
    }

    /// <summary>
    /// Defines extensions to support the JSON encoding.
    /// </summary>
    public interface IJsonEncodeable
    {
        /// <summary>
        /// Returns the NodeId for the default JSON encoding for the type.
        /// </summary>
        /// <value>The NodeId for the JSON encoding.</value>
        ExpandedNodeId JsonEncodingId { get; }
    }
}
