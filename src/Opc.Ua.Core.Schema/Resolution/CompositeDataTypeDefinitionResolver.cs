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
    /// Aggregates multiple <see cref="IDataTypeDefinitionResolver"/> sources and
    /// resolves a type from the first source that knows it. Used to combine an
    /// explicit <see cref="DataTypeDefinitionRegistry"/> with, for example, an
    /// <see cref="EncodeableFactoryDefinitionSource"/>.
    /// </summary>
    public sealed class CompositeDataTypeDefinitionResolver : IDataTypeDefinitionResolver
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CompositeDataTypeDefinitionResolver"/> class.
        /// </summary>
        /// <param name="resolvers">The resolver sources, tried in order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resolvers"/> is <c>null</c>.</exception>
        public CompositeDataTypeDefinitionResolver(IEnumerable<IDataTypeDefinitionResolver> resolvers)
        {
            if (resolvers == null)
            {
                throw new ArgumentNullException(nameof(resolvers));
            }
            m_resolvers = [.. resolvers];
        }

        /// <inheritdoc/>
        public bool TryResolve(
            ExpandedNodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            for (int i = 0; i < m_resolvers.Count; i++)
            {
                if (m_resolvers[i].TryResolve(typeId, out description))
                {
                    return true;
                }
            }
            description = null;
            return false;
        }

        /// <inheritdoc/>
        public bool TryResolve(
            NodeId typeId,
            [NotNullWhen(true)] out UaTypeDescription? description)
        {
            for (int i = 0; i < m_resolvers.Count; i++)
            {
                if (m_resolvers[i].TryResolve(typeId, out description))
                {
                    return true;
                }
            }
            description = null;
            return false;
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<UaTypeDescription> GetNamespaceTypes(string namespaceUri)
        {
            var result = new List<UaTypeDescription>();
            var seen = new HashSet<NodeId>();
            for (int i = 0; i < m_resolvers.Count; i++)
            {
                foreach (UaTypeDescription description in m_resolvers[i].GetNamespaceTypes(namespaceUri))
                {
                    if (seen.Add(description.TypeId.InnerNodeId))
                    {
                        result.Add(description);
                    }
                }
            }
            return result;
        }

        private readonly List<IDataTypeDefinitionResolver> m_resolvers;
    }
}
