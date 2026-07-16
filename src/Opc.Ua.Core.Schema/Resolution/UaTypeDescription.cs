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

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Describes an OPC UA data type for schema generation. It bundles the
    /// type identifier, its browse name and its runtime structure definition
    /// (<see cref="StructureDefinition"/> or <see cref="EnumDefinition"/>).
    /// </summary>
    public sealed class UaTypeDescription
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UaTypeDescription"/> class.
        /// </summary>
        /// <param name="typeId">The data type identifier.</param>
        /// <param name="browseName">The browse name of the data type.</param>
        /// <param name="definition">The runtime structure or enum definition.</param>
        /// <param name="namespaceUri">The namespace uri of the data type. When
        /// omitted the namespace uri of <paramref name="typeId"/> is used.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <c>null</c>.</exception>
        public UaTypeDescription(
            ExpandedNodeId typeId,
            QualifiedName browseName,
            DataTypeDefinition definition,
            string? namespaceUri = null)
        {
            TypeId = typeId;
            BrowseName = browseName;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            NamespaceUri = string.IsNullOrEmpty(namespaceUri)
                ? typeId.NamespaceUri ?? string.Empty
                : namespaceUri!;
        }

        /// <summary>
        /// The data type identifier.
        /// </summary>
        public ExpandedNodeId TypeId { get; }

        /// <summary>
        /// The browse name of the data type.
        /// </summary>
        public QualifiedName BrowseName { get; }

        /// <summary>
        /// The runtime structure or enum definition of the data type.
        /// </summary>
        public DataTypeDefinition Definition { get; }

        /// <summary>
        /// The namespace uri of the data type.
        /// </summary>
        public string NamespaceUri { get; }

        /// <summary>
        /// The local name of the data type used as the schema type name.
        /// </summary>
        public string Name
            => !BrowseName.IsNull && !string.IsNullOrEmpty(BrowseName.Name)
                ? BrowseName.Name!
                : "UnnamedType";
    }
}
