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

namespace Quickstarts.ViewsServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ViewsNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ViewsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Quickstarts.Views.Namespaces.Views, Quickstarts.Views.Namespaces.Engineering, Quickstarts.Views.Namespaces.Operations)
        {
            SystemContext.NodeIdFactory = this;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<ViewsServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new ViewsServerConfiguration();
            }
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
            BaseInstanceState instance = node as BaseInstanceState;

            if (instance != null && instance.Parent != null)
            {
                ParsedNodeId pnd = ParsedNodeId.Parse(instance.Parent.NodeId);

                if (pnd != null)
                {
                    return pnd.Construct(instance.SymbolicName);
                }
            }

            return node.NodeId;
        }
        #endregion
        
        #region Overridden Methods
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, 
                "Quickstarts.ViewsServer.Model.Quickstarts.Views.PredefinedNodes.uanodes",
                typeof(ViewsNodeManager).GetTypeInfo().Assembly, 
                true);
            return predefinedNodes;
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
                base.CreateAddressSpace(externalReferences);

                NodeState root = FindPredefinedNode(new NodeId(Quickstarts.Views.Objects.Plant, NamespaceIndex), typeof(NodeState));

                Quickstarts.Views.BoilerState boiler1 = new Quickstarts.Views.BoilerState(null);
                ParsedNodeId pnd1 = new ParsedNodeId() { NamespaceIndex = NamespaceIndex, RootId = "Boiler #1" };

                boiler1.Create(
                    SystemContext,
                    pnd1.Construct(),
                    new QualifiedName("Boiler #1", NamespaceIndex),
                    null,
                    true);

                boiler1.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, root.NodeId);
                root.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, boiler1.NodeId);

                AddPredefinedNode(SystemContext, boiler1);

                Quickstarts.Views.BoilerState boiler2 = new Quickstarts.Views.BoilerState(null);
                ParsedNodeId pnd2 = new ParsedNodeId() { NamespaceIndex = NamespaceIndex, RootId = "Boiler #2" };

                boiler2.Create(
                    SystemContext,
                    pnd2.Construct(),
                    new QualifiedName("Boiler #2", NamespaceIndex),
                    null,
                    true);

                boiler2.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, root.NodeId);
                root.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, boiler2.NodeId);

                AddPredefinedNode(SystemContext, boiler2);
            } 
        }

        /// <summary>
        /// Checks if the node is in the view.
        /// </summary>
        protected override bool IsNodeInView(ServerSystemContext context, ContinuationPoint continuationPoint, NodeState node)
        {
            if (continuationPoint.View != null)
            {
                if (continuationPoint.View.ViewId == new NodeId(Quickstarts.Views.Views.Engineering, NamespaceIndex))
                {
                    // suppress operations properties.
                    if (node != null && node.BrowseName.NamespaceIndex == NamespaceIndexes[2])
                    {
                        return false;
                    }
                }

                if (continuationPoint.View.ViewId == new NodeId(Quickstarts.Views.Views.Operations, NamespaceIndex))
                {
                    // suppress engineering properties.
                    if (node != null && node.BrowseName.NamespaceIndex == NamespaceIndexes[1])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the reference is in the view.
        /// </summary>
        protected override bool IsReferenceInView(ServerSystemContext context, ContinuationPoint continuationPoint, IReference reference)
        {
            if (continuationPoint.View != null)
            {
                // guard against absolute node ids.
                if (reference.TargetId.IsAbsolute)
                {
                    return true;
                }

                // find the node.
                NodeState node = FindPredefinedNode((NodeId)reference.TargetId, typeof(NodeState));

                if (node != null)
                {
                    return IsNodeInView(context, continuationPoint, node);
                }
            }

            return true;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override Opc.Ua.Server.NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                NodeState node = null;

                // check cache (the cache is used because the same node id can appear many times in a single request).
                if (cache != null)
                {
                    if (cache.TryGetValue(nodeId, out node))
                    {
                        return new Opc.Ua.Server.NodeHandle(nodeId, node);
                    }
                }

                // look up predefined node.
                if (PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    Opc.Ua.Server.NodeHandle handle = new Opc.Ua.Server.NodeHandle(nodeId, node);

                    if (cache != null)
                    {
                        cache.Add(nodeId, node);
                    }

                    return handle;
                }

                return null;
            } 
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            Opc.Ua.Server.NodeHandle handle,
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

            // lookup in operation cache.
            NodeState target = FindNodeInCache(context, handle, cache);

            if (target == null)
            {
                // TBD
                return null;
            }

            return ValidationComplete(context, handle, target, cache);
        }
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        private ViewsServerConfiguration m_configuration;
        #endregion
    }
}
