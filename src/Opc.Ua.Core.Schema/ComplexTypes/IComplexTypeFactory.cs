/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua
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
