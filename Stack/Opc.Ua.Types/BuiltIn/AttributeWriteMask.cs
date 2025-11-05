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
    /// Attribute write mask
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [Flags]
    public enum AttributeWriteMask : uint
    {
        /// <summary>
        /// None
        /// </summary>
        [EnumMember(Value = "None_0")]
        None = 0,

        /// <summary>
        /// Access level
        /// </summary>
        [EnumMember(Value = "AccessLevel_1")]
        AccessLevel = 1,

        /// <summary>
        /// Array dimensions
        /// </summary>
        [EnumMember(Value = "ArrayDimensions_2")]
        ArrayDimensions = 2,

        /// <summary>
        /// Browse name
        /// </summary>
        [EnumMember(Value = "BrowseName_4")]
        BrowseName = 4,

        /// <summary>
        /// Contains no loops
        /// </summary>
        [EnumMember(Value = "ContainsNoLoops_8")]
        ContainsNoLoops = 8,

        /// <summary>
        /// Data type
        /// </summary>
        [EnumMember(Value = "DataType_16")]
        DataType = 16,

        /// <summary>
        /// Description
        /// </summary>
        [EnumMember(Value = "Description_32")]
        Description = 32,

        /// <summary>
        /// Display name
        /// </summary>
        [EnumMember(Value = "DisplayName_64")]
        DisplayName = 64,

        /// <summary>
        /// Event notifier
        /// </summary>
        [EnumMember(Value = "EventNotifier_128")]
        EventNotifier = 128,

        /// <summary>
        /// Executable
        /// </summary>
        [EnumMember(Value = "Executable_256")]
        Executable = 256,

        /// <summary>
        /// Historizing
        /// </summary>
        [EnumMember(Value = "Historizing_512")]
        Historizing = 512,

        /// <summary>
        /// Inverse name
        /// </summary>
        [EnumMember(Value = "InverseName_1024")]
        InverseName = 1024,

        /// <summary>
        /// Is abstract
        /// </summary>
        [EnumMember(Value = "IsAbstract_2048")]
        IsAbstract = 2048,

        /// <summary>
        /// Minimum sampling interval
        /// </summary>
        [EnumMember(Value = "MinimumSamplingInterval_4096")]
        MinimumSamplingInterval = 4096,

        /// <summary>
        /// Node class
        /// </summary>
        [EnumMember(Value = "NodeClass_8192")]
        NodeClass = 8192,

        /// <summary>
        /// Node id
        /// </summary>
        [EnumMember(Value = "NodeId_16384")]
        NodeId = 16384,

        /// <summary>
        /// Symmetric
        /// </summary>
        [EnumMember(Value = "Symmetric_32768")]
        Symmetric = 32768,

        /// <summary>
        /// User access level
        /// </summary>
        [EnumMember(Value = "UserAccessLevel_65536")]
        UserAccessLevel = 65536,

        /// <summary>
        /// User executable
        /// </summary>
        [EnumMember(Value = "UserExecutable_131072")]
        UserExecutable = 131072,

        /// <summary>
        /// User write mask
        /// </summary>
        [EnumMember(Value = "UserWriteMask_262144")]
        UserWriteMask = 262144,

        /// <summary>
        /// Value rank
        /// </summary>
        [EnumMember(Value = "ValueRank_524288")]
        ValueRank = 524288,

        /// <summary>
        /// Write mask
        /// </summary>
        [EnumMember(Value = "WriteMask_1048576")]
        WriteMask = 1048576,

        /// <summary>
        /// Value of variable type
        /// </summary>
        [EnumMember(Value = "ValueForVariableType_2097152")]
        ValueForVariableType = 2097152,

        /// <summary>
        /// Data type definition
        /// </summary>
        [EnumMember(Value = "DataTypeDefinition_4194304")]
        DataTypeDefinition = 4194304,

        /// <summary>
        /// Role permission
        /// </summary>
        [EnumMember(Value = "RolePermissions_8388608")]
        RolePermissions = 8388608,

        /// <summary>
        /// Access restrictions
        /// </summary>
        [EnumMember(Value = "AccessRestrictions_16777216")]
        AccessRestrictions = 16777216,

        /// <summary>
        /// Access level ex
        /// </summary>
        [EnumMember(Value = "AccessLevelEx_33554432")]
        AccessLevelEx = 33554432,
    }
}
