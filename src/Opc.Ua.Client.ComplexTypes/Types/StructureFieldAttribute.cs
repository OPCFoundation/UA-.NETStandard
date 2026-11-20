/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Attribute for a base complex type field definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class StructureFieldAttribute : Attribute
    {
        /// <summary>
        /// Initialize a field attribute with defaults.
        /// </summary>
        public StructureFieldAttribute()
        {
            ValueRank = -1;
            MaxStringLength = 0;
            IsOptional = false;
            BuiltInType = 0;
        }

        /// <summary>
        /// The value rank of the field.
        /// </summary>
        public int ValueRank { get; set; }

        /// <summary>
        /// The maximum string length of the field.
        /// </summary>
        public uint MaxStringLength { get; set; }

        /// <summary>
        /// If the field is optional.
        /// </summary>
        public bool IsOptional { get; set; }

        /// <summary>
        /// The datatype of a field as BuiltInType.
        /// </summary>
        public int BuiltInType { get; set; }

        /// <summary>
        /// The declared DataType of an encodeable field as a namespace-uri
        /// qualified ExpandedNodeId string, or <c>null</c> if the DataType is
        /// given by the field type.
        /// </summary>
        public string? DataTypeId { get; set; }
    }
}
