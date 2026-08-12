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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di.Server;
using Opc.Ua.Gpos;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Positioning.Server.Hosting;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Rsl;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.NodeManager;

namespace Robotics
{
    /// <summary>
    /// Builds the sample robot cell using the stock Robotics hosting configurator.
    /// </summary>
    public sealed partial class RobotCell : IRoboticsConfigurator, IDisposable
    {
        private const string CellPrimPath = "/Cell";
        private const string RobotsScopePrimPath = "/Cell/Robots";
        private const string ToolSuffix = "/Base/J1/J2/J3/J4/J5/J6/Flange/Tool";
        private const uint MotionDeviceTypeId = 1004;
        private const uint AxisTypeId = 16601;

        // Axis table of the reference robot, a KUKA KR 16-2. The limits are the published
        // software limits; the home values are a plausible ready pose and match the
        // defaults authored into robot.usda so the asset looks right when opened on its
        // own. LinkPrimPath and RotateOp are the binding contract with the USD asset.
        private static readonly (
            string Name,
            string LinkPrimPath,
            string RotateOp,
            double Home,
            double Min,
            double Max)[] s_axisTemplate =
        [
            ("A1", "Base/J1", "xformOp:rotateZ", 0.0, -185.0, 185.0),
            ("A2", "Base/J1/J2", "xformOp:rotateY", -60.0, -155.0, 35.0),
            ("A3", "Base/J1/J2/J3", "xformOp:rotateY", 75.0, -130.0, 154.0),
            ("A4", "Base/J1/J2/J3/J4", "xformOp:rotateX", 0.0, -350.0, 350.0),
            ("A5", "Base/J1/J2/J3/J4/J5", "xformOp:rotateY", 45.0, -130.0, 130.0),
            ("A6", "Base/J1/J2/J3/J4/J5/J6", "xformOp:rotateX", 0.0, -350.0, 350.0)
        ];

        private static readonly (string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds)[] s_robots =
        [
            ("R1", "/Cell/Robots/R1", true, 0.0),
            ("R2", "/Cell/Robots/R2", true, 3.0)
        ];

        // TODO: Use a collection expression once net48 is dropped. ConditionalWeakTable is not
        // constructible from a collection expression on net48, so IDE0028 cannot be satisfied here.
#pragma warning disable IDE0028 // Simplify collection initialization
        private static readonly ConditionalWeakTable<AsyncCustomNodeManager, RobotCell> s_cells = new();
#pragma warning restore IDE0028 // Simplify collection initialization

        private readonly ILogger<RobotCell> m_logger;
        private readonly List<AxisRuntime> m_axes = [];
        private readonly List<OpenUsdRepresentationState> m_axisReps = [];
        private readonly List<RobotRuntime> m_robots = [];
        private readonly List<PositioningProviderSubscription> m_positioningSubscriptions = [];
        private BaseDataVariableState? m_estopVar;
        private BaseDataVariableState? m_speedOverrideVar;
        private DiNodeManager? m_manager;
        private NodeId m_r1NodeId;
        private MotionDeviceSystemState? m_robotCell;
        private readonly CellChoreographer m_choreographer;

        /// <summary>
        /// Creates the sample Robotics configurator.
        /// </summary>
        /// <param name="logger">
        /// The configurator logger.
        /// </param>
        /// <param name="choreographer">
        /// The cell choreography every robot, workpiece and twin binding is driven from.
        /// </param>
        /// <remarks>
        /// Required rather than optional: without it the simulation tick has nothing to
        /// advance and the whole cell silently freezes with its axes at the home pose. A
        /// missing registration should fail where it is made, not look like a stopped
        /// robot.
        /// </remarks>
        public RobotCell(ILogger<RobotCell> logger, CellChoreographer choreographer)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_choreographer = choreographer ??
                throw new ArgumentNullException(nameof(choreographer));
        }

        private DiNodeManager Manager => m_manager ??
            throw new InvalidOperationException(
                "RobotCell has not been attached to a Robotics build context.");

        private IServerInternal Server => Manager.Server;

        private ServerSystemContext SystemContext => Manager.SystemContext;

        /// <inheritdoc/>
        public async ValueTask ConfigureAsync(
            IRoboticsBuildContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            m_manager = context.Manager;
            s_cells.Remove(context.Manager);
            s_cells.Add(context.Manager, this);
            await MaterialiseOpenUsdFacilityAsync(cancellationToken).ConfigureAwait(false);
            await MaterialiseTwinNodesAsync(context.InstanceNamespaceIndex, cancellationToken)
                .ConfigureAwait(false);
            await MaterialiseRobotCellAsync(context, cancellationToken).ConfigureAwait(false);
            Configure(context.Nodes);
            m_logger.RoboticsAddressSpaceReady(m_axes.Count + m_robots.Count + 1);
        }

        internal static RobotCell GetForManager(AsyncCustomNodeManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            return s_cells.TryGetValue(manager, out RobotCell? cell)
                ? cell
                : throw new InvalidOperationException("RobotCell has not configured this manager.");
        }

        internal sealed class AxisRuntime
        {
            public BaseDataVariableState Position = null!;
            public double Home;
            public double Min;
            public double Max;
            public double PhaseSeconds;
            public int Index;
            public string RobotId = string.Empty;
        }

        internal sealed class RobotRuntime
        {
            public string SourceId = string.Empty;
            public string PrimPath = string.Empty;
            public MotionDeviceState Robot = null!;
            public OpenUsdRepresentationState Representation = null!;
        }

        private NodeId RoboticsType(uint id)
        {
            return NodeId.Create(id, Opc.Ua.Robotics.Namespaces.Robotics, Server.NamespaceUris);
        }

        private async ValueTask MaterialiseRobotCellAsync(
            IRoboticsBuildContext context,
            CancellationToken cancellationToken)
        {
            if (m_cellStage == null)
            {
                return;
            }

            try
            {
                ushort ns = context.InstanceNamespaceIndex;
                ushort usdNs = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
                OpenUsdRepresentationState? cellRep = null;
                var robotReps = new List<OpenUsdRepresentationState>();

                await context.AddMotionDeviceSystemAsync(
                    "RobotCell",
                    system =>
                    {
                        system.WithComponentName("RobotCell");
                        cellRep = AttachRepresentation(system.State, CellPrimPath, usdNs);
                        m_robotCell = system.State;

                        IControllerBuilder controller = system.AddController(
                            "Controller_C1",
                            ConfigureController);
                        ISafetyStateBuilder safety = system.AddSafetyState(
                            "Safety",
                            ConfigureSafetyState);

                        foreach ((string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds) robot
                            in s_robots)
                        {
                            IMotionDeviceBuilder motionDevice = system.AddMotionDevice(
                                robot.BrowseName,
                                builder => BuildRobot(builder, robot, ns, usdNs, robotReps));
                            controller.Controls(motionDevice).UsesSafetyState(safety);
                        }

                        CreateComponentBinding(cellRep, usdNs, "RobotsAggregation",
                            new Guid("a1b2c3d4-0001-4a10-9c01-100000000001"),
                            OpenUsdCardinalityEnum.Many, OpenUsdCompositionArcEnum.Reference,
                            RobotsScopePrimPath, assetReference: "@robot.usda@</Robot>",
                            componentTypeDefinition: RoboticsType(MotionDeviceTypeId));

                        CreateBinding(cellRep, usdNs, "EmergencyStopBeacon",
                            new Guid("a1b2c3d4-0002-4a10-9c01-100000000002"),
                            m_estopVar!.NodeId, "/Cell/SafetyBeacon", "visibility", "token",
                            OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                            bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                            alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

                        MaterialiseTwinBindings(cellRep, usdNs);

                        CreateBinding(cellRep, usdNs, "SpeedOverrideCommand",
                            new Guid("a1b2c3d4-0003-4a10-9c01-100000000003"),
                            NodeId.Null, "/Cell", "inputs:speedOverride", "double",
                            kind: null, 1.0,
                            bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdCommandBindingType,
                            signalRole: OpenUsdSignalRoleEnum.Controllable,
                            commandTargetNodeId: m_speedOverrideVar!.NodeId,
                            commandTriggerPropertyName: "inputs:speedOverride");
                    },
                    cancellationToken).ConfigureAwait(false);

                if (cellRep == null || m_estopVar == null || m_speedOverrideVar == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "RobotCell was not fully materialised.");
                }

                OrganiseRepresentation(cellRep);
                foreach (OpenUsdRepresentationState robotRep in robotReps)
                {
                    OrganiseRepresentation(robotRep);
                }
                foreach (OpenUsdRepresentationState axisRep in m_axisReps)
                {
                    OrganiseRepresentation(axisRep);
                }

                Manager.ModelChangeEmissionEnabled = true;
                _ = RunDynamicToolAsync(ns, usdNs);

                m_logger.MaterialisedRobotCell(
                    s_robots.Length,
                    m_axes.Count,
                    1 + robotReps.Count + m_axisReps.Count);
            }
            catch (Exception ex)
            {
                m_logger.RobotCellFailed(ex);
            }
        }

        private void ConfigureController(IControllerBuilder controller)
        {
            controller.WithComponentName("Controller_C1")
                .WithCurrentUser(user => user.WithLevel("Operator").WithName("Operator"))
                .AddSoftware("ControllerSoftware");
            controller.AddTaskControl(
                "TaskControl_C1",
                taskControl => taskControl
                    .WithComponentName("TaskControl_C1")
                    .WithTaskProgramLoaded(false)
                    .WithTaskProgramName(string.Empty)
                    .AddTaskControlOperation());
        }

        private void ConfigureSafetyState(ISafetyStateBuilder safety)
        {
            safety.WithComponentName("Safety")
                .WithEmergencyStop(false)
                .AddEmergencyStop("EmergencyStop", "EmergencyStop", stop => stop.WithActive(false));
            safety.Configure((state, ctx) => m_estopVar = FindRequiredChild<BaseDataVariableState>(
                state.ParameterSet!,
                "EmergencyStop"));
        }

        private void BuildRobot(
            IMotionDeviceBuilder builder,
            (string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds) robot,
            ushort ns,
            ushort usdNs,
            List<OpenUsdRepresentationState> robotReps)
        {
            builder.WithComponentName(robot.BrowseName)
                .WithCategory(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT)
                .WithSpeedOverride(100.0);
            if (robot.HasTool)
            {
                builder.BindSpeedOverrideWrite(WriteSpeedOverrideAsync);
            }

            OpenUsdRepresentationState? robotRep = null;
            IPowerTrainBuilder powerTrain = builder.AddPowerTrain(
                $"{robot.BrowseName}_PowerTrain",
                pt => pt.WithComponentName($"{robot.BrowseName}_PowerTrain")
                    .AddMotor($"{robot.BrowseName}_Motor", motor => motor.WithMotorTemperature(25.0)));

            for (int i = 0; i < s_axisTemplate.Length; i++)
            {
                IAxisBuilder axis = builder.AddAxis(
                    s_axisTemplate[i].Name,
                    axisBuilder => BuildAxis(axisBuilder, robot, s_axisTemplate[i], i, usdNs));
                axis.Requires(powerTrain);
                powerTrain.Moves(axis);
            }

            builder.Configure((state, ctx) =>
            {
                // Keyed on the robot itself, not on whether it carries a tool: both robots
                // do now, and the dynamic tool demo and the speed-override command target
                // are deliberately R1's.
                if (string.Equals(robot.BrowseName, "R1", StringComparison.Ordinal))
                {
                    m_r1NodeId = state.NodeId;
                    m_speedOverrideVar = FindRequiredChild<BaseDataVariableState>(
                        state.ParameterSet!,
                        "SpeedOverride");
                    m_speedOverrideVar.OnReadUserAccessLevel = OnReadCommandTargetUserAccessLevel;

                    // Part 5 §9.32.2: only a node carrying a NodeVersion property may
                    // trigger a ModelChangeEvent, and the framework drops entries for
                    // nodes that lack one. Mounting the tool adds a reference to this
                    // robot, so without this the addition is filtered out and no client
                    // is ever told the gripper appeared - while the *removal* still
                    // reports, because a deleted node is no longer in the manager's index
                    // and takes the "not mine, pass it through" path. A connector that
                    // starts while the tool is detached would then never compose it.
                    _ = state.EnableModelChangeTracking(ns);
                }

                robotRep = AttachRepresentation(state, robot.PrimPath, usdNs);
                m_robots.Add(new RobotRuntime
                {
                    SourceId = robot.BrowseName,
                    PrimPath = robot.PrimPath,
                    Robot = state,
                    Representation = robotRep
                });
                robotReps.Add(robotRep);
            });

            if (robotRep == null)
            {
                return;
            }

            CreateComponentBinding(robotRep, usdNs, "AxesAggregation",
                GuidFor(robot.BrowseName + ":axes"),
                OpenUsdCardinalityEnum.Many, OpenUsdCompositionArcEnum.Child,
                robot.PrimPath, componentTypeDefinition: RoboticsType(AxisTypeId));

            CreateBinding(robotRep, usdNs, "EmergencyStopWarning",
                GuidFor(robot.BrowseName + ":warning"),
                m_estopVar!.NodeId, robot.PrimPath + "/Warning", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0,
                bindingTypeId: Opc.Ua.OpenUsd.ObjectTypes.OpenUsdAlarmBindingType,
                alarmAspect: OpenUsdAlarmAspectEnum.ActiveState);

            if (robot.HasTool)
            {
                CreateComponentBinding(robotRep, usdNs, "GripperTool",
                    GuidFor(robot.BrowseName + ":tool"),
                    OpenUsdCardinalityEnum.One, OpenUsdCompositionArcEnum.Reference,
                    robot.PrimPath + ToolSuffix, assetReference: "@tool.usda@</Gripper>",
                    dynamic: true, changeEventSource: Opc.Ua.ObjectIds.Server,
                    componentTypeDefinition: Opc.Ua.ObjectTypeIds.BaseObjectType);
            }
        }

        private void BuildAxis(
            IAxisBuilder builder,
            (string BrowseName, string PrimPath, bool HasTool, double PhaseSeconds) robot,
            (string Name, string LinkPrimPath, string RotateOp, double Home, double Min, double Max) axis,
            int index,
            ushort usdNs)
        {
            builder.WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                .WithActualPosition(axis.Home);
            builder.Configure((state, ctx) =>
            {
                BaseDataVariableState position = FindRequiredChild<BaseDataVariableState>(
                    state.ParameterSet!,
                    "ActualPosition");
                string linkPrim = robot.PrimPath + "/" + axis.LinkPrimPath;
                OpenUsdRepresentationState axisRep = AttachRepresentation(state, linkPrim, usdNs);

                CreateBinding(axisRep, usdNs, "Articulation",
                    GuidFor($"{robot.BrowseName}:{axis.Name}:articulation"),
                    position.NodeId, linkPrim, axis.RotateOp, "double",
                    OpenUsdRenderTargetKindEnum.Rotation, 1.0,
                    signalRole: OpenUsdSignalRoleEnum.Observable,
                    sourceSemanticId: "0173-1#02-BAF564#005");

                m_axisReps.Add(axisRep);
                m_axes.Add(new AxisRuntime
                {
                    Position = position,
                    Home = axis.Home,
                    Min = axis.Min,
                    Max = axis.Max,
                    PhaseSeconds = robot.PhaseSeconds,
                    Index = index,
                    RobotId = robot.BrowseName
                });
            });
        }

        private T FindRequiredChild<T>(NodeState parent, string browseName)
            where T : BaseInstanceState
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var children = new List<BaseInstanceState>();
            parent.GetChildren(SystemContext, children);
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is T typed &&
                    string.Equals(children[i].BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return typed;
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Child '{0}' was not found below '{1}'.",
                browseName,
                parent.BrowseName);
        }

        private void AssignInstanceSubtree(BaseInstanceState node, NodeState referenceRoot)
        {
            NodeId previousNodeId = SystemContext.AssignInstanceNodeId(node);
            SystemContext.AssignInstanceChildNodeIds(node, previousNodeId, referenceRoot);
        }

        private ValueTask<ServiceResult> WriteSpeedOverrideAsync(
            Variant value,
            CancellationToken cancellationToken)
        {
            if (!value.TryGetValue(out double speedOverride))
            {
                return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadTypeMismatch));
            }

            UpdateDouble(m_speedOverrideVar, speedOverride);
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        private static ServiceResult OnReadCommandTargetUserAccessLevel(
            ISystemContext context,
            NodeState node,
            ref byte value)
        {
            value = IsAuthenticatedSession(context)
                ? AccessLevels.CurrentReadOrWrite
                : AccessLevels.CurrentRead;
            return ServiceResult.Good;
        }

        private static bool IsAuthenticatedSession(ISystemContext context)
        {
            IUserIdentity? identity = (context as ISessionSystemContext)?.UserIdentity;
            if (identity == null || identity.TokenType == UserTokenType.Anonymous)
            {
                return false;
            }
            ArrayOf<NodeId> roles = identity.GrantedRoleIds;
            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i] != Opc.Ua.ObjectIds.WellKnownRole_Anonymous)
                {
                    return true;
                }
            }
            return false;
        }

        private async Task RunDynamicToolAsync(ushort ns, ushort usdNs)
        {
            try
            {
                if (m_r1NodeId.IsNull)
                {
                    return;
                }
                await Task.Delay(3000).ConfigureAwait(false);
                while (!m_r1NodeId.IsNull)
                {
                    NodeId toolId = await AddMountedToolAsync(ns, usdNs).ConfigureAwait(false);
                    if (toolId.IsNull)
                    {
                        return;
                    }
                    m_logger.GripperToolMounted();
                    await Task.Delay(12000).ConfigureAwait(false);
                    _ = await Manager.DeleteNodeAsync(SystemContext, toolId).ConfigureAwait(false);
                    m_logger.DetachedGripperTool(toolId);
                    await Task.Delay(6000).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.DynamicToolFailed(ex);
            }
        }

        private async Task<NodeId> AddMountedToolAsync(ushort ns, ushort usdNs)
        {
            if (m_r1NodeId.IsNull)
            {
                return NodeId.Null;
            }
            var tool = new BaseObjectState(null)
            {
                SymbolicName = "MountedTool",
                BrowseName = new QualifiedName("MountedTool", ns),
                DisplayName = new LocalizedText("MountedTool"),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };

            _ = SystemContext.CreateRepresentation(
                tool,
                m_cellStage!.NodeId,
                s_robots[0].PrimPath + ToolSuffix,
                usdNs);

            NodeId newId = await Manager.AddNodeAsync(
                SystemContext,
                m_r1NodeId,
                tool,
                CancellationToken.None).ConfigureAwait(false);
            m_logger.AttachedGripperTool(newId);
            return newId;
        }

        internal async ValueTask ConfigurePositioningAsync(PositioningServerContext context)
        {
            if (m_robotCell == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "RobotCell must exist before Positioning is configured.");
            }

            MobileRobotPositionProvider? provider = null;
            for (int i = 0; i < context.GeoLocationProviders.Count; i++)
            {
                if (context.GeoLocationProviders[i] is MobileRobotPositionProvider candidate)
                {
                    provider = candidate;
                    break;
                }
            }
            if (provider == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "MobileRobotPositionProvider is not registered.");
            }

            PositioningAddressSpaceBuilder builder = context.AddressSpace;
            ushort rslNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.Rsl.Namespaces.RSL);
            ushort gposNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.Gpos.Namespaces.GPOS);
            ushort usdNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            EUInformation metres = new("m", "metre", "http://www.opcfoundation.org/UA/units/un/cefact");
            EUInformation degrees = new("deg", "degree", "http://www.opcfoundation.org/UA/units/un/cefact");

            SpatialObjectsListState list = builder.CreateSpatialObjectsList(
                m_robotCell,
                new QualifiedName("RobotCellFrames", rslNamespaceIndex),
                "RobotCell",
                CreateZeroFrame(),
                metres,
                degrees);
            await builder.RegisterAsync(list, context.CancellationToken).ConfigureAwait(false);

            ZoneState zone = builder.CreateZone(
                new QualifiedName("RobotCellZone", gposNamespaceIndex),
                RobotPositioningScenario.ZoneId,
                provider.Scenario.GroundControlPoints);
            await builder.RegisterAsync(zone, context.CancellationToken).ConfigureAwait(false);

            foreach (RobotRuntime runtime in m_robots)
            {
                await ConfigureRobotPositioningAsync(
                    context,
                    builder,
                    provider,
                    runtime,
                    list,
                    zone,
                    metres,
                    degrees,
                    rslNamespaceIndex,
                    gposNamespaceIndex,
                    usdNamespaceIndex).ConfigureAwait(false);
            }

            m_logger.PositioningConfigured(m_robots.Count);
        }

        private async ValueTask ConfigureRobotPositioningAsync(
            PositioningServerContext context,
            PositioningAddressSpaceBuilder builder,
            MobileRobotPositionProvider provider,
            RobotRuntime runtime,
            SpatialObjectsListState list,
            ZoneState zone,
            EUInformation metres,
            EUInformation degrees,
            ushort rslNamespaceIndex,
            ushort gposNamespaceIndex,
            ushort usdNamespaceIndex)
        {
            GeoLocationSample initial = await provider.ReadAsync(
                runtime.SourceId,
                context.CancellationToken).ConfigureAwait(false);
            GeoPosition initialPosition = initial.Position!.Value;
            GeoOrientation initialOrientation = initial.Orientation!.Value;
            ThreeDCartesianCoordinates localPosition =
                provider.Scenario.Fit.GlobalToLocal(
                    ToGeographic(initialPosition),
                    AngleUnit.Degrees);
            var localFrame = new ThreeDFrame
            {
                CartesianCoordinates = localPosition,
                Orientation = new ThreeDOrientation
                {
                    A = initialOrientation.A,
                    B = initialOrientation.B,
                    C = initialOrientation.C
                }
            };

            SpatialObjectState spatialObject = builder.AttachSpatialObject(
                runtime.Robot,
                list,
                new QualifiedName("SpatialObject", rslNamespaceIndex),
                runtime.SourceId,
                localFrame,
                metres,
                degrees);
            var positionFrame = (CartesianFrameAngleOrientationState)spatialObject.PositionFrame!;
            if (runtime.SourceId == "R1")
            {
                _ = builder.AddAttachPoint(
                    spatialObject,
                    new QualifiedName("ToolFlange", rslNamespaceIndex),
                    positionFrame.NodeId,
                    CreateZeroFrame(),
                    metres,
                    degrees);
            }
            await builder.RegisterAsync(spatialObject, context.CancellationToken).ConfigureAwait(false);

            GlobalLocationState globalLocation = builder.AttachGlobalLocation(
                runtime.Robot,
                new QualifiedName("GlobalLocation", gposNamespaceIndex),
                zone.NodeId,
                4326);
            globalLocation.Position!.AddElevationReference(SystemContext);
            globalLocation.Position.ElevationReference!.Value = 1;
            globalLocation.Orientation!.AddAngleUnit(SystemContext);
            globalLocation.Orientation!.AngleUnit!.Value = degrees;

            PositioningProviderSubscription subscription =
                await builder.BindGlobalLocationAsync(
                    globalLocation,
                    provider,
                    runtime.SourceId,
                    (sample, _) =>
                    {
                        GeoPosition samplePosition = sample.Position!.Value;
                        GeoOrientation sampleOrientation = sample.Orientation!.Value;
                        ThreeDCartesianCoordinates local =
                            provider.Scenario.Fit.GlobalToLocal(
                                ToGeographic(samplePosition),
                                AngleUnit.Degrees);
                        builder.SetFrameValue(
                            positionFrame,
                            new ThreeDFrame
                            {
                                CartesianCoordinates = local,
                                Orientation = new ThreeDOrientation
                                {
                                    A = sampleOrientation.A,
                                    B = sampleOrientation.B,
                                    C = sampleOrientation.C
                                }
                            },
                            sample.StatusCode,
                            sample.GetEffectiveSourceTimestamp());
                        RecordPublishedPose(
                            runtime.SourceId, local.X, local.Y, sampleOrientation.C);
                        return default;
                    },
                    context.CancellationToken).ConfigureAwait(false);
            m_positioningSubscriptions.Add(subscription);
            await builder.RegisterAsync(globalLocation, context.CancellationToken).ConfigureAwait(false);

            OpenUsdLiveBindingState[] bindings =
            [
                CreateBinding(runtime.Representation, usdNamespaceIndex, $"{runtime.SourceId}Position",
                    GuidFor($"{runtime.SourceId}:position"), positionFrame.Position!.NodeId,
                    runtime.PrimPath, "xformOp:translate", "double3", OpenUsdRenderTargetKindEnum.Translation, 1.0),
                CreateBinding(runtime.Representation, usdNamespaceIndex, $"{runtime.SourceId}Orientation",
                    GuidFor($"{runtime.SourceId}:orientation"), positionFrame.Orientation!.NodeId,
                    runtime.PrimPath, "xformOp:rotateXYZ", "double3", OpenUsdRenderTargetKindEnum.Rotation, 1.0),
                CreateBinding(runtime.Representation, usdNamespaceIndex, $"{runtime.SourceId}Longitude",
                    GuidFor($"{runtime.SourceId}:longitude"), globalLocation.Position.Longitude!.NodeId,
                    runtime.PrimPath, "inputs:longitude", "double", OpenUsdRenderTargetKindEnum.Custom, 1.0),
                CreateBinding(runtime.Representation, usdNamespaceIndex, $"{runtime.SourceId}Latitude",
                    GuidFor($"{runtime.SourceId}:latitude"), globalLocation.Position.Latitude!.NodeId,
                    runtime.PrimPath, "inputs:latitude", "double", OpenUsdRenderTargetKindEnum.Custom, 1.0),
                CreateBinding(runtime.Representation, usdNamespaceIndex, $"{runtime.SourceId}Elevation",
                    GuidFor($"{runtime.SourceId}:elevation"), globalLocation.Position.Elevation!.NodeId,
                    runtime.PrimPath, "inputs:elevation", "double", OpenUsdRenderTargetKindEnum.Custom, 1.0)
            ];

            foreach (OpenUsdLiveBindingState binding in bindings)
            {
                runtime.Representation.RemoveChild(binding);
                _ = await Manager.AddNodeAsync(
                    SystemContext,
                    runtime.Representation.NodeId,
                    binding,
                    context.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Records the platform pose that was just written to the twin.
        /// </summary>
        /// <remarks>
        /// Taken here rather than where the sample is produced, so it is the pose that
        /// actually reached the address space: a carried part is drawn against it, and a
        /// part drawn against anything newer floats out in front of the jaws holding it.
        /// </remarks>
        /// <param name="robotId">The robot the pose belongs to.</param>
        /// <param name="x">The X position written, in metres.</param>
        /// <param name="y">The Y position written, in metres.</param>
        /// <param name="headingDegrees">The heading written, in degrees.</param>
        private void RecordPublishedPose(
            string robotId,
            double x,
            double y,
            double headingDegrees)
        {
            foreach (RobotAgent agent in m_choreographer.Robots)
            {
                if (string.Equals(agent.Id, robotId, StringComparison.Ordinal))
                {
                    agent.PublishedPose = (x, y, headingDegrees);
                    return;
                }
            }
        }

        private static ThreeDFrame CreateZeroFrame()
        {
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates(),
                Orientation = new ThreeDOrientation()
            };
        }

        /// <summary>
        /// Rebuilds the OPC 10000-211 geographic coordinate a provider position
        /// describes, so the scenario's fit can project it back into the cell's
        /// local frame.
        /// </summary>
        private static S3DGeographicCoordinateDataType ToGeographic(GeoPosition position)
        {
            return new S3DGeographicCoordinateDataType
            {
                EncodingMask = position.Height.HasValue
                    ? (uint)S3DGeographicCoordinateDataTypeFields.Elevation
                    : 0,
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                Elevation = position.Height ?? 0.0
            };
        }

        partial void Configure(INodeManagerBuilder builder);

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (PositioningProviderSubscription subscription in m_positioningSubscriptions)
            {
                subscription.Dispose();
            }
            m_positioningSubscriptions.Clear();
        }

        private static Guid GuidFor(string key)
        {
            byte[] hash;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("robotics:" + key));
            }
#pragma warning restore CA1850
            byte[] g = new byte[16];
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

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 3,
            Level = LogLevel.Information,
            Message = "Configured RSL/GPOS positioning for {RobotCount} robots.")]
        public static partial void PositioningConfigured(this ILogger logger, int robotCount);

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 5,
            Level = LogLevel.Error,
            Message = "Failed to materialise RobotCell.")]
        public static partial void RobotCellFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 6,
            Level = LogLevel.Information,
            Message = "Dynamic composition mounted the gripper tool on R1.")]
        public static partial void GripperToolMounted(this ILogger logger);

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 7,
            Level = LogLevel.Warning,
            Message = "Dynamic tool composition failed.")]
        public static partial void DynamicToolFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 8,
            Level = LogLevel.Information,
            Message = "Dynamic composition: detached gripper tool (NodeId={NodeId}); model-change emitted.")]
        public static partial void DetachedGripperTool(this ILogger logger, NodeId nodeId);

        [LoggerMessage(EventId = MinimalRobotServerEventIds.RobotCell + 9,
            Level = LogLevel.Information,
            Message = "RobotCell configurator: address space ready ({NodeCount} runtime nodes).")]
        public static partial void RoboticsAddressSpaceReady(this ILogger logger, int nodeCount);
    }
}
