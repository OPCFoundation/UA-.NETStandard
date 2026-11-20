/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Access restrictions
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
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
        ApplyRestrictionsToBrowse = 8
    }
}
