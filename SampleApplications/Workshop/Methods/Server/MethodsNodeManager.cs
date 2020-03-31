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

namespace Quickstarts.MethodsServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class MethodsNodeManager :CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public MethodsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.Methods)
        {
            SystemContext.NodeIdFactory = this;

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<MethodsServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new MethodsServerConfiguration();
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
                // create a object to represent the process being controlled.
                BaseObjectState process = new BaseObjectState(null);

                process.NodeId = new NodeId(1, NamespaceIndex);
                process.BrowseName = new QualifiedName("My Process", NamespaceIndex);
                process.DisplayName = process.BrowseName.Name;
                process.TypeDefinitionId = ObjectTypeIds.BaseObjectType; 

                // ensure the process object can be found via the server object. 
                IList<IReference> references = null;

                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                process.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, process.NodeId));

                // a property to report the process state.
                PropertyState<uint> state = m_stateNode = new PropertyState<uint>(process);

                state.NodeId = new NodeId(2, NamespaceIndex);
                state.BrowseName = new QualifiedName("State", NamespaceIndex);
                state.DisplayName = state.BrowseName.Name;
                state.TypeDefinitionId = VariableTypeIds.PropertyType;
                state.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                state.DataType = DataTypeIds.UInt32;
                state.ValueRank = ValueRanks.Scalar;

                process.AddChild(state);

                // a method to start the process.
                MethodState start = new MethodState(process);

                start.NodeId = new NodeId(3, NamespaceIndex);
                start.BrowseName = new QualifiedName("Start", NamespaceIndex);
                start.DisplayName = start.BrowseName.Name;
                start.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                start.UserExecutable = true;
                start.Executable = true;

                // add input arguments.
                start.InputArguments = new PropertyState<Argument[]>(start);
                start.InputArguments.NodeId = new NodeId(4, NamespaceIndex);
                start.InputArguments.BrowseName = BrowseNames.InputArguments;
                start.InputArguments.DisplayName = start.InputArguments.BrowseName.Name;
                start.InputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
                start.InputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                start.InputArguments.DataType = DataTypeIds.Argument;
                start.InputArguments.ValueRank = ValueRanks.OneDimension;

                Argument[] args = new Argument[2];
                args[0] = new Argument();
                args[0].Name = "Initial State";
                args[0].Description = "The initialize state for the process.";
                args[0].DataType = DataTypeIds.UInt32;
                args[0].ValueRank = ValueRanks.Scalar;

                args[1] = new Argument();
                args[1].Name = "Final State";
                args[1].Description = "The final state for the process.";
                args[1].DataType = DataTypeIds.UInt32;
                args[1].ValueRank = ValueRanks.Scalar;

                start.InputArguments.Value = args;

                // add output arguments.
                start.OutputArguments = new PropertyState<Argument[]>(start);
                start.OutputArguments.NodeId = new NodeId(5, NamespaceIndex);
                start.OutputArguments.BrowseName = BrowseNames.OutputArguments;
                start.OutputArguments.DisplayName = start.OutputArguments.BrowseName.Name;
                start.OutputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
                start.OutputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                start.OutputArguments.DataType = DataTypeIds.Argument;
                start.OutputArguments.ValueRank = ValueRanks.OneDimension;

                args = new Argument[2];
                args[0] = new Argument();
                args[0].Name = "Revised Initial State";
                args[0].Description = "The revised initialize state for the process.";
                args[0].DataType = DataTypeIds.UInt32;
                args[0].ValueRank = ValueRanks.Scalar;

                args[1] = new Argument();
                args[1].Name = "Revised Final State";
                args[1].Description = "The revised final state for the process.";
                args[1].DataType = DataTypeIds.UInt32;
                args[1].ValueRank = ValueRanks.Scalar;

                start.OutputArguments.Value = args;

                process.AddChild(start);

                // save in dictionary. 
                AddPredefinedNode(SystemContext, process);

                // set up method handlers. 
                start.OnCallMethod = new GenericMethodCalledEventHandler(OnStart);
            } 
        }

        private object m_processLock = new object();
        private uint m_state;
        private uint m_finalState;
        private Timer m_processTimer;
        private PropertyState<uint> m_stateNode;

        /// <summary>
        /// Called when the Start method is called.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="method">The method.</param>
        /// <param name="inputArguments">The input arguments.</param>
        /// <param name="outputArguments">The output arguments.</param>
        /// <returns></returns>
        public ServiceResult OnStart(
            ISystemContext context, 
            MethodState method, 
            IList<object> inputArguments, 
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            // check the data type of the input arguments.
            uint? initialState = inputArguments[0] as uint?;
            uint? finalState = inputArguments[1] as uint?;

            if (initialState == null || finalState == null)
            {
                return StatusCodes.BadTypeMismatch;
            }

            lock (m_processLock)
            {
                // check if the process is running.
                if (m_processTimer != null)
                {
                    m_processTimer.Dispose();
                    m_processTimer = null;
                }

                // start the process.
                m_state = initialState.Value;
                m_finalState = finalState.Value;
                m_processTimer = new Timer(OnUpdateProcess, null, 1000, 1000);

                // the calling function sets default values for all output arguments.
                // only need to update them here.
                outputArguments[0] = m_state;
                outputArguments[1] = m_finalState;
            }

            // signal update to state node.
            lock (Lock)
            {
                m_stateNode.Value = m_state;
                m_stateNode.ClearChangeMasks(SystemContext, true);
            }

            return ServiceResult.Good;
        }
        
        /// <summary>
        /// Called when updating the process.
        /// </summary>
        /// <param name="state">The state.</param>
        private void OnUpdateProcess(object state)
        {
            try
            {
                lock (m_processLock)
                {
                    // check if increasing.
                    if (m_state < m_finalState)
                    {
                        m_state++;
                    }

                    // check if decreasing.
                    else if (m_state > m_finalState)
                    {
                        m_state--;
                    }

                    // check if all done.
                    else
                    {
                        m_processTimer.Dispose();
                        m_processTimer = null;
                    };
                }

                // signal update to state node.
                lock (Lock)
                {
                    m_stateNode.Value = m_state;
                    m_stateNode.ClearChangeMasks(SystemContext, true);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error updating process.");
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
        private MethodsServerConfiguration m_configuration;
        #endregion
    }
}
