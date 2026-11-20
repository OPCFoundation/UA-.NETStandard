// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Schema
{
    /// <summary>
    /// The default <see cref="ISchemaProvider"/>. It dispatches schema
    /// generation to the registered <see cref="IUaSchemaGenerator"/> instances
    /// based on the requested <see cref="UaSchemaFormat"/>.
    /// </summary>
    public sealed class DefaultSchemaProvider : ISchemaProvider
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultSchemaProvider"/> class.
        /// </summary>
        /// <param name="resolver">The data type definition resolver.</param>
        /// <param name="generators">The schema generators.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public DefaultSchemaProvider(
            IDataTypeDefinitionResolver resolver,
            IEnumerable<IUaSchemaGenerator> generators)
        {
            m_resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            if (generators == null)
            {
                throw new ArgumentNullException(nameof(generators));
            }
            m_generators = [.. generators];
        }

        /// <inheritdoc/>
        public IUaSchema CreateSchema(
            UaTypeDescription type,
            UaSchemaFormat format,
            UaSchemaScope scope = UaSchemaScope.Type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            for (int i = 0; i < m_generators.Count; i++)
            {
                IUaSchemaGenerator generator = m_generators[i];
                if (generator.CanGenerate(format))
                {
                    return generator.Generate(type, m_resolver, format, scope);
                }
            }

            throw new NotSupportedException(
                $"No schema generator is registered for the format '{format}'.");
        }

        /// <inheritdoc/>
        public bool TryGetSchema(
            ExpandedNodeId typeId,
            UaSchemaFormat format,
            UaSchemaScope scope,
            [NotNullWhen(true)] out IUaSchema? schema)
        {
            if (m_resolver.TryResolve(typeId, out UaTypeDescription? type))
            {
                schema = CreateSchema(type, format, scope);
                return true;
            }
            schema = null;
            return false;
        }

        private readonly IDataTypeDefinitionResolver m_resolver;
        private readonly List<IUaSchemaGenerator> m_generators;
    }
}
