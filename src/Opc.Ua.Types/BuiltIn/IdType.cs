// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Node Id type
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    public enum IdType
    {
        /// <summary>
        /// Numeric
        /// </summary>
        [EnumMember(Value = "Numeric_0")]
        Numeric = 0,

        /// <summary>
        /// String type
        /// </summary>
        [EnumMember(Value = "String_1")]
        String = 1,

        /// <summary>
        /// Guid
        /// </summary>
        [EnumMember(Value = "Guid_2")]
        Guid = 2,

        /// <summary>
        /// Opaque
        /// </summary>
        [EnumMember(Value = "Opaque_3")]
        Opaque = 3
    }
}
