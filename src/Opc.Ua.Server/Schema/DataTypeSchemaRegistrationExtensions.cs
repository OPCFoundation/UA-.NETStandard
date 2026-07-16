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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Schema;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Extension methods that register server-side data type nodes with the
    /// schema generation registry.
    /// </summary>
    public static class DataTypeSchemaRegistrationExtensions
    {
        /// <summary>
        /// Registers all known data type nodes from a running server's type tree
        /// into a schema generation registry.
        /// </summary>
        /// <param name="server">The server to inspect.</param>
        /// <param name="registry">The registry to populate.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The number of data types that were registered.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public static async ValueTask<int> RegisterDataTypeSchemasAsync(
            this IServerInternal server,
            DataTypeDefinitionRegistry registry,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            int count = 0;
            var visited = new HashSet<NodeId>();
            var pending = new Stack<NodeId>();
            pending.Push(DataTypeIds.BaseDataType);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId typeId = pending.Pop();

                if (!visited.Add(typeId))
                {
                    continue;
                }

                if (await RegisterDataTypeSchemaAsync(server, typeId, registry, cancellationToken)
                    .ConfigureAwait(false))
                {
                    count++;
                }

                ArrayOf<NodeId> subtypes = server.TypeTree.FindSubTypes(typeId);
                for (int ii = 0; ii < subtypes.Count; ii++)
                {
                    pending.Push(subtypes[ii]);
                }
            }

            return count;
        }

        /// <summary>
        /// Registers all data type states in a server-side node collection into a
        /// schema generation registry.
        /// </summary>
        /// <param name="nodes">The server-side nodes to inspect.</param>
        /// <param name="registry">The registry to populate.</param>
        /// <param name="namespaceUris">The namespace table used to resolve namespace URIs.</param>
        /// <returns>The number of data types that were registered.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public static int RegisterDataTypeSchemas(
            this IEnumerable<NodeState> nodes,
            DataTypeDefinitionRegistry registry,
            NamespaceTable? namespaceUris = null)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            int count = 0;
            foreach (NodeState node in nodes)
            {
                if (node is DataTypeState dataType && dataType.TryRegisterDataTypeSchema(registry, namespaceUris))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Registers a server-side data type state into a schema generation registry.
        /// </summary>
        /// <param name="node">The data type state to register.</param>
        /// <param name="registry">The registry to populate.</param>
        /// <param name="namespaceUris">The namespace table used to resolve the namespace URI.</param>
        /// <returns><c>true</c> when the data type definition was registered; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public static bool TryRegisterDataTypeSchema(
            this DataTypeState node,
            DataTypeDefinitionRegistry registry,
            NamespaceTable? namespaceUris = null)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            return registry.TryAddDataType(ToDataTypeNode(node), namespaceUris);
        }

        private static async ValueTask<bool> RegisterDataTypeSchemaAsync(
            IServerInternal server,
            NodeId typeId,
            DataTypeDefinitionRegistry registry,
            CancellationToken cancellationToken)
        {
            NodeState? state = await server.NodeManager
                .FindNodeInAddressSpaceAsync(typeId, cancellationToken)
                .ConfigureAwait(false);

            if (state is DataTypeState dataType)
            {
                return dataType.TryRegisterDataTypeSchema(registry, server.NamespaceUris);
            }

            return await ReadAndRegisterDataTypeSchemaAsync(server, typeId, registry, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async ValueTask<bool> ReadAndRegisterDataTypeSchemaAsync(
            IServerInternal server,
            NodeId typeId,
            DataTypeDefinitionRegistry registry,
            CancellationToken cancellationToken)
        {
            var context = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = typeId,
                    AttributeId = Attributes.BrowseName
                },
                new ReadValueId
                {
                    NodeId = typeId,
                    AttributeId = Attributes.DataTypeDefinition
                }
            ];

            (ArrayOf<DataValue> values, _) = await server.NodeManager
                .ReadAsync(context, 0, TimestampsToReturn.Neither, nodesToRead, cancellationToken)
                .ConfigureAwait(false);

            if (values.Count != nodesToRead.Count ||
                StatusCode.IsBad(values[0].StatusCode) ||
                StatusCode.IsBad(values[1].StatusCode) ||
                !values[0].WrappedValue.TryGetValue(out QualifiedName browseName) ||
                browseName.IsNull ||
                !values[1].WrappedValue.TryGetValue(out ExtensionObject dataTypeDefinition) ||
                dataTypeDefinition.IsNull)
            {
                return false;
            }

            var node = new DataTypeNode
            {
                NodeId = typeId,
                BrowseName = browseName,
                DataTypeDefinition = dataTypeDefinition
            };

            return registry.TryAddDataType(node, server.NamespaceUris);
        }

        private static DataTypeNode ToDataTypeNode(DataTypeState node)
        {
            return new DataTypeNode
            {
                NodeId = node.NodeId,
                BrowseName = node.BrowseName,
                DataTypeDefinition = node.DataTypeDefinition
            };
        }
    }
}
