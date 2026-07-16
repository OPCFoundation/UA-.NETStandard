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
    /// Extension methods that populate a <see cref="DataTypeDefinitionRegistry"/>
    /// from OPC UA address-space nodes.
    /// </summary>
    public static class DataTypeDefinitionRegistryExtensions
    {
        /// <summary>
        /// Registers the data type definition carried by an address-space
        /// <see cref="DataTypeNode"/> (for example a node obtained by browsing a
        /// server or from the client node cache) so a schema can be generated
        /// for it.
        /// </summary>
        /// <param name="registry">The registry to add the data type to.</param>
        /// <param name="node">The data type node.</param>
        /// <param name="namespaceUris">The namespace table used to resolve the
        /// node namespace uri. May be <c>null</c>.</param>
        /// <returns>
        /// <c>true</c> when the node carried a usable structure or enum
        /// definition and was added; otherwise <c>false</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public static bool TryAddDataType(
            this DataTypeDefinitionRegistry registry,
            DataTypeNode node,
            NamespaceTable? namespaceUris = null)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.NodeId.IsNull ||
                node.DataTypeDefinition.IsNull ||
                !node.DataTypeDefinition.TryGetValue(out DataTypeDefinition? definition))
            {
                return false;
            }

            string namespaceUri = namespaceUris?.GetString(node.NodeId.NamespaceIndex) ?? string.Empty;
            registry.Add(new UaTypeDescription(
                new ExpandedNodeId(node.NodeId),
                node.BrowseName,
                definition,
                namespaceUri));
            return true;
        }
    }
}
