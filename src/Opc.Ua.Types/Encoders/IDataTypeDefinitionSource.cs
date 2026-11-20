/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Implemented by encodeable and enumerated type activators, including
    /// generated activators, that can expose the OPC UA data type definition
    /// (a <see cref="StructureDefinition"/> or <see cref="EnumDefinition"/>)
    /// of the type they represent. Schema generation uses this to obtain a
    /// type's structure from the encodeable type registry without reflection.
    /// </summary>
    public interface IDataTypeDefinitionSource
    {
        /// <summary>
        /// Returns the data type definition of the type. Implementations
        /// always return a non-null definition (a
        /// <see cref="StructureDefinition"/> or an
        /// <see cref="EnumDefinition"/>).
        /// </summary>
        /// <param name="namespaceUris">
        /// The namespace table used to resolve the namespace indexes of the
        /// referenced data type ids in the returned definition.
        /// </param>
        /// <returns>The data type definition. Never <c>null</c>.</returns>
        DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris);
    }
}
