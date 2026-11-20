/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// The type interface represents a type in the OPC UA
    /// type system. It is used to represent built-in types,
    /// enumerations, and structures (encodeable types) in
    /// a common way
    /// </summary>
    public interface IType
    {
        /// <summary>
        /// The underlying .net type of the type in the
        /// OPC UA type system.
        /// </summary>
        /// <remarks>
        /// This type is not guaranteed to be useable for
        /// construction or reflection as it might have
        /// been trimmed or reflection is not supported
        /// on the platform (e.g. NativeAoT).
        /// </remarks>
        Type Type { get; }

        /// <summary>
        /// Get the xml qualified name for the OPC UA
        /// type. This value is used during registration
        /// and to perform lookups in the type registry
        /// by Xml parsers.
        /// </summary>
        XmlQualifiedName XmlName { get; }
    }

    /// <summary>
    /// Built in type or sub datatype of a built in type.
    /// </summary>
    public interface IBuiltInType : IType
    {
        /// <summary>
        /// The base built-in type identifier of this
        /// primitive data type.
        /// </summary>
        BuiltInType BuiltInType { get; }
    }

    /// <summary>
    /// Enumeration type (Built-in type Enumeration)
    /// </summary>
    public interface IEnumeratedType : IType
    {
        /// <summary>
        /// Get the default value
        /// </summary>
        EnumValue Default { get; }

        /// <summary>
        /// Get symbol for the value
        /// </summary>
        bool TryGetSymbol(int value, out string? symbol);

        /// <summary>
        /// Get value for the symbol
        /// </summary>
        bool TryGetValue(string symbol, out int value);
    }

    /// <summary>
    /// Structure type (Built-in type UserDefinedType)
    /// Represents a encodeable structure (complex data
    /// type) that implements <see cref="IEncodeable"/>.
    /// </summary>
    public interface IEncodeableType : IType
    {
        /// <summary>
        /// Create instance of the encodeable type during
        /// decoding and to create a default value when a
        /// null value needs to be encoded by BinaryEncoder.
        /// </summary>
        /// <returns></returns>
        IEncodeable CreateInstance();
    }
}
