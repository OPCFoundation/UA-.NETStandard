// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// The known base complex types.
    /// </summary>
    public enum StructureBaseDataType
    {
        /// <summary>
        /// The type is a structure.
        /// </summary>
        Structure = 0,

        /// <summary>
        /// The type is an OptionSet.
        /// </summary>
        OptionSet = 1,

        /// <summary>
        /// The type is a Union.
        /// </summary>
        Union = 2
    }

    /// <summary>
    /// Attribute for a base complex type structure definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class StructureDefinitionAttribute : Attribute
    {
        /// <summary>
        /// Create the attribute for a structure definition.
        /// </summary>
        public StructureDefinitionAttribute()
        {
            StructureType = StructureType.Structure;
        }

        /// <summary>
        /// Convert the base type node id to a <see cref="StructureBaseDataType"/>.
        /// </summary>
        /// <param name="baseTypeId">The base type nodeId.</param>
        public static StructureBaseDataType FromBaseType(NodeId baseTypeId)
        {
            if (baseTypeId == DataTypeIds.Union)
            {
                return StructureBaseDataType.Union;
            }
            if (baseTypeId == DataTypeIds.OptionSet)
            {
                return StructureBaseDataType.OptionSet;
            }
            return StructureBaseDataType.Structure;
        }

        /// <summary>
        /// The default encoding Id.
        /// </summary>
        public string? DefaultEncodingId { get; set; }

        /// <summary>
        /// The base DataType.
        /// </summary>
        public StructureBaseDataType BaseDataType { get; set; }

        /// <summary>
        /// The structure type.
        /// </summary>
        public StructureType StructureType { get; set; }
    }
}
