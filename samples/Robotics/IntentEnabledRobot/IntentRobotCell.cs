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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Robotics.IntentEnabledRobot.Kinematics;
using Robotics.IntentEnabledRobot.Simulation;
using ThreeDCartesianCoordinates = Opc.Ua.ThreeDCartesianCoordinates;
using ThreeDFrame = Opc.Ua.ThreeDFrame;
using ThreeDOrientation = Opc.Ua.ThreeDOrientation;

namespace Robotics.IntentEnabledRobot
{
    /// <summary>
    /// Builds the minimal Robot Intent sample cell.
    /// </summary>
    public sealed partial class IntentRobotCell : IDisposable
    {
        /// <summary>
        /// Creates the sample cell configurator.
        /// </summary>
        public IntentRobotCell(
            ILogger<IntentRobotCell> logger,
            SimulatedArmExecutor executor,
            SampleSafetySource safetySource)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_executor = executor ?? throw new ArgumentNullException(nameof(executor));
            m_safetySource = safetySource ?? throw new ArgumentNullException(nameof(safetySource));
            m_executor.SnapshotChanged += OnSnapshotChanged;
        }

        internal AsyncCustomNodeManager Manager => m_manager ??
            throw new InvalidOperationException(
                "IntentRobotCell has not been attached to a Robot Intent build context.");

        internal ServerSystemContext SystemContext => m_systemContext ??
            throw new InvalidOperationException(
                "IntentRobotCell has not been attached to a Robot Intent build context.");

        internal IIntentControllerBuilder Controller => m_controller ??
            throw new InvalidOperationException(
                "The intent controller has not been materialised.");

        internal IEnumerable<global::Opc.Ua.RobotIntent.AxisState> Axes => m_axes;

        internal IEnumerable<global::Opc.Ua.RobotIntent.LocationState> Locations => m_locations;

        internal IReadOnlyDictionary<string, NodeId> LocationNodes => m_locationNodes;

        /// <summary>
        /// Configures Robot Intent and OpenUSD nodes.
        /// </summary>
        public async ValueTask ConfigureAsync(IRobotIntentBuildContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            m_manager = context.Manager;
            m_systemContext = context.Manager.SystemContext;
            await MaterialiseOpenUsdFacilityAsync(cancellationToken).ConfigureAwait(false);
            m_controller = await context.AddIntentControllerAsync(
                "UR5eIntentController",
                ConfigureController,
                cancellationToken).ConfigureAwait(false);
            await MaterialiseRepresentationsAsync(cancellationToken).ConfigureAwait(false);
            PublishSnapshot(m_executor.CurrentSnapshot);
            ArrayOf<string> facets = m_controller.ComputeFacets();
            m_logger.IntentCellReady(m_axes.Count, m_locations.Count, facets);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_executor.SnapshotChanged -= OnSnapshotChanged;
        }

        private void ConfigureController(IIntentControllerBuilder controller)
        {
            controller
                .WithOperationalMode(OperationalModeEnum.AutomaticExternal)
                .WithReady(true)
                .WithMaxQueueDepth(16)
                .Accepts<JointMoveIntentDataType>(cancelSupported: true)
                .Accepts<LinearMoveIntentDataType>(cancelSupported: true, pauseSupported: true)
                .Accepts<CircularMoveIntentDataType>(cancelSupported: true)
                .Accepts<TrajectoryIntentDataType>(cancelSupported: true)
                .Accepts<CartesianPathIntentDataType>(cancelSupported: true)
                .Accepts<GraspIntentDataType>(cancelSupported: false)
                .Accepts<ReleaseIntentDataType>(cancelSupported: true)
                .Accepts<PickIntentDataType>(cancelSupported: false)
                .Accepts<PlaceIntentDataType>(cancelSupported: true)
                .Accepts<ToolChangeIntentDataType>(cancelSupported: false)
                .Accepts<WaitIntentDataType>(cancelSupported: true, pauseSupported: true, retrySupported: true)
                .WithSafetyState(m_safetySource);

            controller.State.Capabilities!.MissionsSupported!.Value = true;
            controller.State.Capabilities.MissionHorizonSupported!.Value = true;
            controller.State.Capabilities.MissionBranchingSupported!.Value = true;
            controller.State.Capabilities.BlendingSupported!.Value = false;
            controller.State.Capabilities.ForceControlSupported!.Value = false;
            controller.State.Capabilities.MaxTrajectoryPoints!.Value = 64u;

            IIntentFrameBuilder world = controller.AddFrame(
                "World",
                WorldFrameId,
                FrameRoleEnum.World,
                Pose(WorldFrameId, 0.0, 0.0, 0.0));
            IIntentFrameBuilder @base = controller.AddFrame(
                "Base",
                BaseFrameId,
                FrameRoleEnum.Base,
                Pose(WorldFrameId, 0.0, 0.0, 0.829),
                frame => frame.WithParent(world));
            IIntentFrameBuilder flange = controller.AddFrame(
                "MechanicalInterface",
                FlangeFrameId,
                FrameRoleEnum.MechanicalInterface,
                Pose(BaseFrameId, 0.0, 0.0, 0.1625),
                frame => frame.WithParent(@base));
            IIntentFrameBuilder tool = controller.AddFrame(
                "GripperTcp",
                ToolFrameId,
                FrameRoleEnum.Tool,
                Pose(FlangeFrameId, 0.0, 0.0, 0.115),
                frame => frame.WithParent(flange));
            controller.AddTool("ParallelGripper", tool, fitted: true);

            for (uint index = 0; index < s_axisUsd.Length; index++)
            {
                IIntentAxisBuilder axis = controller.AddAxis(s_axisUsd[index].Name, index, AxisKindEnum.Revolute);
                ConfigureAxis(axis.State, index == 2 ? -Math.PI : -FullTurn, index == 2 ? Math.PI : FullTurn);
                m_axes.Add(axis.State);
            }

            foreach ((string name, string _, double x, double y, double z, double rz) in s_locations)
            {
                uint capacity = string.Equals(name, "Bin", StringComparison.Ordinal) ||
                    string.Equals(name, "Fixture", StringComparison.Ordinal)
                        ? PayloadSlotCount
                        : 1u;
                IIntentLocationBuilder location = controller.AddLocation(
                    name,
                    Pose(WorldFrameId, x, y, z, rz),
                    builder => builder.WithOccupancy(string.Equals(name, "Bin", StringComparison.Ordinal), capacity));
                m_locations.Add(location.State);
                m_locationNodes[name] = location.State.NodeId;
            }

            IIntentOutputSignalBuilder gripperOpen = controller.AddOutput(
                "GripperOpen",
                Opc.Ua.DataTypeIds.Boolean,
                ToVariant(true));
            IIntentOutputSignalBuilder benchLight = controller.AddOutput(
                "BenchLight",
                Opc.Ua.DataTypeIds.Boolean,
                ToVariant(false));
            IIntentOutputSignalBuilder heldPartPosition = controller.AddOutput(
                "HeldPartPosition",
                Opc.Ua.DataTypeIds.Double,
                ToVariant(m_executor.CurrentSnapshot.HeldPartPosition));
            IIntentOutputSignalBuilder heldPartVisible = controller.AddOutput(
                "HeldPartVisible",
                Opc.Ua.DataTypeIds.Boolean,
                ToVariant(false));
            m_gripperOpenValue = gripperOpen.State.Value;
            m_benchLightValue = benchLight.State.Value;
            m_heldPartPositionValue = heldPartPosition.State.Value;
            m_heldPartVisibleValue = heldPartVisible.State.Value;

            for (int ii = 0; ii < PayloadSlotCount; ii++)
            {
                IIntentOutputSignalBuilder slotFilled = controller.AddOutput(
                    $"PayloadSlot{ii + 1:00}Filled",
                    Opc.Ua.DataTypeIds.Boolean,
                    ToVariant(false));
                m_payloadSlotFilledValues.Add(slotFilled.State.Value);
            }

            controller.AddRealTimeChannel(
                "JointTelemetry", "joint-telemetry", RealTimeTransportEnum.OpcUaFx, "udp://239.0.0.40:4840");
            controller.WithDescription(description => description
                .WithKinematicChain(CreateKinematicChain())
                .WithLimits(
                    SimulatedArmKinematics.Reach,
                    payloadLimit: 5.0,
                    maxCartesianSpeed: 0.25,
                    maxCartesianAcceleration: 0.7));
        }

        private void ConfigureAxis(global::Opc.Ua.RobotIntent.AxisState axis, double min, double max)
        {
            axis.CreateOrReplaceMinPosition(SystemContext, null!).Value = min;
            axis.CreateOrReplaceMaxPosition(SystemContext, null!).Value = max;
            axis.CreateOrReplaceMaxSpeed(SystemContext, null!).Value = 2.0;
            if (axis.Position != null)
            {
                axis.Position.Value = 0.0;
            }
        }

        private ArrayOf<KinematicJointDataType> CreateKinematicChain()
        {
            var joints = new KinematicJointDataType[s_axisUsd.Length];
            for (int ii = 0; ii < joints.Length; ii++)
            {
                joints[ii] = new KinematicJointDataType
                {
                    AxisId = s_axisUsd[ii].Name,
                    Kind = AxisKindEnum.Revolute,
                    OriginTransform = Pose(
                        ii == 0 ? BaseFrameId : s_axisUsd[ii - 1].Name, 0.0, 0.0, ii == 0 ? 0.1625 : 0.12),
                    AxisVector = s_axisUsd[ii].AxisVector
                };
            }
            return joints.ToArrayOf();
        }

        private void OnSnapshotChanged(object? sender, SimulatedArmSnapshot snapshot)
        {
            PublishSnapshot(snapshot);
        }

        private void PublishSnapshot(SimulatedArmSnapshot snapshot)
        {
            for (int ii = 0; ii < m_axes.Count && ii < snapshot.JointAngles.Count; ii++)
            {
                global::Opc.Ua.RobotIntent.AxisState axis = m_axes[ii];
                if (axis.Position != null)
                {
                    axis.Position.Value = snapshot.JointAngles[ii] * 180.0 / Math.PI;
                    axis.Position.ClearChangeMasks(SystemContext, true);
                }
            }
            if (m_gripperOpenValue != null)
            {
                m_gripperOpenValue.Value = snapshot.GripperOpening > 0.04;
                m_gripperOpenValue.ClearChangeMasks(SystemContext, true);
            }
            if (m_benchLightValue != null)
            {
                m_benchLightValue.Value = snapshot.HasObject;
                m_benchLightValue.ClearChangeMasks(SystemContext, true);
            }
            if (m_heldPartPositionValue != null)
            {
                m_heldPartPositionValue.Value = snapshot.HeldPartPosition;
                m_heldPartPositionValue.ClearChangeMasks(SystemContext, true);
            }
            if (m_heldPartVisibleValue != null)
            {
                m_heldPartVisibleValue.Value = snapshot.HasObject;
                m_heldPartVisibleValue.ClearChangeMasks(SystemContext, true);
            }
            int filledCount = 0;
            for (int ii = 0; ii < m_payloadSlotFilledValues.Count && ii < snapshot.StackSlotsFilled.Count; ii++)
            {
                bool filled = snapshot.StackSlotsFilled[ii];
                if (filled)
                {
                    filledCount++;
                }
                BaseVariableState? slot = m_payloadSlotFilledValues[ii];
                if (slot != null)
                {
                    slot.Value = filled;
                    slot.ClearChangeMasks(SystemContext, true);
                }
            }
            UpdateLocationOccupancy("Bin", filledCount < PayloadSlotCount || snapshot.HasObject);
            UpdateLocationOccupancy("Fixture", filledCount > 0);
        }

        private void UpdateLocationOccupancy(string name, bool occupied)
        {
            global::Opc.Ua.RobotIntent.LocationState? location = FindLocation(name);
            if (location?.Occupied != null)
            {
                location.Occupied.Value = occupied;
                location.Occupied.ClearChangeMasks(SystemContext, true);
            }
        }

        private static Pose3DDataType Pose(string frameId, double x, double y, double z, double rzDegrees = 0.0)
        {
            return PoseMath.FromThreeDFrame(
                new ThreeDFrame
                {
                    CartesianCoordinates = new ThreeDCartesianCoordinates
                    {
                        X = x,
                        Y = y,
                        Z = z
                    },
                    Orientation = new ThreeDOrientation
                    {
                        C = rzDegrees * Math.PI / 180.0
                    }
                },
                frameId);
        }

        private static ArrayOf<double> Vector(double x, double y, double z)
        {
            return new[] { x, y, z }.ToArrayOf();
        }

        private static Variant ToVariant(bool value)
        {
            var builder = new VariantBuilder();
            return ((IVariantBuilder<bool>)builder).WithValue(value);
        }

        private static Variant ToVariant(double value)
        {
            var builder = new VariantBuilder();
            return ((IVariantBuilder<double>)builder).WithValue(value);
        }

        private static Variant ToVariant(ArrayOf<double> value)
        {
            var builder = new VariantBuilder();
            return ((IVariantBuilder<ArrayOf<double>>)builder).WithValue(value);
        }

        private static Guid GuidFor(string key)
        {
            byte[] hash;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("minimal-intent:" + key));
            }
#pragma warning restore CA1850
            byte[] guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, guidBytes.Length);
            return new Guid(guidBytes);
        }

        private const string WorldFrameId = "world";
        private const string BaseFrameId = "ur5e-base";
        private const string FlangeFrameId = "ur5e-flange";
        private const string ToolFrameId = "parallel-gripper-tcp";
        private const double FullTurn = Math.PI * 2.0;
        private const int PayloadSlotCount = 8;

        private static readonly (string Name, string PrimPath, double X, double Y, double Z, double Rz)[] s_locations =
        [
            ("Bin", "/World/Targets/Bin", 0.41, -0.28, 0.829, 0.0),
            ("Fixture", "/World/Targets/Fixture", 0.48, 0.26, 0.829, 25.0),
            ("Inspect", "/World/Targets/Inspect", -0.25, 0.30, 0.829, -20.0),
            ("Handoff", "/World/Targets/Handoff", -0.46, -0.26, 0.829, 40.0)
        ];

        private static readonly (string Name, string PrimPath, string RotateOp, ArrayOf<double> AxisVector)[]
            s_axisUsd =
        [
            ("J1", "/Arm/Base/J1", "xformOp:rotateZ", Vector(0.0, 0.0, 1.0)),
            ("J2", "/Arm/Base/J1/J2", "xformOp:rotateY", Vector(0.0, 1.0, 0.0)),
            ("J3", "/Arm/Base/J1/J2/J3", "xformOp:rotateY", Vector(0.0, 1.0, 0.0)),
            ("J4", "/Arm/Base/J1/J2/J3/J4", "xformOp:rotateY", Vector(0.0, 1.0, 0.0)),
            ("J5", "/Arm/Base/J1/J2/J3/J4/J5", "xformOp:rotateZ", Vector(0.0, 0.0, 1.0)),
            ("J6", "/Arm/Base/J1/J2/J3/J4/J5/J6", "xformOp:rotateY", Vector(0.0, 1.0, 0.0))
        ];

        private readonly ILogger<IntentRobotCell> m_logger;
        private readonly SimulatedArmExecutor m_executor;
        private readonly SampleSafetySource m_safetySource;
        private readonly List<global::Opc.Ua.RobotIntent.AxisState> m_axes = [];
        private readonly List<global::Opc.Ua.RobotIntent.LocationState> m_locations = [];
        private readonly Dictionary<string, NodeId> m_locationNodes = new(StringComparer.Ordinal);
        private readonly List<BaseVariableState?> m_payloadSlotFilledValues = [];
        private AsyncCustomNodeManager? m_manager;
        private ServerSystemContext? m_systemContext;
        private IIntentControllerBuilder? m_controller;
        private BaseVariableState? m_gripperOpenValue;
        private BaseVariableState? m_benchLightValue;
        private BaseVariableState? m_heldPartPositionValue;
        private BaseVariableState? m_heldPartVisibleValue;
    }

    internal static partial class IntentRobotCellLog
    {
        [LoggerMessage(EventId = IntentEnabledRobotEventIds.IntentRobotCell + 1,
            Level = LogLevel.Information,
            Message = "Materialised IntentEnabledRobot ({AxisCount} axes, " +
                "{LocationCount} locations, facets {Facets}).")]
        public static partial void IntentCellReady(
            this ILogger logger, int axisCount, int locationCount, ArrayOf<string> facets);
    }

    /// <summary>
    /// Mutable read-only safety source driven by the console sample commands.
    /// </summary>
    public sealed class SampleSafetySource : IRobotIntentSafetySource
    {
        /// <inheritdoc/>
        public ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                return ValueTask.FromResult(m_snapshot);
            }
        }

        internal void Reset()
        {
            Set(new RobotIntentSafetySnapshot(
                SafeMotionFunctionEnum.None,
                EmergencyStopActive: false,
                ProtectiveStopActive: false,
                SafeSpeedLimitActive: false,
                SafeSpeedLimit: 0.0,
                SafetyControllerOk: true,
                LastStopReason: LocalizedText.Null));
        }

        internal void TripProtectiveStop()
        {
            Set(new RobotIntentSafetySnapshot(
                SafeMotionFunctionEnum.Ss1,
                EmergencyStopActive: false,
                ProtectiveStopActive: true,
                SafeSpeedLimitActive: false,
                SafeSpeedLimit: 0.0,
                SafetyControllerOk: true,
                LastStopReason: LocalizedText.From("Simulated protective stop")));
        }

        internal void LimitSpeed(double limit)
        {
            Set(new RobotIntentSafetySnapshot(
                SafeMotionFunctionEnum.Sls,
                EmergencyStopActive: false,
                ProtectiveStopActive: false,
                SafeSpeedLimitActive: true,
                SafeSpeedLimit: limit,
                SafetyControllerOk: true,
                LastStopReason: LocalizedText.From($"Simulated safe speed limit {limit:0.###} m/s")));
        }

        private void Set(RobotIntentSafetySnapshot snapshot)
        {
            lock (m_lock)
            {
                m_snapshot = snapshot;
            }
        }

        private readonly Lock m_lock = new();

        private RobotIntentSafetySnapshot m_snapshot = new(
            SafeMotionFunctionEnum.None,
            EmergencyStopActive: false,
            ProtectiveStopActive: false,
            SafeSpeedLimitActive: false,
            SafeSpeedLimit: 0.0,
            SafetyControllerOk: true,
            LastStopReason: LocalizedText.Null);
    }

    internal sealed class SafetyAwareArmExecutor : IIntentExecutor
    {
        public SafetyAwareArmExecutor(SimulatedArmExecutor inner, SampleSafetySource safety)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_safety = safety ?? throw new ArgumentNullException(nameof(safety));
        }

        public async ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            RobotIntentSafetySnapshot safety = await m_safety.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (safety.ProtectiveStopActive || safety.EmergencyStopActive || !safety.SafetyControllerOk)
            {
                return IntentOutcome.Fail(
                    IntentFailureEnum.NotPermittedInMode,
                    safety.LastStopReason.Text ?? "The simulated safety system is stopping motion.");
            }
            if (safety.SafeSpeedLimitActive &&
                safety.SafeSpeedLimit > 0.0 &&
                ExceedsSafeSpeed(execution.Intent, safety.SafeSpeedLimit))
            {
                return IntentOutcome.Fail(
                    IntentFailureEnum.SafetyLimitExceeded,
                    $"The requested speed exceeds the simulated safe limit of {safety.SafeSpeedLimit:0.###} m/s.");
            }
            if (execution.Intent is LinearMoveIntentDataType linear)
            {
                IntentOutcome approach = await MoveToApproachAsync(
                    execution,
                    linear,
                    cancellationToken).ConfigureAwait(false);
                if (approach.State != ExecutionStateEnum.Succeeded)
                {
                    return approach;
                }
            }
            return await m_inner.ExecuteAsync(execution, cancellationToken).ConfigureAwait(false);
        }

        public bool CanCancel(IntentExecution execution)
        {
            return m_inner.CanCancel(execution);
        }

        private async ValueTask<IntentOutcome> MoveToApproachAsync(
            IntentExecution execution,
            LinearMoveIntentDataType linear,
            CancellationToken cancellationToken)
        {
            Pose3DDataType target = linear.Target;
            ReadOnlySpan<double> position = target.Position.Span;
            if (position.Length != 3)
            {
                return IntentOutcome.Fail(
                    IntentFailureEnum.ParameterInvalid,
                    "A linear move target must carry a 3D position.");
            }
            var approach = new Pose3DDataType
            {
                FrameId = target.FrameId,
                Position = new[]
                {
                    position[0],
                    position[1],
                    position[2] - TargetApproachOffset
                }.ToArrayOf(),
                Orientation = target.Orientation.IsNull
                    ? ArrayOf<double>.Empty
                    : target.Orientation.Span.ToArray().ToArrayOf()
            };
            var jointMove = new JointMoveIntentDataType
            {
                IntentId = execution.IntentId + "-approach",
                Label = LocalizedText.From("Approach " + linear.Label.Text),
                Constraints = linear.Constraints
            };
            if (m_kinematics.TrySelectNearest(
                approach,
                m_inner.CurrentSnapshot.JointAngles.Span,
                out SimulatedArmIkSolution? solution,
                out SimulatedArmKinematicFailure failure))
            {
                jointMove.HasJointTargets = true;
                jointMove.JointTargets = solution.JointAngles;
            }
            else
            {
                return IntentOutcome.Fail(
                    SimulatedArmKinematics.ToIntentFailure(failure),
                    "The target approach pose cannot be reached.");
            }
            var approachExecution = new IntentExecution(
                execution.IntentId + "-approach",
                jointMove,
                execution.Progress)
            {
                MissionId = execution.MissionId
            };
            return await m_inner.ExecuteAsync(approachExecution, cancellationToken).ConfigureAwait(false);
        }

        private static bool ExceedsSafeSpeed(IntentDataType intent, double safeSpeedLimit)
        {
            return intent is MotionIntentDataType motion &&
                motion.Constraints.CartesianSpeed > 0.0 &&
                motion.Constraints.CartesianSpeed > safeSpeedLimit;
        }

        private const double TargetApproachOffset = 0.05;

        private readonly SimulatedArmExecutor m_inner;
        private readonly SimulatedArmKinematics m_kinematics = new();
        private readonly SampleSafetySource m_safety;
    }

    internal sealed class SafetyConsoleService : Microsoft.Extensions.Hosting.BackgroundService
    {
        public SafetyConsoleService(SampleSafetySource safety, ILogger<SafetyConsoleService> logger)
        {
            m_safety = safety ?? throw new ArgumentNullException(nameof(safety));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            m_logger.SafetyCommandsReady();
            while (!stoppingToken.IsCancellationRequested)
            {
                string? line = await Console.In.ReadLineAsync(stoppingToken).ConfigureAwait(false);
                if (line == null)
                {
                    return;
                }
                ApplyCommand(line.Trim());
            }
        }

        private void ApplyCommand(string command)
        {
            if (command.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                m_safety.TripProtectiveStop();
                m_logger.ProtectiveStopActive();
            }
            else if (command.StartsWith("limit", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = command.Split(
                    ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                double limit = parts.Length > 1 && double.TryParse(parts[1], out double parsed) ? parsed : 0.05;
                m_safety.LimitSpeed(limit);
                m_logger.SafeSpeedLimitActive(limit);
            }
            else if (command.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                m_safety.Reset();
                m_logger.SafetyReset();
            }
        }

        private readonly SampleSafetySource m_safety;
        private readonly ILogger<SafetyConsoleService> m_logger;
    }

    internal static partial class SafetyConsoleLog
    {
        [LoggerMessage(EventId = IntentEnabledRobotEventIds.SafetyConsole + 1,
            Level = LogLevel.Information,
            Message = "Safety commands: type 'stop', 'limit <m/s>' or 'reset' in the console.")]
        public static partial void SafetyCommandsReady(this ILogger logger);

        [LoggerMessage(EventId = IntentEnabledRobotEventIds.SafetyConsole + 2,
            Level = LogLevel.Warning,
            Message = "Simulated protective stop is active; submitted intents are refused with NotPermittedInMode.")]
        public static partial void ProtectiveStopActive(this ILogger logger);

        [LoggerMessage(EventId = IntentEnabledRobotEventIds.SafetyConsole + 3,
            Level = LogLevel.Warning,
            Message = "Simulated safe speed limit is {Limit} m/s; faster motions are refused " +
                "with SafetyLimitExceeded.")]
        public static partial void SafeSpeedLimitActive(this ILogger logger, double limit);

        [LoggerMessage(EventId = IntentEnabledRobotEventIds.SafetyConsole + 4,
            Level = LogLevel.Information,
            Message = "Simulated safety state reset to nominal.")]
        public static partial void SafetyReset(this ILogger logger);
    }
}
