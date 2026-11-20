// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Browse direction
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
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
