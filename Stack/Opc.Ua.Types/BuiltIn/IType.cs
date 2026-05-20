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
using System.Diagnostics.CodeAnalysis;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// The type interface represents a type in the OPC UA
    /// type system. It is used to represent built-in types,
    /// enumerations, and structures (encodeable types) in
    /// a common way
    /// </summary>
    [Experimental("UA_NETStandard_1")]
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
    [Experimental("UA_NETStandard_1")]
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
    [Experimental("UA_NETStandard_1")]
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
    [Experimental("UA_NETStandard_1")]
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
