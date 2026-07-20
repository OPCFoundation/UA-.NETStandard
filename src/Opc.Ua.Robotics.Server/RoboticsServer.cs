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

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Reusable server-side helpers for the OPC 40010 Robotics companion model:
    /// loading the Robotics type system into a node manager and instantiating
    /// Robotics-typed objects from the numeric type NodeIds in
    /// <see cref="RoboticsModel"/>.
    /// </summary>
    public static class RoboticsServer
    {
        /// <summary>
        /// Loads the full Robotics type system into <paramref name="nodes"/>: the
        /// OPC UA DI base model, then the IA and Robotics companion models (all
        /// source-generated), in dependency order. Call this while building the
        /// predefined-node collection of a node manager. Returns the number of
        /// nodes added by the IA + Robotics models.
        /// </summary>
        public static int AddRoboticsTypeSystem(
            this NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            nodes.AddOpcUaDi(context);
            int before = nodes.Count;
            nodes.AddOpcUaIA(context);
            nodes.AddOpcUaRobotics(context);
            return nodes.Count - before;
        }

        /// <summary>
        /// Instantiates a Robotics-typed Object (e.g. MotionDeviceSystem,
        /// MotionDevice, Axis, Controller) under <paramref name="parent"/>, using
        /// a numeric type NodeId resolved via
        /// <see cref="RoboticsModel.TypeNodeId"/>. Assigns a per-instance NodeId
        /// from the context's NodeIdFactory.
        /// </summary>
        public static BaseObjectState CreateTypedObject(
            this ISystemContext context,
            NodeState parent,
            string name,
            ushort ns,
            NodeId typeDefinition,
            NodeId referenceType)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            var obj = new BaseObjectState(parent)
            {
                SymbolicName = name,
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = referenceType,
                TypeDefinitionId = typeDefinition
            };
            parent.AddChild(obj);
            obj.NodeId = context.NodeIdFactory.New(context, obj);
            return obj;
        }
    }
}
