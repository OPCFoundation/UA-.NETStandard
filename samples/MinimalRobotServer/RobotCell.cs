/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.Robotics;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Robotics
{
    /// <summary>
    /// Builds the robot cell and wires the draft OPC UA — OpenUSD Bindings
    /// composition/aggregation model (spec §5.12–5.13) onto it: an OPC 40010
    /// <c>MotionDeviceSystem</c> "RobotCell" composed of 1..n 6-axis robots
    /// (Many, Reference), each robot composed of its 6 axes (Many, Child) with
    /// per-axis articulation, plus a cell emergency-stop safety visual, an opt-in
    /// speed-override command, and a gripper tool composed dynamically on R1's
    /// flange from model-change events (§5.13). The composition is recursive:
    /// system → devices → axes, and device → tool.
    /// </summary>
    public partial class RoboticsNodeManager
    {
        private const string CellPrimPath = "/Cell";
        private const string RobotsScopePrimPath = "/Cell/Robots";
        private const string ToolSuffix = "/Base/J1/J2/J3/J4/J5/J6/Flange/Tool";

        // OPC 40010 Robotics type NodeIds used only for OpenUSD component-binding type
        // definitions (the robotics instances themselves are created via the generated
        // CreateInstanceOf<Type> factories, so they are properly typed — not bare
        // BaseObjectStates carrying only a type-definition reference).
        private const uint MotionDeviceTypeId = 1004;
        private const uint AxisTypeId = 16601;

        // 6-axis articulated arm. Link Xforms are nested in robot.usda to form a serial
        // kinematic chain; each Axis' ActualPosition drives the named rotate op.
        private static readonly (string Name, string LinkPrimPath, string RotateOp, double Home, double Min, double Max)[] s_axisTemplate =
        {
            ("A1", "Base/J1", "xformOp:rotateZ", 0.0, -170.0, 170.0),
            ("A2", "Base/J1/J2", "xformOp:rotateY", -30.0, -120.0, 120.0),
            ("A3", "Base/J1/J2/J3", "xformOp:rotateY", 45.0, -120.0, 120.0),
            ("A4", "Base/J1/J2/J3/J4", "xformOp:rotateX", 0.0, -180.0, 180.0),
            ("A5", "Base/J1/J2/J3/J4/J5", "xformOp:rotateY", 60.0, -120.0, 120.0),
            ("A6", "Base/J1/J2/J3/J4/J5/J6", "xformOp:rotateX", 0.0, -360.0, 360.0),
        };

        // The cell aggregates two independently-articulated robots; R2 is phase-shifted
        // so the two arms move differently (proving the Reference — not Instance —
        // aggregation gives each robot its own overridable copy of the chain).
        private static readonly (string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds)[] s_robots =
        {
            ("R1", "/Cell/Robots/R1", true, 0.0),
            ("R2", "/Cell/Robots/R2", false, 3.0),
        };

        // Runtime simulation state (animated by the Configure.cs tick).
        internal sealed class AxisRuntime
        {
            public BaseDataVariableState Position = null!;
            public double Home;
            public double Min;
            public double Max;
            public double PhaseSeconds;
            public int Index;
        }

        private readonly List<AxisRuntime> m_axes = new();
        private readonly List<OpenUsdRepresentationState> m_axisReps = new();
        private BaseDataVariableState? m_estopVar;
        private BaseDataVariableState? m_speedOverrideVar;
        private NodeId? m_r1NodeId;

        private NodeId RoboticsType(uint id)
            => NodeId.Create(id, RoboticsNamespaceUri, Server.NamespaceUris);

        private async ValueTask MaterialiseRobotCellAsync(CancellationToken cancellationToken)
        {
            if (m_cellStage == null)
            {
                return;
            }
            try
            {
                ushort ns = (ushort)Server.NamespaceUris.GetIndex(RoboticsNamespaceUri);
                ushort usdNs = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);

                NodeState? deviceSet = PredefinedNodes.FindById(NodeId.Create(
                    Opc.Ua.Di.Objects.DeviceSet, DiNamespaceUri, Server.NamespaceUris));
                if (deviceSet == null)
                {
                    m_logger.LogWarning("DI DeviceSet not found — RobotCell will not be created.");
                    return;
                }

                // MotionDeviceSystem "RobotCell" — created as a generated
                // MotionDeviceSystemType instance (MotionDeviceSystemState), NOT a bare
                // BaseObjectState carrying only a type-definition reference, so the instance
                // has the real companion-type structure (MotionDevices / Controllers /
                // SafetyStates). The generator's CreateInstanceOf factory stamps TYPE NodeIds
                // on the materialised children; the single AssignChildNodeIds(cell) walk below
                // re-stamps per-instance NodeIds before registration.
                MotionDeviceSystemState cell = SystemContext.CreateInstanceOfMotionDeviceSystemType(
                    deviceSet, new QualifiedName("RobotCell", ns));
                cell.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                cell.NodeId = SystemContext.NodeIdFactory.New(SystemContext, cell);
                deviceSet.AddChild(cell);
                OpenUsdRepresentationState cellRep = AttachRepresentation(cell, CellPrimPath, usdNs);

                // SafetyStates / EmergencyStop (Boolean, animated demo signal).
                NodeState safety = (NodeState?)cell.SafetyStates
                    ?? cell.CreateOrReplaceSafetyStates(SystemContext, null!);
                m_estopVar = EnsureVariable(safety, "EmergencyStop", Opc.Ua.DataTypeIds.Boolean,
                    new Variant(false), writable: false, ns);

                // Controller_C1 as a generated ControllerType instance in the system's
                // mandatory Controllers folder; SpeedOverride is a writable demo command target.
                FolderState controllers = cell.Controllers
                    ?? cell.CreateOrReplaceControllers(SystemContext, null!);
                ControllerState controller = SystemContext.CreateInstanceOfControllerType(
                    controllers, new QualifiedName("Controller_C1", ns));
                controller.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                controller.NodeId = SystemContext.NodeIdFactory.New(SystemContext, controller);
                controllers.AddChild(controller);
                FolderState ctrlParams = EnsureFolder(controller, "ParameterSet", ns);
                m_speedOverrideVar = EnsureVariable(ctrlParams, "SpeedOverride", Opc.Ua.DataTypeIds.Double,
                    new Variant(100.0), writable: true, ns);

                // Robots are generated MotionDeviceType instances in the mandatory
                // MotionDevices folder (each with its own representation, axes, bindings).
                FolderState motionDevices = cell.MotionDevices
                    ?? cell.CreateOrReplaceMotionDevices(SystemContext, null!);
                var robotReps = new List<OpenUsdRepresentationState>();
                foreach ((string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds) r in s_robots)
                {
                    robotReps.Add(BuildRobot(motionDevices, r, ns, usdNs));
                }

                // System-level composition + bindings.
                // RobotsAggregation (Many, Reference): 1..n robots -> reference prims of
                // robot.usda. Reference (not Instance) so each robot articulates
                // independently.
                CreateComponentBinding(cellRep, usdNs, "RobotsAggregation",
                    new Guid("a1b2c3d4-0001-4a10-9c01-100000000001"),
                    OpenUsdCardinalityEnum.Many, OpenUsdCompositionArcEnum.Reference,
                    RobotsScopePrimPath, assetReference: "@robot.usda@</Robot>",
                    componentTypeDefinition: RoboticsType(MotionDeviceTypeId));

                // EmergencyStopBeacon (UaAlarmToUsd, Visibility): the safety beacon shows
                // while the cell emergency-stop is active.
                CreateBinding(cellRep, usdNs, "EmergencyStopBeacon",
                    new Guid("a1b2c3d4-0002-4a10-9c01-100000000002"),
                    m_estopVar.NodeId, "/Cell/SafetyBeacon", "visibility", "token",
                    OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                    bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                    alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

                // SpeedOverrideCommand (UsdToUaCommand, opt-in): a USD-side speed-override
                // intent is written to the controller SpeedOverride Variable.
                CreateBinding(cellRep, usdNs, "SpeedOverrideCommand",
                    new Guid("a1b2c3d4-0003-4a10-9c01-100000000003"),
                    null, "/Cell", "inputs:speedOverride", "double",
                    kind: null, 1.0,
                    bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdCommandBindingType,
                    signalRole: OpenUsdSignalRoleEnum.Controllable,
                    commandTargetNodeId: m_speedOverrideVar.NodeId,
                    commandTriggerPropertyName: "inputs:speedOverride");

                // Assign per-instance NodeIds across the whole cell subtree (the
                // generator-emitted CreateOrReplace helpers stamp TYPE NodeIds on the
                // representation/binding member nodes), then register the tree.
                AssignChildNodeIds(cell);
                await AddPredefinedNodeAsync(SystemContext, cell, cancellationToken).ConfigureAwait(false);

                // Register every representation (system + robots + axes) in the discovery
                // registry so a generic connector processes all 15 without domain
                // knowledge (spec §4.2).
                OrganiseRepresentation(cellRep);
                foreach (OpenUsdRepresentationState robotRep in robotReps)
                {
                    OrganiseRepresentation(robotRep);
                }
                foreach (OpenUsdRepresentationState axisRep in m_axisReps)
                {
                    OrganiseRepresentation(axisRep);
                }

                // Dynamic composition (§5.13): emit model-change events on runtime tool
                // attach/detach so a connector reconciles the gripper prim.
                ModelChangeEmissionEnabled = true;
                _ = RunDynamicToolAsync(ns, usdNs);

                m_logger.MaterialisedRobotCell(
                    s_robots.Length, m_axes.Count, 1 + robotReps.Count + m_axisReps.Count);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Failed to materialise the RobotCell.");
            }
        }

        private OpenUsdRepresentationState BuildRobot(
            FolderState motionDevices,
            (string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds) r,
            ushort ns, ushort usdNs)
        {
            MotionDeviceState robot = SystemContext.CreateInstanceOfMotionDeviceType(
                motionDevices, new QualifiedName(r.BrowseName, ns));
            robot.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            robot.NodeId = SystemContext.NodeIdFactory.New(SystemContext, robot);
            motionDevices.AddChild(robot);
            if (r.HasTool)
            {
                m_r1NodeId = robot.NodeId;
            }

            OpenUsdRepresentationState robotRep = AttachRepresentation(robot, r.PrimPath, usdNs);

            // 6 axes as generated AxisType instances in the motion device's mandatory Axes
            // folder (each with its own representation + articulation).
            FolderState axes = robot.Axes ?? robot.CreateOrReplaceAxes(SystemContext, null!);
            for (int i = 0; i < s_axisTemplate.Length; i++)
            {
                BuildAxis(axes, r, s_axisTemplate[i], i, ns, usdNs);
            }

            // AxesAggregation (Many, Child): the robot is composed of its 6 axes.
            CreateComponentBinding(robotRep, usdNs, "AxesAggregation",
                GuidFor(r.BrowseName + ":axes"),
                OpenUsdCardinalityEnum.Many, OpenUsdCompositionArcEnum.Child,
                r.PrimPath, componentTypeDefinition: RoboticsType(AxisTypeId));

            // EmergencyStopWarning (UaAlarmToUsd, Visibility): a warning halo on the
            // robot shows while the shared cell emergency-stop is active.
            CreateBinding(robotRep, usdNs, "EmergencyStopWarning",
                GuidFor(r.BrowseName + ":warning"),
                m_estopVar!.NodeId, r.PrimPath + "/Warning", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

            // GripperTool (One, Reference, dynamic): a tool is composed on R1's flange at
            // runtime. No ComponentRepresentation is set so the connector resolves it by
            // presence — the tool prim only appears once the MountedTool node is added.
            if (r.HasTool)
            {
                CreateComponentBinding(robotRep, usdNs, "GripperTool",
                    GuidFor(r.BrowseName + ":tool"),
                    OpenUsdCardinalityEnum.One, OpenUsdCompositionArcEnum.Reference,
                    r.PrimPath + ToolSuffix, assetReference: "@tool.usda@</Gripper>",
                    dynamic: true, changeEventSource: Opc.Ua.ObjectIds.Server,
                    componentTypeDefinition: Opc.Ua.ObjectTypeIds.BaseObjectType);
            }

            return robotRep;
        }

        private void BuildAxis(
            FolderState axesFolder,
            (string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds) r,
            (string Name, string LinkPrimPath, string RotateOp, double Home, double Min, double Max) a,
            int index, ushort ns, ushort usdNs)
        {
            AxisState axis = SystemContext.CreateInstanceOfAxisType(
                axesFolder, new QualifiedName(a.Name, ns));
            axis.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            axis.NodeId = SystemContext.NodeIdFactory.New(SystemContext, axis);
            axesFolder.AddChild(axis);
            FolderState paramSet = EnsureFolder(axis, "ParameterSet", ns);
            BaseDataVariableState pos = EnsureVariable(
                paramSet, "ActualPosition", Opc.Ua.DataTypeIds.Double, new Variant(a.Home), writable: false, ns);

            string linkPrim = r.PrimPath + "/" + a.LinkPrimPath;
            OpenUsdRepresentationState axisRep = AttachRepresentation(axis, linkPrim, usdNs);

            // Articulation (UaToUsdTelemetry, Rotation): the Axis' ActualPosition drives
            // the link's rotate op (degrees map 1:1 to the USD rotate op).
            CreateBinding(axisRep, usdNs, "Articulation",
                GuidFor($"{r.BrowseName}:{a.Name}:articulation"),
                pos.NodeId, linkPrim, a.RotateOp, "double",
                OpenUsdRenderTargetKindEnum.Rotation, 1.0,
                signalRole: OpenUsdSignalRoleEnum.Observable,
                sourceSemanticId: "0173-1#02-BAF564#005");

            m_axisReps.Add(axisRep);
            m_axes.Add(new AxisRuntime
            {
                Position = pos,
                Home = a.Home,
                Min = a.Min,
                Max = a.Max,
                PhaseSeconds = r.PhaseSeconds,
                Index = index,
            });
        }

        private FolderState EnsureFolder(NodeState parent, string name, ushort ns)
        {
            if (parent.FindChild(SystemContext, new QualifiedName(name, ns)) is FolderState existing)
            {
                return existing;
            }
            return CreateFolder(parent, name, ns);
        }

        private BaseDataVariableState EnsureVariable(
            NodeState parent, string name, NodeId dataType, Variant initial, bool writable, ushort ns)
        {
            if (parent.FindChild(SystemContext, new QualifiedName(name, ns)) is BaseDataVariableState existing)
            {
                existing.Value = initial;
                return existing;
            }
            return CreateVariable(parent, name, dataType, initial, writable, ns);
        }

        // Dynamic composition (§5.13): mount the gripper tool on R1's flange shortly
        // after startup. The CreateNode emits a GeneralModelChangeEvent, so a connector
        // already watching reconciles the tool prim (recompose path), and a connector
        // attaching later composes it from the now-present MountedTool (initial path).
        // The tool then stays mounted, so the composed stage renders it deterministically.
        private async Task RunDynamicToolAsync(ushort ns, ushort usdNs)
        {
            try
            {
                if (m_r1NodeId == null)
                {
                    return;
                }
                await Task.Delay(3000).ConfigureAwait(false);
                _ = await AddMountedToolAsync(ns, usdNs).ConfigureAwait(false);
                m_logger.LogInformation(
                    "Dynamic composition: gripper tool mounted on R1 flange at runtime (stays attached).");
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex, "Dynamic tool composition demo failed.");
            }
        }

        private async Task<NodeId?> AddMountedToolAsync(ushort ns, ushort usdNs)
        {
            if (m_r1NodeId == null)
            {
                return null;
            }
            var tool = new BaseObjectState(null)
            {
                SymbolicName = "MountedTool",
                BrowseName = new QualifiedName("MountedTool", ns),
                DisplayName = new LocalizedText("MountedTool"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };

            // The tool carries its own representation so the connector's component
            // resolver (which requires a child OpenUsdRepresentation) includes it.
            OpenUsdRepresentationState rep = SystemContext.CreateInstanceOfOpenUsdRepresentationType(
                tool, new QualifiedName("OpenUsdRepresentation", usdNs));
            rep.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            tool.AddChild(rep);
            rep.CreateOrReplaceStage(SystemContext, null!).Value = m_cellStage!.NodeId;
            rep.CreateOrReplacePrimPath(SystemContext, null!).Value = s_robots[0].PrimPath + ToolSuffix;

            NodeId newId = await CreateNodeAsync(SystemContext, (NodeId)m_r1NodeId,
                ReferenceTypeIds.HasComponent, new QualifiedName("MountedTool", ns), tool, CancellationToken.None)
                .ConfigureAwait(false);
            m_logger.AttachedGripperTool(newId);
            return newId;
        }

        // Deterministic Guid from a stable key (SHA-256 truncated to 16 bytes), so
        // binding ids are reproducible across builds without a broken hash algorithm.
        private static Guid GuidFor(string key)
        {
            byte[] hash;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("robotics:" + key));
            }
#pragma warning restore CA1850
            var g = new byte[16];
            Array.Copy(hash, g, 16);
            return new Guid(g);
        }
    }

    internal static partial class RobotCellLog
    {
        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 1,
            Level = LogLevel.Information,
            Message = "Materialised RobotCell ({RobotCount} robots, {AxisCount} axes, {RepCount} representations).")]
        public static partial void MaterialisedRobotCell(
            this ILogger logger, int robotCount, int axisCount, int repCount);

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 2,
            Level = LogLevel.Information,
            Message = "Dynamic composition: attached gripper tool (NodeId={NodeId}); model-change emitted.")]
        public static partial void AttachedGripperTool(this ILogger logger, NodeId nodeId);
    }
}
