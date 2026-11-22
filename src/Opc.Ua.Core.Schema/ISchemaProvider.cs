// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Produces schemas for OPC UA data types in the supported encodings
    /// (XSD, OPC Binary and JSON Schema). Resolve the provider from dependency
    /// injection or construct it directly.
    /// </summary>
    public interface ISchemaProvider
    {
        /// <summary>
        /// Creates a schema for the supplied data type description.
        /// </summary>
        /// <param name="type">The data type to generate a schema for.</param>
        /// <param name="format">The schema format to generate.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns>The generated schema.</returns>
        IUaSchema CreateSchema(
            UaTypeDescription type,
            UaSchemaFormat format,
            UaSchemaScope scope = UaSchemaScope.Type);

        /// <summary>
        /// Resolves the supplied data type id and creates a schema for it.
        /// </summary>
        /// <param name="typeId">The data type id.</param>
        /// <param name="format">The schema format to generate.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <param name="schema">The generated schema.</param>
        /// <returns><c>true</c> when the type was resolved and a schema produced.</returns>
        bool TryGetSchema(
            ExpandedNodeId typeId,
            UaSchemaFormat format,
            UaSchemaScope scope,
            [NotNullWhen(true)] out IUaSchema? schema);
    }
}
