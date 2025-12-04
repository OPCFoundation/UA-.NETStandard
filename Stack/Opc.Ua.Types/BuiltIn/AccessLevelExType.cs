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
