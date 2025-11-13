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
    /// Access level extended type
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [Flags]
    public enum AccessLevelExType : uint
    {
        /// <summary>
        /// No extensions
        /// </summary>
        [EnumMember(Value = "None_0")]
        None = 0,

        /// <summary>
        /// Current read
        /// </summary>
        [EnumMember(Value = "CurrentRead_1")]
        CurrentRead = 1,

        /// <summary>
        /// Current write
        /// </summary>
        [EnumMember(Value = "CurrentWrite_2")]
        CurrentWrite = 2,

        /// <summary>
        /// History read
        /// </summary>
        [EnumMember(Value = "HistoryRead_4")]
        HistoryRead = 4,

        /// <summary>
        /// History write
        /// </summary>
        [EnumMember(Value = "HistoryWrite_8")]
        HistoryWrite = 8,

        /// <summary>
        /// Semantic change
        /// </summary>
        [EnumMember(Value = "SemanticChange_16")]
        SemanticChange = 16,

        /// <summary>
        /// Status write
        /// </summary>
        [EnumMember(Value = "StatusWrite_32")]
        StatusWrite = 32,

        /// <summary>
        /// Timestamp write
        /// </summary>
        [EnumMember(Value = "TimestampWrite_64")]
        TimestampWrite = 64,

        /// <summary>
        /// Non atomic read
        /// </summary>
        [EnumMember(Value = "NonatomicRead_256")]
        NonatomicRead = 256,

        /// <summary>
        /// Non atomic write
        /// </summary>
        [EnumMember(Value = "NonatomicWrite_512")]
        NonatomicWrite = 512,

        /// <summary>
        /// Write full array only
        /// </summary>
        [EnumMember(Value = "WriteFullArrayOnly_1024")]
        WriteFullArrayOnly = 1024,

        /// <summary>
        /// No sub data types
        /// </summary>
        [EnumMember(Value = "NoSubDataTypes_2048")]
        NoSubDataTypes = 2048,

        /// <summary>
        /// Non volatile
        /// </summary>
        [EnumMember(Value = "NonVolatile_4096")]
        NonVolatile = 4096,

        /// <summary>
        /// Constant
        /// </summary>
        [EnumMember(Value = "Constant_8192")]
        Constant = 8192
    }
}
