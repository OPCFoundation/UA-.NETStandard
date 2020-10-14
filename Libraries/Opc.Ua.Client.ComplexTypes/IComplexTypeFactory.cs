/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Factory class for the complex type builder.
    /// </summary>
    public abstract class IComplexTypeFactory
    {
        /// <summary>
        /// Create a new type builder instance for this factory.
        /// </summary>
        public abstract IComplexTypeBuilder Create(
            string targetNamespace,
            int targetNamespaceIndex,
            string moduleName = null);

        /// <summary>
        /// Types defined in the factory.
        /// </summary>
        public abstract Type[] GetTypes();
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
        /// Create an enum type from a binary schema definition.
        /// Available before OPC UA V1.04.
        /// </summary>
        Type AddEnumType(Schema.Binary.EnumeratedType enumeratedType);

        /// <summary>
        /// Create an enum type from an EnumDefinition in an ExtensionObject.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        Type AddEnumType(QualifiedName typeName, ExtensionObject typeDefinition);

        /// <summary>
        /// Create an enum type from an EnumValue property of a DataType node.
        /// Available before OPC UA V1.04.
        /// </summary>
        Type AddEnumType(QualifiedName typeName, ExtensionObject[] enumDefinition);

        /// <summary>
        /// Create an enum type from the EnumString array of a DataType node.
        /// Available before OPC UA V1.04.
        /// </summary>
        Type AddEnumType(QualifiedName typeName, LocalizedText[] enumDefinition);

        /// <summary>
        /// Create a complex type from a StructureDefinition.
        /// Available since OPC UA V1.04 in the DataTypeDefinition attribute.
        /// </summary>
        IComplexTypeFieldBuilder AddStructuredType(
            QualifiedName name,
            StructureDefinition structureDefinition);
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
            ExpandedNodeId xmlEncodingId
            );

        /// <summary>
        /// Create a property field of a class with get and set.
        /// </summary>
        void AddField(StructureField field, Type fieldType, int order);

        /// <summary>
        /// Finish the type creation and returns the new type.
        /// </summary>
        Type CreateType();
    }
}//namespace
