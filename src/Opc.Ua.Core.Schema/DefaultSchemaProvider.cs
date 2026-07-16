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
        /// <param name="generators">The registered schema generators.</param>
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
