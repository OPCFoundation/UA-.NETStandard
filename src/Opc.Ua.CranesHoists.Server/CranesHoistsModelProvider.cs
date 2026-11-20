/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Loads the released crane model and missing dependencies on either existing stock namespace owner.
    /// </summary>
    public sealed class CranesHoistsModelProvider : IMachineryModelProvider, IRoboticsModelProvider
    {
        /// <inheritdoc/>
        public int Order => 100;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
        [
            Di.Namespaces.OpcUaDi,
            IA.Namespaces.IA,
            Machinery.Namespaces.Machinery,
            Robotics.Namespaces.Robotics,
            CranesHoistsModel.NamespaceUri
        ];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (!Contains(nodes, NodeId.Create(
                Di.ObjectTypes.DeviceType, Di.Namespaces.OpcUaDi, context.NamespaceUris)))
            {
                nodes.AddOpcUaDi(context);
            }
            if (!Contains(nodes, NodeId.Create(IA.ObjectTypes.StacklightType, IA.Namespaces.IA, context.NamespaceUris)))
            {
                nodes.AddOpcUaIA(context);
            }
            if (!Contains(nodes, NodeId.Create(
                Machinery.ObjectTypes.MachineIdentificationType,
                Machinery.Namespaces.Machinery, context.NamespaceUris)))
            {
                nodes.AddOpcUaMachinery(context);
            }
            if (!Contains(nodes, NodeId.Create(
                Robotics.ObjectTypes.MotionDeviceSystemType, Robotics.Namespaces.Robotics, context.NamespaceUris)))
            {
                nodes.AddOpcUaRobotics(context);
            }
            if (!Contains(nodes, NodeId.Create(
                ObjectTypes.CraneMotionDeviceSystemType, CranesHoistsModel.NamespaceUri, context.NamespaceUris)))
            {
                nodes.AddOpcUaCranesHoists(context);
            }
            context.EncodeableFactory.Builder.AddOpcUaCranesHoists().Commit();
        }

        /// <summary>
        /// Avoids loading another copy of dependencies already owned by the manager.
        /// </summary>
        private static bool Contains(NodeStateCollection nodes, NodeId nodeId)
        {
            foreach (NodeState node in nodes)
            {
                if (node.NodeId == nodeId)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
