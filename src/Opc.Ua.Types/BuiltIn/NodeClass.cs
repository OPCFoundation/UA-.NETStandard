/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Node class
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
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
