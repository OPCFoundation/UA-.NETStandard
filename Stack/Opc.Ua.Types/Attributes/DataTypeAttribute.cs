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
    /// <para>
    /// Declares a type as an IEncodeable data type with the specified data type
    /// and encoding identifiers. The source generator will implement the IEncodeable
    /// interface with the given information and register the type as an encodeable.
    /// </para>
    /// <para>
    /// If the DataContractAttribute is also applied to the class then the source
    /// generator will only generate the IEncodeable implementation for its
    /// DataMember properties. The type does not need to be serializable.
    /// </para>
    /// <para>
    /// If a namespace uri is not supplied via a DataContractAttribute then the
    /// namespace of the DataType NodeId will be used.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DataTypeAttribute : Attribute
    {
        /// <summary>
        /// Creates a new encodeable attribute.
        /// </summary>
        /// <param name="dataTypeId">The type node id of the data type</param>
        /// <param name="binaryEncodingId">The binary encoding id</param>
        /// <param name="xmlEncodingId">The xml encoding id</param>
        /// <param name="jsonEncodingId">The json encoding id></param>
        public DataTypeAttribute(
            string dataTypeId,
            string? binaryEncodingId = null,
            string? xmlEncodingId = null,
            string? jsonEncodingId = null)
        {
            DataTypeId = dataTypeId;
            BinaryEncodingId = binaryEncodingId;
            XmlEncodingId = xmlEncodingId;
            JsonEncodingId = jsonEncodingId;
        }

        /// <summary>
        /// Data type identifier to use
        /// </summary>
        public string DataTypeId { get; }

        /// <summary>
        /// The binary encoding id
        /// </summary>
        public string? BinaryEncodingId { get; }

        /// <summary>
        /// The xml encoding id
        /// </summary>
        public string? XmlEncodingId { get; }

        /// <summary>
        /// Json encoding id
        /// </summary>
        public string? JsonEncodingId { get; }
    }
}
