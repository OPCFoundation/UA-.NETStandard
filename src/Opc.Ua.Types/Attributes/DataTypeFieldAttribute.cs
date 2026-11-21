// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Data type field defines the fields of the data type.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false)]
    public sealed class DataTypeFieldAttribute : Attribute
    {
        /// <summary>
        /// Order during encoding or decoding. Order is important for
        /// binary encoding and the field should always be specified
        /// to ensure ordering is not left to the compiler.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// The name of the field in the structure if different from
        /// the name of the property. Used during serialization and
        /// deserialization.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Controls how IEncodeable fields are encoded.
        /// <see cref="StructureHandling.Auto"/> (default) lets the
        /// generator decide.
        /// <see cref="StructureHandling.Inline"/> forces
        /// WriteEncodeable/ReadEncodeable.
        /// <see cref="StructureHandling.ExtensionObject"/> forces
        /// ExtensionObject wrapping.
        /// Only applicable to IEncodeable fields.
        /// </summary>
        public StructureHandling StructureHandling { get; set; }

        /// <summary>
        /// Controls default value handling during encode/decode.
        /// <see cref="DefaultValueHandling.Exclude"/> (default)
        /// omits type defaults on write and preserves constructor
        /// defaults on read when the field is absent from XML (a
        /// field absent from JSON is the type default).
        /// <see cref="DefaultValueHandling.Include"/> always
        /// writes and reads.
        /// </summary>
        public DefaultValueHandling DefaultValueHandling { get; set; }

        /// <summary>
        /// Indicates whether the field is required.
        /// Reserved for future use in optional-field structures.
        /// </summary>
        public bool IsRequired { get; set; }
    }
}
