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

namespace Quickstarts.PerfTestServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class PerfTestNodeManager : CustomNodeManager2
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public PerfTestNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration, Namespaces.PerfTest)
        {
            SystemContext.NodeIdFactory = this;
            SystemContext.SystemHandle = m_system = new UnderlyingSystem();

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<PerfTestServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new PerfTestServerConfiguration();
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
                m_system.Initialize();

                IList<MemoryRegister> registers = m_system.GetRegisters();

                for (int ii = 0; ii < registers.Count; ii++)
                {
                    NodeId targetId = ModelUtils.GetRegisterId(registers[ii], NamespaceIndex);

                    IList<IReference> references = null;

                    if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                    {
                        externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                    }

                    references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, targetId));
                }
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

                NodeHandle handle = new NodeHandle();
                handle.NodeId = nodeId;
                handle.Validated = true;

                uint id = (uint)nodeId.Identifier;

                // find register
                int registerId = (int)((id & 0xFF000000)>>24);
                int index = (int)(id & 0x00FFFFFF);

                if (registerId == 0)
                {
                    MemoryRegister register = m_system.GetRegister(index);

                    if (register == null)
                    {
                        return null;
                    }

                    handle.Node = ModelUtils.GetRegister(register, NamespaceIndex);
                }

                // find register variable.
                else
                {
                    MemoryRegister register = m_system.GetRegister(registerId);

                    if (register == null)
                    {
                        return null;
                    }

                    // find register variable.
                    BaseDataVariableState variable = ModelUtils.GetRegisterVariable(register, index, NamespaceIndex);

                    if (variable == null)
                    {
                        return null;
                    }

                    handle.Node = variable;
                }

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
        protected override void OnCreateMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                NodeHandle handle = IsHandleInNamespace(monitoredItems[ii].ManagerHandle);

                if (handle == null)
                {
                    continue;
                }

                MemoryRegister register = handle.Node.Handle as MemoryRegister;
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (register != null)
                {
                    register.Subscribe((int)variable.NumericId, (IDataChangeMonitoredItem2)monitoredItems[ii]);
                }
            }
        }

        protected override void OnDeleteMonitoredItemsComplete(ServerSystemContext context, IList<IMonitoredItem> monitoredItems)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                NodeHandle handle = IsHandleInNamespace(monitoredItems[ii].ManagerHandle);

                if (handle == null)
                {
                    continue;
                }

                MemoryRegister register = handle.Node.Handle as MemoryRegister;
                BaseVariableState variable = handle.Node as BaseVariableState;

                if (register != null)
                {
                    register.Unsubscribe((int)variable.NumericId, (IDataChangeMonitoredItem2)monitoredItems[ii]);
                }
            }
        }
        #endregion

        #region Private Fields
        private PerfTestServerConfiguration m_configuration;
        private UnderlyingSystem m_system;
        #endregion
    }
}
