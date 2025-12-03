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
