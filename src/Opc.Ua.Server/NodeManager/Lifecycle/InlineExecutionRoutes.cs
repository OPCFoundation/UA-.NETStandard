/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Tells, for one generation of the NodeManager routing table, whether every
    /// NodeManager a request can reach supports inline execution.
    /// </summary>
    /// <remarks>
    /// Read and Write call only the NodeManagers of the namespaces of their nodes,
    /// and every NodeManager for a namespace without a route. The other services
    /// that reach NodeManagers call every one of them, or follow references,
    /// continuation points and monitored items into any of them. The subscription
    /// services that only change the subscription itself, and discovery, reach
    /// none.
    /// </remarks>
    internal sealed class InlineExecutionRoutes
    {
        private InlineExecutionRoutes(bool all, bool[] namespaces)
        {
            All = all;
            m_namespaces = namespaces;
        }

        /// <summary>
        /// The routes of a routing table without NodeManagers.
        /// </summary>
        public static InlineExecutionRoutes Empty { get; } = new(true, []);

        /// <summary>
        /// Whether every NodeManager supports inline execution.
        /// </summary>
        public bool All { get; }

        /// <summary>
        /// Computes the routes of one generation of the routing table.
        /// </summary>
        /// <param name="nodeManagers">The NodeManagers Clients can reach.</param>
        /// <param name="namespaceManagers">The NodeManagers serving each namespace index.</param>
        public static InlineExecutionRoutes Create(
            IAsyncNodeManager[] nodeManagers,
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> namespaceManagers)
        {
            bool all = true;
            foreach (IAsyncNodeManager nodeManager in nodeManagers)
            {
                all &= SupportsInlineExecution(nodeManager);
            }
            if (all)
            {
                return Empty;
            }

            int count = 0;
            foreach (int index in namespaceManagers.Keys)
            {
                if (index >= count && index <= ushort.MaxValue)
                {
                    count = index + 1;
                }
            }
            bool[] namespaces = new bool[count];
            foreach (KeyValuePair<int, IReadOnlyList<IAsyncNodeManager>> route in namespaceManagers)
            {
                if (route.Key < 0 || route.Key >= count)
                {
                    continue;
                }
                bool supported = true;
                for (int ii = 0; ii < route.Value.Count; ii++)
                {
                    supported &= SupportsInlineExecution(route.Value[ii]);
                }
                namespaces[route.Key] = supported;
            }
            return new InlineExecutionRoutes(false, namespaces);
        }

        /// <summary>
        /// Whether a NodeManager declares that it supports inline execution.
        /// </summary>
        public static bool SupportsInlineExecution(IAsyncNodeManager nodeManager)
        {
            return nodeManager is INodeManagerInlineExecution inline && inline.SupportsInlineExecution;
        }

        /// <summary>
        /// Whether every NodeManager the request can reach supports inline execution.
        /// </summary>
        /// <param name="request">The request.</param>
        public bool Allows(IServiceRequest request)
        {
            if (All)
            {
                return true;
            }
            switch (request)
            {
                case ReadRequest read:
                    for (int ii = 0; ii < read.NodesToRead.Count; ii++)
                    {
                        ReadValueId item = read.NodesToRead[ii];
                        if (item != null && !AllowsNamespace(item.NodeId))
                        {
                            return false;
                        }
                    }
                    return true;
                case WriteRequest write:
                    for (int ii = 0; ii < write.NodesToWrite.Count; ii++)
                    {
                        WriteValue item = write.NodesToWrite[ii];
                        if (item != null && !AllowsNamespace(item.NodeId))
                        {
                            return false;
                        }
                    }
                    return true;
                case CreateSubscriptionRequest:
                case ModifySubscriptionRequest:
                case SetPublishingModeRequest:
                case SetTriggeringRequest:
                case RepublishRequest:
                case FindServersRequest:
                case FindServersOnNetworkRequest:
                case GetEndpointsRequest:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether the namespace of a node routes only to NodeManagers that support
        /// inline execution. A namespace without a route reaches every NodeManager.
        /// </summary>
        private bool AllowsNamespace(NodeId nodeId)
        {
            ushort index = nodeId.NamespaceIndex;
            return index < m_namespaces.Length && m_namespaces[index];
        }

        private readonly bool[] m_namespaces;
    }
}
