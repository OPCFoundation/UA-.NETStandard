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
        AccessLevelEx = 33554432
    }
}
