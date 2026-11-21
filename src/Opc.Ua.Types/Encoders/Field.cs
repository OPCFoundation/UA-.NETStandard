// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Encoders
{
    /// <summary>
    /// Complex type property info.
    /// </summary>
    internal class Field : ICloneable, IStructureField
    {
        /// <summary>
        /// Create the property state of the structure field
        /// </summary>
        public Field(int order, StructureField fieldAttribute, BuiltInType builtInType)
        {
            Definition = fieldAttribute;
            Order = order;
            BuiltInType = builtInType;
            OptionalFieldMask = 0;
            Value = Variant.CreateDefault(TypeInfo);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        private Field(Field field)
        {
            Definition = field.Definition;
            Order = field.Order;
            BuiltInType = field.BuiltInType;
            OptionalFieldMask = field.OptionalFieldMask;
            Value = field.Value.Copy();
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return new Field(this);
        }

        /// <summary>
        /// Order in the list of properties for the complex type.
        /// </summary>
        public BuiltInType BuiltInType { get; }

        /// <summary>
        /// Order in the list of properties for the complex type.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Property value
        /// </summary>
        public Variant Value { get; set; }

        /// <summary>
        /// The structure field attributes of the complex type.
        /// </summary>
        public StructureField Definition { get; }

        /// <summary>
        /// Type information object for the complex type property.
        /// </summary>
        public TypeInfo TypeInfo => new(BuiltInType, Definition.ValueRank);

        /// <summary>
        /// Get the name of the complex type.
        /// </summary>
        public string? Name => Definition.Name;

        /// <summary>
        /// FieldState is optional
        /// </summary>
        public bool IsOptional => Definition.IsOptional;

        /// <summary>
        /// Optional mask for the field in the property.
        /// </summary>
        public uint OptionalFieldMask { get; set; }
    }
}
