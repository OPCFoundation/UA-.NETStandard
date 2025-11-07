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
    /// Permission type
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [Flags]
    public enum PermissionType : uint
    {
        /// <summary>
        /// None
        /// </summary>
        [EnumMember(Value = "None_0")]
        None = 0,

        /// <summary>
        /// Browse
        /// </summary>
        [EnumMember(Value = "Browse_1")]
        Browse = 1,

        /// <summary>
        /// Read role permissions
        /// </summary>
        [EnumMember(Value = "ReadRolePermissions_2")]
        ReadRolePermissions = 2,

        /// <summary>
        /// Write attribute
        /// </summary>
        [EnumMember(Value = "WriteAttribute_4")]
        WriteAttribute = 4,

        /// <summary>
        /// Write role permissions
        /// </summary>
        [EnumMember(Value = "WriteRolePermissions_8")]
        WriteRolePermissions = 8,

        /// <summary>
        /// Write historizing
        /// </summary>
        [EnumMember(Value = "WriteHistorizing_16")]
        WriteHistorizing = 16,

        /// <summary>
        /// Read
        /// </summary>
        [EnumMember(Value = "Read_32")]
        Read = 32,

        /// <summary>
        /// Write
        /// </summary>
        [EnumMember(Value = "Write_64")]
        Write = 64,

        /// <summary>
        /// Read history
        /// </summary>
        [EnumMember(Value = "ReadHistory_128")]
        ReadHistory = 128,

        /// <summary>
        /// Insert history
        /// </summary>
        [EnumMember(Value = "InsertHistory_256")]
        InsertHistory = 256,

        /// <summary>
        /// Modify history
        /// </summary>
        [EnumMember(Value = "ModifyHistory_512")]
        ModifyHistory = 512,

        /// <summary>
        /// Delete history
        /// </summary>
        [EnumMember(Value = "DeleteHistory_1024")]
        DeleteHistory = 1024,

        /// <summary>
        /// Receive events
        /// </summary>
        [EnumMember(Value = "ReceiveEvents_2048")]
        ReceiveEvents = 2048,

        /// <summary>
        /// Call
        /// </summary>
        [EnumMember(Value = "Call_4096")]
        Call = 4096,

        /// <summary>
        /// Add reference
        /// </summary>
        [EnumMember(Value = "AddReference_8192")]
        AddReference = 8192,

        /// <summary>
        /// Remove reference
        /// </summary>
        [EnumMember(Value = "RemoveReference_16384")]
        RemoveReference = 16384,

        /// <summary>
        /// Delete node
        /// </summary>
        [EnumMember(Value = "DeleteNode_32768")]
        DeleteNode = 32768,

        /// <summary>
        /// Add node
        /// </summary>
        [EnumMember(Value = "AddNode_65536")]
        AddNode = 65536
    }
}
