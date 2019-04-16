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

namespace Quickstarts.DataAccessServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class DataAccessServerNodeManager : QuickstartNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public DataAccessServerNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.DataAccess)
        {
            this.AliasRoot = "DA";

            SystemContext.SystemHandle = m_system = new UnderlyingSystem();
            SystemContext.NodeIdFactory = this;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<DataAccessServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new DataAccessServerConfiguration();
            }

            // create the table to store the cached blocks.
            m_blocks = new Dictionary<NodeId, BlockState>();
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
                m_system.Dispose();
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        /// <remarks>
        /// This method is called by the NodeState.Create() method which initializes a Node from
        /// the type model. During initialization a number of child nodes are created and need to 
        /// have NodeIds assigned to them. This implementation constructs NodeIds by constructing
        /// strings. Other implementations could assign unique integers or Guids and save the new
        /// Node in a dictionary for later lookup.
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return ModelUtils.ConstructIdForComponent(node, NamespaceIndex);
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
                // find the top level segments and link them to the ObjectsFolder.
                IList<UnderlyingSystemSegment> segments = m_system.FindSegments(null);

                for (int ii = 0; ii < segments.Count; ii++)
                {
                    // Top level areas need a reference from the Server object. 
                    // These references are added to a list that is returned to the caller.
                    // The caller will update the Objects folder node.
                    IList<IReference> references = null;

                    if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                    {
                        externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                    }

                    // construct the NodeId of a segment.
                    NodeId segmentId = ModelUtils.ConstructIdForSegment(segments[ii].Id, NamespaceIndex);

                    // add an organizes reference from the ObjectsFolder to the area.
                    references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, segmentId));
                }

                // start the simulation.
                m_system.StartSimulation();
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                m_system.StopSimulation();
                m_blocks.Clear();
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

                // check for check for nodes that are being currently monitored.
                MonitoredNode monitoredNode = null;

                if (MonitoredNodes.TryGetValue(nodeId, out monitoredNode))
                {
                    NodeHandle handle = new NodeHandle();

                    handle.NodeId = nodeId;
                    handle.Validated = true;
                    handle.Node = monitoredNode.Node;

                    return handle;
                }

                if (nodeId.IdType != IdType.String)
                {
                    NodeState node = null;

                    if (PredefinedNodes.TryGetValue(nodeId, out node))
                    {
                        NodeHandle handle = new NodeHandle();

                        handle.NodeId = nodeId;
                        handle.Node = node;
                        handle.Validated = true;

                        return handle;
                    }
                }

                // parse the identifier.
                ParsedNodeId parsedNodeId = ParsedNodeId.Parse(nodeId);

                if (parsedNodeId != null)
                {
                    NodeHandle handle = new NodeHandle();

                    handle.NodeId = nodeId;
                    handle.Validated = false;
                    handle.Node = null;
                    handle.ParsedNodeId = parsedNodeId;

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

            NodeState target = null;

            // check if already in the cache.
            if (cache != null)
            {
                if (cache.TryGetValue(handle.NodeId, out target))
                {
                    // nulls mean a NodeId which was previously found to be invalid has been referenced again.
                    if (target == null)
                    {
                        return null;
                    }

                    handle.Node = target;
                    handle.Validated = true;
                    return handle.Node;
                }

                target = null;
            }

            try
            {
                // check if the node id has been parsed.
                if (handle.ParsedNodeId == null)
                {
                    return null;
                }

                NodeState root = null;

                // validate a segment.
                if (handle.ParsedNodeId.RootType == ModelUtils.Segment)
                {
                    UnderlyingSystemSegment segment = m_system.FindSegment(handle.ParsedNodeId.RootId);

                    // segment does not exist.
                    if (segment == null)
                    {
                        return null;
                    }

                    NodeId rootId = ModelUtils.ConstructIdForSegment(segment.Id, NamespaceIndex);

                    // create a temporary object to use for the operation.
                    root = new SegmentState(context, rootId, segment);
                }

                // validate segment.
                else if (handle.ParsedNodeId.RootType == ModelUtils.Block)
                {
                    // validate the block.
                    UnderlyingSystemBlock block = m_system.FindBlock(handle.ParsedNodeId.RootId);

                    // block does not exist.
                    if (block == null)
                    {
                        return null;
                    }

                    NodeId rootId = ModelUtils.ConstructIdForBlock(block.Id, NamespaceIndex);

                    // check for check for blocks that are being currently monitored.
                    BlockState node = null;

                    if (m_blocks.TryGetValue(rootId, out node))
                    {
                        root = node;
                    }

                    // create a temporary object to use for the operation.
                    else
                    {
                        root = new BlockState(this, rootId, block);
                    }
                }

                // unknown root type.
                else
                {
                    return null;
                }

                // all done if no components to validate.
                if (String.IsNullOrEmpty(handle.ParsedNodeId.ComponentPath))
                {
                    handle.Validated = true;
                    handle.Node = target = root;
                    return handle.Node;
                }

                // validate component.
                NodeState component = root.FindChildBySymbolicName(context, handle.ParsedNodeId.ComponentPath);

                // component does not exist.
                if (component == null)
                {
                    return null;
                }

                // found a valid component.
                handle.Validated = true;
                handle.Node = target = component;
                return handle.Node;
            }
            finally
            {
                // store the node in the cache to optimize subsequent lookups.
                if (cache != null)
                {
                    cache.Add(handle.NodeId, target);
                }
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Called after creating a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void OnMonitoredItemCreated(ServerSystemContext context, NodeHandle handle, MonitoredItem monitoredItem)
        {
            BlockState block = handle.Node.GetHierarchyRoot() as BlockState;

            if (block != null)
            {
                block.StartMonitoring(context);

                // need to save the block to ensure that multiple monitored items use the same instance.
                m_blocks[block.NodeId] = block;
            }
        }

        /// <summary>
        /// Called after deleting a MonitoredItem.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="handle">The handle for the node.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        protected override void OnMonitoredItemDeleted(ServerSystemContext context, NodeHandle handle, MonitoredItem monitoredItem)
        {
            BlockState block = handle.Node.GetHierarchyRoot() as BlockState;

            if (block != null)
            {
                if (!block.StopMonitoring(context))
                {
                    // can remove the block since all monitored items for the block are gone.
                    m_blocks.Remove(block.NodeId);
                }
            }
        }
        #endregion

        #region Private Fields
        private UnderlyingSystem m_system;
        private DataAccessServerConfiguration m_configuration;
        private Dictionary<NodeId,BlockState> m_blocks;
        #endregion
    }
}
