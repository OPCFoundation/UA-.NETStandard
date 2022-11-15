/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;
using System.Linq;

namespace AggregatingServer.Servers
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ServerTypeNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ServerTypeNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.AggregatingServer)
        {
            SystemContext.NodeIdFactory = this;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<ServerTypeConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new ServerTypeConfiguration();
            }


            // append uri
            //AddNamespaceUri("phi-ware.com/Kokot");
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {  
            if (disposing)
            {
                // TBD
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                // Load dictionary for standard nodes namespce "http://opcfoundation.org/UA/" 
                NodeStateCollection predefinedNodes = new NodeStateCollection();
                predefinedNodes.LoadFromBinaryResource(SystemContext, "Opc.Ua.PredefinedNodes.uanodes", typeof(ArgumentCollection).GetTypeInfo().Assembly, true);
                NodeState nodeState = predefinedNodes.Find(n => n.NodeId == ObjectIds.ObjectsFolder);

                predefinedNodes.ForEach(n => AddPredefinedNode(SystemContext, n));
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace. 
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                if (!PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    return null;
                }

                NodeHandle handle = new NodeHandle();

                handle.NodeId = nodeId;
                handle.Node = node;
                handle.Validated = true;

                return handle;
            } 
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }
            
            // TBD

            return null;
        }
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        private ServerTypeConfiguration m_configuration;
        #endregion

        #region phi-ware Custom       
        /*
        public BaseInstanceState AddNodeVariable(NodeState parent, string path, string name, VariableTypeIds variableTypeIds, ushort namespaceIndex)
        {
            BaseInstanceState baseInstanceState;
            switch (variableTypeIds)
            {
                case VariableTypeIds.PropertyType:
                    baseInstanceState = new PropertyState(null);
                    break;
                default:
                    baseInstanceState = new BaseDataVariableState(null);
                    break;
            }

            return baseInstanceState;
        }
        */
        /// <summary>
        /// Creates folder structure for aggregating server.
        /// </summary>
        /// <param name="parent">Must come from address space</param>
        /// <param name="path">Used for node id</param>
        /// <param name="name">Used for node name</param>
        /// <param name="nodeTypeId"></param>
        /// <returns></returns>
        public BaseInstanceState AddNode(NodeState parent, string path, string name, NodeId nodeTypeId, ushort namespaceIndex, NodeClass nodeClass = NodeClass.Unspecified)
        {
            BaseInstanceState baseInstanceState;
            switch (nodeClass)
            {
                
                case NodeClass.Variable:
                case NodeClass.VariableType:

                    if((uint)nodeTypeId.Identifier == (uint)VariableTypeIds.PropertyType.Identifier)
                        baseInstanceState = new PropertyState(null);
                    else
                        baseInstanceState = new BaseDataVariableState(null);
                    
                    break;
                case NodeClass.Method:
                    MethodState methodState = new MethodState(null);
                    baseInstanceState = methodState;                                       
                    break;

                case NodeClass.Object:
                case NodeClass.ObjectType:
                default:
                    baseInstanceState = new BaseObjectState(null);
                    break;
            }            

            
            baseInstanceState.Create(SystemContext, new NodeId(path, namespaceIndex), new QualifiedName(path, namespaceIndex), name, true);            

            if (parent != null)
            {
                parent.AddChild(baseInstanceState);
                baseInstanceState.AddReference(ReferenceTypes.Organizes, true, parent.NodeId);
            }

            //baseInstanceState.EventNotifier = EventNotifiers.SubscribeToEvents;
            
            //references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, root.NodeId));

            AddRootNotifier(baseInstanceState);
            AddPredefinedNode(SystemContext, baseInstanceState);

            return baseInstanceState;
        }
        
        /// <summary>
        /// AddNamespaceUri. See also UA QuaickStart Application project DataTypesManager.cs contructor.
        /// </summary>
        /// <param name="uri"></param>
        public ushort AddNamespaceUri(string uri)
        {
            int index = 0;
            // try to find 
            if((index = this.NamespaceUris.ToList().IndexOf(uri)) < 0)
            {
                // append namespace uri (index is set automatically) 
                // TODO - check if it is not better to handle indexing manually
                SetNamespaces( NamespaceUris.Concat(new[] { uri }).ToArray());
                return NamespaceIndexes.Last();
            }            

            return (ushort)index;
        }
        #endregion
    }
}
