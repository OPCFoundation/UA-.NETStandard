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
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Node class
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [Flags]
    public enum NodeClass
    {
        /// <summary>
        /// Unspecified node class.
        /// </summary>
        [EnumMember(Value = "Unspecified_0")]
        Unspecified = 0,

        /// <summary>
        /// Object class
        /// </summary>
        [EnumMember(Value = "Object_1")]
        Object = 1,

        /// <summary>
        /// Variable node class
        /// </summary>
        [EnumMember(Value = "Variable_2")]
        Variable = 2,

        /// <summary>
        /// Method node class
        /// </summary>
        [EnumMember(Value = "Method_4")]
        Method = 4,

        /// <summary>
        /// Object type node class
        /// </summary>
        [EnumMember(Value = "ObjectType_8")]
        ObjectType = 8,

        /// <summary>
        /// Variable type node class
        /// </summary>
        [EnumMember(Value = "VariableType_16")]
        VariableType = 16,

        /// <summary>
        /// Reference type node class
        /// </summary>
        [EnumMember(Value = "ReferenceType_32")]
        ReferenceType = 32,

        /// <summary>
        /// Data type node class
        /// </summary>
        [EnumMember(Value = "DataType_64")]
        DataType = 64,

        /// <summary>
        /// View node class
        /// </summary>
        [EnumMember(Value = "View_128")]
        View = 128
    }
}
