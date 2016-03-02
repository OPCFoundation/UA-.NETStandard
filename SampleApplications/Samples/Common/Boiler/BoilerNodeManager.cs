/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;
using Opc.Ua.Sample;
using System.Reflection;

namespace Boiler
{
    /// <summary>
    /// A node manager the diagnostic information exposed by the server.
    /// </summary>
    public class BoilerNodeManager : SampleNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public BoilerNodeManager(
            Opc.Ua.Server.IServerInternal server, 
            ApplicationConfiguration configuration)
        :
            base(server)
        {
            List<string> namespaceUris = new List<string>();
            namespaceUris.Add(Namespaces.Boiler);
            namespaceUris.Add(Namespaces.Boiler +"/Instance");
            NamespaceUris = namespaceUris;

            m_typeNamespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);

            m_lastUsedId = 0;
            m_boilers = new List<BoilerState>();
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            uint id = Utils.IncrementIdentifier(ref m_lastUsedId);
            return new NodeId(id, m_namespaceIndex);
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
                CreateBoiler(SystemContext, 2);
            }
        }

        /// <summary>
        /// Creates a boiler and adds it to the address space.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="unitNumber">The unit number for the boiler.</param>
        private void CreateBoiler(SystemContext context, int unitNumber)
        {
            BoilerState boiler = new BoilerState(null);

            string name = Utils.Format("Boiler #{0}", unitNumber);

            boiler.Create(
                context,
                null, 
                new QualifiedName(name, m_namespaceIndex),
                null, 
                true);

            NodeState folder = (NodeState)FindPredefinedNode(
                ExpandedNodeId.ToNodeId(ObjectIds.Boilers, Server.NamespaceUris),
                typeof(NodeState));

            folder.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, boiler.NodeId);
            boiler.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, folder.NodeId);

            string unitLabel = Utils.Format("{0}0", unitNumber);

            UpdateDisplayName(boiler.InputPipe, unitLabel);
            UpdateDisplayName(boiler.Drum, unitLabel);
            UpdateDisplayName(boiler.OutputPipe, unitLabel);
            UpdateDisplayName(boiler.LevelController, unitLabel);
            UpdateDisplayName(boiler.FlowController, unitLabel);
            UpdateDisplayName(boiler.CustomController, unitLabel);

            m_boilers.Add(boiler);

            AddPredefinedNode(context, boiler);
        }

        /// <summary>
        /// Updates the display name for an instance with the unit label name.
        /// </summary>
        /// <param name="instance">The instance to update.</param>
        /// <param name="label">The label to apply.</param>
        /// <remarks>This method assumes the DisplayName has the form NameX001 where X0 is the unit label placeholder.</remarks>
        private void UpdateDisplayName(BaseInstanceState instance, string unitLabel)
        {
            LocalizedText displayName = instance.DisplayName;

            if (displayName != null)
            {
                string text = displayName.Text;

                if (text != null)
                {
                    text = text.Replace("X0", unitLabel);
                }

                displayName = new LocalizedText(displayName.Locale, text);
            }

            instance.DisplayName = displayName;
        }

        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, "Opc.Ua.Sample.Boiler.Boiler.PredefinedNodes.uanodes", this.GetType().GetTypeInfo().Assembly, true);
            return predefinedNodes;
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            BaseObjectState passiveNode = predefinedNode as BaseObjectState;

            if (passiveNode == null)
            {
                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || typeId.IdType != IdType.Numeric)
            {
                return predefinedNode;
            }

            switch ((uint)typeId.Identifier)
            {
                case ObjectTypes.BoilerType:
                {
                    if (passiveNode is BoilerState)
                    {
                        break;
                    }

                    BoilerState activeNode = new BoilerState(passiveNode.Parent);
                    activeNode.Create(context, passiveNode);

                    // replace the node in the parent.
                    if (passiveNode.Parent != null)
                    {
                        passiveNode.Parent.ReplaceChild(context, activeNode);
                    }

                    return activeNode;
                }
            }

            return predefinedNode;
        }

        /// <summary>
        /// Does any processing after a monitored item is created.
        /// </summary>
        protected override void OnCreateMonitoredItem(
            ISystemContext systemContext,
            MonitoredItemCreateRequest itemToCreate,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem)
        {
            // TBD
        }

        /// <summary>
        /// Does any processing after a monitored item is created.
        /// </summary>
        protected override void OnModifyMonitoredItem(
            ISystemContext systemContext,
            MonitoredItemModifyRequest itemToModify,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem,
            double previousSamplingInterval)
        {
            // TBD
        }

        /// <summary>
        /// Does any processing after a monitored item is deleted.
        /// </summary>
        protected override void OnDeleteMonitoredItem(
            ISystemContext systemContext,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem)
        {
            // TBD
        }

        /// <summary>
        /// Does any processing after a monitored item is created.
        /// </summary>
        protected override void OnSetMonitoringMode(
            ISystemContext systemContext,
            MonitoredNode monitoredNode,
            DataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode currentMode)
        {
            // TBD
        }
        #endregion

        #region Private Fields
        private ushort m_namespaceIndex;
        private ushort m_typeNamespaceIndex;
        private long m_lastUsedId;
        private List<BoilerState> m_boilers;
        #endregion
    }
}
