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

using System.Collections.Generic;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Factory class for the complex type builder.
    /// </summary>
    public interface IComplexTypeFactory
    {
        /// <summary>
        /// Create a new type builder instance for this factory.
        /// </summary>
        IComplexTypeBuilder Create(
            string targetNamespace,
            int targetNamespaceIndex,
            string? moduleName = null);

        /// <summary>
        /// Types defined in the factory.
        /// </summary>
        IReadOnlyList<IType> GetTypes();
    }

    /// <summary>
    /// Interface to dynamically build custom
    /// enum types and structured types.
    /// </summary>
    public interface IComplexTypeBuilder
    {
        /// <summary>
        /// Target namespace information.
        /// </summary>
        string TargetNamespace { get; }

        /// <summary>
        /// Target namespace index.
        /// </summary>
        int TargetNamespaceIndex { get; }

        /// <summary>
        /// Create an enum type from an EnumDefinition in an ExtensionObject.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        IEnumeratedType AddEnumType(
            QualifiedName typeName,
            EnumDefinition enumDefinition);

        /// <summary>
        /// Create a complex type from a StructureDefinition.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        IComplexTypeFieldBuilder AddStructuredType(
            QualifiedName name,
            StructureDefinition structureDefinition);

        /// <summary>
        /// Create an OptionSet type for a concrete sub-type of the
        /// abstract <c>OptionSet</c> DataType. The wire format is the
        /// inherited <c>Value</c>/<c>ValidBits</c> ByteStrings; the
        /// <paramref name="enumDefinition"/> names the bits and is
        /// either obtained from the DataTypeDefinition attribute or
        /// synthesized from the <c>OptionSetValues</c> property.
        /// </summary>
        IEncodeableType AddOptionSetType(
            QualifiedName typeName,
            ExpandedNodeId typeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            ExpandedNodeId jsonEncodingId,
            EnumDefinition enumDefinition);
    }

    /// <summary>
    /// Interface to build property fields.
    /// </summary>
    public interface IComplexTypeFieldBuilder
    {
        /// <summary>
        /// Build the StructureTypeId attribute for a complex type.
        /// </summary>
        void AddTypeIdAttribute(
            ExpandedNodeId complexTypeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId);

        /// <summary>
        /// Add a field of a class with get and set.
        /// </summary>
        void AddField(
            StructureField field,
            IType? fieldType,
            int order,
            bool allowSubTypes);

        /// <summary>
        /// The type of the structure of the field.
        /// </summary>
        IEncodeableType GetStructureType();

        /// <summary>
        /// Finish the type creation and returns the new type.
        /// </summary>
        IEncodeableType CreateType();
    }
}
