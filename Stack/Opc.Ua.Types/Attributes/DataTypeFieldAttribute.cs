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
        /// omits defaults on write and preserves constructor
        /// defaults on read when field is absent.
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
