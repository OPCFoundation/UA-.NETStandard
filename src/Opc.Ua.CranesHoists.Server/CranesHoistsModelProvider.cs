/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
