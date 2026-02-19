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

namespace Opc.Ua
{
    /// <summary>
    /// The set of built-in data types for UA type descriptions.
    /// </summary>
    /// <remarks>
    /// An enumeration that lists all of the built-in data types for OPC UA Type Descriptions.
    /// </remarks>
    public enum BuiltInType
    {
        /// <summary>
        /// An invalid or unspecified value.
        /// </summary>
        Null = 0,

        /// <summary>
        /// A boolean logic value (true or false).
        /// </summary>
        Boolean = 1,

        /// <summary>
        /// An 8 bit signed integer value.
        /// </summary>
        SByte = 2,

        /// <summary>
        /// An 8 bit unsigned integer value.
        /// </summary>
        Byte = 3,

        /// <summary>
        /// A 16 bit signed integer value.
        /// </summary>
        Int16 = 4,

        /// <summary>
        /// A 16 bit signed integer value.
        /// </summary>
        UInt16 = 5,

        /// <summary>
        /// A 32 bit signed integer value.
        /// </summary>
        Int32 = 6,

        /// <summary>
        /// A 32 bit unsigned integer value.
        /// </summary>
        UInt32 = 7,

        /// <summary>
        /// A 64 bit signed integer value.
        /// </summary>
        Int64 = 8,

        /// <summary>
        /// A 64 bit unsigned integer value.
        /// </summary>
        UInt64 = 9,

        /// <summary>
        /// An IEEE single precision (32 bit) floating point value.
        /// </summary>
        Float = 10,

        /// <summary>
        /// An IEEE double precision (64 bit) floating point value.
        /// </summary>
        Double = 11,

        /// <summary>
        /// A sequence of Unicode characters.
        /// </summary>
        String = 12,

        /// <summary>
        /// An instance in time.
        /// </summary>
        DateTime = 13,

        /// <summary>
        /// A 128-bit globally unique identifier.
        /// </summary>
        Guid = 14,

        /// <summary>
        /// A sequence of bytes.
        /// </summary>
        ByteString = 15,

        /// <summary>
        /// An XML element.
        /// </summary>
        XmlElement = 16,

        /// <summary>
        /// An identifier for a node in the address space of a UA server.
        /// </summary>
        NodeId = 17,

        /// <summary>
        /// A node id that stores the namespace URI instead of the namespace index.
        /// </summary>
        ExpandedNodeId = 18,

        /// <summary>
        /// A structured result code.
        /// </summary>
        StatusCode = 19,

        /// <summary>
        /// A string qualified with a namespace.
        /// </summary>
        QualifiedName = 20,

        /// <summary>
        /// A localized text string with an locale identifier.
        /// </summary>
        LocalizedText = 21,

        /// <summary>
        /// An opaque object with a syntax that may be unknown to the receiver.
        /// </summary>
        ExtensionObject = 22,

        /// <summary>
        /// A data value with an associated quality and timestamp.
        /// </summary>
        DataValue = 23,

        /// <summary>
        /// Any of the other built-in types.
        /// </summary>
        Variant = 24,

        /// <summary>
        /// A diagnostic information associated with a result code.
        /// </summary>
        DiagnosticInfo = 25,

        //
        // The following BuiltInTypes are for coding convenience
        // internally used in the .NET Standard library.
        // The enumerations are not used for encoding/decoding.
        //

        /// <summary>
        /// Any numeric value.
        /// </summary>
        Number = 26,

        /// <summary>
        /// A signed integer.
        /// </summary>
        Integer = 27,

        /// <summary>
        /// An unsigned integer.
        /// </summary>
        UInteger = 28,

        /// <summary>
        /// An enumerated value
        /// </summary>
        Enumeration = 29
    }
}
