// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Schema
{
    /// <summary>
    /// A generator that produces a schema for a single supported
    /// <see cref="UaSchemaFormat"/>. Implementations can be registered with the
    /// dependency injection container or supplied directly to a
    /// <see cref="DefaultSchemaProvider"/> and are selected based on the
    /// requested format.
    /// </summary>
    public interface IUaSchemaGenerator
    {
        /// <summary>
        /// Returns whether the generator supports the requested format.
        /// </summary>
        /// <param name="format">The requested schema format.</param>
        /// <returns><c>true</c> when the format is supported.</returns>
        bool CanGenerate(UaSchemaFormat format);

        /// <summary>
        /// Generates the schema for the supplied data type description.
        /// </summary>
        /// <param name="type">The data type to generate a schema for.</param>
        /// <param name="resolver">The resolver used to look up referenced types.</param>
        /// <param name="format">The schema format to generate.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns>The generated schema.</returns>
        IUaSchema Generate(
            UaTypeDescription type,
            IDataTypeDefinitionResolver resolver,
            UaSchemaFormat format,
            UaSchemaScope scope);
    }
}
