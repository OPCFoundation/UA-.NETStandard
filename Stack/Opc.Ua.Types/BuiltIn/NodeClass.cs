/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
