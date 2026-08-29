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
using System.Globalization;
using System.Text.Json;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Converts strongly-typed MCP DTOs into OPC UA Robot Intent data types.
    /// Every scoped name reference is resolved through the per-call
    /// <see cref="RoboticsScopeResolver"/> before conversion; a null resolver
    /// means the caller has no controller scope and every reference must then
    /// be a full NodeId.
    /// </summary>
    internal static class RoboticsIntentDtoConverter
    {
        public static IntentDataType ConvertIntent(
            MissionIntentInput input,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(input);
            ValidateMissionIntentFields(input);

            return input.Kind switch
            {
                IntentKind.JointMove => ConvertJointMove(
                    CopyMissionMotion(input, new JointMoveIntentInput
                    {
                        JointTargets = input.JointTargets,
                        TargetPose = input.Target
                    }),
                    0,
                    scope),
                IntentKind.LinearMove => ConvertLinearMove(
                    CopyMissionMotion(input, new LinearMoveIntentInput
                    {
                        Target = input.Target
                    }),
                    scope),
                IntentKind.CircularMove => ConvertCircularMove(
                    CopyMissionMotion(input, new CircularMoveIntentInput
                    {
                        ViaPoint = input.ViaPoint,
                        Target = input.Target
                    }),
                    scope),
                IntentKind.Trajectory => ConvertTrajectory(
                    CopyMissionMotion(input, new TrajectoryIntentInput
                    {
                        Points = input.Points ?? []
                    }),
                    scope),
                IntentKind.CartesianPath => ConvertCartesianPath(
                    CopyMissionMotion(input, new CartesianPathIntentInput
                    {
                        Waypoints = input.Waypoints ?? []
                    }),
                    scope),
                IntentKind.Force => ConvertForce(
                    CopyMissionMotion(input, new ForceIntentInput
                    {
                        Direction = input.Direction ?? [],
                        ContactForce = input.ContactForce ?? 0,
                        FrameId = input.FrameId,
                        MaxDistance = input.MaxDistance ?? 0,
                        HoldForce = input.HoldForce ?? false
                    }),
                    scope),
                IntentKind.ArcWeld => ConvertArcWeld(
                    CopyMissionProcess(input, new ArcWeldIntentInput
                    {
                        Voltage = input.Voltage ?? 0,
                        WireFeedSpeed = input.WireFeedSpeed ?? 0,
                        TravelSpeed = input.TravelSpeed ?? 0,
                        SeamTrackingEnabled = input.SeamTrackingEnabled ?? false,
                        WeldProcedureRef = input.WeldProcedureRef
                    }),
                    scope),
                IntentKind.SpotWeld => ConvertSpotWeld(
                    CopyMissionProcess(input, new SpotWeldIntentInput
                    {
                        WeldSchedule = input.WeldSchedule ?? 0,
                        GunForce = input.GunForce ?? 0
                    }),
                    scope),
                IntentKind.Dispense => ConvertDispense(
                    CopyMissionProcess(input, new DispenseIntentInput
                    {
                        FlowRate = input.FlowRate ?? 0,
                        BeadWidth = input.BeadWidth ?? 0,
                        PurgeCycles = input.PurgeCycles ?? 0
                    }),
                    scope),
                IntentKind.Fasten => ConvertFasten(
                    CopyMissionProcess(input, new FastenIntentInput
                    {
                        Joint = input.Joint,
                        ProgramNumber = input.ProgramNumber ?? 0,
                        TargetTorque = input.TargetTorque ?? 0
                    }),
                    scope),
                IntentKind.Palletise => ConvertPalletise(
                    CopyMissionProcess(input, new PalletiseIntentInput
                    {
                        Pattern = input.Pattern,
                        Layer = input.Layer ?? 0,
                        Row = input.Row ?? 0,
                        Column = input.Column ?? 0
                    }),
                    scope),
                IntentKind.SurfaceFinish => ConvertSurfaceFinish(
                    CopyMissionProcess(input, new SurfaceFinishIntentInput
                    {
                        ContactForce = input.ContactForce ?? 0,
                        FeedRate = input.FeedRate ?? 0,
                        ToolSpeed = input.ToolSpeed ?? 0,
                        StepOver = input.StepOver ?? 0
                    }),
                    scope),
                IntentKind.Grasp => ConvertGrasp(
                    CopyMissionCommon(input, new GraspIntentInput
                    {
                        Tool = input.Tool ?? string.Empty,
                        Force = input.Force ?? 0
                    }),
                    scope),
                IntentKind.Release => ConvertRelease(
                    CopyMissionCommon(input, new ReleaseIntentInput
                    {
                        Tool = input.Tool ?? string.Empty
                    }),
                    scope),
                IntentKind.Pick => ConvertPick(
                    CopyMissionCommon(input, new PickIntentInput
                    {
                        Source = input.Source ?? string.Empty,
                        Tool = input.Tool ?? string.Empty,
                        ObjectClass = input.ObjectClass
                    }),
                    scope),
                IntentKind.Place => ConvertPlace(
                    CopyMissionCommon(input, new PlaceIntentInput
                    {
                        Destination = input.Destination ?? string.Empty,
                        Tool = input.Tool ?? string.Empty
                    }),
                    scope),
                IntentKind.ToolChange => ConvertToolChange(
                    CopyMissionCommon(input, new ToolChangeIntentInput
                    {
                        Tool = input.Tool,
                        DockStation = input.DockStation
                    }),
                    scope),
                IntentKind.SetOutput => ConvertSetOutput(
                    CopyMissionCommon(input, new SetOutputIntentInput
                    {
                        Output = input.Output ?? string.Empty,
                        Value = input.Value
                    }),
                    scope),
                IntentKind.CallProgram => ConvertCallProgram(
                    CopyMissionCommon(input, new CallProgramIntentInput
                    {
                        Program = input.Program ?? string.Empty,
                        Arguments = input.Arguments
                    }),
                    scope),
                IntentKind.Wait => ConvertWait(
                    CopyMissionCommon(input, new WaitIntentInput
                    {
                        Duration = input.Duration ?? 0,
                        Signal = input.Signal
                    }),
                    scope),
                _ => throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Unknown intent kind '{input.Kind}'."),
                    nameof(input))
            };
        }

        public static IntentDataType ConvertJointMove(
            JointMoveIntentInput dto,
            uint axisCount,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            bool hasJointTargets = dto.JointTargets is { Length: > 0 };
            if (hasJointTargets && dto.TargetPose != null)
            {
                throw new ArgumentException(
                    "JointMove accepts either jointTargets or targetPose, not both.",
                    nameof(dto));
            }

            JointMoveIntentBuilder builder = RobotIntentBuilder.JointMove(axisCount);
            IntentDataType intent;
            if (hasJointTargets)
            {
                ValidateFiniteValues(dto.JointTargets!, "jointTargets");
                intent = builder.ToJoints(dto.JointTargets!).Build();
            }
            else if (dto.TargetPose != null)
            {
                intent = builder.ToPose(ConvertPose(dto.TargetPose, "targetPose", scope)).Build();
            }
            else
            {
                throw new ArgumentException(
                    "JointMove requires either jointTargets or targetPose.",
                    nameof(dto));
            }

            ApplyMotionCommon(dto, intent, scope);
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertLinearMove(
            LinearMoveIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            ValidateSpeedFraction(dto.SpeedFraction, "speedFraction");
            IntentDataType intent = RobotIntentBuilder.LinearMove(
                ConvertPose(dto.Target, "target", scope),
                dto.SpeedFraction).Build();
            ApplyMotionCommon(dto, intent, scope);
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertCircularMove(
            CircularMoveIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            IntentDataType intent = RobotIntentBuilder.CircularMove(
                ConvertPose(dto.ViaPoint, "viaPoint", scope),
                ConvertPose(dto.Target, "target", scope)).Build();
            ApplyMotionCommon(dto, intent, scope);
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertTrajectory(
            TrajectoryIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (dto.Points is null || dto.Points.Length == 0)
            {
                throw new ArgumentException("Trajectory requires at least one point.", nameof(dto));
            }

            var points = new List<TrajectoryPointDataType>(dto.Points.Length);
            int jointCount = -1;
            double previousTime = double.NegativeInfinity;
            for (int i = 0; i < dto.Points.Length; i++)
            {
                TrajectoryPointDto point = dto.Points[i];
                if (point is null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Trajectory point [{i}] is required."),
                        nameof(dto));
                }

                string prefix = string.Create(CultureInfo.InvariantCulture, $"points[{i}]");
                if (point.Positions is null || point.Positions.Length == 0)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{prefix}.positions' must have at least one value."),
                        nameof(dto));
                }

                if (jointCount < 0)
                {
                    jointCount = point.Positions.Length;
                }
                else if (point.Positions.Length != jointCount)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{prefix}.positions' has {point.Positions.Length} values but the " +
                            $"trajectory uses {jointCount}."),
                        nameof(dto));
                }

                ValidateFiniteValues(point.Positions, prefix + ".positions");
                ValidateOptionalTrajectoryComponent(point.Velocities, jointCount, prefix + ".velocities");
                ValidateOptionalTrajectoryComponent(point.Accelerations, jointCount, prefix + ".accelerations");

                ValidateFinite(point.TimeFromStart, prefix + ".timeFromStart");
                if (point.TimeFromStart < 0)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{prefix}.timeFromStart' must not be negative but was {point.TimeFromStart}."),
                        nameof(dto));
                }

                if (i > 0 && point.TimeFromStart <= previousTime)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{prefix}.timeFromStart' must be strictly greater than the previous " +
                            $"point ({previousTime})."),
                        nameof(dto));
                }

                previousTime = point.TimeFromStart;
                points.Add(new TrajectoryPointDataType
                {
                    TimeFromStart = point.TimeFromStart,
                    Positions = point.Positions,
                    Velocities = point.Velocities ?? [],
                    Accelerations = point.Accelerations ?? []
                });
            }

            IntentDataType intent = RobotIntentBuilder.Trajectory().WithPoints([.. points]).Build();
            ApplyMotionCommon(dto, intent, scope);
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertCartesianPath(
            CartesianPathIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            if (dto.Waypoints is null || dto.Waypoints.Length == 0)
            {
                throw new ArgumentException("CartesianPath requires at least one waypoint.", nameof(dto));
            }

            var waypoints = new List<PathWaypointDataType>(dto.Waypoints.Length);
            for (int i = 0; i < dto.Waypoints.Length; i++)
            {
                CartesianWaypointDto waypoint = dto.Waypoints[i];
                if (waypoint is null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Waypoint [{i}] is required."),
                        nameof(dto));
                }

                string prefix = string.Create(CultureInfo.InvariantCulture, $"waypoints[{i}]");
                waypoints.Add(new PathWaypointDataType
                {
                    Pose = ConvertPose(waypoint.Pose, prefix + ".pose", scope),
                    Blend = waypoint.Blend != null
                        ? ConvertBlend(waypoint.Blend, prefix + ".blend")
                        : new BlendDataType()
                });
            }

            IntentDataType intent = RobotIntentBuilder.CartesianPath().WithWaypoints([.. waypoints]).Build();
            ApplyMotionCommon(dto, intent, scope);
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertForce(
            ForceIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateDirection(dto.Direction, "direction");
            ValidateFinite(dto.ContactForce, "contactForce");
            if (dto.ContactForce <= 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'contactForce' must be greater than zero but was {dto.ContactForce}."),
                    nameof(dto));
            }

            ValidateNonNegative(dto.MaxDistance, "maxDistance");

            ForceIntentDataType intent = RobotIntentBuilder.Force(dto.Direction, dto.ContactForce).Build();
            intent.FrameId = ResolveFrameId(dto.FrameId, scope);
            intent.MaxDistance = dto.MaxDistance;
            intent.HoldForce = dto.HoldForce;
            ApplyMotionCommon(dto, intent, scope);
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertArcWeld(
            ArcWeldIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.Voltage, "voltage");
            ValidateNonNegative(dto.WireFeedSpeed, "wireFeedSpeed");
            ValidateNonNegative(dto.TravelSpeed, "travelSpeed");

            ProcessIntentBuilder<ArcWeldIntentDataType> builder = RobotIntentBuilder.ArcWeld();
            ApplyProcessBuilder(builder, dto, scope);
            ArcWeldIntentDataType intent = builder.Build();
            intent.Voltage = dto.Voltage;
            intent.WireFeedSpeed = dto.WireFeedSpeed;
            intent.TravelSpeed = dto.TravelSpeed;
            intent.SeamTrackingEnabled = dto.SeamTrackingEnabled;
            intent.WeldProcedureRef = dto.WeldProcedureRef ?? string.Empty;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertSpotWeld(
            SpotWeldIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.GunForce, "gunForce");

            ProcessIntentBuilder<SpotWeldIntentDataType> builder = RobotIntentBuilder.SpotWeld();
            ApplyProcessBuilder(builder, dto, scope);
            SpotWeldIntentDataType intent = builder.Build();
            intent.WeldSchedule = dto.WeldSchedule;
            intent.GunForce = dto.GunForce;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertDispense(
            DispenseIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.FlowRate, "flowRate");
            ValidateNonNegative(dto.BeadWidth, "beadWidth");

            ProcessIntentBuilder<DispenseIntentDataType> builder = RobotIntentBuilder.Dispense();
            ApplyProcessBuilder(builder, dto, scope);
            DispenseIntentDataType intent = builder.Build();
            intent.FlowRate = dto.FlowRate;
            intent.BeadWidth = dto.BeadWidth;
            intent.PurgeCycles = dto.PurgeCycles;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertFasten(
            FastenIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.TargetTorque, "targetTorque");

            ProcessIntentBuilder<FastenIntentDataType> builder = RobotIntentBuilder.Fasten();
            ApplyProcessBuilder(builder, dto, scope);
            FastenIntentDataType intent = builder.Build();
            intent.Joint = ResolveNodeId(dto.Joint);
            intent.ProgramNumber = dto.ProgramNumber;
            intent.TargetTorque = dto.TargetTorque;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertPalletise(
            PalletiseIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            ProcessIntentBuilder<PalletiseIntentDataType> builder = RobotIntentBuilder.Palletise();
            ApplyProcessBuilder(builder, dto, scope);
            PalletiseIntentDataType intent = builder.Build();
            intent.Pattern = scope != null
                ? scope.ResolveLocation(dto.Pattern)
                : ResolveNodeId(dto.Pattern);
            intent.Layer = dto.Layer;
            intent.Row = dto.Row;
            intent.Column = dto.Column;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertSurfaceFinish(
            SurfaceFinishIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.ContactForce, "contactForce");
            ValidateNonNegative(dto.FeedRate, "feedRate");
            ValidateNonNegative(dto.ToolSpeed, "toolSpeed");
            ValidateNonNegative(dto.StepOver, "stepOver");

            ProcessIntentBuilder<SurfaceFinishIntentDataType> builder = RobotIntentBuilder.SurfaceFinish();
            ApplyProcessBuilder(builder, dto, scope);
            SurfaceFinishIntentDataType intent = builder.Build();
            intent.ContactForce = dto.ContactForce;
            intent.FeedRate = dto.FeedRate;
            intent.ToolSpeed = dto.ToolSpeed;
            intent.StepOver = dto.StepOver;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertGrasp(
            GraspIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.Force, "force");

            NodeId tool = scope != null
                ? scope.ResolveRequiredTool(dto.Tool, "tool")
                : ResolveRequiredNodeId(dto.Tool, "tool");
            IntentDataType intent = RobotIntentBuilder.Grasp(tool, dto.Force).Build();
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertRelease(
            ReleaseIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            NodeId tool = scope != null
                ? scope.ResolveRequiredTool(dto.Tool, "tool")
                : ResolveRequiredNodeId(dto.Tool, "tool");
            IntentDataType intent = RobotIntentBuilder.Release(tool).Build();
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertPick(
            PickIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            NodeId source = scope != null
                ? scope.ResolveRequiredLocation(dto.Source, "source")
                : ResolveRequiredNodeId(dto.Source, "source");
            NodeId tool = scope != null
                ? scope.ResolveRequiredTool(dto.Tool, "tool")
                : ResolveRequiredNodeId(dto.Tool, "tool");

            IntentDataType intent = RobotIntentBuilder.Pick(
                source, tool, dto.ObjectClass ?? string.Empty).Build();
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertPlace(
            PlaceIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            NodeId destination = scope != null
                ? scope.ResolveRequiredLocation(dto.Destination, "destination")
                : ResolveRequiredNodeId(dto.Destination, "destination");
            NodeId tool = scope != null
                ? scope.ResolveRequiredTool(dto.Tool, "tool")
                : ResolveRequiredNodeId(dto.Tool, "tool");

            IntentDataType intent = RobotIntentBuilder.Place(destination, tool).Build();
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertToolChange(
            ToolChangeIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            NodeId tool = scope != null ? scope.ResolveTool(dto.Tool) : ResolveNodeId(dto.Tool);
            NodeId dock = scope != null
                ? scope.ResolveLocation(dto.DockStation)
                : ResolveNodeId(dto.DockStation);

            IntentDataType intent = RobotIntentBuilder.ToolChange(tool, dock).Build();
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertSetOutput(
            SetOutputIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            NodeId output = scope != null
                ? scope.ResolveRequiredOutput(dto.Output, "output")
                : ResolveRequiredNodeId(dto.Output, "output");
            Variant value = ConvertTypedValue(dto.Value, "value");

            IntentDataType intent = RobotIntentBuilder.SetOutput(output, value).Build();
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertCallProgram(
            CallProgramIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);

            NodeId program = scope != null
                ? scope.ResolveRequiredProgram(dto.Program, "program")
                : ResolveRequiredNodeId(dto.Program, "program");

            CallProgramIntentDataType intent = RobotIntentBuilder.CallProgram(program).Build();
            intent.Arguments = ConvertNamedTypedValues(dto.Arguments, "arguments");
            ApplyCommon(dto, intent);
            return intent;
        }

        public static IntentDataType ConvertWait(
            WaitIntentInput dto,
            RoboticsScopeResolver? scope)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ValidateNonNegative(dto.Duration, "duration");

            NodeId signal = scope != null
                ? scope.ResolveOutput(dto.Signal)
                : ResolveNodeId(dto.Signal);
            if (signal.IsNull && dto.Duration <= 0)
            {
                throw new ArgumentException(
                    "Wait requires a positive duration or a signal.",
                    nameof(dto));
            }

            WaitIntentDataType intent = RobotIntentBuilder.Wait(dto.Duration).Build();
            intent.Signal = signal;
            ApplyCommon(dto, intent);
            return intent;
        }

        public static ArrayOf<MissionStepDataType> ConvertMissionSteps(
            MissionStepInput[]? steps,
            RoboticsScopeResolver? scope)
        {
            if (steps is null || steps.Length == 0)
            {
                return [];
            }

            var result = new List<MissionStepDataType>(steps.Length);
            var seenStepIds = new HashSet<string>(StringComparer.Ordinal);
            uint sequence = 1;
            for (int i = 0; i < steps.Length; i++)
            {
                MissionStepInput step = steps[i];
                if (step is null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Step [{i}] is required."),
                        nameof(steps));
                }

                if (string.IsNullOrWhiteSpace(step.StepId))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Step [{i}] is missing stepId."),
                        nameof(steps));
                }

                if (!seenStepIds.Add(step.StepId))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"Step [{i}] repeats stepId '{step.StepId}'."),
                        nameof(steps));
                }

                if (step.Intent is null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"Step '{step.StepId}' is missing its intent."),
                        nameof(steps));
                }

                IntentDataType intent = ConvertIntent(step.Intent, scope);
                result.Add(new MissionStepDataType
                {
                    StepId = step.StepId,
                    SequenceId = step.SequenceId ?? sequence,
                    Released = step.Released,
                    Intent = intent,
                    ErrorPolicy = step.ErrorPolicy ?? ErrorPolicyEnum.Abort,
                    FallbackStepId = step.FallbackStepId ?? string.Empty
                });
                sequence++;
            }

            return [.. result];
        }

        public static ArrayOf<MissionTransitionDataType> ConvertMissionTransitions(
            MissionTransitionInput[]? transitions)
        {
            if (transitions is null || transitions.Length == 0)
            {
                return [];
            }

            var result = new List<MissionTransitionDataType>(transitions.Length);
            for (int i = 0; i < transitions.Length; i++)
            {
                MissionTransitionInput transition = transitions[i];
                if (transition is null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Transition [{i}] is required."),
                        nameof(transitions));
                }

                if (string.IsNullOrWhiteSpace(transition.FromStepId))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Transition [{i}] is missing fromStepId."),
                        nameof(transitions));
                }

                if (string.IsNullOrWhiteSpace(transition.ToStepId))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"Transition [{i}] is missing toStepId."),
                        nameof(transitions));
                }

                result.Add(new MissionTransitionDataType
                {
                    FromStepId = transition.FromStepId,
                    ToStepId = transition.ToStepId,
                    DivergenceKind = transition.DivergenceKind ?? DivergenceKindEnum.Alternative,
                    Condition = MissionCondition.Always()
                });
            }

            return [.. result];
        }

        internal static NodeId ResolveNodeId(string? nameOrNodeId)
        {
            if (string.IsNullOrWhiteSpace(nameOrNodeId))
            {
                return NodeId.Null;
            }

            if (!NodeId.TryParse(nameOrNodeId, out NodeId nodeId))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{nameOrNodeId}' is not a valid NodeId."),
                    nameof(nameOrNodeId));
            }

            return nodeId;
        }

        private static T CopyMissionCommon<T>(MissionIntentInput source, T target)
            where T : IntentCommonDto
        {
            target.IntentId = source.IntentId;
            target.Label = source.Label;
            target.BufferMode = source.BufferMode;
            target.BlockingMode = source.BlockingMode;
            return target;
        }

        private static T CopyMissionMotion<T>(MissionIntentInput source, T target)
            where T : MotionIntentDto
        {
            CopyMissionCommon(source, target);
            target.ToolFrame = source.ToolFrame;
            target.Constraints = source.Constraints;
            target.Blend = source.Blend;
            target.SpeedFraction = source.SpeedFraction ?? 0;
            target.CartesianSpeed = source.CartesianSpeed ?? 0;
            return target;
        }

        private static T CopyMissionProcess<T>(MissionIntentInput source, T target)
            where T : ProcessIntentDto
        {
            CopyMissionCommon(source, target);
            target.ProcessProgram = source.ProcessProgram;
            target.Attributes = source.Attributes;
            return target;
        }

        private static void ValidateMissionIntentFields(MissionIntentInput input)
        {
            IntentKind kind = input.Kind;
            bool motion = kind is IntentKind.JointMove or IntentKind.LinearMove or
                IntentKind.CircularMove or IntentKind.Trajectory or IntentKind.CartesianPath or
                IntentKind.Force;
            bool process = kind is IntentKind.ArcWeld or IntentKind.SpotWeld or IntentKind.Dispense or
                IntentKind.Fasten or IntentKind.Palletise or IntentKind.SurfaceFinish;

            RejectUnlessAllowed(
                input.ToolFrame != null || input.Constraints != null || input.Blend != null ||
                    input.SpeedFraction.HasValue || input.CartesianSpeed.HasValue,
                motion,
                "motion fields",
                kind);
            RejectUnlessAllowed(
                input.ProcessProgram != null || input.Attributes != null,
                process,
                "process fields",
                kind);
            RejectUnlessAllowed(
                input.JointTargets != null,
                kind == IntentKind.JointMove,
                nameof(input.JointTargets),
                kind);
            RejectUnlessAllowed(
                input.Target != null,
                kind is IntentKind.JointMove or IntentKind.LinearMove or IntentKind.CircularMove,
                nameof(input.Target),
                kind);
            RejectUnlessAllowed(
                input.ViaPoint != null,
                kind == IntentKind.CircularMove,
                nameof(input.ViaPoint),
                kind);
            RejectUnlessAllowed(
                input.Points != null,
                kind == IntentKind.Trajectory,
                nameof(input.Points),
                kind);
            RejectUnlessAllowed(
                input.Waypoints != null,
                kind == IntentKind.CartesianPath,
                nameof(input.Waypoints),
                kind);
            RejectUnlessAllowed(
                input.Direction != null || input.FrameId != null || input.MaxDistance.HasValue ||
                    input.HoldForce.HasValue,
                kind == IntentKind.Force,
                "Force fields",
                kind);
            RejectUnlessAllowed(
                input.ContactForce.HasValue,
                kind is IntentKind.Force or IntentKind.SurfaceFinish,
                nameof(input.ContactForce),
                kind);
            RejectUnlessAllowed(
                input.Voltage.HasValue || input.WireFeedSpeed.HasValue || input.TravelSpeed.HasValue ||
                    input.SeamTrackingEnabled.HasValue || input.WeldProcedureRef != null,
                kind == IntentKind.ArcWeld,
                "ArcWeld fields",
                kind);
            RejectUnlessAllowed(
                input.WeldSchedule.HasValue || input.GunForce.HasValue,
                kind == IntentKind.SpotWeld,
                "SpotWeld fields",
                kind);
            RejectUnlessAllowed(
                input.FlowRate.HasValue || input.BeadWidth.HasValue || input.PurgeCycles.HasValue,
                kind == IntentKind.Dispense,
                "Dispense fields",
                kind);
            RejectUnlessAllowed(
                input.Joint != null || input.ProgramNumber.HasValue || input.TargetTorque.HasValue,
                kind == IntentKind.Fasten,
                "Fasten fields",
                kind);
            RejectUnlessAllowed(
                input.Pattern != null || input.Layer.HasValue || input.Row.HasValue || input.Column.HasValue,
                kind == IntentKind.Palletise,
                "Palletise fields",
                kind);
            RejectUnlessAllowed(
                input.FeedRate.HasValue || input.ToolSpeed.HasValue || input.StepOver.HasValue,
                kind == IntentKind.SurfaceFinish,
                "SurfaceFinish fields",
                kind);
            RejectUnlessAllowed(
                input.Tool != null,
                kind is IntentKind.Grasp or IntentKind.Release or IntentKind.Pick or IntentKind.Place or
                    IntentKind.ToolChange,
                nameof(input.Tool),
                kind);
            RejectUnlessAllowed(
                input.Force.HasValue,
                kind == IntentKind.Grasp,
                nameof(input.Force),
                kind);
            RejectUnlessAllowed(
                input.Source != null || input.ObjectClass != null,
                kind == IntentKind.Pick,
                "Pick fields",
                kind);
            RejectUnlessAllowed(
                input.Destination != null,
                kind == IntentKind.Place,
                nameof(input.Destination),
                kind);
            RejectUnlessAllowed(
                input.DockStation != null,
                kind == IntentKind.ToolChange,
                nameof(input.DockStation),
                kind);
            RejectUnlessAllowed(
                input.Output != null || input.Value != null,
                kind == IntentKind.SetOutput,
                "SetOutput fields",
                kind);
            RejectUnlessAllowed(
                input.Program != null || input.Arguments != null,
                kind == IntentKind.CallProgram,
                "CallProgram fields",
                kind);
            RejectUnlessAllowed(
                input.Duration.HasValue || input.Signal != null,
                kind == IntentKind.Wait,
                "Wait fields",
                kind);
        }

        private static void RejectUnlessAllowed(
            bool isSet,
            bool isAllowed,
            string field,
            IntentKind kind)
        {
            if (isSet && !isAllowed)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Intent kind '{kind}' does not accept {field}."),
                    nameof(kind));
            }
        }

        private static Pose3DDataType ConvertPose(
            PoseDto? dto,
            string name,
            RoboticsScopeResolver? scope)
        {
            if (dto is null)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{name}' is required."), name);
            }

            if (dto.Position is null)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{name}.position' is required."), name);
            }

            if (dto.Orientation is null)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{name}.orientation' is required."), name);
            }

            ValidateFinite(dto.Position.X, name + ".position.x");
            ValidateFinite(dto.Position.Y, name + ".position.y");
            ValidateFinite(dto.Position.Z, name + ".position.z");
            ValidateFinite(dto.Orientation.X, name + ".orientation.x");
            ValidateFinite(dto.Orientation.Y, name + ".orientation.y");
            ValidateFinite(dto.Orientation.Z, name + ".orientation.z");
            ValidateFinite(dto.Orientation.W, name + ".orientation.w");

            double norm = Math.Sqrt(
                (dto.Orientation.X * dto.Orientation.X) +
                (dto.Orientation.Y * dto.Orientation.Y) +
                (dto.Orientation.Z * dto.Orientation.Z) +
                (dto.Orientation.W * dto.Orientation.W));
            if (Math.Abs(norm - 1.0) > 1e-3)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}.orientation' must be a unit quaternion but its norm was {norm}."),
                    name);
            }

            return RobotIntentBuilder.Pose(
                dto.Position.X,
                dto.Position.Y,
                dto.Position.Z,
                dto.Orientation.X,
                dto.Orientation.Y,
                dto.Orientation.Z,
                dto.Orientation.W,
                ResolveFrameId(dto.FrameId, scope));
        }

        private static string ResolveFrameId(string? frameId, RoboticsScopeResolver? scope)
        {
            if (scope != null)
            {
                return scope.ResolveFrameId(frameId);
            }

            return frameId?.Trim() ?? string.Empty;
        }

        private static BlendDataType ConvertBlend(BlendDto dto, string name)
        {
            ValidateNonNegative(dto.Radius, name + ".radius");
            if (dto.Termination == TerminationModeEnum.Blend && dto.Radius <= 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}.radius' must be greater than zero when termination is Blend."),
                    name);
            }

            return new BlendDataType
            {
                Termination = dto.Termination,
                Radius = dto.Radius
            };
        }

        private static MotionConstraintsDataType ConvertConstraints(MotionConstraintsDto dto, string name)
        {
            ValidateSpeedFraction(dto.SpeedFraction, name + ".speedFraction");
            ValidateNonNegative(dto.CartesianSpeed, name + ".cartesianSpeed");
            ValidateNonNegative(dto.CartesianAcceleration, name + ".cartesianAcceleration");
            ValidateNonNegative(dto.Jerk, name + ".jerk");

            return new MotionConstraintsDataType
            {
                SpeedFraction = dto.SpeedFraction,
                CartesianSpeed = dto.CartesianSpeed,
                CartesianAcceleration = dto.CartesianAcceleration,
                Jerk = dto.Jerk
            };
        }

        private static void ApplyCommon(IntentCommonDto dto, IntentDataType intent)
        {
            if (!string.IsNullOrEmpty(dto.IntentId))
            {
                intent.IntentId = dto.IntentId;
            }
            if (!string.IsNullOrEmpty(dto.Label))
            {
                intent.Label = new LocalizedText(dto.Label);
            }
            if (dto.BufferMode.HasValue)
            {
                intent.BufferMode = dto.BufferMode.Value;
            }
            if (dto.BlockingMode.HasValue)
            {
                intent.BlockingMode = dto.BlockingMode.Value;
            }
        }

        private static void ApplyMotionCommon(
            MotionIntentDto dto,
            IntentDataType intent,
            RoboticsScopeResolver? scope)
        {
            if (intent is not MotionIntentDataType motion)
            {
                return;
            }

            motion.ToolFrame = scope != null
                ? scope.ResolveFrame(dto.ToolFrame)
                : ResolveNodeId(dto.ToolFrame);

            if (dto.Constraints != null)
            {
                motion.Constraints = ConvertConstraints(dto.Constraints, "constraints");
            }
            else
            {
                ValidateSpeedFraction(dto.SpeedFraction, "speedFraction");
                ValidateNonNegative(dto.CartesianSpeed, "cartesianSpeed");
                motion.Constraints = new MotionConstraintsDataType
                {
                    SpeedFraction = dto.SpeedFraction,
                    CartesianSpeed = dto.CartesianSpeed
                };
            }

            if (dto.Blend != null)
            {
                motion.Blend = ConvertBlend(dto.Blend, "blend");
            }
        }

        private static void ApplyProcessBuilder<T>(
            ProcessIntentBuilder<T> builder,
            ProcessIntentDto dto,
            RoboticsScopeResolver? scope)
            where T : ProcessIntentDataType
        {
            NodeId processProgram = scope != null
                ? scope.ResolveProgram(dto.ProcessProgram)
                : ResolveNodeId(dto.ProcessProgram);
            if (!processProgram.IsNull)
            {
                builder.WithProcessProgram(processProgram);
            }
            builder.WithAttributes(ConvertNamedTypedValues(dto.Attributes, "attributes"));
        }

        private static Variant ConvertTypedValue(TypedValueDto? dto, string name)
        {
            if (dto is null)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{name}' is required."), name);
            }

            if (string.IsNullOrWhiteSpace(dto.DataType))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}.dataType' is required so the value is written with an explicit type."),
                    name);
            }

            if (dto.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{name}.value' is required."), name);
            }

            return OpcUaJsonHelper.JsonElementToVariant(dto.Value, dto.DataType);
        }

        private static ArrayOf<KeyValuePair> ConvertNamedTypedValues(
            NamedTypedValueDto[]? values,
            string name)
        {
            if (values is null || values.Length == 0)
            {
                return [];
            }

            var result = new List<KeyValuePair>(values.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Length; i++)
            {
                NamedTypedValueDto value = values[i];
                string prefix = string.Create(CultureInfo.InvariantCulture, $"{name}[{i}]");
                if (value is null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"'{prefix}' is required."), name);
                }

                if (string.IsNullOrWhiteSpace(value.Name))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"'{prefix}.name' is required."), name);
                }

                if (!seen.Add(value.Name))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{prefix}.name' repeats '{value.Name}'."),
                        name);
                }

                if (string.IsNullOrWhiteSpace(value.DataType))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{prefix}.dataType' is required so the value is sent with an explicit type."),
                        name);
                }

                if (value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"'{prefix}.value' is required."), name);
                }

                result.Add(new KeyValuePair
                {
                    Key = new QualifiedName(value.Name),
                    Value = OpcUaJsonHelper.JsonElementToVariant(value.Value, value.DataType)
                });
            }

            return [.. result];
        }

        private static NodeId ResolveRequiredNodeId(string? nameOrNodeId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(nameOrNodeId))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{parameterName}' is required."),
                    parameterName);
            }

            if (!NodeId.TryParse(nameOrNodeId, out NodeId nodeId) || nodeId.IsNull)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{parameterName}' value '{nameOrNodeId}' is not a valid NodeId."),
                    parameterName);
            }

            return nodeId;
        }

        private static void ValidateDirection(double[] vector, string name)
        {
            if (vector is null || vector.Length != 3)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}' must be exactly 3 elements but had {(vector?.Length) ?? 0}."),
                    name);
            }

            ValidateFiniteValues(vector, name);

            double magnitude = Math.Sqrt(
                (vector[0] * vector[0]) + (vector[1] * vector[1]) + (vector[2] * vector[2]));
            if (magnitude <= 1e-9)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}' must not be the zero vector."),
                    name);
            }
        }

        private static void ValidateOptionalTrajectoryComponent(
            double[]? values,
            int expectedCount,
            string name)
        {
            if (values is null || values.Length == 0)
            {
                return;
            }

            if (values.Length != expectedCount)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}' has {values.Length} values but the trajectory uses {expectedCount}."),
                    name);
            }

            ValidateFiniteValues(values, name);
        }

        private static void ValidateFiniteValues(double[] values, string name)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.IsFinite(values[i]))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"'{name}[{i}]' must be a finite number but was {values[i]}."),
                        name);
                }
            }
        }

        private static void ValidateFinite(double value, string name)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}' must be a finite number but was {value}."),
                    name);
            }
        }

        private static void ValidateNonNegative(double value, string name)
        {
            ValidateFinite(value, name);
            if (value < 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}' must not be negative but was {value}."),
                    name);
            }
        }

        private static void ValidateSpeedFraction(double value, string name)
        {
            ValidateFinite(value, name);
            if (value is < 0 or > 1)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{name}' must be within [0, 1] but was {value}."),
                    name);
            }
        }
    }
}
