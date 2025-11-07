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

using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Browse direction
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public enum BrowseDirection
    {
        /// <summary>
        /// Forward
        /// </summary>
        [EnumMember(Value = "Forward_0")]
        Forward = 0,

        /// <summary>
        /// Inverse
        /// </summary>
        [EnumMember(Value = "Inverse_1")]
        Inverse = 1,

        /// <summary>
        /// Both directions
        /// </summary>
        [EnumMember(Value = "Both_2")]
        Both = 2,

        /// <summary>
        /// Invalid value
        /// </summary>
        [EnumMember(Value = "Invalid_3")]
        Invalid = 3
    }
}
