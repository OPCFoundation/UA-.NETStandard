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

namespace Quickstarts.SimpleEvents.Server
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class SimpleEventsNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public SimpleEventsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // set one namespace for the type model and one names for dynamically created nodes.
            string[] namespaceUrls = new string[1];
            namespaceUrls[0] = Namespaces.SimpleEvents;
            SetNamespaces(namespaceUrls);

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<SimpleEventsServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new SimpleEventsServerConfiguration();
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
                if (m_simulationTimer != null)
                {
                    Utils.SilentDispose(m_simulationTimer);
                    m_simulationTimer = null;
                }
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

        #region Overridden Methods
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context,
                "Quickstarts.SimpleEvents.Server.Quickstarts.SimpleEvents.PredefinedNodes.uanodes",
                typeof(SimpleEventsNodeManager).GetTypeInfo().Assembly, 
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
                LoadPredefinedNodes(SystemContext, externalReferences);

                // start a simulation that changes the values of the nodes.
                m_simulationTimer = new Timer(DoSimulation, null, 3000, 3000);
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                base.DeleteAddressSpace();
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

                // check for predefined nodes.
                if (PredefinedNodes != null)
                {
                    NodeState node = null;

                    if (PredefinedNodes.TryGetValue(nodeId, out node))
                    {
                        NodeHandle handle = new NodeHandle();

                        handle.NodeId = nodeId;
                        handle.Validated = true;
                        handle.Node = node;

                        return handle;
                    }
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
            
            // TBD

            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        /// <param name="state">The state.</param>
        private void DoSimulation(object state)
        {
            try
            {
                for (int ii = 1; ii < 3; ii++)
                {
                    // construct translation object with default text.
                    TranslationInfo info = new TranslationInfo(
                        "SystemCycleStarted",
                        "en-US",
                        "The system cycle '{0}' has started.",
                        ++m_cycleId);

                    // construct the event.
                    SystemCycleStartedEventState e = new SystemCycleStartedEventState(null);

                    e.Initialize(
                        SystemContext,
                        null,
                        (EventSeverity)ii,
                        new LocalizedText(info));

                    e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceName, "System", false);
                    e.SetChildValue(SystemContext, Opc.Ua.BrowseNames.SourceNode, Opc.Ua.ObjectIds.Server, false);
                    e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.CycleId, NamespaceIndex), m_cycleId.ToString(), false);

                    CycleStepDataType step = new CycleStepDataType();
                    step.Name = "Step 1";
                    step.Duration = 1000;

                    e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.CurrentStep, NamespaceIndex), step, false);
                    e.SetChildValue(SystemContext, new QualifiedName(BrowseNames.Steps, NamespaceIndex), new CycleStepDataType[] { step, step }, false);

                    Server.ReportEvent(e);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error during simulation.");
            }
        }
        #endregion

        #region Private Fields
        private SimpleEventsServerConfiguration m_configuration;
        private Timer m_simulationTimer;
        private int m_cycleId;
        #endregion
    }
}
