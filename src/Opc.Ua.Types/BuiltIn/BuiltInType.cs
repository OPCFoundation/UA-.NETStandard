/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
