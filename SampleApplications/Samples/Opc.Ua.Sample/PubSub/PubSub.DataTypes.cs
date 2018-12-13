/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace Opc.Ua.PubSub
{
    #region PermissionType Enumeration
    #if (!OPCUA_EXCLUDE_PermissionType)
    /// <summary>
    /// A description for the PermissionType DataType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [DataContract(Namespace = Opc.Ua.Namespaces.OpcUaXsd)]
    public enum PermissionType
    {
        /// <summary>
        /// A description for the None field.
        /// </summary>
        [EnumMember(Value = "None_0")]
        None = 0,

        /// <summary>
        /// A description for the Browse field.
        /// </summary>
        [EnumMember(Value = "Browse_1")]
        Browse = 1,

        /// <summary>
        /// A description for the ReadRolePermissions field.
        /// </summary>
        [EnumMember(Value = "ReadRolePermissions_2")]
        ReadRolePermissions = 2,

        /// <summary>
        /// A description for the WriteAttribute field.
        /// </summary>
        [EnumMember(Value = "WriteAttribute_4")]
        WriteAttribute = 4,

        /// <summary>
        /// A description for the WriteRolePermissions field.
        /// </summary>
        [EnumMember(Value = "WriteRolePermissions_8")]
        WriteRolePermissions = 8,

        /// <summary>
        /// A description for the WriteHistorizing field.
        /// </summary>
        [EnumMember(Value = "WriteHistorizing_16")]
        WriteHistorizing = 16,

        /// <summary>
        /// A description for the Read field.
        /// </summary>
        [EnumMember(Value = "Read_32")]
        Read = 32,

        /// <summary>
        /// A description for the Write field.
        /// </summary>
        [EnumMember(Value = "Write_64")]
        Write = 64,

        /// <summary>
        /// A description for the ReadHistory field.
        /// </summary>
        [EnumMember(Value = "ReadHistory_128")]
        ReadHistory = 128,

        /// <summary>
        /// A description for the InsertHistory field.
        /// </summary>
        [EnumMember(Value = "InsertHistory_256")]
        InsertHistory = 256,

        /// <summary>
        /// A description for the ModifyHistory field.
        /// </summary>
        [EnumMember(Value = "ModifyHistory_512")]
        ModifyHistory = 512,

        /// <summary>
        /// A description for the DeleteHistory field.
        /// </summary>
        [EnumMember(Value = "DeleteHistory_1024")]
        DeleteHistory = 1024,

        /// <summary>
        /// A description for the ReceiveEvents field.
        /// </summary>
        [EnumMember(Value = "ReceiveEvents_2048")]
        ReceiveEvents = 2048,

        /// <summary>
        /// A description for the Call field.
        /// </summary>
        [EnumMember(Value = "Call_4096")]
        Call = 4096,

        /// <summary>
        /// A description for the AddReference field.
        /// </summary>
        [EnumMember(Value = "AddReference_8192")]
        AddReference = 8192,

        /// <summary>
        /// A description for the RemoveReference field.
        /// </summary>
        [EnumMember(Value = "RemoveReference_16384")]
        RemoveReference = 16384,

        /// <summary>
        /// A description for the DeleteNode field.
        /// </summary>
        [EnumMember(Value = "DeleteNode_32768")]
        DeleteNode = 32768,

        /// <summary>
        /// A description for the AddNode field.
        /// </summary>
        [EnumMember(Value = "AddNode_65536")]
        AddNode = 65536,

        /// <summary>
        /// A description for the All field.
        /// </summary>
        [EnumMember(Value = "All_131071")]
        All = 131071,
    }
#endif
    #endregion

}