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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.OpenUsd;

namespace Robotics
{
    /// <summary>
    /// Publishes the workpieces and the gripper jaws, so the transfer cycle is visible.
    /// </summary>
    /// <remarks>
    /// The choreography moved parts between the tables from the first commit, but only
    /// inside the simulation: the address space exposed the platform poses and the twelve
    /// axis angles and nothing else. A connector therefore had nothing to subscribe to for
    /// the parts, the blocks sat where the asset authored them, and the cell looked like
    /// two robots miming. These are the variables and bindings that were missing.
    /// </remarks>
    public sealed partial class RobotCell
    {
        private const string PartsScopePrimPath = "/Cell/Parts";
        private const string JawUpperSuffix = "/JawUpper";
        private const string JawLowerSuffix = "/JawLower";

        /// <summary>
        /// Jaw offset from the tool axis with the gripper closed on a part, in metres.
        /// </summary>
        /// <remarks>
        /// The finger inner face sits 12 mm inboard of its jaw origin, so 0.047 puts it on
        /// the 35 mm half-width of the block: closed means gripping, not intersecting.
        /// </remarks>
        private const double JawClosedOffset = 0.047;

        /// <summary>
        /// Jaw offset with the gripper fully open, in metres - 13 mm of stroke per jaw.
        /// </summary>
        private const double JawOpenOffset = 0.060;

        private readonly List<PartTwin> m_partTwins = [];
        private readonly List<JawTwin> m_jawTwins = [];

        /// <summary>
        /// Creates the twin variables for the workpieces and the gripper jaws.
        /// </summary>
        /// <remarks>
        /// The variables are registered before the motion device system is built, because
        /// the bindings that reference them are authored inside the system callback and a
        /// binding can only carry a SourceNodeId that already exists. They deliberately sit
        /// outside the robotics subtree: a workpiece is not a device, and the robotics
        /// builder validates every node below its root as one it allocated itself.
        /// </remarks>
        /// <param name="ns">The instance namespace index.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async ValueTask MaterialiseTwinNodesAsync(
            ushort ns,
            CancellationToken cancellationToken)
        {
            var folder = new FolderState(null)
            {
                SymbolicName = "CellTwin",
                NodeId = new NodeId("CellTwin", ns),
                BrowseName = new QualifiedName("CellTwin", ns),
                DisplayName = new LocalizedText("CellTwin"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                EventNotifier = EventNotifiers.None
            };
            folder.AddReference(ReferenceTypeIds.Organizes, true, Opc.Ua.ObjectIds.ObjectsFolder);

            foreach (CellPart part in m_choreographer.Parts)
            {
                m_partTwins.Add(new PartTwin(
                    part.Id,
                    CreateCoordinateVariable(folder, ns, part.Id + "Position"),
                    CreateOrientationVariable(folder, ns, part.Id + "Heading")));
            }

            foreach (RobotAgent agent in m_choreographer.Robots)
            {
                m_jawTwins.Add(new JawTwin(
                    agent.Id,
                    CreateCoordinateVariable(folder, ns, agent.Id + "JawUpper"),
                    CreateCoordinateVariable(folder, ns, agent.Id + "JawLower")));
            }

            SystemContext.AssignInstanceChildNodeIds(folder);
            await Manager.AddPredefinedNodeAsync(folder, cancellationToken).ConfigureAwait(false);
            await Server.NodeManager.AddReferencesAsync(
                Opc.Ua.ObjectIds.ObjectsFolder,
                [new NodeStateReference(ReferenceTypeIds.Organizes, false, folder.NodeId)],
                cancellationToken).ConfigureAwait(false);

            PublishTwinState(m_choreographer);
        }

        /// <summary>
        /// Binds the twin variables to the prims they drive.
        /// </summary>
        /// <remarks>
        /// The vector translate and rotate ops are what a connector composes into a prim's
        /// matrix; the matrix4d profile itself is explicitly left unresolved by the
        /// reference connector, and a binding it cannot convert is dropped in silence.
        /// </remarks>
        /// <param name="cellRep">The cell representation the bindings hang from.</param>
        /// <param name="usdNs">The OpenUSD namespace index.</param>
        private void MaterialiseTwinBindings(OpenUsdRepresentationState cellRep, ushort usdNs)
        {
            foreach (PartTwin part in m_partTwins)
            {
                string primPath = PartsScopePrimPath + "/" + part.Id;
                CreateBinding(cellRep, usdNs, part.Id + "Position",
                    GuidFor("part:" + part.Id + ":position"), part.Position.NodeId,
                    primPath, "xformOp:translate", "double3",
                    OpenUsdRenderTargetKindEnum.Translation, 1.0);
                CreateBinding(cellRep, usdNs, part.Id + "Heading",
                    GuidFor("part:" + part.Id + ":heading"), part.Heading.NodeId,
                    primPath, "xformOp:rotateXYZ", "double3",
                    OpenUsdRenderTargetKindEnum.Rotation, 1.0);
            }

            foreach (JawTwin jaw in m_jawTwins)
            {
                string toolPath = PrimPathOf(jaw.RobotId) + ToolSuffix;
                CreateBinding(cellRep, usdNs, jaw.RobotId + "JawUpper",
                    GuidFor("jaw:" + jaw.RobotId + ":upper"), jaw.Upper.NodeId,
                    toolPath + JawUpperSuffix, "xformOp:translate", "double3",
                    OpenUsdRenderTargetKindEnum.Translation, 1.0);
                CreateBinding(cellRep, usdNs, jaw.RobotId + "JawLower",
                    GuidFor("jaw:" + jaw.RobotId + ":lower"), jaw.Lower.NodeId,
                    toolPath + JawLowerSuffix, "xformOp:translate", "double3",
                    OpenUsdRenderTargetKindEnum.Translation, 1.0);
            }
        }

        /// <summary>
        /// Pushes the current workpiece poses and jaw positions into their variables.
        /// </summary>
        /// <param name="cell">The choreographer holding the state to publish.</param>
        private void PublishTwinState(CellChoreographer cell)
        {
            foreach (PartTwin twin in m_partTwins)
            {
                CellPart? part = FindPart(cell, twin.Id);
                if (part == null)
                {
                    continue;
                }
                (double x, double y, double z, double heading) = ResolvePartPose(cell, part);
                UpdateCoordinates(twin.Position, x, y, z);
                UpdateOrientation(twin.Heading, heading);
            }

            foreach (JawTwin twin in m_jawTwins)
            {
                RobotAgent? agent = FindAgent(cell, twin.RobotId);
                if (agent == null)
                {
                    continue;
                }
                double offset = JawOffset(agent.GripperOpening);
                UpdateCoordinates(twin.Upper, 0.0, offset, 0.0);
                UpdateCoordinates(twin.Lower, 0.0, -offset, 0.0);
            }
        }

        /// <summary>
        /// The pose to draw a workpiece at.
        /// </summary>
        /// <remarks>
        /// A resting part is simply where it rests. A carried one is recomputed from the
        /// platform pose that was last published rather than taken from the simulation,
        /// because the platform and the part reach the twin on different loops: drawing the
        /// part from the newer of the pair floats it out in front of the jaws by however far
        /// the robot has driven since the platform was last sent.
        /// </remarks>
        private static (double X, double Y, double Z, double Heading) ResolvePartPose(
            CellChoreographer cell,
            CellPart part)
        {
            if (part.CarriedBy == null)
            {
                return (part.X, part.Y, part.Z, part.HeadingDegrees);
            }
            RobotAgent? carrier = FindAgent(cell, part.CarriedBy);
            if (carrier?.PublishedPose is not (double px, double py, double heading))
            {
                return (part.X, part.Y, part.Z, part.HeadingDegrees);
            }
            RigidTransform mount = RobotKinematics.CreateMountPose(px, py, 0.0, heading);
            (double x, double y, double z) = RobotKinematics
                .ComputeToolCentrePoint(mount, carrier.Axes).Origin;
            return (x, y, z, heading);
        }

        /// <summary>
        /// The jaw offset from the tool axis for a gripper opening.
        /// </summary>
        /// <param name="opening">The opening, from 0 for closed to 1 for open.</param>
        /// <returns>The offset along the jaw stroke axis, in metres.</returns>
        private static double JawOffset(double opening)
        {
            double clamped = Math.Max(0.0, Math.Min(1.0, opening));
            return JawClosedOffset + ((JawOpenOffset - JawClosedOffset) * clamped);
        }

        private static CellPart? FindPart(CellChoreographer cell, string partId)
        {
            foreach (CellPart part in cell.Parts)
            {
                if (string.Equals(part.Id, partId, StringComparison.Ordinal))
                {
                    return part;
                }
            }
            return null;
        }

        private static string PrimPathOf(string robotId)
        {
            foreach ((string browseName, string primPath, bool _, double _) in s_robots)
            {
                if (string.Equals(browseName, robotId, StringComparison.Ordinal))
                {
                    return primPath;
                }
            }
            return RobotsScopePrimPath + "/" + robotId;
        }

        /// <summary>
        /// Creates a 3D Cartesian coordinate variable the connector can read as a
        /// structured translation.
        /// </summary>
        /// <remarks>
        /// The translation profile accepts a ThreeDCartesianCoordinates or a ThreeDFrame
        /// and nothing else; a bare array of three doubles is not a 3D vector to it and
        /// would be coerced to a scalar, which authors the wrong arity onto the prim.
        /// </remarks>
        private ThreeDCartesianCoordinatesState CreateCoordinateVariable(
            NodeState parent,
            ushort ns,
            string name)
        {
            ThreeDCartesianCoordinatesState state = SystemContext
                .CreateInstanceOfThreeDCartesianCoordinatesType(
                    parent, new QualifiedName(name, ns));
            state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            state.AccessLevel = AccessLevels.CurrentRead;
            state.UserAccessLevel = AccessLevels.CurrentRead;
            parent.AddChild(state);
            return state;
        }

        /// <summary>
        /// Creates a 3D orientation variable the connector can read as a structured
        /// rotation.
        /// </summary>
        /// <remarks>
        /// The rotation is published as a vector rather than a bare angle because a
        /// connector composes a prim's matrix from the vector ops; a scalar
        /// <c>xformOp:rotateZ</c> is authored straight onto the prim instead, which only
        /// works where the asset declares that op, and a live-bound prim declares the
        /// matrix op alone.
        /// </remarks>
        private ThreeDOrientationState CreateOrientationVariable(
            NodeState parent,
            ushort ns,
            string name)
        {
            ThreeDOrientationState state = SystemContext
                .CreateInstanceOfThreeDOrientationType(parent, new QualifiedName(name, ns));
            state.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            state.AccessLevel = AccessLevels.CurrentRead;
            state.UserAccessLevel = AccessLevels.CurrentRead;
            parent.AddChild(state);
            return state;
        }

        private void UpdateOrientation(ThreeDOrientationState state, double degreesAboutZ)
        {
            state.Value = new ThreeDOrientation { A = 0.0, B = 0.0, C = degreesAboutZ };
            state.Timestamp = DateTime.UtcNow;
            UpdateDouble(state.A, 0.0);
            UpdateDouble(state.B, 0.0);
            UpdateDouble(state.C, degreesAboutZ);
            state.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        private void UpdateCoordinates(
            ThreeDCartesianCoordinatesState state,
            double x,
            double y,
            double z)
        {
            state.Value = new ThreeDCartesianCoordinates { X = x, Y = y, Z = z };
            state.Timestamp = DateTime.UtcNow;
            UpdateDouble(state.X, x);
            UpdateDouble(state.Y, y);
            UpdateDouble(state.Z, z);
            state.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        private void UpdateDouble(BaseDataVariableState<double>? variable, double value)
        {
            if (variable == null)
            {
                return;
            }
            variable.Value = value;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, includeChildren: false);
        }

        private sealed record PartTwin(
            string Id,
            ThreeDCartesianCoordinatesState Position,
            ThreeDOrientationState Heading);

        private sealed record JawTwin(
            string RobotId,
            ThreeDCartesianCoordinatesState Upper,
            ThreeDCartesianCoordinatesState Lower);
    }
}
