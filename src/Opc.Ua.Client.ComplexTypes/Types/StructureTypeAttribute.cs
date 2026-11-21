// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Attribute for type ids of a structure definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class StructureTypeIdAttribute : Attribute
    {
        /// <summary>
        /// The complex type id attribute.
        /// </summary>
        public string? ComplexTypeId { get; set; }

        /// <summary>
        /// The binary encoding id attribute.
        /// </summary>
        public string? BinaryEncodingId { get; set; }

        /// <summary>
        /// The xml encoding id attribute.
        /// </summary>
        public string? XmlEncodingId { get; set; }
    }
}
