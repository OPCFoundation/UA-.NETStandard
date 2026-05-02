/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        public string Name => Definition.Name;

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
