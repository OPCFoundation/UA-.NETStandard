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
    /// Access restrictions
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    [Flags]
    public enum AccessRestrictionType : ushort
    {
        /// <summary>
        /// No restrictions
        /// </summary>
        [EnumMember(Value = "None_0")]
        None = 0,

        /// <summary>
        /// Signing required
        /// </summary>
        [EnumMember(Value = "SigningRequired_1")]
        SigningRequired = 1,

        /// <summary>
        /// Encryption required
        /// </summary>
        [EnumMember(Value = "EncryptionRequired_2")]
        EncryptionRequired = 2,

        /// <summary>
        /// Session required
        /// </summary>
        [EnumMember(Value = "SessionRequired_4")]
        SessionRequired = 4,

        /// <summary>
        /// Apply restrictions to browse
        /// </summary>
        [EnumMember(Value = "ApplyRestrictionsToBrowse_8")]
        ApplyRestrictionsToBrowse = 8,
    }
}
