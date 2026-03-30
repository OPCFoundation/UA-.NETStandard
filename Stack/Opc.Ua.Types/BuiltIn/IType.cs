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
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua.Types;
using System.Text.Json.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Type
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public interface IType
    {
        /// <summary>
        /// System type (either enum or reference type)
        /// Used when using reflection emit to emit other
        /// encodeable types.
        /// </summary>
        Type Type { get; }

        /// <summary>
        /// Get the xml qualified name for the type.
        /// </summary>
        XmlQualifiedName XmlName { get; }
    }

    /// <summary>
    /// Built in type
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public interface IBuiltInType : IType
    {
        /// <summary>
        /// Build in type identifier
        /// </summary>
        BuiltInType BuiltInType { get; }
    }

    /// <summary>
    /// Enumeration type
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public interface IEnumeratedType : IType
    {
    }

    /// <summary>
    /// Represents a real encodeable object factory managed
    /// by the encodeable factory registry.
    /// </summary>
    [Experimental("UA_NETStandard_1")]
    public interface IEncodeableType : IType
    {
        /// <summary>
        /// Create instance of structure type during
        /// decoding. Will change in future iterations.
        /// </summary>
        /// <returns></returns>
        IEncodeable CreateInstance();
    }
}
