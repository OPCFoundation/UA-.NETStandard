/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Sample;
using Opc.Ua.Server;

namespace Boiler
{
    /// <summary>
    /// The factory class to create the boiler node manager.
    /// </summary>
    public class BoilerNodeManagerFactory : INodeManagerFactory
    {
        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new BoilerNodeManager(server, configuration, NamespacesUris.ToArray()!);
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => [Namespaces.Boiler, Namespaces.Boiler + "Instance"];
    }

    /// <summary>
    /// A node manager for the boiler exposed by the server.
    /// </summary>
    public class BoilerNodeManager : SampleNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public BoilerNodeManager(
            IServerInternal server,
            ApplicationConfiguration? configuration,
            string[] namespaceUris)
            : base(server)
        {
            _ = configuration;
            NamespaceUris = namespaceUris;

            Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[1]);

            // counter identifiers in the boiler namespace, which is the
            // second one this manager owns.
            NodeIdFactory = NodeIdFactory
                .WithMode(NodeIdAssignmentMode.Counter)
                .WithDefaultNamespaceIndex(m_namespaceIndex);
            m_boilers = [];
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
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
        private void CreateBoiler(ISystemContext context, int unitNumber)
        {
            var boiler = new BoilerState(null);

            string name = Utils.Format("Boiler #{0}", unitNumber);

            // A generated state object is born carrying its own type's NodeId,
            // and Create only replaces that when handed one. Passing the null
            // NodeId would leave this boiler sitting on BoilerType, so the
            // second boiler would overwrite the ObjectType in the index.
            // CreateInstance builds the subtree and then rebases the root and
            // its children onto fresh instance identifiers.
            context.CreateInstance(
                boiler,
                new QualifiedName(name, m_namespaceIndex),
                new LocalizedText(name));

            NodeState folder = FindPredefinedNode<NodeState>(
                ExpandedNodeId.ToNodeId(ObjectIds.Boilers, Server.NamespaceUris)!)!;

            folder.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, false, boiler.NodeId);
            boiler.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, folder.NodeId);

            string unitLabel = Utils.Format("{0}0", unitNumber);

            UpdateDisplayName(boiler.InputPipe!, unitLabel);
            UpdateDisplayName(boiler.Drum!, unitLabel);
            UpdateDisplayName(boiler.OutputPipe!, unitLabel);
            UpdateDisplayName(boiler.LevelController!, unitLabel);
            UpdateDisplayName(boiler.FlowController!, unitLabel);
            UpdateDisplayName(boiler.CustomController!, unitLabel);

            m_boilers.Add(boiler);

            AddPredefinedNode(context, boiler);

            // Autostart boiler simulation state machine
            MethodState start = boiler.Simulation!.Start!;
            ArrayOf<Variant> inputArguments = [];
            List<Variant> outputArguments = [];
            var errors = new List<ServiceResult>();
            start.Call(context, boiler.NodeId, inputArguments, errors, outputArguments);
        }

        /// <summary>
        /// Updates the display name for an instance with the unit label name.
        /// </summary>
        /// <param name="instance">The instance to update.</param>
        /// <param name="unitLabel">The label to apply.</param>
        /// <remarks>This method assumes the DisplayName has the form NameX001 where X0 is the unit label placeholder.</remarks>
        private static void UpdateDisplayName(BaseInstanceState instance, string unitLabel)
        {
            LocalizedText displayName = instance.DisplayName;

            if (!displayName.IsNullOrEmpty)
            {
                string? text = displayName.Text;

                if (text != null)
                {
                    text = text.Replace("X0", unitLabel, StringComparison.Ordinal);
                }

                displayName = new LocalizedText(displayName.Locale, text);
            }

            instance.DisplayName = displayName;
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            return new NodeStateCollection().AddBoiler(context);
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(
            ISystemContext context,
            NodeState predefinedNode)
        {
            if (predefinedNode is not BaseObjectState passiveNode)
            {
                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || !typeId.TryGetValue(out uint typeIdNumeric))
            {
                return predefinedNode;
            }

            switch (typeIdNumeric)
            {
                case ObjectTypes.BoilerType:
                    if (passiveNode is not BoilerState activeNode)
                    {
                        activeNode = new BoilerState(passiveNode.Parent);
                        activeNode.Create(context, passiveNode);

                        // replace the node in the parent.
                        passiveNode.Parent?.ReplaceChild(context, activeNode);
                    }

                    // Autostart boiler simulation state machine
                    MethodState start = activeNode.Simulation!.Start!;
                    ArrayOf<Variant> inputArguments = [];
                    List<Variant> outputArguments = [];
                    var errors = new List<ServiceResult>();
                    start.Call(context, activeNode.NodeId, inputArguments, errors, outputArguments);

                    return activeNode;
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

        private readonly ushort m_namespaceIndex;
        private readonly List<BoilerState> m_boilers;
    }
}
