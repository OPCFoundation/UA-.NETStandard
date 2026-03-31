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

#nullable enable

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
        /// Order during encoding or decoding.
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// The name of the field in the structure if
        /// different from the name of the property. Used
        /// during serialization and deserialization.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// If explicitly set to <c>true</c>, the field is encoded using
        /// <c>WriteEncodeable</c>/<c>ReadEncodeable</c> (exact type).
        /// If explicitly set to <c>false</c>, the field is encoded using
        /// <c>WriteEncodeableAsExtensionObject</c>/<c>ReadEncodeableAsExtensionObject</c>
        /// (allows subtyping).
        /// If not set (null), the generator decides automatically:
        /// <c>WriteEncodeable</c> is used if the field type is sealed
        /// and does not derive from another IEncodeable base type;
        /// otherwise <c>WriteEncodeableAsExtensionObject</c> is used.
        /// </summary>
        /// <remarks>
        /// Only applicable to fields whose type implements
        /// <see cref="IEncodeable"/>. Ignored for built-in types,
        /// enums, and arrays.
        /// </remarks>
        public object? ForceEncodeable { get; set; }
    }
}
