// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Structure type
    /// </summary>
    [DataContract(Namespace = Types.Namespaces.OpcUaXsd)]
    public enum StructureType
    {
        /// <summary>
        /// Structure type
        /// </summary>
        [EnumMember(Value = "Structure_0")]
        Structure = 0,

        /// <summary>
        /// Structure with optional fields
        /// </summary>
        [EnumMember(Value = "StructureWithOptionalFields_1")]
        StructureWithOptionalFields = 1,

        /// <summary>
        /// Union type
        /// </summary>
        [EnumMember(Value = "Union_2")]
        Union = 2,

        /// <summary>
        /// Structure with subtyped values
        /// </summary>
        [EnumMember(Value = "StructureWithSubtypedValues_3")]
        StructureWithSubtypedValues = 3,

        /// <summary>
        /// Union with subtyped values
        /// </summary>
        [EnumMember(Value = "UnionWithSubtypedValues_4")]
        UnionWithSubtypedValues = 4
    }
}
