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
using System.IO.Ports;


namespace Quickstarts.WeightScale.Server
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class WeightScaleNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public WeightScaleNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Opc.Ua.Ws.Namespaces.OpcUaDi, Opc.Ua.Ws.Namespaces.OpcUaWs)
        {
            SystemContext.NodeIdFactory = this;

            //// set one namespace for the type model and one names for dynamically created nodes.
            //string[] namespaceUrls = new string[2];
            //namespaceUrls[0] = Namespaces.Boiler;
            //namespaceUrls[1] = Namespaces.Boiler + "/Instance";
            //SetNamespaces(namespaceUrls);

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<WeightScaleServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new WeightScaleServerConfiguration();
            }
        }
        #endregion

        /// <summary>
        /// Called when the OnTare method is called.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="method">The method.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <returns></returns>
        public ServiceResult OnTare(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            lock(m_COMLock)
            {
                try
                {
                    SerialPort port = new SerialPort("COM2", 9600, Parity.None, 8, StopBits.One);
                    string response = "T\r\n";
                    byte[] sData;
                    sData = Encoding.ASCII.GetBytes(response);
                    port.Open();
                    port.BaseStream.Write(sData, 0, sData.Length);
                    port.Close();

                }
                catch (Exception ee)
                {

                }
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the OnZero method is called.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="method">The method.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <returns></returns>
        public ServiceResult OnZero(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            lock (m_COMLock)
            {
                try
                {
                    SerialPort port = new SerialPort("COM2", 9600, Parity.None, 8, StopBits.One);
                    string response = "Z\r\n";
                    byte[] sData;
                    sData = Encoding.ASCII.GetBytes(response);
                    port.Open();
                    port.BaseStream.Write(sData, 0, sData.Length);
                    port.Close();

                }
                catch (Exception ee)
                {

                }
            }
            return ServiceResult.Good;
        }


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
            // generate a new numeric id in the instance namespace.
            return new NodeId(++m_nodeIdCounter, NamespaceIndexes[1]);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();

            NodeStateCollection tmp = new NodeStateCollection();

            tmp.LoadFromBinaryResource(context,
            "Quickstarts.WeightScale.Server.Opc.Ua.Di.PredefinedNodes.uanodes",
            typeof(WeightScaleNodeManager).GetTypeInfo().Assembly,
            true);

            tmp.ForEach((ns) => predefinedNodes.Add(ns));
            
            tmp.LoadFromBinaryResource(context,
                "Quickstarts.WeightScale.Server.Opc.Ua.Ws.PredefinedNodes.uanodes",
                typeof(WeightScaleNodeManager).GetTypeInfo().Assembly, 
                true);

            tmp.ForEach((ns) => predefinedNodes.Add(ns));
            

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

                // Add method handler
                MethodState methodState;

                methodState = (MethodState)FindPredefinedNode(new NodeId(Opc.Ua.Ws.Methods.WeightScale01_MethodSet_Tare, 3), typeof(MethodState));
                methodState.OnCallMethod = OnTare;

                methodState = (MethodState)FindPredefinedNode(new NodeId(Opc.Ua.Ws.Methods.WeightScale01_MethodSet_Zero, 3), typeof(MethodState));
                methodState.OnCallMethod = OnZero;

                // link root to objects folder.
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[Opc.Ua.ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                //references.Add(new NodeStateReference(Opc.Ua.ReferenceTypeIds.Organizes, false, m_boiler2.NodeId));

                //// store it and all of its children in the pre-defined nodes dictionary for easy look up.
                //AddPredefinedNode(SystemContext, m_boiler2);

                //// start a simulation that changes the values of the nodes.
                //m_simulationTimer = new Timer(DoSimulation, null, 1000, 1000);
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

        #region Overridden Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        /// <param name="state">The state.</param>
        private void DoSimulation(object state)
        {
            try
            {
                //double value1 = m_boiler1.Drum.LevelIndicator.Output.Value;
                //value1 = ((int)(++value1))%100;
                //m_boiler1.Drum.LevelIndicator.Output.Value = value1;
                //m_boiler1.ClearChangeMasks(SystemContext, true);
                
                //double value2 = m_boiler2.Drum.LevelIndicator.Output.Value;
                //value2 = ((int)(++value2))%20;
                //m_boiler2.Drum.LevelIndicator.Output.Value = value2;
                //m_boiler2.ClearChangeMasks(SystemContext, true);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error during simulation.");
            }
        }
        #endregion

        #region Private Fields
        private WeightScaleServerConfiguration m_configuration;
        private object m_COMLock = new object();
        private uint m_nodeIdCounter;
        private Timer m_simulationTimer;
        #endregion
    }
}
